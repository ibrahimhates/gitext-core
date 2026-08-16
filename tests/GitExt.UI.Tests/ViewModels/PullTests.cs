using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T06 + P06-T07 — the Pull/Fetch screen (the ViewModel side).
/// </summary>
public class PullTests
{
    private const string Path = "/depo";

    private static (PullViewModel Model, FakeFetchWriter Fetch, FakePullWriter Pull) Create(
        ResolvedPullStrategy? configured = null)
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });
        remotes.Remotes.Add(new GitRemote { Name = "upstream", FetchUrls = ["https://example.com/b.git"] });

        FakeFetchWriter fetch = new();
        FakePullWriter pull = new();

        if (configured is not null)
        {
            pull.Configured = configured;
        }

        return (new PullViewModel(remotes, fetch, pull), fetch, pull);
    }

    /// <remarks>
    /// ⚠️ The symbolic <c>origin/HEAD</c> is in the list deliberately: git abbreviates it not as
    /// <c>origin/HEAD</c> but as <b><c>origin</c></b> (measured in P03-T12) and the fake data reflects
    /// that — if the filtering is wrong, the test must turn red.
    /// </remarks>
    private static IReadOnlyList<GitRef> Branches() =>
    [
        FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1)).Ref,
        FakeGitData.RemoteBranch("origin/dev", FakeGitData.Sha(2)).Ref,
        FakeGitData.SymbolicRemoteHead("origin", "refs/remotes/origin/main", FakeGitData.Sha(1)).Ref,
        FakeGitData.RemoteBranch("upstream/main", FakeGitData.Sha(3)).Ref,
    ];

    private static Task LoadAsync(PullViewModel model) =>
        model.LoadAsync(Path, "main", Branches());

    [AvaloniaFact]
    public async Task Ekran_kullanicinin_AYARIYLA_aciliyor_ve_sebebi_yazili()
    {
        // 🔑 Opening a screen with merge selected for a user whose setting is rebase would silently make
        // them do something they did not expect.
        (PullViewModel model, _, _) = Create(
            new ResolvedPullStrategy(PullStrategy.Rebase, PullStrategySource.BranchSetting, "true"));

        await LoadAsync(model);

        model.Action.ShouldBe(PullAction.Rebase);
        model.IsRebase.ShouldBeTrue();
        model.StrategyNotice.ShouldNotBeNull();
        model.StrategyNotice!.ShouldContain("branch.main.rebase");
    }

    [AvaloniaFact]
    public async Task Ayar_yokken_birlestirme_ve_bu_da_YAZILI()
    {
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);

        model.Action.ShouldBe(PullAction.Merge);
        model.StrategyNotice!.ShouldContain("default");
    }

    [AvaloniaFact]
    public async Task Komut_onizlemesi_secimlerle_birlikte_degisiyor()
    {
        // The "show the command" principle: the text is produced from the actual selections.
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);

        model.CommandPreview.ShouldBe("git pull --no-rebase origin main");

        model.Action = PullAction.Rebase;
        model.AutoStash = true;
        model.CommandPreview.ShouldBe("git pull --rebase --autostash origin main");

        model.Prune = true;
        model.PruneTags = true;
        model.Tags = FetchTagMode.None;
        model.CommandPreview.ShouldBe(
            "git pull --rebase --autostash --prune --prune-tags --no-tags origin main");

        model.Action = PullAction.FetchOnly;
        model.CommandPreview.ShouldBe("git fetch --prune --prune-tags --no-tags origin");
    }

    [AvaloniaFact]
    public async Task Uzak_dal_listesi_secili_remote_a_gore_SUZULUYOR()
    {
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);

        model.SelectedRemote.ShouldBe("origin");

        // `origin/HEAD` is symbolic — it must not be in the list (the lesson of P03-T12 and P06-T06).
        model.RemoteBranches.ShouldBe(["main", "dev"]);
        model.SelectedBranch.ShouldBe("main");

        model.SelectedRemote = "upstream";
        model.RemoteBranches.ShouldBe(["main"]);
    }

    [AvaloniaFact]
    public async Task PruneTags_yalnizca_Prune_isaretliyken_ACIK()
    {
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);

        model.CanPruneTags.ShouldBeFalse();

        model.Prune = true;
        model.CanPruneTags.ShouldBeTrue();
        model.PruneTags = true;

        // When prune is turned off the sub-option has to go off too: git does not accept `--prune-tags`
        // on its own.
        model.Prune = false;
        model.PruneTags.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Yalnizca_getir_secildiginde_FETCH_calisiyor_pull_DEGIL()
    {
        (PullViewModel model, FakeFetchWriter fetch, FakePullWriter pull) = Create();

        await LoadAsync(model);

        model.Action = PullAction.FetchOnly;
        model.Prune = true;
        await model.RunCommand.ExecuteAsync(null);

        pull.Pulled.ShouldBeEmpty();
        fetch.Fetched.Single().Remote.ShouldBe("origin");
        fetch.Fetched.Single().Prune.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Fetch_kismi_basarisizligi_UYARI_olarak_gosteriliyor()
    {
        // 🔴 With `--all`, when one remote is broken the others still come through; staying silent will
        // not do.
        (PullViewModel model, FakeFetchWriter fetch, _) = Create();

        fetch.Result = new FetchResult
        {
            Changes = [new RefChange("refs/remotes/origin/main", "a", "b", RefChangeKind.Updated)],
            Failures = [new FetchFailure("bozuk", "Uzak depoya ulaşılamadı.")],
        };

        await LoadAsync(model);
        model.Action = PullAction.FetchOnly;
        await model.RunCommand.ExecuteAsync(null);

        model.HasWarning.ShouldBeTrue();
        model.Warning!.ShouldContain("bozuk");
        model.Notice!.ShouldContain("updated");
    }

    [AvaloniaFact]
    public async Task Pull_secimleri_oldugu_gibi_gecirilyor()
    {
        (PullViewModel model, _, FakePullWriter pull) = Create();

        await LoadAsync(model);

        model.Action = PullAction.Rebase;
        model.AutoStash = true;
        model.SelectedBranch = "dev";
        await model.RunCommand.ExecuteAsync(null);

        PullOptions options = pull.Pulled.Single();
        options.Strategy.ShouldBe(PullStrategy.Rebase);
        options.AutoStash.ShouldBeTrue();
        options.Remote.ShouldBe("origin");
        options.Branch.ShouldBe("dev");
    }

    [AvaloniaFact]
    public async Task Cakisma_UYARI_olarak_gosteriliyor()
    {
        (PullViewModel model, _, FakePullWriter pull) = Create();

        pull.Result = new PullResult
        {
            Strategy = new ResolvedPullStrategy(PullStrategy.Merge, PullStrategySource.UserChoice, null),
            HeadBefore = "aaaa",
            HeadAfter = "bbbb",
            HasConflicts = true,
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasWarning.ShouldBeTrue();
        model.Warning!.ShouldContain("UNRESOLVED");
    }

    [AvaloniaFact]
    public async Task AUTOSTASH_cakismasi_AYRI_bir_metinle_anlatiliyor()
    {
        // 🔴 The two situations tell the user to do different things: here the pull SUCCEEDED, and what
        // conflicted is the user's own uncommitted change, which is sitting in the stash.
        (PullViewModel model, _, FakePullWriter pull) = Create();

        pull.Result = new PullResult
        {
            Strategy = new ResolvedPullStrategy(PullStrategy.Rebase, PullStrategySource.UserChoice, null),
            HeadBefore = "aaaa",
            HeadAfter = "bbbb",
            HasConflicts = true,
            AutoStashConflict = true,
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.Warning!.ShouldContain("stash");
        model.Warning!.ShouldNotContain("Birleştirme çakışmayla durdu");
    }

    [AvaloniaFact]
    public async Task HEAD_ilerlediyse_GERI_ALMA_komutu_gosteriliyor()
    {
        (PullViewModel model, _, FakePullWriter pull) = Create();

        pull.Result = new PullResult
        {
            Strategy = new ResolvedPullStrategy(PullStrategy.Merge, PullStrategySource.UserChoice, null),
            HeadBefore = "1234567890",
            HeadAfter = "abcdef1234",
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasRecoveryCommand.ShouldBeTrue();
        model.RecoveryCommand.ShouldBe("git reset --hard 1234567890");
    }

    [AvaloniaFact]
    public async Task Zaten_guncelken_geri_alma_komutu_GOSTERILMIYOR()
    {
        // If nothing changed, saying "undo" would leave the user in doubt.
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasRecoveryCommand.ShouldBeFalse();
        model.Notice.ShouldBe("Already up to date.");
    }

    [AvaloniaFact]
    public async Task Depo_acikken_MENU_komutu_etkin_ve_ekran_DOLU_aciliyor()
    {
        // The menu wiring: with a dependency missing, the item would silently stay dead
        // (the same class of bug `MainWindowBindingTests` caught).
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePullPrompt prompt = new();

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(FakeGitData.Refs(
                    localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(2), isCurrent: true)],
                    remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(2))])),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            remoteReader: remotes,
            fetchWriter: new FakeFetchWriter(),
            pullWriter: new FakePullWriter())
        {
            PullPrompt = prompt,
        };

        model.CanPull.ShouldBeFalse("depo açılmadan etkin olmamalı");

        await model.OpenRepositoryAsync("/depo");

        model.CanPull.ShouldBeTrue();
        await model.PullCommand.ExecuteAsync(null);

        prompt.Shown.ShouldNotBeNull();
        prompt.Shown!.Remotes.ShouldBe(["origin"]);
        prompt.Shown.CurrentBranch.ShouldBe("main");
        prompt.Shown.RemoteBranches.ShouldBe(["main"]);
    }

    [AvaloniaFact]
    public async Task Remote_secilmeden_calistirilamaz()
    {
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);
        model.CanRun.ShouldBeTrue();

        model.SelectedRemote = null;
        model.CanRun.ShouldBeFalse();
    }
}
