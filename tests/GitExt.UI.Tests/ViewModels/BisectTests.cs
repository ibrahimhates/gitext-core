using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T20 — driving a bisect from the UI.
/// </summary>
/// <remarks>
/// The rule that shapes this screen: naming the culprit does <b>not</b> end the bisect. Measured
/// against real git — the <c>BISECT_*</c> files survive the answer and HEAD stays detached, so the
/// banner is still up and the user is the one who presses <b>Stop bisect</b>.
/// </remarks>
public class BisectTests
{
    private sealed class FakeBisectWriter : IBisectWriter
    {
        public List<string> Calls { get; } = [];

        public BisectStatus Next { get; set; } = new("Bisecting: 5 revisions left to test after this "
            + "(roughly 3 steps)", RemainingRevisions: 5, RemainingSteps: 3);

        public Exception? Failure { get; set; }

        public Task<BisectStatus> StartAsync(
            string workingDirectory,
            string? badCommit = null,
            string? goodCommit = null,
            CancellationToken cancellationToken = default) =>
            Record("start");

        public Task<BisectStatus> MarkAsync(
            string workingDirectory,
            BisectVerdict verdict,
            string? commit = null,
            CancellationToken cancellationToken = default) =>
            Record(verdict.ToString().ToLowerInvariant());

        public Task<BisectStatus> ResetAsync(
            string workingDirectory,
            CancellationToken cancellationToken = default) =>
            Record("reset");

        private Task<BisectStatus> Record(string call)
        {
            Calls.Add(call);

            return Failure is not null
                ? Task.FromException<BisectStatus>(Failure)
                : Task.FromResult(Next);
        }
    }

    private static MainWindowViewModel CreateMain(
        FakeBisectWriter bisect,
        FakeInProgressOperationReader? operations = null) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(6)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            operations: operations ?? new FakeInProgressOperationReader(),
            advanced: new AdvancedOperationServices { Bisect = bisect });

    private static async Task<MainWindowViewModel> OpenedAsync(
        FakeBisectWriter bisect,
        InProgressOperation operation = InProgressOperation.None)
    {
        FakeInProgressOperationReader operations = new() { Operation = operation };
        MainWindowViewModel model = CreateMain(bisect, operations);

        await model.OpenRepositoryAsync("/tmp/depo");

        return model;
    }

    [AvaloniaFact]
    public async Task Bisect_disindayken_serit_yok()
    {
        MainWindowViewModel model = await OpenedAsync(new FakeBisectWriter());

        model.IsBisecting.ShouldBeFalse();
        model.ShowOperationBanner.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Bisect_surerken_serit_ve_dugmeler_var()
    {
        MainWindowViewModel model = await OpenedAsync(
            new FakeBisectWriter(), InProgressOperation.Bisect);

        model.IsBisecting.ShouldBeTrue();
        model.ShowOperationBanner.ShouldBeTrue();
        model.OperationText.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public async Task Bisect_surerken_ayrik_HEAD_seridi_GORUNMEZ()
    {
        // Measured: HEAD really is detached during a bisect. Two strips saying different things
        // would send the user to "create a branch here", which is the wrong move mid-search.
        MainWindowViewModel model = await OpenedAsync(
            new FakeBisectWriter(), InProgressOperation.Bisect);

        model.ShowDetachedBanner.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Baslatmak_git_bisect_start_calistiriyor()
    {
        FakeBisectWriter bisect = new();
        MainWindowViewModel model = await OpenedAsync(bisect);

        await model.BisectStartCommand.ExecuteAsync(null);

        bisect.Calls.ShouldBe(["start"]);
    }

    [AvaloniaFact]
    public async Task Saglam_bozuk_ve_atla_dogru_kelimeyi_gonderiyor()
    {
        FakeBisectWriter bisect = new();
        MainWindowViewModel model = await OpenedAsync(bisect, InProgressOperation.Bisect);

        await model.BisectMarkCommand.ExecuteAsync(BisectVerdict.Good);
        await model.BisectMarkCommand.ExecuteAsync(BisectVerdict.Bad);
        await model.BisectMarkCommand.ExecuteAsync(BisectVerdict.Skip);

        bisect.Calls.ShouldBe(["good", "bad", "skip"]);
    }

    [AvaloniaFact]
    public async Task Durdurmak_reset_calistiriyor()
    {
        FakeBisectWriter bisect = new();
        MainWindowViewModel model = await OpenedAsync(bisect, InProgressOperation.Bisect);

        await model.BisectResetCommand.ExecuteAsync(null);

        bisect.Calls.ShouldBe(["reset"]);
    }

    [AvaloniaFact]
    public async Task Ilerleme_seritte_yaziyor()
    {
        FakeBisectWriter bisect = new();
        MainWindowViewModel model = await OpenedAsync(bisect, InProgressOperation.Bisect);

        await model.BisectMarkCommand.ExecuteAsync(BisectVerdict.Good);

        model.BisectNotice.ShouldNotBeNull();
        model.BisectNotice!.ShouldContain("5");
        model.BisectNotice!.ShouldContain("3");
    }

    [AvaloniaFact]
    public async Task Ilk_bozuk_commit_bulununca_seritte_yaziyor()
    {
        FakeBisectWriter bisect = new()
        {
            Next = new BisectStatus(
                "3c34e8d7a1844835d88cc9f4415af2f8e088a7f0 is the first 'bad' commit",
                FirstBadCommit: "3c34e8d7a1844835d88cc9f4415af2f8e088a7f0"),
        };

        MainWindowViewModel model = await OpenedAsync(bisect, InProgressOperation.Bisect);

        await model.BisectMarkCommand.ExecuteAsync(BisectVerdict.Bad);

        model.BisectNotice.ShouldNotBeNull();
        model.BisectNotice!.ShouldContain("3c34e8d7a1");
    }

    [AvaloniaFact]
    public async Task Bitis_seridi_KAPATMIYOR()
    {
        // The answer is found, but git has not been reset: the strip stays and so do the buttons.
        FakeBisectWriter bisect = new()
        {
            Next = new BisectStatus("abc is the first 'bad' commit", FirstBadCommit: "abc"),
        };

        MainWindowViewModel model = await OpenedAsync(bisect, InProgressOperation.Bisect);

        await model.BisectMarkCommand.ExecuteAsync(BisectVerdict.Bad);

        model.IsBisecting.ShouldBeTrue();
        model.ShowOperationBanner.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Git_hatasi_seritte_gosteriliyor()
    {
        FakeBisectWriter bisect = new()
        {
            Failure = new GitExt.Core.Git.GitException(
                GitExt.Core.Git.GitFailureKind.Unknown,
                "hata",
                "git bisect good",
                1,
                "You need to start by \"git bisect start\""),
        };

        MainWindowViewModel model = await OpenedAsync(bisect, InProgressOperation.Bisect);

        await model.BisectMarkCommand.ExecuteAsync(BisectVerdict.Good);

        model.BisectNotice.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public async Task Bisect_yazicisi_yokken_komutlar_calismaz()
    {
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore());

        await model.OpenRepositoryAsync("/tmp/depo");

        model.BisectStartCommand.CanExecute(null).ShouldBeFalse();
        model.BisectResetCommand.CanExecute(null).ShouldBeFalse();
    }
}
