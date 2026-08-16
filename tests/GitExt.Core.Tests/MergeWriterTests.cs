using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T11 + P06-T12 — merge and getting out of a merge.
/// </summary>
/// <remarks>
/// The two silent points of the measurement: <c>--squash</c> giving exit code 0 while <b>not
/// committing</b>, and the conflict text landing on <c>stdout</c> (the same trap was fallen into
/// for pull in P06-T07).
/// </remarks>
public class MergeWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(TestRepository Repository, MergeWriter Writer, GitWriteQueue Queue)
        : IDisposable
    {
        public string Path => Repository.Path;

        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        public string Head => Repository.Git("rev-parse", "HEAD").Trim();

        /// <summary>Creates a branch, commits on it, then returns to the main branch.</summary>
        public void BranchWithCommit(string branch, string file, string content)
        {
            Repository.Git("checkout", "-q", "-b", branch);
            Repository.WriteFile(file, content);
            Repository.Git("add", "-A");
            Repository.Git("commit", "-m", $"{branch} commit");
            Repository.Git("checkout", "-q", "main");
        }

        public void CommitOnMain(string file, string content)
        {
            Repository.WriteFile(file, content);
            Repository.Git("add", "-A");
            Repository.Git("commit", "-m", "main commit");
        }
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("branch", "-M", "main");

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(repository, new MergeWriter(new GitWriter(runner, queue), runner), queue);
    }

    // -------------------------------------------------------------- temel

    [Fact]
    public async Task Ileri_sarma_yeni_commit_URETMIYOR()
    {
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "g.txt", "dal\n");

        MergeResult result = await harness.Writer.MergeAsync(
            harness.Path, new MergeOptions { Source = "dal" }, Ct);

        result.Outcome.ShouldBe(MergeOutcome.FastForward);
        result.RequiresCommit.ShouldBeFalse();
        result.RecoveryCommand.ShouldBe($"git reset --hard {result.HeadBefore}");
    }

    [Fact]
    public async Task No_ff_her_zaman_birlestirme_commit_i_uretiyor()
    {
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "g.txt", "dal\n");

        MergeResult result = await harness.Writer.MergeAsync(
            harness.Path,
            new MergeOptions { Source = "dal", Strategy = MergeStrategy.NoFastForward },
            Ct);

        result.Outcome.ShouldBe(MergeOutcome.MergeCommit);

        harness.Repository.Git("rev-list", "--parents", "-1", "HEAD")
            .Trim()
            .Split(' ')
            .Length
            .ShouldBe(3, "iki ebeveynli olmalı");
    }

    [Fact]
    public async Task SQUASH_commit_YAPMIYOR_ve_bu_bildiriliyor()
    {
        // 🔴 MEASURED: git prints "Squash commit -- not updating HEAD" and gives exit code 0.
        // Calling that "successful" and moving on meant the user would think they had merged and
        // never commit — and if they deleted the branch their work would be lost.
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "g.txt", "dal\n");
        string before = harness.Head;

        MergeResult result = await harness.Writer.MergeAsync(
            harness.Path,
            new MergeOptions { Source = "dal", Strategy = MergeStrategy.Squash },
            Ct);

        result.Outcome.ShouldBe(MergeOutcome.Staged);
        result.RequiresCommit.ShouldBeTrue();
        result.HeadAfter.ShouldBe(before, "HEAD gerçekten ilerlememiş olmalı");
        result.RecoveryCommand.ShouldBeNull("geri alınacak bir commit yok");

        // The draft git prepared is handed to the user.
        result.SuggestedMessage.ShouldNotBeNull();
        result.SuggestedMessage!.ShouldContain("dal commit");

        // The change really is waiting in the index.
        harness.Repository.Git("diff", "--cached", "--name-only").Trim().ShouldBe("g.txt");
    }

    [Fact]
    public async Task No_commit_de_ayni_sekilde_bildiriliyor()
    {
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "g.txt", "dal\n");
        harness.CommitOnMain("h.txt", "main\n");

        MergeResult result = await harness.Writer.MergeAsync(
            harness.Path,
            new MergeOptions
            {
                Source = "dal",
                Strategy = MergeStrategy.NoFastForward,
                NoCommit = true,
            },
            Ct);

        result.RequiresCommit.ShouldBeTrue();
    }

    [Fact]
    public async Task Zaten_guncelken_hicbir_sey_yapilmiyor()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Git("branch", "dal");

        MergeResult result = await harness.Writer.MergeAsync(
            harness.Path, new MergeOptions { Source = "dal" }, Ct);

        result.Outcome.ShouldBe(MergeOutcome.AlreadyUpToDate);
        result.RecoveryCommand.ShouldBeNull();
    }

    [Fact]
    public async Task Ozel_mesaj_gecirilyor()
    {
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "g.txt", "dal\n");

        await harness.Writer.MergeAsync(
            harness.Path,
            new MergeOptions
            {
                Source = "dal",
                Strategy = MergeStrategy.NoFastForward,
                Message = "benim mesajim",
            },
            Ct);

        harness.Repository.Git("log", "-1", "--format=%s").Trim().ShouldBe("benim mesajim");
    }

    // ------------------------------------------------------------ conflict

    [Fact]
    public async Task Cakisma_ISTISNA_degil_DURUM_olarak_donuyor()
    {
        // 🔴 MEASURED: the conflict text is on stdout, stderr is EMPTY. The classifier looks at
        // stderr, so it says "Unknown" — the decision is made by looking at the index, not the text.
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "f.txt", "benim\n");
        harness.CommitOnMain("f.txt", "onun\n");

        MergeResult result = await harness.Writer.MergeAsync(
            harness.Path, new MergeOptions { Source = "dal" }, Ct);

        result.Outcome.ShouldBe(MergeOutcome.Conflicted);
        result.HasConflicts.ShouldBeTrue();
        result.ConflictedPaths.ShouldBe(["f.txt"]);
    }

    [Fact]
    public async Task GERCEK_hata_istisna_olarak_KALIYOR()
    {
        // While rescuing conflicts from being exceptions, it is mandatory not to swallow every
        // error: if a non-existent branch were silently counted as "merged", the user would never
        // notice that nothing happened.
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<GitException>(
            () => harness.Writer.MergeAsync(
                harness.Path, new MergeOptions { Source = "boyle-bir-dal-yok" }, Ct));
    }

    [Fact]
    public async Task ff_only_ileri_sarilamayan_durumda_REDDEDIYOR()
    {
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "g.txt", "dal\n");
        harness.CommitOnMain("h.txt", "main\n");

        await Should.ThrowAsync<GitException>(
            () => harness.Writer.MergeAsync(
                harness.Path,
                new MergeOptions { Source = "dal", Strategy = MergeStrategy.FastForwardOnly },
                Ct));

        // The repository must be untouched.
        harness.Repository.Git("status", "--porcelain").Trim().ShouldBeEmpty();
    }

    // ------------------------------------------------------------- preview

    [Fact]
    public async Task Onizleme_ileri_sarilabilirligi_soyluyor()
    {
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "g.txt", "dal\n");

        MergePreview preview = await harness.Writer.PreviewAsync(harness.Path, "dal", Ct);

        preview.HasChanges.ShouldBeTrue();
        preview.CanFastForward.ShouldBeTrue();
        preview.HasCommonAncestor.ShouldBeTrue();
        preview.Ahead.ShouldBe(1);
    }

    [Fact]
    public async Task Iraksayan_dalda_ileri_sarilamiyor()
    {
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "g.txt", "dal\n");
        harness.CommitOnMain("h.txt", "main\n");

        MergePreview preview = await harness.Writer.PreviewAsync(harness.Path, "dal", Ct);

        preview.CanFastForward.ShouldBeFalse();
        preview.HasChanges.ShouldBeTrue();
    }

    [Fact]
    public async Task Yapacak_bir_sey_yoksa_onizleme_bunu_soyluyor()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Git("branch", "dal");

        MergePreview preview = await harness.Writer.PreviewAsync(harness.Path, "dal", Ct);

        preview.HasChanges.ShouldBeFalse();
        preview.Ahead.ShouldBe(0);
    }

    [Fact]
    public async Task Ilgisiz_gecmis_onizlemede_belirtiliyor()
    {
        using Harness harness = await CreateAsync();

        // Orphan branch: in the same repository but with NO common ancestor.
        harness.Repository.Git("checkout", "-q", "--orphan", "yetim");
        harness.Repository.WriteFile("baska.txt", "bambaska\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilgisiz");
        harness.Repository.Git("checkout", "-q", "main");

        MergePreview preview = await harness.Writer.PreviewAsync(harness.Path, "yetim", Ct);

        preview.HasCommonAncestor.ShouldBeFalse();
        preview.CanFastForward.ShouldBeFalse();
    }

    // ------------------------------------------------------------ abort

    [Fact]
    public async Task ABORT_calisma_agacini_merge_ONCESINE_donduruyor()
    {
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "f.txt", "benim\n");
        harness.CommitOnMain("f.txt", "onun\n");

        string before = harness.Head;

        await harness.Writer.MergeAsync(harness.Path, new MergeOptions { Source = "dal" }, Ct);

        harness.Repository.Git("diff", "--name-only", "--diff-filter=U").Trim().ShouldBe("f.txt");

        string after = await harness.Writer.AbortAsync(harness.Path, Ct);

        after.ShouldBe(before);
        harness.Repository.Git("status", "--porcelain").Trim().ShouldBeEmpty();
        File.ReadAllText(System.IO.Path.Combine(harness.Path, "f.txt")).ShouldBe("onun\n");
    }

    [Fact]
    public async Task Suren_merge_YOKKEN_abort_istisna_veriyor()
    {
        // Silently saying "aborted" would tell the user about something that never happened.
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<GitException>(() => harness.Writer.AbortAsync(harness.Path, Ct));
    }

    [Fact]
    public async Task Suren_merge_InProgressOperationReader_ile_GORULUYOR()
    {
        // The banner (P06-T04) and the abort button (P06-T12) look at the same source.
        using Harness harness = await CreateAsync();

        harness.BranchWithCommit("dal", "f.txt", "benim\n");
        harness.CommitOnMain("f.txt", "onun\n");

        await harness.Writer.MergeAsync(harness.Path, new MergeOptions { Source = "dal" }, Ct);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        InProgressOperationReader reader = new(new GitProcessRunner(executable));

        (await reader.ReadAsync(harness.Path, Ct)).ShouldBe(InProgressOperation.Merge);

        await harness.Writer.AbortAsync(harness.Path, Ct);

        (await reader.ReadAsync(harness.Path, Ct)).ShouldBe(InProgressOperation.None);
    }

    // ----------------------------------------------------------- komut

    [Fact]
    public void Komut_onizlemesi_secimleri_yansitiyor()
    {
        MergeWriter.Describe(new MergeOptions { Source = "dal" })
            .ShouldBe("git merge -- dal");

        MergeWriter.Describe(new MergeOptions { Source = "dal", Strategy = MergeStrategy.NoFastForward })
            .ShouldBe("git merge --no-ff -- dal");

        MergeWriter.Describe(new MergeOptions { Source = "dal", Strategy = MergeStrategy.Squash })
            .ShouldBe("git merge --squash -- dal");

        MergeWriter.Describe(new MergeOptions
        {
            Source = "dal",
            Strategy = MergeStrategy.NoFastForward,
            Message = "mesaj",
        }).ShouldBe("git merge --no-ff -m mesaj -- dal");
    }

    [Fact]
    public void Squash_ile_no_commit_birlikte_YAZILMIYOR()
    {
        // `--squash` already does not commit; passing both together would be noise.
        MergeWriter.Describe(new MergeOptions
        {
            Source = "dal",
            Strategy = MergeStrategy.Squash,
            NoCommit = true,
        }).ShouldBe("git merge --squash -- dal");
    }
}
