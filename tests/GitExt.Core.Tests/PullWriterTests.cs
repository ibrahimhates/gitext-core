using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T07 — pull.
/// </summary>
/// <remarks>
/// The three silent points of the measurement: with no configuration and divergent branches git
/// <b>performs the fetch and only then refuses</b>, a conflict cannot be read from the exit code,
/// and the <c>--autostash</c> restore can conflict with <b>exit code 0</b>.
/// </remarks>
public class PullWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Local,
        TestRepository Upstream,
        TestRepository Other,
        PullWriter Writer,
        GitWriteQueue Queue) : IDisposable
    {
        public string Path => Local.Path;

        public void Dispose()
        {
            Queue.Dispose();
            Local.Dispose();
            Other.Dispose();
            Upstream.Dispose();
        }

        /// <summary>"Someone else" pushes a commit to the remote.</summary>
        public void RemoteCommit(string content, string file = "f.txt")
        {
            Other.WriteFile(file, content);
            Other.Git("add", "-A");
            Other.Git("commit", "-m", $"uzak-{content}");
            Other.Git("push", "-q", "up", "HEAD:main");
        }

        /// <summary>Yerelde bir commit.</summary>
        public void LocalCommit(string content, string file = "yerel.txt")
        {
            Local.WriteFile(file, content);
            Local.Git("add", "-A");
            Local.Git("commit", "-m", $"yerel-{content}");
        }

        public int MergeCommitCount =>
            Local.Git("log", "--merges", "--oneline")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        public string Head => Local.Git("rev-parse", "HEAD").Trim();
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository upstream = TestRepository.CreateBare();

        TestRepository other = TestRepository.CreateEmpty();
        other.WriteFile("f.txt", "s1\n");
        other.Git("add", "-A");
        other.Git("commit", "-m", "ilk");
        other.Git("remote", "add", "up", upstream.Path);
        other.Git("push", "-q", "up", "HEAD:main");

        TestRepository local = TestRepository.CreateEmpty();
        local.Git("remote", "add", "origin", upstream.Path);
        local.Git("fetch", "-q", "origin");
        local.Git("checkout", "-q", "-b", "main", "--track", "origin/main");

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(
            local,
            upstream,
            other,
            new PullWriter(new GitWriter(runner, queue), runner, new GitConfigReader(runner)),
            queue);
    }

    // ---- Strategy resolution ----

    [Fact]
    public async Task Ayar_yokken_varsayilan_BIRLESTIR_ve_kaynagi_yaziliyor()
    {
        using Harness harness = await CreateAsync();

        ResolvedPullStrategy strategy =
            await harness.Writer.ResolveStrategyAsync(harness.Path, cancellationToken: Ct);

        strategy.Strategy.ShouldBe(PullStrategy.Merge);
        strategy.Source.ShouldBe(PullStrategySource.ApplicationDefault);
    }

    [Fact]
    public async Task Dal_ayari_pull_rebase_i_EZIYOR()
    {
        // MEASURED: `pull.rebase=true` + `branch.main.rebase=false` → git did a MERGE.
        using Harness harness = await CreateAsync();

        harness.Local.Git("config", "pull.rebase", "true");
        harness.Local.Git("config", "branch.main.rebase", "false");

        ResolvedPullStrategy strategy =
            await harness.Writer.ResolveStrategyAsync(harness.Path, cancellationToken: Ct);

        strategy.Strategy.ShouldBe(PullStrategy.Merge);
        strategy.Source.ShouldBe(PullStrategySource.BranchSetting);
        strategy.ConfigValue.ShouldBe("false");
    }

    [Theory]
    [InlineData("true", PullStrategy.Rebase)]
    [InlineData("interactive", PullStrategy.Rebase)]
    [InlineData("merges", PullStrategy.Rebase)]
    [InlineData("false", PullStrategy.Merge)]
    public async Task pull_rebase_degerleri_dogru_cevriliyor(string value, PullStrategy expected)
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("config", "pull.rebase", value);

        ResolvedPullStrategy strategy =
            await harness.Writer.ResolveStrategyAsync(harness.Path, cancellationToken: Ct);

        strategy.Strategy.ShouldBe(expected);
        strategy.Source.ShouldBe(PullStrategySource.PullRebaseSetting);
    }

    [Fact]
    public async Task pull_ff_only_ayari_okunuyor()
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("config", "pull.ff", "only");

        ResolvedPullStrategy strategy =
            await harness.Writer.ResolveStrategyAsync(harness.Path, cancellationToken: Ct);

        strategy.Strategy.ShouldBe(PullStrategy.FastForwardOnly);
        strategy.Source.ShouldBe(PullStrategySource.PullFfSetting);
    }

    [Fact]
    public async Task Kullanicinin_secimi_ayarlari_eziyor()
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("config", "pull.rebase", "true");

        ResolvedPullStrategy strategy = await harness.Writer.ResolveStrategyAsync(
            harness.Path, PullStrategy.FastForwardOnly, Ct);

        strategy.Strategy.ShouldBe(PullStrategy.FastForwardOnly);
        strategy.Source.ShouldBe(PullStrategySource.UserChoice);
    }

    // ---- Pull behaviour ----

    [Fact]
    public async Task Guncelken_HEAD_ilerlemiyor()
    {
        using Harness harness = await CreateAsync();

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.AlreadyUpToDate.ShouldBeTrue();
        result.HasConflicts.ShouldBeFalse();
        result.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ileri_sarma_HEAD_i_tasiyor_ve_degisikligi_bildiriyor()
    {
        using Harness harness = await CreateAsync();

        harness.RemoteCommit("s2\n");

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.AlreadyUpToDate.ShouldBeFalse();
        result.HeadAfter.ShouldBe(harness.Head);
        result.Changes.ShouldContain(change => change.RefName == "refs/remotes/origin/main");
        harness.MergeCommitCount.ShouldBe(0);
    }

    [Fact]
    public async Task Ayarsiz_IRAKSAYAN_depoda_git_REDDETMIYOR_cunku_bayrak_aciktan_geciliyor()
    {
        // 🔴 The reason this test exists: with no configuration and divergent branches, a bare
        // `git pull` REFUSES TO RUN (rc=128, nine lines of `hint:`) — and on top of that it
        // completes the fetch stage BEFORE refusing, so the repository changes while the user sees
        // "failed".
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.Strategy.Strategy.ShouldBe(PullStrategy.Merge);
        result.HasConflicts.ShouldBeFalse();
        harness.MergeCommitCount.ShouldBe(1);
    }

    /// <summary>
    /// Counter-evidence: with no configuration, what a bare <c>git pull</c> does on a divergent
    /// repository is <b>the git version's decision, not ours</b> — which is why
    /// <see cref="PullWriter"/> always writes an explicit flag.
    /// </summary>
    /// <remarks>
    /// 🔴 MEASURED ON TWO VERSIONS — the behaviour splits at git <b>2.34</b>, and BOTH halves make
    /// the same case:
    /// <list type="bullet">
    ///   <item>
    ///     <b>2.34 and up</b>: refuses, rc≠0, nine lines of <c>hint:</c>. The insidious part is that
    ///     the FETCH HAS ALREADY COMPLETED — the repository changed while the user sees "failed".
    ///   </item>
    ///   <item>
    ///     <b>Below 2.34</b> (2.30.2, the ADR-0002 minimum, measured): it only WARNS and merges
    ///     anyway, rc=0. Worse still: the same command silently produces a merge commit the user
    ///     never asked for.
    ///   </item>
    /// </list>
    /// The test used to assert the refusal unconditionally and so was red on the <c>min-git</c> CI
    /// job — the assertion was written against one git, not against git.
    /// </remarks>
    [Fact]
    public async Task KARSI_KANIT_ciplak_git_pull_ayarsiz_iraksayan_depoda_KONTROLU_ELDEN_ALIYOR()
    {
        // ⚠️ In a separate repository: if the bare command did run it would change the repository
        // and break the setup of the actual test.
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);

        (int exitCode, string error) = harness.Local.TryGit("pull");

        if (executable.Version >= new GitVersion(2, 34, 0))
        {
            exitCode.ShouldNotBe(0);
            error.ShouldContain("divergent branches");
        }
        else
        {
            exitCode.ShouldBe(0);
            error.ShouldContain("hint:");
            harness.MergeCommitCount.ShouldBe(1, "eski git uyarıp yine de birleştiriyor");
        }

        // And the truly insidious part of the measurement: whichever branch was taken, the FETCH has
        // completed.
        harness.Local.Git("rev-parse", "origin/main").Trim()
            .ShouldBe(harness.Other.Git("rev-parse", "HEAD").Trim());
    }

    [Fact]
    public async Task Rebase_stratejisi_merge_commit_URETMIYOR()
    {
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");

        PullResult result = await harness.Writer.PullAsync(
            harness.Path, new PullOptions { Strategy = PullStrategy.Rebase }, Ct);

        result.HasConflicts.ShouldBeFalse();
        harness.MergeCommitCount.ShouldBe(0);
        harness.Local.Git("log", "--oneline", "-2").ShouldContain("yerel-a");
    }

    [Fact]
    public async Task ff_only_iraksayan_depoda_HATA_veriyor_ve_HEAD_e_dokunmuyor()
    {
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");
        string before = harness.Head;

        await Should.ThrowAsync<GitException>(() => harness.Writer.PullAsync(
            harness.Path, new PullOptions { Strategy = PullStrategy.FastForwardOnly }, Ct));

        harness.Head.ShouldBe(before);
    }

    [Fact]
    public async Task Cakisma_ISTISNA_degil_SONUC_olarak_bildiriliyor()
    {
        // A conflict is not an error: the repository is in the conflict state and what to do next
        // is clear. Raised as an exception, the interface would only show a red box.
        using Harness harness = await CreateAsync();

        harness.Local.WriteFile("f.txt", "YEREL\n");
        harness.Local.Git("commit", "-am", "yerel-cakisma");
        harness.RemoteCommit("UZAK\n");

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.HasConflicts.ShouldBeTrue();
        result.AutoStashConflict.ShouldBeFalse();
        harness.Local.Git("status", "--porcelain").ShouldContain("UU");

        harness.Local.Git("merge", "--abort");
    }

    [Fact]
    public async Task AUTOSTASH_geri_koyma_cakismasi_cikis_kodu_0_ILE_geliyor()
    {
        // 🔴 MEASURED: in this case `git pull` gives EXIT CODE 0, yet there is a `UU` file in the
        // working tree and conflict markers INSIDE that file. An interface that looks only at the
        // exit code would say "pull succeeded" — exactly the same trap as `switch --merge` in
        // P06-T02.
        using Harness harness = await CreateAsync();

        harness.RemoteCommit("UZAK\n");
        harness.Local.WriteFile("f.txt", "KAYDEDILMEMIS-YEREL\n");

        PullResult result = await harness.Writer.PullAsync(
            harness.Path,
            new PullOptions { Strategy = PullStrategy.Rebase, AutoStash = true },
            Ct);

        result.HasConflicts.ShouldBeTrue();
        result.AutoStashConflict.ShouldBeTrue("pull başarılı, çakışan kullanıcının stash'i");

        // The user's work is not lost: the stash is still in the list.
        harness.Local.Git("stash", "list").ShouldContain("autostash");

        harness.Local.Git("reset", "--hard", "-q");
        harness.Local.Git("stash", "drop");
    }

    /// <summary>
    /// 🔴 Regression: an OLDER stash of the user's must not be mistaken for an autostash conflict.
    /// </summary>
    /// <remarks>
    /// The detection reads the state, not git's message ("does a stash exist after the pull?"), and
    /// the trap that comes with that is exactly this: someone with a stash already sitting there
    /// runs into an ordinary merge conflict and gets told "your stashed changes conflicted" — sent
    /// after the wrong files. What is compared is therefore the stash ref taken BEFORE the pull.
    /// </remarks>
    [Fact]
    public async Task Onceden_var_olan_stash_AUTOSTASH_cakismasi_sanilmaz()
    {
        using Harness harness = await CreateAsync();

        // A stash from earlier that has nothing to do with this pull.
        harness.Local.WriteFile("baska.txt", "eski is\n");
        harness.Local.Git("add", "-A");
        harness.Local.Git("stash", "push", "-m", "eski");

        harness.Local.WriteFile("f.txt", "YEREL\n");
        harness.Local.Git("commit", "-am", "yerel-cakisma");
        harness.RemoteCommit("UZAK\n");

        PullResult result = await harness.Writer.PullAsync(
            harness.Path,
            new PullOptions { AutoStash = true },
            Ct);

        result.HasConflicts.ShouldBeTrue();
        result.AutoStashConflict.ShouldBeFalse("çakışan kullanıcının stash'i değil, birleştirmenin kendisi");

        harness.Local.Git("merge", "--abort");
    }

    [Fact]
    public async Task Autostash_cakismasizken_temiz_ve_dosya_yerinde()
    {
        using Harness harness = await CreateAsync();

        harness.RemoteCommit("s2\n");
        harness.Local.WriteFile("baska.txt", "kirli\n");

        PullResult result = await harness.Writer.PullAsync(
            harness.Path,
            new PullOptions { Strategy = PullStrategy.Rebase, AutoStash = true },
            Ct);

        result.HasConflicts.ShouldBeFalse();
        result.AutoStashConflict.ShouldBeFalse();
        harness.Local.Git("stash", "list").Trim().ShouldBeEmpty();
        File.Exists(Path.Combine(harness.Path, "baska.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Geri_alma_komutu_GERCEKTEN_calisiyor()
    {
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");
        string before = harness.Head;

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.HeadBefore.ShouldBe(before);
        result.RecoveryCommand.ShouldBe($"git reset --hard {before}");
        harness.MergeCommitCount.ShouldBe(1);

        harness.Local.Git("reset", "--hard", before, "-q");

        harness.Head.ShouldBe(before);
        harness.MergeCommitCount.ShouldBe(0);
        harness.Local.Git("log", "--oneline", "-1").ShouldContain("yerel-a");
    }

    [Fact]
    public async Task Kirli_agacta_pull_HEAD_e_dokunmuyor()
    {
        using Harness harness = await CreateAsync();

        harness.RemoteCommit("UZAK\n");
        harness.Local.WriteFile("f.txt", "kirli\n");
        string before = harness.Head;

        await Should.ThrowAsync<GitException>(() =>
            harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct));

        harness.Head.ShouldBe(before);
    }
}
