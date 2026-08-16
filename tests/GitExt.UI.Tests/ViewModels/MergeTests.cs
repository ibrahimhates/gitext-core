using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T11 + P06-T12 — the merge screen and getting out of a merge (the UI side).
/// </summary>
public class MergeTests
{
    private static (MergeViewModel Model, FakeMergeWriter Merge) Create()
    {
        FakeMergeWriter merge = new();

        return (new MergeViewModel(merge), merge);
    }

    private static Task LoadAsync(MergeViewModel model) =>
        model.LoadAsync("/depo", "main", ["main", "ozellik", "origin/main"]);

    [AvaloniaFact]
    public async Task Mevcut_dal_listede_YOK()
    {
        // Merging something into itself is meaningless; keeping it out of the list prevents the error
        // from the start.
        (MergeViewModel model, _) = Create();

        await LoadAsync(model);

        model.Sources.ShouldBe(["ozellik", "origin/main"]);
        model.CurrentBranch.ShouldBe("main");
    }

    [AvaloniaFact]
    public async Task Komut_onizlemesi_secimlerle_degisiyor()
    {
        (MergeViewModel model, _) = Create();

        await LoadAsync(model);

        model.CommandPreview.ShouldBe("git merge -- ozellik");

        model.Strategy = MergeStrategy.NoFastForward;
        model.CommandPreview.ShouldBe("git merge --no-ff -- ozellik");

        model.Strategy = MergeStrategy.Squash;
        model.CommandPreview.ShouldBe("git merge --squash -- ozellik");

        model.Strategy = MergeStrategy.NoFastForward;
        model.Message = "elle yazilmis";
        model.CommandPreview.ShouldBe("git merge --no-ff -m elle yazilmis -- ozellik");
    }

    [AvaloniaFact]
    public async Task Onizleme_ne_getirecegini_soyluyor()
    {
        (MergeViewModel model, FakeMergeWriter merge) = Create();

        merge.Preview = new MergePreview
        {
            HasChanges = true,
            CanFastForward = false,
            HasCommonAncestor = true,
            Ahead = 3,
        };

        await LoadAsync(model);

        model.HasPreviewNotice.ShouldBeTrue();
        model.PreviewNotice!.ShouldContain("3 commit");
        model.PreviewNotice!.ShouldContain("cannot fast-forward");
    }

    [AvaloniaFact]
    public async Task Ilgisiz_gecmis_onizlemede_UYARILIYOR()
    {
        (MergeViewModel model, FakeMergeWriter merge) = Create();

        merge.Preview = new MergePreview
        {
            HasChanges = true,
            CanFastForward = false,
            HasCommonAncestor = false,
        };

        await LoadAsync(model);

        model.PreviewNotice!.ShouldContain("no common ancestor");
    }

    [AvaloniaFact]
    public async Task SQUASH_secilince_ne_olacagi_ONCEDEN_yazili()
    {
        // 🔴 It has to be said afterwards as well, but saying it beforehand is better: let the user start
        // knowing what they are getting into.
        (MergeViewModel model, _) = Create();

        await LoadAsync(model);

        model.HasSquashNotice.ShouldBeFalse();

        model.Strategy = MergeStrategy.Squash;

        model.HasSquashNotice.ShouldBeTrue();
        model.SquashNotice!.ShouldContain("does NOT create a commit");
    }

    [AvaloniaFact]
    public async Task SQUASH_sonrasi_commit_gerektigi_UYARI_olarak_bildiriliyor()
    {
        // 🔴 git gives exit code 0 and HEAD stays put (measured). Saying only "prepared" would be enough
        // for the user to think they had merged.
        (MergeViewModel model, FakeMergeWriter merge) = Create();

        merge.Result = new MergeResult
        {
            Outcome = MergeOutcome.Staged,
            HeadBefore = "aaaa",
            HeadAfter = "aaaa",
        };

        await LoadAsync(model);
        model.Strategy = MergeStrategy.Squash;
        await model.RunCommand.ExecuteAsync(null);

        model.HasWarning.ShouldBeTrue();
        model.Warning!.ShouldContain("Nothing was COMMITTED");
        model.HasRecoveryCommand.ShouldBeFalse("geri alınacak bir commit yok");
    }

    [AvaloniaFact]
    public async Task Cakisma_dosya_adlariyla_bildiriliyor()
    {
        (MergeViewModel model, FakeMergeWriter merge) = Create();

        merge.Result = new MergeResult
        {
            Outcome = MergeOutcome.Conflicted,
            HeadBefore = "aaaa",
            HeadAfter = "aaaa",
            ConflictedPaths = ["a.txt", "b.txt"],
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.Warning!.ShouldContain("UNRESOLVED");
        model.Warning!.ShouldContain("a.txt");
    }

    [AvaloniaFact]
    public async Task Ileri_sarmada_GERI_ALMA_komutu_gosteriliyor()
    {
        (MergeViewModel model, FakeMergeWriter merge) = Create();

        merge.Result = new MergeResult
        {
            Outcome = MergeOutcome.FastForward,
            HeadBefore = "1234567890",
            HeadAfter = "abcdef1234",
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.RecoveryCommand.ShouldBe("git reset --hard 1234567890");
        model.Notice!.ShouldContain("Fast-forwarded");
    }

    [AvaloniaFact]
    public async Task Zaten_guncelken_geri_alma_komutu_GOSTERILMIYOR()
    {
        (MergeViewModel model, FakeMergeWriter merge) = Create();

        merge.Result = new MergeResult
        {
            Outcome = MergeOutcome.AlreadyUpToDate,
            HeadBefore = "aaaa",
            HeadAfter = "aaaa",
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasRecoveryCommand.ShouldBeFalse();
        model.Notice.ShouldBe("Already up to date.");
    }

    // ------------------------------------------------------- ana pencere

    private static MainWindowViewModel CreateMain(
        FakeMergeWriter merge,
        FakeMergePrompt? prompt = null,
        FakeMergeAbortConfirmer? confirmer = null) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(FakeGitData.Refs(
                    localBranches:
                    [
                        FakeGitData.LocalBranch("main", FakeGitData.Sha(2), isCurrent: true),
                        FakeGitData.LocalBranch("ozellik", FakeGitData.Sha(1)),
                    ],
                    remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(2))])),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            mergeWriter: merge)
        {
            MergePrompt = prompt ?? new FakeMergePrompt(),
            MergeAbortConfirmer = confirmer,
        };

    [AvaloniaFact]
    public async Task Depo_acikken_MENU_etkin_ve_ekran_DOLU_aciliyor()
    {
        FakeMergePrompt prompt = new();
        MainWindowViewModel model = CreateMain(new FakeMergeWriter(), prompt);

        model.CanMerge.ShouldBeFalse("depo açılmadan etkin olmamalı");

        await model.OpenRepositoryAsync("/depo");

        model.CanMerge.ShouldBeTrue();
        await model.MergeCommand.ExecuteAsync(null);

        prompt.Shown.ShouldNotBeNull();

        // Remote branches can be merged too (§ 9: GitExtensions' list includes both as well).
        prompt.Shown!.Sources.ShouldBe(["ozellik", "origin/main"]);
        prompt.Shown.CurrentBranch.ShouldBe("main");
    }

    [AvaloniaFact]
    public async Task IPTAL_dugmesi_yalnizca_MERGE_surerken_gorunuyor()
    {
        // Aborting a rebase/cherry-pick takes different commands; offering the wrong one would break a
        // half-finished job.
        FakeInProgressOperationReader operations = new();
        FakeMergeWriter merge = new();

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            operations: operations,
            mergeWriter: merge)
        {
            MergePrompt = new FakeMergePrompt(),
        };

        operations.Operation = InProgressOperation.Rebase;
        await model.OpenRepositoryAsync("/depo");
        model.CanAbortMerge.ShouldBeFalse();

        operations.Operation = InProgressOperation.Merge;
        await model.OpenRepositoryAsync("/depo");
        model.CanAbortMerge.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Iptal_ONAYSIZ_yapilmiyor()
    {
        // `merge --abort` returns the working tree to its pre-merge state: everything written while
        // resolving conflicts is lost (measured).
        FakeMergeWriter merge = new();
        FakeMergeAbortConfirmer confirmer = new() { Answer = false };

        MainWindowViewModel model = CreateMain(merge, confirmer: confirmer);

        await model.OpenRepositoryAsync("/depo");
        await model.AbortMergeCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldBeTrue();
        merge.Aborted.ShouldBe(0, "onaylanmadan iptal edilmemeli");

        confirmer.Answer = true;
        await model.AbortMergeCommand.ExecuteAsync(null);

        merge.Aborted.ShouldBe(1);
        model.BranchNotice!.ShouldContain("aborted");
    }

    // ---------------------------------------------------------- layout

    [AvaloniaFact]
    public async Task Yerlesim_FormMergeBranch_sirasiyla_ayni()
    {
        (MergeViewModel model, _) = Create();
        await LoadAsync(model);

        MergeWindow window = new() { DataContext = model, Width = 580, Height = 620 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        static double Top(Window host, Control control) =>
            control.TranslatePoint(default, host)?.Y
            ?? throw new InvalidOperationException($"'{control.Name}' görsel ağaçta değil.");

        double source = Top(window, window.GetControl<ComboBox>("SourceBox"));
        double current = Top(window, window.GetControl<TextBox>("CurrentBranchBox"));
        double fastForward = Top(window, window.GetControl<RadioButton>("FastForwardRadio"));
        double mergeCommit = Top(window, window.GetControl<RadioButton>("MergeCommitRadio"));
        double noCommit = Top(window, window.GetControl<CheckBox>("NoCommitBox"));
        double advanced = Top(window, window.GetControl<ToggleButton>("ShowAdvancedToggle"));
        double merge = Top(window, window.GetControl<Button>("MergeButton"));

        source.ShouldBeLessThan(current);
        current.ShouldBeLessThan(fastForward);
        fastForward.ShouldBeLessThan(mergeCommit);
        mergeCommit.ShouldBeLessThan(noCommit);
        noCommit.ShouldBeLessThan(advanced);
        advanced.ShouldBeLessThan(merge);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Gelismis_panel_baslangicta_GIZLI_ve_squash_orada()
    {
        (MergeViewModel model, _) = Create();
        await LoadAsync(model);

        MergeWindow window = new() { DataContext = model, Width = 580, Height = 620 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetControl<StackPanel>("AdvancedPanel").IsVisible.ShouldBeFalse();

        window.GetControl<ToggleButton>("ShowAdvancedToggle").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        window.GetControl<StackPanel>("AdvancedPanel").IsVisible.ShouldBeTrue();
        window.GetControl<CheckBox>("SquashBox").IsEnabled.ShouldBeTrue();

        // In place but disabled (§ 9): Phase 07's subject.
        window.GetControl<CheckBox>("CustomStrategyBox").IsEnabled.ShouldBeFalse();
        window.GetControl<CheckBox>("AddLogMessagesBox").IsEnabled.ShouldBeFalse();

        window.Close();
    }
}
