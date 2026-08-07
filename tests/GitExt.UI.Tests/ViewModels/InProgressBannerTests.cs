using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P07-T11 — yarım kalmış operasyon şeridi.
/// </summary>
/// <remarks>
/// Planın gerekçesi: <i>"Kullanıcı uygulamayı kapatıp açtığında rebase'in ortasında
/// olduğunu unutabilir. Bu banner gerçek bir kurtarıcı."</i>
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
        // 🔑 P06-T12'de yalnızca merge iptal edilebiliyordu; diğerlerinin iptali farklı
        // komutlar olduğu için bilinçli olarak dışarıda bırakılmıştı. Faz 07'de doğru
        // fiil DURUM DOSYALARINDAN seçildiği için hepsi sunulabiliyor.
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
        // Bisect'in bir `--abort`u yok; `git bisect reset` bambaşka bir şey. Yanlış
        // komutu sunmak yarım kalmış bir işi bozardı.
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
        // İptal çalışma ağacını işlem öncesine döndürüyor: çakışmaları çözerken yazılan
        // her şey gider (P06-T12'de ölçüldü).
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
        // Faz 07 servisleri verilmezse (testler, kısmi kurulum) düğmeler soluk kalmalı;
        // tıklanıp hiçbir şey olmaması daha kötü olurdu.
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
        // Menü konumları GitExtensions'ı takip ediyor (§ 9); Faz 06'da yer tutucu olarak
        // devre dışı bırakılmışlardı.
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
