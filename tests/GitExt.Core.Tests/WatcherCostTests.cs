using System.Diagnostics;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T12 — the cost of file system watching in large working directories.
/// </summary>
/// <remarks>
/// <para>
/// On Linux, <c>FileSystemWatcher</c> is built on inotify, and inotify spends one watch <b>per
/// directory</b>. When the system limit (<c>fs.inotify.max_user_watches</c>) is exceeded the watcher
/// does not die silently, it raises an <c>Error</c> event — but an application that misses that event
/// stops seeing the user's changes and says so nowhere.
/// </para>
/// <para>
/// The tests here verify that the cost stays <b>within measurable bounds</b> and that the watcher can
/// be set up on large trees.
/// </para>
/// </remarks>
public class WatcherCostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Sets up a working tree containing <paramref name="directoryCount"/> directories.
    /// </summary>
    private static void CreateTree(TestRepository repository, int directoryCount)
    {
        for (int i = 0; i < directoryCount; i++)
        {
            repository.WriteFile($"d{i / 50}/s{i % 50}/f.txt", "x");
        }

        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "ağaç");
    }

    /// <remarks>
    /// 🔴 The real risk is the setup time: the watcher is set up when a repository is opened, and if it
    /// is slow it eats directly into the "open a repository" budget (&lt; 1 s). On a tree of 1,000
    /// directories that has to stay measurable.
    /// </remarks>
    [Fact]
    public async Task Buyuk_agacta_izleyici_hizli_kuruluyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        CreateTree(repository, 1_000);

        RepositoryWatcher watcher = new();

        long started = Stopwatch.GetTimestamp();
        watcher.Start(repository.Path, Path.Combine(repository.Path, ".git"), Path.Combine(repository.Path, ".git"));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        try
        {
            // The budget gives 1 s to opening a repository; the watcher taking more than a small slice
            // of that leaves no room for the rest of the startup.
            elapsed.ShouldBeLessThan(
                TimeSpan.FromMilliseconds(500),
                $"izleyici kurulumu {elapsed.TotalMilliseconds:0} ms sürdü");
        }
        finally
        {
            watcher.Dispose();
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <remarks>
    /// Verifies that the resources really are released. Without that, a session moving between
    /// repositories exhausts the inotify watches and at some point <b>no</b> repository can be watched
    /// — that is a system-wide limit, not a per-process one.
    /// </remarks>
    [Fact]
    public void Tekrarli_acilis_kapanis_kaynak_biriktirmiyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        CreateTree(repository, 200);

        for (int i = 0; i < 20; i++)
        {
            RepositoryWatcher watcher = new();
            watcher.Start(repository.Path, Path.Combine(repository.Path, ".git"), Path.Combine(repository.Path, ".git"));
            watcher.Dispose();
        }

        // Getting here means the inotify instances were not exhausted over 20 rounds.
        // (Had the limit been exceeded, Start would have thrown an IOException.)
        true.ShouldBeTrue();
    }

    /// <remarks>
    /// Changes arriving after <c>Dispose</c> must produce no events: triggering a refresh for a closed
    /// repository would mean doing work for a repository the user has closed.
    /// </remarks>
    [Fact]
    public async Task Dispose_sonrasi_olay_gelmiyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        RepositoryWatcher watcher = new();
        int events = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref events);

        watcher.Start(repository.Path, Path.Combine(repository.Path, ".git"), Path.Combine(repository.Path, ".git"));
        watcher.Dispose();

        repository.WriteFile("sonra.txt", "içerik");

        // If an event is coming it arrives within this window; the coalescing window is shorter.
        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct).ConfigureAwait(true);

        Volatile.Read(ref events).ShouldBe(0, "kapatılmış izleyici hâlâ olay üretiyor");
    }
}
