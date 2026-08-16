using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T01 — branch creation.
/// </summary>
/// <remarks>
/// The weight of these tests is on the <b>silent</b> behaviours found by measurement: git doing
/// something wrong without calling it an error (nested ref name), or describing the error wrongly
/// (empty repository).
/// </remarks>
public class BranchWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        BranchWriter Writer,
        WorkingTreeWriter WorkingTree,
        GitWriteQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        public string Path => Repository.Path;

        public string Read(string name) =>
            File.ReadAllText(System.IO.Path.Combine(Repository.Path, name));

        public string CurrentBranch => Repository.Git("symbolic-ref", "--short", "HEAD").Trim();

        public IReadOnlyList<string> Branches =>
        [
            .. Repository
                .Git("for-each-ref", "--format=%(refname)", "refs/heads")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()),
        ];
    }

    private static async Task<Harness> CreateAsync(bool withCommit = true)
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        if (withCommit)
        {
            repository.WriteFile("a.txt", "a\n");
            repository.Git("add", "-A");
            repository.Git("commit", "-m", "ilk");
        }

        GitWriter writer = new(runner, queue);
        WorkingTreeWriter workingTree = new(writer, runner);

        return new Harness(
            repository, new BranchWriter(writer, runner, workingTree), workingTree, queue);
    }

    [Fact]
    public async Task Dal_olusturulup_gecilir()
    {
        using Harness harness = await CreateAsync();

        BranchCreateResult result = await harness.Writer.CreateAsync(
            harness.Path, new BranchCreateOptions { Name = "ozellik" }, Ct);

        result.Name.ShouldBe("ozellik");
        result.CheckedOut.ShouldBeTrue();
        harness.CurrentBranch.ShouldBe("ozellik");
    }

    [Fact]
    public async Task Checkout_kapaliyken_DAL_DEGISMIYOR()
    {
        using Harness harness = await CreateAsync();
        string before = harness.CurrentBranch;

        await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions { Name = "ozellik", Checkout = false },
            Ct);

        harness.CurrentBranch.ShouldBe(before);
        harness.Branches.ShouldContain("refs/heads/ozellik");
    }

    [Fact]
    public async Task Baslangic_noktasi_verilince_ORADAN_olusuyor()
    {
        using Harness harness = await CreateAsync();
        string ilk = harness.Repository.Git("rev-parse", "HEAD").Trim();

        harness.Repository.WriteFile("b.txt", "b\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ikinci");

        await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions { Name = "gecmisten", StartPoint = ilk, Checkout = false },
            Ct);

        harness.Repository.Git("rev-parse", "gecmisten").Trim().ShouldBe(ilk);
    }

    [Fact]
    public async Task Kirli_agacta_checkout_SUZ_olusturma_her_zaman_calisir()
    {
        // MEASURED: `git branch` does not touch the working tree at all.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "KIRLI\n");

        await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions { Name = "temiz", Checkout = false },
            Ct);

        harness.Branches.ShouldContain("refs/heads/temiz");
        harness.Repository.Git("status", "--porcelain").ShouldContain("a.txt");
    }

    [Fact]
    public async Task Cakisan_kirli_dosya_varken_switch_REDDEDILIR_ve_dal_OLUSMAZ()
    {
        // 🔴 This is the real guarantee: a rejected operation must not leave a HALF result. If the
        // branch were created and the checkout then failed, the user would be left with a branch
        // whose name is "taken" but which is not where they expected it to be.
        using Harness harness = await CreateAsync();
        string ilk = harness.Repository.Git("rev-parse", "HEAD").Trim();

        harness.Repository.WriteFile("b.txt", "b\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ikinci");

        // b.txt only exists in the second commit; dirtying it and then trying to switch to the
        // first commit produces a conflict.
        harness.Repository.WriteFile("b.txt", "YEREL DEGISIKLIK\n");

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "olmamali", StartPoint = ilk },
                Ct));

        error.Kind.ShouldBe(GitFailureKind.DirtyWorkingTree);
        harness.Branches.ShouldNotContain("refs/heads/olmamali");
        harness.Repository.Git("cat-file", "-p", ":b.txt").ShouldBe("b\n");
    }

    [Fact]
    public async Task Var_olan_dal_ANLAMLI_hatayla_reddediliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "ozellik");

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "ozellik", Checkout = false },
                Ct));

        error.Kind.ShouldBe(GitFailureKind.BranchAlreadyExists);
    }

    [Theory]
    [InlineData("ust", "ust/alt")]
    [InlineData("ust/alt", "ust")]
    public async Task Dizin_dosya_cakismasi_ANLAMLI_hatayla_reddediliyor(string first, string second)
    {
        // 🔴 MEASURED, in both directions: it passes validation because it is COMPLETELY valid by
        // the naming rules; only git can report it, because git stores branches like files.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", first);

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = second, Checkout = false },
                Ct));

        error.Kind.ShouldBe(GitFailureKind.RefNameConflict);
    }

    [Fact]
    public async Task Bos_depoda_hata_DEPONUN_BOS_oldugunu_soyluyor()
    {
        // 🔴 MEASURED: git's own message is "not a valid object name: 'main'" — that falls into
        // UnknownRevision in this classification and would tell the user "branch not found".
        // But the user never typed a branch NAME; the repository is empty.
        using Harness harness = await CreateAsync(withCommit: false);

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "ilk-dal", Checkout = false },
                Ct));

        error.Kind.ShouldBe(GitFailureKind.UnbornHead);
    }

    [Fact]
    public async Task Gecersiz_ad_GIT_CAGRILMADAN_reddediliyor()
    {
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<ArgumentException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "gecersiz ad" },
                Ct));

        harness.Branches.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Tam_ref_adi_yapistirmak_IC_ICE_dal_olusturmuyor()
    {
        // 🔴 MEASURED: `git branch refs/heads/x` does NOT error, it creates
        // `refs/heads/refs/heads/x`. When the user copies a name out of `git branch -a` output they
        // would silently end up with a nested branch.
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<ArgumentException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "refs/heads/x", Checkout = false },
                Ct));

        harness.Branches.ShouldNotContain("refs/heads/refs/heads/x");
    }

    [Fact]
    public async Task Uzak_daldan_olusturulunca_upstream_BILDIRILIYOR()
    {
        // MEASURED: git sets the upstream itself (the `branch.autoSetupMerge` default).
        // We do not imitate it, we READ the result and report it — the user's configuration can
        // change this, and then we would be making it up.
        using TestRepository upstream = TestRepository.CreateEmpty();
        upstream.WriteFile("a.txt", "a\n");
        upstream.Git("add", "-A");
        upstream.Git("commit", "-m", "ilk");
        upstream.Git("branch", "ozellik");

        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", upstream.Path);
        harness.Repository.Git("fetch", "-q", "origin");

        BranchCreateResult fromRemote = await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions
            {
                Name = "ozellik",
                StartPoint = "origin/ozellik",
                Checkout = false,
            },
            Ct);

        fromRemote.Upstream.ShouldBe("origin/ozellik");

        // …and it is not set up when created from a local branch.
        BranchCreateResult fromLocal = await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions { Name = "yerelden", Checkout = false },
            Ct);

        fromLocal.Upstream.ShouldBeNull();
    }

    // ---- Branch switching (P06-T02) ----

    /// <summary>Sets up two branches: `main` (main.txt present) and `ozellik` (main.txt deleted).</summary>
    private static void SetupTwoBranches(Harness harness)
    {
        harness.Repository.WriteFile("ortak.txt", "ortak\n");
        harness.Repository.WriteFile("main.txt", "mainde\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("temel");

        harness.Repository.Git("switch", "-c", "ozellik");
        harness.Repository.Git("rm", "-q", "main.txt");
        harness.Repository.Commit("main.txt silindi");
        harness.Repository.Git("switch", "main");
    }

    [Fact]
    public async Task Temiz_agacta_dal_degistiriliyor()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path, new BranchSwitchOptions { Target = "ozellik" }, Ct);

        harness.CurrentBranch.ShouldBe("ozellik");
        result.HasConflicts.ShouldBeFalse();
        result.StashCreated.ShouldBeFalse();
    }

    [Fact]
    public async Task Ilgisiz_kirli_dosya_YENI_DALA_tasiniyor()
    {
        // MEASURED: git carries over what it can; this is expected behaviour, not a bug.
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("ortak.txt", "KIRLI\n");

        await harness.Writer.SwitchAsync(
            harness.Path, new BranchSwitchOptions { Target = "ozellik" }, Ct);

        harness.CurrentBranch.ShouldBe("ozellik");
        harness.Read("ortak.txt").ShouldBe("KIRLI\n");
    }

    [Fact]
    public async Task Cakisan_kirli_dosyada_gecis_REDDEDILIYOR()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("main.txt", "YEREL\n");

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.SwitchAsync(
                harness.Path, new BranchSwitchOptions { Target = "ozellik" }, Ct));

        error.Kind.ShouldBe(GitFailureKind.DirtyWorkingTree);
        harness.CurrentBranch.ShouldBe("main");
        harness.Read("main.txt").ShouldBe("YEREL\n");
    }

    [Fact]
    public async Task Stash_yolu_cakismayi_cozuyor_ve_icerik_KAYBOLMUYOR()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("main.txt", "YEREL\n");

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = "ozellik", LocalChanges = LocalChangesAction.Stash },
            Ct);

        harness.CurrentBranch.ShouldBe("ozellik");
        result.StashCreated.ShouldBeTrue();

        // The content is sitting in the stash: it can be gone back to and retrieved.
        harness.Repository.Git("switch", "main");
        harness.Repository.Git("stash", "pop");
        harness.Read("main.txt").ShouldBe("YEREL\n");
    }

    [Fact]
    public async Task Stash_TAKIP_EDILMEYEN_dosya_cakismasini_da_cozuyor()
    {
        // 🔴 MEASURED: `--discard-changes` does NOT resolve this case (it refuses and leaves the
        // file alone). So "force" is not a universal escape hatch; stash is more capable.
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);

        // `main.txt` does not exist on the `ozellik` branch; on `main` it is tracked. To build the
        // conflict in the reverse direction we switch to `ozellik` and fill that same name with an
        // untracked file.
        harness.Repository.Git("switch", "ozellik");
        harness.Repository.WriteFile("main.txt", "BENIM YEREL DOSYAM\n");

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = "main", LocalChanges = LocalChangesAction.Stash },
            Ct);

        harness.CurrentBranch.ShouldBe("main");
        result.StashCreated.ShouldBeTrue();
    }

    [Fact]
    public async Task Onaysiz_ATMA_reddediliyor()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("main.txt", "YEREL\n");

        await Should.ThrowAsync<InvalidOperationException>(
            harness.Writer.SwitchAsync(
                harness.Path,
                new BranchSwitchOptions
                {
                    Target = "ozellik",
                    LocalChanges = LocalChangesAction.Discard,
                },
                Ct));

        harness.CurrentBranch.ShouldBe("main");
        harness.Read("main.txt").ShouldBe("YEREL\n");
    }

    [Fact]
    public async Task Atilan_icerik_YEDEKTEN_geri_okunabiliyor()
    {
        // 🔴 The most important guarantee of P06-T02. MEASURED: after `--discard-changes` there is
        // no trace whatsoever of the UNSTAGED content in the object database —
        // not even `fsck --lost-found` finds it. Without a backup there is NO way back.
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("ortak.txt", "KAYBOLMAMASI GEREKEN\n");

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions
            {
                Target = "ozellik",
                LocalChanges = LocalChangesAction.Discard,
                UserConfirmed = true,
            },
            Ct);

        harness.CurrentBranch.ShouldBe("ozellik");
        harness.Read("ortak.txt").ShouldBe("ortak\n");

        DiscardBackup backup = result.Backups.ShouldHaveSingleItem();
        backup.Path.Value.ShouldBe("ortak.txt");
        harness.Repository.Git("cat-file", "-p", backup.BlobId)
            .ShouldBe("KAYBOLMAMASI GEREKEN\n");
    }

    [Fact]
    public async Task Merge_yolunda_CIKIS_KODU_0_olsa_bile_cakisma_bildiriliyor()
    {
        // 🔴 MEASURED: on conflict `switch --merge` gives exit code **0**, leaves the tree
        // unmerged and creates a hidden autostash. An interface that looks at the exit code would
        // say "switched successfully".
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("main.txt", "YEREL SATIR\n");

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = "ozellik", LocalChanges = LocalChangesAction.Merge },
            Ct);

        result.HasConflicts.ShouldBeTrue();
    }

    [Fact]
    public async Task Temiz_agacta_merge_yolu_cakisma_BILDIRMIYOR()
    {
        // A false alarm is just as harmful as a silent bug: the warning of an interface that says
        // "there are conflicts" on every switch stops being read.
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = "ozellik", LocalChanges = LocalChangesAction.Merge },
            Ct);

        result.HasConflicts.ShouldBeFalse();
        harness.CurrentBranch.ShouldBe("ozellik");
    }

    [Fact]
    public async Task Detached_HEAD_e_gecilebiliyor()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        string ilk = harness.Repository.Git("rev-parse", "HEAD").Trim();

        await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = ilk, Detach = true },
            Ct);

        harness.Repository.Git("rev-parse", "HEAD").Trim().ShouldBe(ilk);
        harness.Repository.TryGit("symbolic-ref", "--short", "HEAD").ExitCode.ShouldNotBe(0);
    }

    [Fact]
    public async Task Cozumlenemeyen_hedef_ANLAMLI_hatayla_reddediliyor()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.SwitchAsync(
                harness.Path, new BranchSwitchOptions { Target = "boyle-bir-dal-yok" }, Ct));

        error.Kind.ShouldBe(GitFailureKind.UnknownRevision);
        harness.CurrentBranch.ShouldBe("main");
    }

    // ---- Renaming and deleting (P06-T03) ----

    [Fact]
    public async Task Dal_yeniden_adlandiriliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "eski");

        await harness.Writer.RenameAsync(harness.Path, "eski", "yeni", Ct);

        harness.Branches.ShouldContain("refs/heads/yeni");
        harness.Branches.ShouldNotContain("refs/heads/eski");
    }

    [Fact]
    public async Task Yeniden_adlandirma_UPSTREAM_ve_reflog_u_koruyor()
    {
        // If the upstream were lost, the next `push` would silently go somewhere else.
        using TestRepository upstream = TestRepository.CreateEmpty();
        upstream.WriteFile("a.txt", "a\n");
        upstream.Git("add", "-A");
        upstream.Git("commit", "-m", "ilk");
        upstream.Git("branch", "ozellik");

        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", upstream.Path);
        harness.Repository.Git("fetch", "-q", "origin");
        harness.Repository.Git("branch", "ozellik", "origin/ozellik");

        await harness.Writer.RenameAsync(harness.Path, "ozellik", "yeniad", Ct);

        harness.Repository
            .Git("for-each-ref", "--format=%(upstream:short)", "refs/heads/yeniad")
            .Trim()
            .ShouldBe("origin/ozellik");

        harness.Repository.Git("reflog", "show", "yeniad").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Var_olan_ada_yeniden_adlandirma_HEDEFI_EZMIYOR()
    {
        // 🔴 MEASURED: `git branch -M <existing>` destroys the target branch without any warning.
        // No force option is offered; the conflict is reported as an error.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "kaynak");
        harness.Repository.WriteFile("b.txt", "b\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("hedefin ucu");
        harness.Repository.Git("branch", "hedef");

        string hedefOnce = harness.Repository.Git("rev-parse", "hedef").Trim();

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.RenameAsync(harness.Path, "kaynak", "hedef", Ct));

        error.Kind.ShouldBe(GitFailureKind.BranchAlreadyExists);

        harness.Branches.ShouldContain("refs/heads/kaynak");
        harness.Repository.Git("rev-parse", "hedef").Trim().ShouldBe(hedefOnce);
    }

    [Fact]
    public async Task Gecersiz_yeni_ad_GIT_CAGRILMADAN_reddediliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "eski");

        await Should.ThrowAsync<ArgumentException>(
            harness.Writer.RenameAsync(harness.Path, "eski", "gecersiz ad", Ct));

        harness.Branches.ShouldContain("refs/heads/eski");
    }

    [Fact]
    public async Task Merge_edilmis_dal_siliniyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "birlesmis");

        BranchDeleteResult result = await harness.Writer.DeleteAsync(
            harness.Path, "birlesmis", cancellationToken: Ct);

        harness.Branches.ShouldNotContain("refs/heads/birlesmis");
        result.WasUnmerged.ShouldBeFalse();
        result.LastCommitId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Merge_EDILMEMIS_dal_zorlama_olmadan_SILINMIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("switch", "-c", "birlesmemis");
        harness.Repository.WriteFile("yeni.txt", "iş\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("KAYBOLABILIR IS");
        harness.Repository.Git("switch", "-");

        BranchNotMergedException error = await Should.ThrowAsync<BranchNotMergedException>(
            harness.Writer.DeleteAsync(harness.Path, "birlesmemis", cancellationToken: Ct));

        // The error MUST carry the hash needed for recovery: the user has to be able to see what
        // would be lost before choosing to force.
        error.LastCommitId.ShouldNotBeNullOrWhiteSpace();
        harness.Branches.ShouldContain("refs/heads/birlesmemis");
    }

    [Fact]
    public async Task Zorlanan_silmede_SON_COMMIT_kullaniciya_veriliyor()
    {
        // 🔴 The most important guarantee of P06-T03. MEASURED: the deleted branch's OWN reflog is
        // deleted too; a trace in the HEAD reflog only exists if that branch was worked on in THIS
        // working tree. When a branch created in a linked worktree is deleted, no reflog trace is
        // left at all. The hash is the only reliable way to recover.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("switch", "-c", "birlesmemis");
        harness.Repository.WriteFile("yeni.txt", "iş\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("KAYBOLABILIR IS");
        string beklenen = harness.Repository.Git("rev-parse", "HEAD").Trim();
        harness.Repository.Git("switch", "-");

        BranchDeleteResult result = await harness.Writer.DeleteAsync(
            harness.Path, "birlesmemis", force: true, cancellationToken: Ct);

        result.LastCommitId.ShouldBe(beklenen);
        result.WasUnmerged.ShouldBeTrue();

        // Does the returned hash actually recover it?
        harness.Repository.Git("branch", "kurtarilan", result.LastCommitId);
        harness.Repository.Git("rev-parse", "kurtarilan").Trim().ShouldBe(beklenen);
    }

    [Fact]
    public async Task Uzerinde_olunan_dal_SILINMIYOR()
    {
        using Harness harness = await CreateAsync();
        string current = harness.CurrentBranch;

        await Should.ThrowAsync<GitException>(
            harness.Writer.DeleteAsync(harness.Path, current, force: true, cancellationToken: Ct));

        harness.Branches.ShouldContain($"refs/heads/{current}");
    }

    [Fact]
    public async Task Upstream_e_merge_edilmis_dal_YANLIS_ALARM_uretmiyor()
    {
        // 🔴 MEASURED: `-d` deletes the branch even when it is merged into its UPSTREAM rather
        // than into HEAD. If we computed merged-ness ourselves with
        // `merge-base --is-ancestor … HEAD` we would raise an "unmerged" alarm for this branch —
        // while git deletes it without complaint.
        using TestRepository upstream = TestRepository.CreateEmpty();
        upstream.WriteFile("a.txt", "a\n");
        upstream.Git("add", "-A");
        upstream.Git("commit", "-m", "ilk");

        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", upstream.Path);
        harness.Repository.Git("push", "-q", "origin", "HEAD:refs/heads/ust");
        harness.Repository.Git("fetch", "-q", "origin");
        harness.Repository.Git("switch", "-c", "ust");
        harness.Repository.WriteFile("z.txt", "z\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("upstream'e gidecek");
        harness.Repository.Git("push", "-q", "origin", "ust");
        harness.Repository.Git("branch", "--set-upstream-to=origin/ust", "ust");
        harness.Repository.Git("switch", "-");

        // Not merged into HEAD, but present in its upstream → git deletes it, so we do not ask.
        BranchDeleteResult result = await harness.Writer.DeleteAsync(
            harness.Path, "ust", cancellationToken: Ct);

        result.WasUnmerged.ShouldBeFalse();
        harness.Branches.ShouldNotContain("refs/heads/ust");
    }
}
