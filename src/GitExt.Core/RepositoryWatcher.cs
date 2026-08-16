namespace GitExt.Core;

/// <summary>
/// Data carried when a change is detected in the repository.
/// </summary>
public sealed class RepositoryChangedEventArgs : EventArgs
{
    public RepositoryChangedEventArgs(RepositoryChangeKind kind) => Kind = kind;

    public RepositoryChangeKind Kind { get; }
}

/// <summary>
/// Watches the working tree and git directory and reports changes (P05-T14).
/// </summary>
public interface IRepositoryWatcher : IDisposable
{
    /// <summary>
    /// Fired when a coalesced change is detected.
    /// Called on the <b>timer thread, not the UI thread</b>.
    /// </summary>
    event EventHandler<RepositoryChangedEventArgs>? Changed;

    /// <summary>Is watching active? Can shut itself down automatically on error.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts watching the given repository. Any previous watch is stopped first.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if watching could be set up. <b>Does not throw</b> — automatic
    /// refresh is a convenience; if it couldn't be set up, the app keeps working with manual
    /// refresh.
    /// </returns>
    bool Start(string workingTreeRoot, string gitDirectory, string commonDirectory);

    /// <summary>Stops watching and discards any pending change.</summary>
    void Stop();

    /// <summary>
    /// Temporarily suspends watching; resumes when the returned object is disposed.
    /// </summary>
    /// <remarks>
    /// Used during our own write operations: stage/commit already does its own refresh, and
    /// having the watcher trigger the same work again would just be a pointless
    /// <c>git status</c>. Can be called nested.
    /// </remarks>
    IDisposable Suspend();
}

/// <inheritdoc cref="IRepositoryWatcher"/>
/// <remarks>
/// <para>
/// <b>⚠️ MEASURED — two separate watchers are needed.</b> In a normal repository <c>.git</c>
/// is under the working tree and one watcher is enough. In a linked working tree
/// (<c>git worktree</c>) and in a submodule, the git directory is <b>elsewhere</b>; the
/// second watcher is only set up in that case, otherwise every event would arrive twice.
/// </para>
/// <para>
/// <b>⚠️ MEASURED — <c>EnableRaisingEvents</c> can throw.</b> On Linux each watcher consumes
/// one <c>inotify</c> instance, and the per-user limit on this machine is 1024; measurement
/// hit an <b><c>IOException</c> on the 949th watcher</b>. If not wrapped, the app would crash
/// while opening a repository. The number of watched directories is also a direct cost: an
/// 11,512-directory tree measured 11,512 <c>inotify</c> watches, 104 ms setup time, and
/// ~30 MB of memory.
/// </para>
/// </remarks>
public sealed class RepositoryWatcher : IRepositoryWatcher
{
    /// <summary>Quiet period expected after the last event.</summary>
    public static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound after the first pending event.</summary>
    public static readonly TimeSpan DefaultMaximumDelay = TimeSpan.FromSeconds(2);

    /// <summary>Shortest time between two refreshes.</summary>
    public static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Periodic refresh done as a safety net against missed events.
    /// </summary>
    /// <remarks>
    /// <b>⚠️ MEASURED:</b> intermediate events can be lost in a deep directory tree created
    /// quickly — when the two levels <c>new/deep</c> were created in a single call,
    /// <c>Created</c> for <c>deep</c> never arrived (the directory already existed by the
    /// time the watch was added). The loss is far more common on network file systems and
    /// under WSL. Periodic refresh closes this gap; its frequency is low because the watcher
    /// is the primary path.
    /// </remarks>
    public static readonly TimeSpan DefaultPeriodicInterval = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly ChangeCoalescer _coalescer;
    private readonly TimeProvider _time;
    private readonly TimeSpan _periodicInterval;
    private readonly ITimer _timer;
    private readonly ITimer _periodicTimer;

    private FileSystemWatcher? _workTreeWatcher;
    private FileSystemWatcher? _gitDirectoryWatcher;
    private FileSystemWatcher? _commonDirectoryWatcher;
    private int _suspendCount;
    private bool _disposed;

    public RepositoryWatcher(
        TimeSpan? debounceDelay = null,
        TimeSpan? maximumDelay = null,
        TimeSpan? minimumInterval = null,
        TimeSpan? periodicInterval = null,
        TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _coalescer = new ChangeCoalescer(
            debounceDelay ?? DefaultDebounceDelay,
            maximumDelay ?? DefaultMaximumDelay,
            minimumInterval ?? DefaultMinimumInterval);

        _periodicInterval = periodicInterval ?? DefaultPeriodicInterval;

        _timer = _time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _periodicTimer = _time.CreateTimer(
            _ => OnRawChange(RepositoryChangeKind.Repository), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<RepositoryChangedEventArgs>? Changed;

    public bool IsRunning
    {
        get { lock (_gate) { return _workTreeWatcher is not null; } }
    }

    public bool Start(string workingTreeRoot, string gitDirectory, string commonDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingTreeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(commonDirectory);

        Stop();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            string root = Path.GetFullPath(workingTreeRoot);
            string gitDir = Path.GetFullPath(gitDirectory);
            string commonDir = Path.GetFullPath(commonDirectory);

            try
            {
                _workTreeWatcher = CreateWatcher(
                    root, RepositoryChangeClassifier.ClassifyWorkingTreePath);

                // If the git directory is UNDER the working tree it's already watched; the
                // second watcher is only needed for a linked working tree / submodule.
                if (!IsInside(gitDir, root))
                {
                    _gitDirectoryWatcher = CreateWatcher(
                        gitDir, RepositoryChangeClassifier.ClassifyGitDirectoryPath);
                }

                // ⚠️ In a linked working tree, refs live here; NOT in the git directory. If
                // only the git directory were watched, commits in that tree would never be
                // seen (measured).
                if (!PathsEqual(commonDir, gitDir) && !IsInside(commonDir, root) && !IsInside(commonDir, gitDir))
                {
                    _commonDirectoryWatcher = CreateWatcher(
                        commonDir, RepositoryChangeClassifier.ClassifyGitDirectoryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // inotify limit, permission error, or a deleted directory. Automatic refresh
                // is a convenience; it must not crash the app.
                DisposeWatchers();
                return false;
            }

            _periodicTimer.Change(_periodicInterval, _periodicInterval);
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            DisposeWatchers();
            _coalescer.Reset();
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _periodicTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public IDisposable Suspend()
    {
        lock (_gate)
        {
            _suspendCount++;
        }

        return new Suspension(this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeWatchers();
        }

        _timer.Dispose();
        _periodicTimer.Dispose();
    }

    private static bool IsInside(string candidate, string root)
    {
        string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.Ordinal);

    /// <summary>
    /// Sets up a <see cref="FileSystemWatcher"/> watching a directory.
    /// </summary>
    /// <param name="root">
    /// Directory to watch. Event paths are made <b>relative</b> to this before being passed to
    /// <paramref name="classifier"/>.
    /// </param>
    /// <param name="classifier">Rule that classifies the relative path.</param>
    /// <remarks>
    /// The root is captured in the <b>closure</b> rather than a field: the watcher and the
    /// root are born and die together, so keeping a separate field would mean an unnecessary
    /// lock acquisition per event — a single branch switch produces 2102 events (measured).
    /// The closure only captures one <see cref="string"/>.
    /// </remarks>
    private FileSystemWatcher CreateWatcher(
        string root,
        Func<string, RepositoryChangeKind?> classifier)
    {
        void Handle(string fullPath)
        {
            if (classifier(Path.GetRelativePath(root, fullPath)) is { } kind)
            {
                OnRawChange(kind);
            }
        }

        FileSystemWatcher watcher = new(root)
        {
            IncludeSubdirectories = true,

            // LastWrite + Size: content changes. FileName + DirectoryName: creation, deletion,
            // renaming. Ref updates arrive as an `x.lock → x` rename, so commits would be
            // missed without DirectoryName/FileName.
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
        };

        watcher.Changed += (_, e) => Handle(e.FullPath);
        watcher.Created += (_, e) => Handle(e.FullPath);
        watcher.Deleted += (_, e) => Handle(e.FullPath);

        // The NEW name is used on rename: a ref update's source is `refs/heads/x.lock`, its
        // target `refs/heads/x`. Using the old name would let the lock filter eat the real signal.
        watcher.Renamed += (_, e) => Handle(e.FullPath);

        // If the event queue overflows, which files were missed is unknown; the only correct
        // answer is to re-read everything.
        watcher.Error += (_, _) => OnRawChange(RepositoryChangeKind.Repository);

        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnRawChange(RepositoryChangeKind kind)
    {
        TimeSpan wait;

        lock (_gate)
        {
            if (_disposed || _workTreeWatcher is null)
            {
                return;
            }

            wait = _coalescer.Add(kind, _time.GetUtcNow());

            if (_suspendCount > 0)
            {
                // No timer is set while suspended; it will be set on resume.
                return;
            }

            _timer.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        RepositoryChangeKind? kind;

        lock (_gate)
        {
            if (_disposed || _workTreeWatcher is null || _suspendCount > 0)
            {
                return;
            }

            kind = _coalescer.TryTake(_time.GetUtcNow(), out TimeSpan? wait);

            if (kind is null)
            {
                if (wait is not null)
                {
                    _timer.Change(wait.Value, Timeout.InfiniteTimeSpan);
                }

                return;
            }
        }

        // The event is fired OUTSIDE the lock: the subscriber will refresh, and that refresh
        // will produce new events; holding the lock would make event handlers block each other.
        Changed?.Invoke(this, new RepositoryChangedEventArgs(kind.Value));
    }

    private void Resume()
    {
        lock (_gate)
        {
            if (_suspendCount > 0)
            {
                _suspendCount--;
            }

            if (_suspendCount > 0 || _disposed || _workTreeWatcher is null)
            {
                return;
            }

            if (_coalescer.HasPending)
            {
                _timer.Change(_coalescer.DebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void DisposeWatchers()
    {
        _workTreeWatcher?.Dispose();
        _gitDirectoryWatcher?.Dispose();
        _commonDirectoryWatcher?.Dispose();
        _workTreeWatcher = null;
        _gitDirectoryWatcher = null;
        _commonDirectoryWatcher = null;
    }

    private sealed class Suspension : IDisposable
    {
        private RepositoryWatcher? _owner;

        public Suspension(RepositoryWatcher owner) => _owner = owner;

        public void Dispose()
        {
            RepositoryWatcher? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Resume();
        }
    }
}
