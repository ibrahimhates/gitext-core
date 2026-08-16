using System.Collections.Concurrent;
using System.Diagnostics;
using GitExt.Core.Git;

namespace GitExt.Core.Diagnostics;

/// <summary>
/// Performance indicators for a running session (P09-T03).
/// </summary>
/// <remarks>
/// <para>
/// The only practical way to diagnose a "it's slow" complaint from a user is to see what's
/// happening on the machine where the slowness occurs. Benchmarks (P09-T02) measure with
/// controlled input on the developer's machine; the numbers here occur in a real repository,
/// on real hardware.
/// </para>
/// <para>
/// The collector is <b>always on</b>: the measurement cost is a few field updates per
/// command, but there's no other way to gather retrospective data once a problem shows up.
/// It's the panel itself that's hidden (P09-T03), not the collection.
/// </para>
/// </remarks>
public interface IPerformanceDiagnostics
{
    /// <summary>Names of long-running operations currently in progress.</summary>
    IReadOnlyList<string> ActiveOperations { get; }

    /// <summary>Execution statistics aggregated by command name.</summary>
    IReadOnlyList<GitCommandStatistics> CommandStatistics { get; }

    /// <summary>Current memory state.</summary>
    MemorySnapshot Memory { get; }

    /// <summary>Time elapsed since the application started.</summary>
    TimeSpan Uptime { get; }

    /// <summary>
    /// Marks a long-running operation as "active"; ends when the returned object is disposed.
    /// </summary>
    /// <remarks>
    /// The active operation list is the most direct way to diagnose a frozen UI: if the
    /// screen isn't responding and there's a <c>fetch</c> that's been sitting here for hours,
    /// the cause is obvious.
    /// </remarks>
    IDisposable TrackOperation(string name);

    /// <summary>Resets all collected counters.</summary>
    void Reset();
}

/// <summary>
/// Aggregated statistics for a single git command (grouped by subcommand name).
/// </summary>
/// <remarks>
/// Grouped by subcommand name, not the full command line: if <c>log -n 50</c> and
/// <c>log -n 100</c> were separate lines, the list would spread across hundreds of unique
/// lines and the question "which command is expensive" would become invisible.
/// </remarks>
public sealed record GitCommandStatistics
{
    public required string Name { get; init; }

    public required int Count { get; init; }

    public required TimeSpan TotalDuration { get; init; }

    public required TimeSpan MaxDuration { get; init; }

    public required int FailureCount { get; init; }

    public TimeSpan AverageDuration => Count == 0
        ? TimeSpan.Zero
        : TimeSpan.FromTicks(TotalDuration.Ticks / Count);
}

/// <summary>
/// A snapshot of memory usage.
/// </summary>
/// <param name="ManagedBytes">Heap managed by the GC.</param>
/// <param name="ProcessBytes">Working set the OS has allocated to the process.</param>
/// <param name="Gen0">Gen 0 collection count.</param>
/// <param name="Gen1">Gen 1 collection count.</param>
/// <param name="Gen2">Gen 2 collection count — this is the expensive one.</param>
public readonly record struct MemorySnapshot(
    long ManagedBytes,
    long ProcessBytes,
    int Gen0,
    int Gen1,
    int Gen2);

/// <summary>
/// Default implementation that collects statistics by listening to the git command log.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IGitCommandLog"/> was chosen as the source because, per ADR-0002, every git
/// call already passes through it — adding a separate measurement point would risk producing
/// paths where someone forgot to add it.
/// </para>
/// <para>
/// ⚠️ Records arrive from <b>pool threads</b>; all state is updated with concurrent
/// collections and <see cref="Interlocked"/>.
/// </para>
/// </remarks>
public sealed class PerformanceDiagnostics : IPerformanceDiagnostics, IDisposable
{
    private readonly IGitCommandLog _log;
    private readonly ConcurrentDictionary<string, CommandCounter> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, string> _active = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    private long _nextOperationId;
    private bool _disposed;

    public PerformanceDiagnostics(IGitCommandLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
        _log.Recorded += OnRecorded;
    }

    public IReadOnlyList<string> ActiveOperations => [.. _active.Values];

    public IReadOnlyList<GitCommandStatistics> CommandStatistics =>
        [.. _counters
            .Select(pair => pair.Value.ToStatistics(pair.Key))
            .OrderByDescending(s => s.TotalDuration)];

    public MemorySnapshot Memory
    {
        get
        {
            // forceFullCollection: false — triggering a full collection while the diagnostics
            // panel is open would change the very thing being measured and stall the UI.
            long managed = GC.GetTotalMemory(forceFullCollection: false);

            long process;
            try
            {
                using Process current = Process.GetCurrentProcess();
                process = current.WorkingSet64;
            }
            catch (PlatformNotSupportedException)
            {
                // Process info may be unreadable in restricted environments; the managed
                // number is still valuable.
                process = 0;
            }

            return new MemorySnapshot(
                managed,
                process,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));
        }
    }

    public TimeSpan Uptime => _uptime.Elapsed;

    public IDisposable TrackOperation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        long id = Interlocked.Increment(ref _nextOperationId);
        _active[id] = name;

        return new OperationScope(this, id);
    }

    public void Reset()
    {
        _counters.Clear();

        // Active operations are not cleared: they're still running, and dropping them from
        // the list would make them look finished. What's reset is the statistics of
        // completed runs.
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _log.Recorded -= OnRecorded;
    }

    private void OnRecorded(object? sender, GitCommandLogEntry entry)
    {
        string name = ExtractCommandName(entry.CommandLine);

        _counters.GetOrAdd(name, _ => new CommandCounter())
            .Add(entry.Duration, entry.IsSuccess);
    }

    /// <summary>
    /// Extracts the subcommand name from the command line: <c>git -c foo=bar log --oneline</c> → <c>log</c>.
    /// </summary>
    /// <remarks>
    /// Global options like <c>-c key=value</c> come BEFORE the subcommand; taking the first
    /// word would group these commands under <c>-c</c> and the statistics would become unreadable.
    /// </remarks>
    internal static string ExtractCommandName(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return "(unknown)";
        }

        string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];

            if (part.Length == 0 || part.Equals("git", StringComparison.Ordinal))
            {
                continue;
            }

            // `-c key=value` is two parts: the option itself and its value.
            if (part.Equals("-c", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            if (part[0] == '-')
            {
                continue;
            }

            return part;
        }

        return "(unknown)";
    }

    private void EndOperation(long id) => _active.TryRemove(id, out _);

    private sealed class OperationScope(PerformanceDiagnostics owner, long id) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.EndOperation(id);
        }
    }

    /// <summary>
    /// Counters for a single command name.
    /// </summary>
    /// <remarks>
    /// Fields are updated with <see cref="Interlocked"/>; taking a lock would turn the
    /// measurement itself into a bottleneck during a scroll where hundreds of commands run
    /// per second.
    /// </remarks>
    private sealed class CommandCounter
    {
        private long _count;
        private long _totalTicks;
        private long _maxTicks;
        private long _failures;

        public void Add(TimeSpan duration, bool success)
        {
            Interlocked.Increment(ref _count);
            Interlocked.Add(ref _totalTicks, duration.Ticks);

            if (!success)
            {
                Interlocked.Increment(ref _failures);
            }

            // Retry until the largest value is written: if someone else wrote a larger value
            // in between, it must not be overwritten.
            long ticks = duration.Ticks;
            long current = Interlocked.Read(ref _maxTicks);

            while (ticks > current)
            {
                long previous = Interlocked.CompareExchange(ref _maxTicks, ticks, current);

                if (previous == current)
                {
                    break;
                }

                current = previous;
            }
        }

        public GitCommandStatistics ToStatistics(string name) => new()
        {
            Name = name,
            Count = (int)Interlocked.Read(ref _count),
            TotalDuration = TimeSpan.FromTicks(Interlocked.Read(ref _totalTicks)),
            MaxDuration = TimeSpan.FromTicks(Interlocked.Read(ref _maxTicks)),
            FailureCount = (int)Interlocked.Read(ref _failures),
        };
    }
}

/// <summary>
/// Diagnostics that collect nothing — used in tests and when diagnostics are off.
/// </summary>
public sealed class NullPerformanceDiagnostics : IPerformanceDiagnostics
{
    public static NullPerformanceDiagnostics Instance { get; } = new();

    private NullPerformanceDiagnostics()
    {
    }

    public IReadOnlyList<string> ActiveOperations => [];

    public IReadOnlyList<GitCommandStatistics> CommandStatistics => [];

    public MemorySnapshot Memory => default;

    public TimeSpan Uptime => TimeSpan.Zero;

    public IDisposable TrackOperation(string name) => NullScope.Instance;

    public void Reset()
    {
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
