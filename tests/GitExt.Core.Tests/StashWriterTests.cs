using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T12 — stash operations.
/// </summary>
/// <remarks>
/// The two silent points from the measurement: <c>pop</c> losing the staged/unstaged distinction, and
/// the entry <b>not being dropped</b> on a conflict.
/// </remarks>
public class StashWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(TestRepository Repository, StashWriter Writer, GitWriteQueue Queue)
        : IDisposable
    {
        public string Path => Repository.Path;

        public string Status => Repository.Git("status", "--porcelain=v2");

        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("f.txt", "taban\n");
        repository.WriteFile("g.txt", "taban\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "taban");

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(repository, new StashWriter(new GitWriter(runner, queue), runner), queue);
    }

    // ------------------------------------------------------------ push

    [Fact]
    public async Task Mesajli_stash_olusuyor_ve_agac_temizleniyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");

        bool stashed = await harness.Writer.PushAsync(
            harness.Path, new StashPushOptions { Message = "benim stash" }, Ct);

        stashed.ShouldBeTrue();
        harness.Status.Trim().ShouldBeEmpty();

        IReadOnlyList<StashEntry> entries = await harness.Writer.ListAsync(harness.Path, Ct);
        entries.ShouldHaveSingleItem().Message.ShouldContain("benim stash");
    }

    [Fact]
    public async Task Degisiklik_YOKKEN_stash_olusmadi_diye_bildiriliyor()
    {
        // git says "No local changes to save" and gives exit code 0. Reporting that as "stashed" would
        // send the user looking for an entry that does not exist.
        using Harness harness = await CreateAsync();

        bool stashed = await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        stashed.ShouldBeFalse();
        (await harness.Writer.ListAsync(harness.Path, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task TAKIP_EDILMEYEN_dosya_varsayilan_olarak_KALIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");
        harness.Repository.WriteFile("yeni.txt", "takipsiz\n");

        await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        File.Exists(Path.Combine(harness.Path, "yeni.txt"))
            .ShouldBeTrue("--include-untracked verilmedi");
    }

    [Fact]
    public async Task Untracked_DAHIL_edilince_ucuncu_ebeveynden_anlasiliyor()
    {
        // MEASURED: a stash taken with `-u` has a 3rd parent. Looking at the message would be unreliable
        // — the message is written by the user.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");
        harness.Repository.WriteFile("yeni.txt", "takipsiz\n");

        await harness.Writer.PushAsync(
            harness.Path, new StashPushOptions { IncludeUntracked = true }, Ct);

        File.Exists(Path.Combine(harness.Path, "yeni.txt")).ShouldBeFalse();

        IReadOnlyList<StashEntry> entries = await harness.Writer.ListAsync(harness.Path, Ct);
        entries.ShouldHaveSingleItem().IncludesUntracked.ShouldBeTrue();
    }

    [Fact]
    public async Task Untracked_HARIC_stash_ucuncu_ebeveyn_TASIMIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");

        await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        (await harness.Writer.ListAsync(harness.Path, Ct))
            .ShouldHaveSingleItem().IncludesUntracked.ShouldBeFalse();
    }

    [Fact]
    public async Task SECILI_yollar_stashleniyor_digerleri_KALIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "f degisti\n");
        harness.Repository.WriteFile("g.txt", "g degisti\n");

        await harness.Writer.PushAsync(
            harness.Path,
            new StashPushOptions { Paths = [RepositoryPath.Parse("f.txt")] },
            Ct);

        File.ReadAllText(Path.Combine(harness.Path, "f.txt")).ShouldBe("taban\n");
        File.ReadAllText(Path.Combine(harness.Path, "g.txt")).ShouldBe("g degisti\n");
    }

    [Fact]
    public async Task Keep_index_stagelenmisleri_agacta_BIRAKIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "stageli\n");
        harness.Repository.Git("add", "f.txt");

        await harness.Writer.PushAsync(
            harness.Path, new StashPushOptions { KeepIndex = true }, Ct);

        File.ReadAllText(Path.Combine(harness.Path, "f.txt")).ShouldBe("stageli\n");
    }

    // ------------------------------------------------------------ liste

    [Fact]
    public async Task Liste_en_yeniden_eskiye_ve_indeksli()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("f.txt", "bir\n");
        await harness.Writer.PushAsync(harness.Path, new StashPushOptions { Message = "bir" }, Ct);

        harness.Repository.WriteFile("f.txt", "iki\n");
        await harness.Writer.PushAsync(harness.Path, new StashPushOptions { Message = "iki" }, Ct);

        IReadOnlyList<StashEntry> entries = await harness.Writer.ListAsync(harness.Path, Ct);

        entries.Count.ShouldBe(2);
        entries[0].Message.ShouldContain("iki");
        entries[0].ShortSelector.ShouldBe("stash@{0}");
        entries[1].ShortSelector.ShouldBe("stash@{1}");
    }

    [Fact]
    public void SEKME_iceren_stash_mesaji_alanlari_KAYDIRMIYOR()
    {
        // The message is written by the user; it can contain a tab. Hence the NUL separator.
        string output = "\u001erefs/stash@{0}\0abc\01786083756\0On main: konu\tsekmeli\0p1 p2\n";

        IReadOnlyList<StashEntry> entries = StashWriter.Parse(output);

        entries.ShouldHaveSingleItem().Message.ShouldBe("On main: konu\tsekmeli");
        entries[0].IncludesUntracked.ShouldBeFalse();
    }

    [Fact]
    public async Task Turkce_mesaj_BOZULMUYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");

        await harness.Writer.PushAsync(
            harness.Path, new StashPushOptions { Message = "ğüşiöç değişikliği" }, Ct);

        (await harness.Writer.ListAsync(harness.Path, Ct))
            .ShouldHaveSingleItem().Message.ShouldContain("ğüşiöç değişikliği");
    }

    // ------------------------------------------------------------ apply

    [Fact]
    public async Task POP_STAGE_ayrimini_KORUYOR()
    {
        // 🔴 MEASURED: the default `pop` silently loses this distinction — with `f` staged and `g`
        // unstaged, after the pop BOTH were unstaged. `--index` preserves it.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("f.txt", "stageli\n");
        harness.Repository.Git("add", "f.txt");
        harness.Repository.WriteFile("g.txt", "stagesiz\n");

        await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        StashApplyResult result =
            await harness.Writer.ApplyAsync(harness.Path, "stash@{0}", drop: true, Ct);

        result.IndexRestored.ShouldBeTrue();

        // `M.` = stage'li, `.M` = stage'siz.
        harness.Status.ShouldContain("M. ");
        harness.Status.ShouldContain(".M ");
    }

    [Fact]
    public async Task APPLY_girdiyi_listede_BIRAKIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");
        await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        StashApplyResult result =
            await harness.Writer.ApplyAsync(harness.Path, "stash@{0}", drop: false, Ct);

        result.EntryKept.ShouldBeTrue();
        (await harness.Writer.ListAsync(harness.Path, Ct)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task POP_girdiyi_DUSURUYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");
        await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        StashApplyResult result =
            await harness.Writer.ApplyAsync(harness.Path, "stash@{0}", drop: true, Ct);

        result.HasConflicts.ShouldBeFalse();
        result.EntryKept.ShouldBeFalse();
        (await harness.Writer.ListAsync(harness.Path, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task POP_CAKISIRSA_girdi_KALIYOR_ve_bu_bildiriliyor()
    {
        // 🔴 MEASURED: git says "The stash entry is kept in case you need it again." and gives rc=1.
        // Unless it is said, the user either applies it twice or loses it while deleting it by hand.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("f.txt", "stashli\n");
        await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        harness.Repository.WriteFile("f.txt", "baska\n");
        harness.Repository.Git("commit", "-am", "cakisacak");

        StashApplyResult result =
            await harness.Writer.ApplyAsync(harness.Path, "stash@{0}", drop: true, Ct);

        result.HasConflicts.ShouldBeTrue();
        result.ConflictedPaths.ShouldContain(path => path.Value == "f.txt");
        result.EntryKept.ShouldBeTrue("çakışmada git girdiyi DÜŞÜRMÜYOR");
        (await harness.Writer.ListAsync(harness.Path, Ct)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task BILINMEYEN_secici_FIRLATIYOR()
    {
        // A real error; it must not be swallowed the way a conflict is.
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<GitException>(async () =>
            await harness.Writer.ApplyAsync(harness.Path, "stash@{42}", drop: false, Ct));
    }

    // ------------------------------------------------------------ other

    [Fact]
    public async Task Drop_girdiyi_siliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");
        await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        await harness.Writer.DropAsync(harness.Path, "stash@{0}", Ct);

        (await harness.Writer.ListAsync(harness.Path, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Branch_stashi_yeni_dala_aciyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");
        await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        await harness.Writer.BranchAsync(harness.Path, "stash@{0}", "stash-dali", Ct);

        harness.Repository.Git("rev-parse", "--abbrev-ref", "HEAD").Trim().ShouldBe("stash-dali");
        File.ReadAllText(Path.Combine(harness.Path, "f.txt")).ShouldBe("degisti\n");
        (await harness.Writer.ListAsync(harness.Path, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Show_diff_uretiyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "degisti\n");
        await harness.Writer.PushAsync(harness.Path, new StashPushOptions(), Ct);

        string diff = await harness.Writer.ShowAsync(harness.Path, "stash@{0}", Ct);

        diff.ShouldContain("f.txt");
        diff.ShouldContain("+degisti");
        diff.ShouldContain("-taban");
    }
}
