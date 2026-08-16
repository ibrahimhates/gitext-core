using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P07-T11 — the half-finished operation strip.
/// </summary>
/// <remarks>
/// The plan's reasoning: <i>"When the user closes the application and reopens it they can forget they
/// are in the middle of a rebase. This banner is a genuine rescue."</i>
/// </remarks>
public class InProgressBannerTests
{
    private static MainWindowViewModel CreateMain(
        FakeInProgressOperationReader operations,
        AdvancedOperationServices? advanced = null,
        IMergeAbortConfirmer? confirmer = null) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            operations: operations,
            advanced: advanced)
        {
            MergeAbortConfirmer = confirmer ?? new FakeMergeAbortConfirmer { Answer = true },
            ConflictPrompt = new FakeConflictPrompt(),
        };

    private static AdvancedOperationServices Advanced(FakeConflictResolver resolver) => new()
    {
        Resolver = resolver,
        Conflicts = new FakeConflictReader([]),
    };

    [AvaloniaTheory]
    [InlineData(InProgressOperation.Rebase)]
    [InlineData(InProgressOperation.CherryPick)]
    [InlineData(InProgressOperation.Revert)]
    [InlineData(InProgressOperation.Merge)]
    [InlineData(InProgressOperation.ApplyMailbox)]
    public async Task HER_yarim_islemden_CIKIS_yolu_var(InProgressOperation operation)
    {
        // 🔑 In P06-T12 only a merge could be aborted; the others were deliberately left out because
        // aborting them takes different commands. In Phase 07, because the right verb is chosen FROM THE
        // STATE FILES, all of them can be offered.
        FakeInProgressOperationReader operations = new();
        FakeConflictResolver resolver = new(operation, []);

        MainWindowViewModel model = CreateMain(operations, Advanced(resolver));

        operations.Operation = operation;
        await model.OpenRepositoryAsync("/depo");

        model.ShowOperationBanner.ShouldBeTrue();
        model.CanAbortOperation.ShouldBeTrue();
        model.OperationText.ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public async Task Islem_yokken_serit_GORUNMUYOR()
    {
        FakeInProgressOperationReader operations = new();
        FakeConflictResolver resolver = new(InProgressOperation.None, []);

        MainWindowViewModel model = CreateMain(operations, Advanced(resolver));

        operations.Operation = InProgressOperation.None;
        await model.OpenRepositoryAsync("/depo");

        model.ShowOperationBanner.ShouldBeFalse();
        model.CanAbortOperation.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task BISECT_icin_iptal_SUNULMUYOR()
    {
        // Bisect has no `--abort`; `git bisect reset` is a different thing entirely. Offering the wrong
        // command would break a half-finished job.
        FakeInProgressOperationReader operations = new();
        FakeConflictResolver resolver = new(InProgressOperation.Bisect, []);

        MainWindowViewModel model = CreateMain(operations, Advanced(resolver));

        operations.Operation = InProgressOperation.Bisect;
        await model.OpenRepositoryAsync("/depo");

        model.ShowOperationBanner.ShouldBeTrue("kullanıcı bisect'te olduğunu bilmeli");
        model.CanAbortOperation.ShouldBeFalse("ama iptal komutu yok");
    }

    [AvaloniaFact]
    public async Task Iptal_ONAYSIZ_yapilmiyor()
    {
        // Aborting returns the working tree to its pre-operation state: everything written while
        // resolving conflicts is lost (measured in P06-T12).
        FakeInProgressOperationReader operations = new();
        FakeConflictResolver resolver = new(InProgressOperation.Rebase, []);
        FakeMergeAbortConfirmer confirmer = new() { Answer = false };

        MainWindowViewModel model = CreateMain(operations, Advanced(resolver), confirmer);

        operations.Operation = InProgressOperation.Rebase;
        await model.OpenRepositoryAsync("/depo");
        await model.AbortOperationCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldBeTrue();
        resolver.Aborted.ShouldBeFalse("onaylanmadan iptal edilmemeli");

        confirmer.Answer = true;
        await model.AbortOperationCommand.ExecuteAsync(null);

        resolver.Aborted.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Cakisma_cozum_ekrani_SERITTEN_acilabiliyor()
    {
        FakeInProgressOperationReader operations = new();
        FakeConflictResolver resolver = new(InProgressOperation.Merge, ["f.txt"]);

        MainWindowViewModel model = CreateMain(operations, Advanced(resolver));

        operations.Operation = InProgressOperation.Merge;
        await model.OpenRepositoryAsync("/depo");

        model.CanResolveConflicts.ShouldBeTrue();

        await model.ResolveConflictsCommand.ExecuteAsync(null);

        ((FakeConflictPrompt)model.ConflictPrompt!).Shown.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task Servis_YOKKEN_komutlar_ETKIN_DEGIL()
    {
        // When the Phase 07 services are not supplied (tests, a partial setup) the buttons must stay
        // dimmed; being clickable and doing nothing would be worse.
        FakeInProgressOperationReader operations = new();

        MainWindowViewModel model = CreateMain(operations);

        operations.Operation = InProgressOperation.Rebase;
        await model.OpenRepositoryAsync("/depo");

        model.CanAbortOperation.ShouldBeFalse();
        model.CanResolveConflicts.ShouldBeFalse();
        model.CanShowStash.ShouldBeFalse();
        model.CanShowReflog.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Serit_ogeleri_ANA_PENCEREDE_yerinde()
    {
        FakeInProgressOperationReader operations = new();
        FakeConflictResolver resolver = new(InProgressOperation.Rebase, []);

        MainWindowViewModel model = CreateMain(operations, Advanced(resolver));

        operations.Operation = InProgressOperation.Rebase;
        await model.OpenRepositoryAsync("/depo");

        MainWindow window = new() { DataContext = model, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetControl<Border>("OperationBanner").IsVisible.ShouldBeTrue();
        window.GetControl<Button>("AbortOperationButton").ShouldNotBeNull();
        window.GetControl<Button>("ResolveConflictsButton").ShouldNotBeNull();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Faz07_menu_ogeleri_ETKIN()
    {
        // The menu positions follow GitExtensions (§ 9); they were disabled as placeholders in Phase 06.
        FakeInProgressOperationReader operations = new();

        MainWindowViewModel model = CreateMain(
            operations,
            new AdvancedOperationServices
            {
                Stash = new FakeStashWriter([]),
                Reflog = new FakeReflogReader([]),
                Rebase = new FakeRebaseWriter([]),
                Sequencer = new FakeSequencerWriter(),
                Reset = new FakeResetWriter(new ResetPreview { IsTargetValid = true }),
            });

        model.StashPrompt = new FakeStashPrompt();
        model.ReflogPrompt = new FakeReflogPrompt();
        model.RebasePrompt = new FakeRebasePrompt();

        await model.OpenRepositoryAsync("/depo");

        model.CanShowStash.ShouldBeTrue();
        model.CanShowReflog.ShouldBeTrue();
        model.CanRebase.ShouldBeTrue();
    }

    private sealed class FakeConflictPrompt : IConflictPrompt
    {
        public ConflictViewModel? Shown { get; private set; }

        public Task ShowAsync(ConflictViewModel model)
        {
            Shown = model;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStashPrompt : IStashPrompt
    {
        public Task ShowAsync(StashViewModel model) => Task.CompletedTask;
    }

    private sealed class FakeReflogPrompt : IReflogPrompt
    {
        public Task ShowAsync(ReflogViewModel model) => Task.CompletedTask;
    }

    private sealed class FakeRebasePrompt : IRebasePrompt
    {
        public Task ShowAsync(RebaseViewModel model) => Task.CompletedTask;
    }
}
