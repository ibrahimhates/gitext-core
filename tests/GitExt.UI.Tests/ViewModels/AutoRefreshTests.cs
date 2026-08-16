using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T14 — how the ViewModel side reacts to file system watching.
/// </summary>
/// <remarks>
/// The watcher itself is tested against real <c>git</c> in <c>RepositoryWatcherTests</c>; what is
/// tested here is <b>what happens when an event arrives</b>.
/// </remarks>
public class AutoRefreshTests
{
    private static FileStatus Unstaged(string path) =>
        new() { Path = RepositoryPath.Parse(path), UnstagedChange = FileChangeKind.Modified };

    private static MainWindowViewModel CreateMain(FakeRepositoryWatcher watcher) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            watcher: watcher);

    private static async Task<(WorkingTreeViewModel Model, FakeStatusReader Status)> CreateWorkingTreeAsync(
        FakeRepositoryWatcher watcher)
    {
        FakeStatusReader status = new([Unstaged("a.txt")]);

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()),
            watcher: watcher);

        await model.OpenAsync("/tmp/depo");

        return (model, status);
    }

    /// <summary>The event is posted to the <c>Dispatcher</c>; waits for the queue to drain.</summary>
    /// <param name="until">
    /// The expected outcome; once it holds we exit early. If not given, the queue is only drained.
    /// </param>
    private static async Task DrainAsync(Func<bool>? until = null)
    {
        for (int i = 0; i < 40; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            if (until?.Invoke() == true)
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    [AvaloniaFact]
    public async Task Depo_acilinca_izleme_baslar()
    {
        FakeRepositoryWatcher watcher = new();
        MainWindowViewModel model = CreateMain(watcher);

        await model.OpenRepositoryAsync("/tmp/depo");

        watcher.IsRunning.ShouldBeTrue();
        watcher.WorkingTreeRoot.ShouldBe("/tmp/depo");

        // ⚠️ All three paths are given: in a linked worktree the refs live in the common directory,
        // while HEAD and the index live in that worktree's own git directory (CLAUDE.md § 5, item 9).
        watcher.GitDirectory.ShouldNotBeNullOrEmpty();
        watcher.CommonDirectory.ShouldNotBeNullOrEmpty();
    }

    [AvaloniaFact]
    public async Task Depo_kapaninca_izleme_durur()
    {
        FakeRepositoryWatcher watcher = new();
        MainWindowViewModel model = CreateMain(watcher);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.CloseRepository();

        watcher.IsRunning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Ref_degisimi_commit_listesini_tazeler()
    {
        // A commit made in another terminal: the user must not have to refresh by hand.
        FakeRepositoryWatcher watcher = new();
        MainWindowViewModel model = CreateMain(watcher);

        await model.OpenRepositoryAsync("/tmp/depo");

        int before = watcher.StartCount;

        watcher.Raise(RepositoryChangeKind.Repository);
        await DrainAsync(() => watcher.StartCount > before);

        // A refresh reopens the repository; the watch is set up again as well.
        watcher.StartCount.ShouldBeGreaterThan(before);
    }

    [AvaloniaFact]
    public async Task Calisma_agaci_degisimi_commit_listesini_tazelemez()
    {
        // 🔴 Re-reading the commit history on every file save would, at the measured times
        // (git/git 2.1 s · Linux 31.6 s), make the application unusable.
        FakeRepositoryWatcher watcher = new();
        MainWindowViewModel model = CreateMain(watcher);

        await model.OpenRepositoryAsync("/tmp/depo");

        int before = watcher.StartCount;

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync();

        watcher.StartCount.ShouldBe(before);
    }

    [AvaloniaFact]
    public async Task Calisma_agaci_degisimi_commit_ekranini_tazeler()
    {
        FakeRepositoryWatcher watcher = new();
        (WorkingTreeViewModel model, FakeStatusReader status) = await CreateWorkingTreeAsync(watcher);

        int before = status.ReadCallCount;

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync(() => status.ReadCallCount > before);

        status.ReadCallCount.ShouldBeGreaterThan(before);

        model.Dispose();
    }

    [AvaloniaFact]
    public async Task Otomatik_tazeleme_YAZILAN_MESAJI_silmez()
    {
        // 🔴 The invariant from P05-T13: no background event overwrites text the user typed.
        // The user did not ask for this refresh — losing the commit message because some file
        // changed is unacceptable.
        FakeRepositoryWatcher watcher = new();
        (WorkingTreeViewModel model, _) = await CreateWorkingTreeAsync(watcher);

        model.Message.Text = "yarım kalmış mesaj";

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync();

        model.Message.Text.ShouldBe("yarım kalmış mesaj");

        model.Dispose();
    }

    [AvaloniaFact]
    public async Task Tazeleme_sirasinda_izleme_ASKIYA_ALINIR()
    {
        // 🔴 The ViewModel-side gate against the infinite loop. MEASURED: the first `git status`
        // in a repository rewrites the index — the refresh itself gives birth to a new event.
        // Without the suspension this chain would feed itself.
        FakeRepositoryWatcher watcher = new();
        FakeStatusReader status = new([Unstaged("a.txt")]);
        bool suspendedDuringRead = false;

        status.OnRead = () => suspendedDuringRead |= watcher.IsSuspended;

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()),
            watcher: watcher);

        await model.OpenAsync("/tmp/depo");

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync(() => suspendedDuringRead);

        suspendedDuringRead.ShouldBeTrue();

        model.Dispose();
    }

    [AvaloniaFact]
    public async Task Pencere_kapaninca_abonelik_birakilir()
    {
        // The watcher lives for the lifetime of the application; a subscription that is not
        // released would keep running `git status` for a screen that is already closed.
        FakeRepositoryWatcher watcher = new();
        (WorkingTreeViewModel model, FakeStatusReader status) = await CreateWorkingTreeAsync(watcher);

        model.Dispose();

        int after = status.ReadCallCount;

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync();

        status.ReadCallCount.ShouldBe(after);
    }

    [AvaloniaFact]
    public void Izleyici_verilmeden_de_calisir()
    {
        // Auto-refresh is a convenience; the watcher may have failed to start (inotify limit,
        // permission error) and the application must still open in that case.
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(1)),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore());

        Should.NotThrow(model.CloseRepository);
    }
}
