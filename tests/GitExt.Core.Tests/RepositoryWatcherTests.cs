using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// The watcher's behaviour against <b>a real file system and real <c>git</c></b> (P05-T14).
/// </summary>
/// <remarks>
/// <para>
/// The rules are tested purely in <see cref="RepositoryChangeClassifierTests"/>; what is tested here
/// is <b>the whole chain</b>: whether the expected event actually arrives when git runs a command.
/// The classifier can be right while the watcher hands it the wrong path, and that can only be caught
/// here.
/// </para>
/// <para>
/// The tests wait on real time; the delays were kept short for that reason.
/// </para>
/// </remarks>
public class RepositoryWatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Maximum = TimeSpan.FromSeconds(1);

    private static RepositoryWatcher CreateWatcher() =>
        new(debounceDelay: Debounce,
            maximumDelay: Maximum,
            minimumInterval: TimeSpan.Zero,
            periodicInterval: Timeout.InfiniteTimeSpan);

    private sealed class Recorder
    {
        private readonly List<RepositoryChangeKind> _events = [];
        private readonly Lock _gate = new();

        public void Attach(IRepositoryWatcher watcher) =>
            watcher.Changed += (_, e) => { lock (_gate) { _events.Add(e.Kind); } };

        public IReadOnlyList<RepositoryChangeKind> Events
        {
            get { lock (_gate) { return [.. _events]; } }
        }

        public void Clear() { lock (_gate) { _events.Clear(); } }

        /// <summary>Waits until the expected number of events has arrived.</summary>
        public async Task<bool> WaitAsync(int count, int timeoutMs = 5000)
        {
            for (int waited = 0; waited < timeoutMs; waited += 25)
            {
                if (Events.Count >= count)
                {
                    return true;
                }

                await Task.Delay(25, Ct);
            }

            return false;
        }

        /// <summary>Waits until an event OF THE GIVEN KIND has arrived.</summary>
        /// <remarks>
        /// 🔴 REGRESSION (Windows CI): counting is not enough when the test performs two actions
        /// that produce different kinds. Writing a file and then committing was expected to arrive
        /// coalesced into a single <c>Repository</c> event; on the slower Windows file system the
        /// working-tree event won the debounce window on its own, <c>WaitAsync(1)</c> came back
        /// satisfied, and the assertion ran before the commit's event existed. What the test cares
        /// about is the kind, so that is what is waited for.
        /// </remarks>
        public async Task<bool> WaitForAsync(RepositoryChangeKind kind, int timeoutMs = 5000)
        {
            for (int waited = 0; waited < timeoutMs; waited += 25)
            {
                if (Events.Contains(kind))
                {
                    return true;
                }

                await Task.Delay(25, Ct);
            }

            return false;
        }
    }

    private static bool Start(RepositoryWatcher watcher, TestRepository repository)
    {
        string gitDirectory = Path.Combine(repository.Path, ".git");
        return watcher.Start(repository.Path, gitDirectory, gitDirectory);
    }

    [Fact]
    public async Task Calisma_agacindaki_dosya_degisimi_tazeleme_tetikler()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        repository.WriteFile("yeni.txt", "içerik");

        (await recorder.WaitAsync(1)).ShouldBeTrue();
        recorder.Events[0].ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public async Task HARICI_commit_depo_tazelemesi_tetikler()
    {
        // 🔴 The test for the finding that corrected the plan. MEASURED: `git commit` produces 64
        // events and ALL of them are under .git — zero in the working tree. Had the plan's
        // "filter out .git" instruction been applied literally, this test would be red and a commit
        // made in another terminal would never show on screen.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        repository.WriteFile("harici.txt", "içerik");
        repository.Commit("harici commit");

        (await recorder.WaitForAsync(RepositoryChangeKind.Repository)).ShouldBeTrue();
    }

    [Fact]
    public async Task Dal_olusturma_depo_tazelemesi_tetikler()
    {
        // Only the ref is written; the working tree is not touched.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        repository.Git("branch", "yeni-dal");

        (await recorder.WaitAsync(1)).ShouldBeTrue();
        recorder.Events.ShouldContain(RepositoryChangeKind.Repository);
    }

    [Fact]
    public async Task Gecmis_okumak_hicbir_olay_uretmez()
    {
        // The test for the lock filter: `git log` must not trigger a refresh even when it touches
        // temporary files under .git.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        for (int i = 0; i < 6; i++)
        {
            repository.Git("log", "-n", "1", "--format=%H");
            repository.Git("rev-parse", "HEAD");
            await Task.Delay(100, Ct);
        }

        await Task.Delay(500, Ct);

        recorder.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Kendi_okumamiz_askidayken_HIC_olay_uretmez()
    {
        // 🔴 The real door to the endless loop. ⚠️ MEASURED and WORSE THAN EXPECTED: `git status`
        // rewrites the index (`index.lock → index`) not only on the first run but on CONSECUTIVE runs
        // throughout the "racily clean" window — so a command believed to be read-only produces a
        // refresh signal. In a freshly written repository, 5 out of 6 consecutive reads produced an
        // event.
        //
        // The lock filter does not close this (the rename's target is `index`, not the lock). The only
        // thing that closes it is the refresh path running under suspension; the ViewModel side does
        // exactly that (`AutoRefreshTests`).
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        using (watcher.Suspend())
        {
            for (int i = 0; i < 6; i++)
            {
                repository.Git("status", "--porcelain=v2", "--branch");
                repository.Git("log", "-n", "1", "--format=%H");
                await Task.Delay(100, Ct);
            }

            await Task.Delay(500, Ct);

            recorder.Events.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Taslak_yazimi_tazeleme_tetiklemez()
    {
        // P05-T13's draft is written to `.git/GITEXT_COMMITMESSAGE`. Unless it were filtered out, every
        // save while the user writes a commit message would trigger a refresh.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        string draft = Path.Combine(repository.Path, ".git", CommitMessageStore.DraftFileName);

        for (int i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(draft, $"taslak {i}", Ct);
            await Task.Delay(30, Ct);
        }

        await Task.Delay(600, Ct);

        recorder.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cok_sayida_degisiklik_TEK_tazelemede_birlesir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        for (int i = 0; i < 200; i++)
        {
            repository.WriteFile($"dosya{i}.txt", "içerik");
        }

        (await recorder.WaitAsync(1)).ShouldBeTrue();
        await Task.Delay(400, Ct);

        // 200 files must cause at most two refreshes (a second one if it coincides with the upper
        // bound window).
        recorder.Events.Count.ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task Askiya_alinmisken_tetiklenmez_devam_edince_tetiklenir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        IDisposable suspension = watcher.Suspend();

        repository.WriteFile("askida.txt", "içerik");
        await Task.Delay(500, Ct);

        recorder.Events.ShouldBeEmpty();

        suspension.Dispose();

        // A change accumulated during suspension must not be lost: once our own write finishes we also
        // need to see the changes that came from outside.
        (await recorder.WaitAsync(1)).ShouldBeTrue();
    }

    [Fact]
    public async Task Durdurulunca_tetiklenmez()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();
        watcher.IsRunning.ShouldBeTrue();

        watcher.Stop();
        watcher.IsRunning.ShouldBeFalse();

        repository.WriteFile("kapali.txt", "içerik");
        await Task.Delay(500, Ct);

        recorder.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bagli_calisma_agacinda_git_dizini_AYRICA_izlenir()
    {
        // ⚠️ In a linked working tree the git directory is OUTSIDE the working tree. Unless a second
        // watcher is set up, commits in that working tree are never seen.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using TestRepository linked = repository.AddWorkTree("ikinci-dal");
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        string gitDirectory = linked.Git("rev-parse", "--absolute-git-dir").Trim();
        string commonDirectory = ResolveCommonDirectory(linked);

        Path.GetFullPath(gitDirectory)
            .StartsWith(Path.GetFullPath(linked.Path) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            .ShouldBeFalse("bağlı çalışma ağacının git dizini ağacın dışında olmalı");

        // ⚠️ Two separate directories: HEAD and the index are in this tree's own directory, the REFS
        // are in the common one. Were only the git directory watched, the ref update from a commit here
        // would be missed.
        commonDirectory.ShouldNotBe(gitDirectory);

        watcher.Start(linked.Path, gitDirectory, commonDirectory).ShouldBeTrue();

        linked.WriteFile("baglidosya.txt", "içerik");
        linked.Commit("bağlı ağaçta commit");

        (await recorder.WaitForAsync(RepositoryChangeKind.Repository)).ShouldBeTrue();
        recorder.Events.ShouldContain(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Var_olmayan_dizin_istisna_yerine_false_dondurur()
    {
        // Automatic refresh is a convenience; when it cannot be set up the application must carry on
        // working with manual refresh.
        using RepositoryWatcher watcher = CreateWatcher();

        string missing = Path.Combine(Path.GetTempPath(), $"yok-{Guid.NewGuid():N}");

        watcher.Start(missing, missing, missing).ShouldBeFalse();
        watcher.IsRunning.ShouldBeFalse();
    }

    /// <remarks>
    /// <c>--git-common-dir</c> returns a RELATIVE path in a normal repository (CLAUDE.md § 5, item 8);
    /// it is resolved by hand against the working directory.
    /// </remarks>
    private static string ResolveCommonDirectory(TestRepository repository)
    {
        string value = repository.Git("rev-parse", "--git-common-dir").Trim();

        return Path.GetFullPath(
            Path.IsPathRooted(value) ? value : Path.Combine(repository.Path, value));
    }
}
