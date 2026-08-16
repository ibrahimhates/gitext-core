using Avalonia.Controls;

namespace GitExt.UI.Diagnostics;

/// <summary>
/// Measures frame time (P09-T03).
/// </summary>
/// <remarks>
/// <para>
/// The performance budget says "60 FPS, no dropped frames" for scrolling the graph. Average FPS does
/// <b>not</b> verify that: if one frame a second took 200 ms the average would still look like 55 FPS,
/// while what the user sees is a stutter. That is why what is really reported here are
/// <see cref="WorstMilliseconds"/> and <see cref="DroppedFrames"/>.
/// </para>
/// <para>
/// The measurement uses <see cref="TopLevel.RequestAnimationFrame"/> — it fires when a frame is
/// actually composed. Measuring with a timer would miss the moments the UI freezes: in a frozen UI the
/// timer does not run either.
/// </para>
/// </remarks>
public sealed class FrameTimeMonitor : IDisposable
{
    /// <summary>60 FPS's frame budget.</summary>
    public const double TargetFrameMilliseconds = 1000.0 / 60.0;

    private readonly TopLevel _topLevel;
    private readonly int _capacity;
    private readonly Queue<double> _samples = new();

    private TimeSpan _previous;
    private bool _hasPrevious;
    private bool _running;
    private bool _disposed;

    public FrameTimeMonitor(TopLevel topLevel, int capacity = 240)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _topLevel = topLevel;
        _capacity = capacity;
    }

    /// <summary>The number of frames sampled.</summary>
    public int SampleCount => _samples.Count;

    /// <summary>The average frame time (ms).</summary>
    public double AverageMilliseconds => _samples.Count == 0 ? 0 : _samples.Average();

    /// <summary>The worst frame time (ms) — this is the number that shows a stutter.</summary>
    public double WorstMilliseconds => _samples.Count == 0 ? 0 : _samples.Max();

    /// <summary>The number of frames exceeding the budget.</summary>
    public int DroppedFrames => _samples.Count(s => s > TargetFrameMilliseconds);

    /// <summary>The FPS derived from the average.</summary>
    public double AverageFramesPerSecond =>
        AverageMilliseconds <= 0 ? 0 : 1000.0 / AverageMilliseconds;

    /// <summary>Starts the measurement. Does nothing when it is already running.</summary>
    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        _running = true;
        _hasPrevious = false;
        RequestNext();
    }

    /// <summary>Stops the measurement; the samples collected remain.</summary>
    public void Stop() => _running = false;

    public void Reset()
    {
        _samples.Clear();
        _hasPrevious = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running = false;
    }

    private void RequestNext()
    {
        if (!_running || _disposed)
        {
            return;
        }

        _topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        if (!_running || _disposed)
        {
            return;
        }

        if (_hasPrevious)
        {
            double elapsed = (now - _previous).TotalMilliseconds;

            // Negative or nonsensically large intervals: the application may have been backgrounded, or
            // the clock may have gone backwards. Reporting those as stutters would be a false alarm.
            if (elapsed > 0 && elapsed < 10_000)
            {
                Add(elapsed);
            }
        }

        _previous = now;
        _hasPrevious = true;

        RequestNext();
    }

    private void Add(double milliseconds)
    {
        _samples.Enqueue(milliseconds);

        while (_samples.Count > _capacity)
        {
            _samples.Dequeue();
        }
    }
}
