namespace GitExt.Core;

/// <summary>
/// Coalesces file system events into a single refresh (P05-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately separated from the timer.</b> All that lives here is the "how long should we wait
/// now" calculation; the real timer is on the <see cref="RepositoryWatcher"/> side. That way the
/// coalescing rules can be tested deterministically without any real time passing — otherwise every
/// test would have to sleep for hundreds of milliseconds and would break on a slow machine.
/// </para>
/// <para>
/// <b>⚠️ MEASURED — three numbers shaped this design:</b>
/// </para>
/// <list type="bullet">
///   <item>A branch switch touching 800 files produced <b>2102 events</b>, all within about 50 ms.
///     Without a delay, 2102 <c>git status</c> runs would follow.</item>
///   <item>Even saving a single file is <b>2 events</b>, and the atomic save editors do (temporary
///     file plus rename) is <b>4 events</b>.</item>
///   <item>A single-project <c>dotnet build</c> produced <b>92 events</b> over <b>1.5 seconds</b> —
///     all under <c>obj/</c>, which git ignores. Under that kind of continuous noise a pure "reset the
///     counter on every event" debounce <b>never fires</b>; hence the <see cref="MaximumDelay"/> upper
///     bound.</item>
/// </list>
/// </remarks>
public sealed class ChangeCoalescer
{
    private readonly Lock _gate = new();

    private RepositoryChangeKind? _pending;
    private DateTimeOffset _firstPendingAt;
    private DateTimeOffset _lastEventAt;
    private DateTimeOffset _lastTakenAt = DateTimeOffset.MinValue;

    /// <param name="debounceDelay">The quiet period to wait for after the last event.</param>
    /// <param name="maximumDelay">
    /// The longest wait after the first pending event. Keeps the refresh from being deferred forever
    /// while events keep arriving.
    /// </param>
    /// <param name="minimumInterval">
    /// The shortest time between two refreshes. It overrides <see cref="MaximumDelay"/> as well: in a
    /// noisy repository this is what the upper bound should be.
    /// </param>
    public ChangeCoalescer(
        TimeSpan debounceDelay,
        TimeSpan maximumDelay,
        TimeSpan minimumInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(debounceDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDelay, debounceDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumInterval, TimeSpan.Zero);

        DebounceDelay = debounceDelay;
        MaximumDelay = maximumDelay;
        MinimumInterval = minimumInterval;
    }

    public TimeSpan DebounceDelay { get; }

    public TimeSpan MaximumDelay { get; }

    public TimeSpan MinimumInterval { get; }

    /// <summary>Is there a pending change?</summary>
    public bool HasPending
    {
        get { lock (_gate) { return _pending is not null; } }
    }

    /// <summary>
    /// Records an event and returns how long to wait before firing.
    /// </summary>
    /// <remarks>
    /// When events of different kinds are coalesced, <b>the broader one wins</b>: if a working tree
    /// change and a ref change fall into the same window, the full refresh covering both is performed.
    /// </remarks>
    public TimeSpan Add(RepositoryChangeKind kind, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_pending is null)
            {
                _pending = kind;
                _firstPendingAt = now;
            }
            else if (kind == RepositoryChangeKind.Repository)
            {
                _pending = RepositoryChangeKind.Repository;
            }

            _lastEventAt = now;
            return WaitTime(now);
        }
    }

    /// <summary>
    /// Takes the pending change when its time has come and resets the state.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="wait">
    /// How long to wait before trying again when the time has not come;
    /// <see langword="null"/> when there is no pending change.
    /// </param>
    public RepositoryChangeKind? TryTake(DateTimeOffset now, out TimeSpan? wait)
    {
        lock (_gate)
        {
            if (_pending is null)
            {
                wait = null;
                return null;
            }

            TimeSpan remaining = WaitTime(now);

            if (remaining > TimeSpan.Zero)
            {
                wait = remaining;
                return null;
            }

            RepositoryChangeKind taken = _pending.Value;
            _pending = null;
            _lastTakenAt = now;
            wait = null;
            return taken;
        }
    }

    /// <summary>
    /// Discards the pending change. Used when the repository is closed or watching is stopped.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _pending = null;
        }
    }

    private TimeSpan WaitTime(DateTimeOffset now)
    {
        // The debounce counts FROM THE LAST EVENT: the aim is to answer the question "have the events
        // stopped". Counted from the first event it would be a delay, not waiting for quiet.
        TimeSpan wait = DebounceDelay - (now - _lastEventAt);

        // The upper bound: the time since the first pending event.
        TimeSpan cap = MaximumDelay - (now - _firstPendingAt);

        if (wait > cap)
        {
            wait = cap;
        }

        // The lower bound: the time since the last refresh. It OVERRIDES the upper bound — during a
        // build that writes continuously, this is the only thing limiting the refresh rate.
        if (_lastTakenAt != DateTimeOffset.MinValue)
        {
            TimeSpan floor = MinimumInterval - (now - _lastTakenAt);

            if (wait < floor)
            {
                wait = floor;
            }
        }

        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }
}
