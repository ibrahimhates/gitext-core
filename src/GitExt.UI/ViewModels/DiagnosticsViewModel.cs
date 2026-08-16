using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core.Diagnostics;
using GitExt.UI.Diagnostics;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A single command row in the diagnostics table (P09-T03).
/// </summary>
public sealed class CommandStatisticsRowViewModel
{
    public required GitCommandStatistics Statistics { get; init; }

    public string Name => Statistics.Name;

    public int Count => Statistics.Count;

    public string Total => Format(Statistics.TotalDuration);

    public string Average => Format(Statistics.AverageDuration);

    public string Max => Format(Statistics.MaxDuration);

    public int FailureCount => Statistics.FailureCount;

    public bool HasFailures => Statistics.FailureCount > 0;

    private static string Format(TimeSpan value) => value.TotalSeconds >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{value.TotalSeconds:0.00} sn")
        : string.Create(CultureInfo.InvariantCulture, $"{value.TotalMilliseconds:0} ms");
}

/// <summary>
/// The performance diagnostics panel (P09-T03).
/// </summary>
/// <remarks>
/// <para>
/// The panel is <b>hidden</b>: noise for a normal user, but the only practical route to a diagnosis
/// when a "it's slow" complaint arrives. Collection is always on (see
/// <see cref="IPerformanceDiagnostics"/>); what is hidden is only the display.
/// </para>
/// <para>
/// The numbers are refreshed on a timer. Polling was chosen over a live event stream because memory
/// and frame time are continuously changing quantities anyway — updating the UI on every change would
/// make the diagnostics panel itself the source of the slowness.
/// </para>
/// </remarks>
public sealed class DiagnosticsViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// The refresh interval. Going below a second makes the panel's own cost visible.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IPerformanceDiagnostics _diagnostics;
    private readonly FrameTimeMonitor? _frames;
    private readonly DispatcherTimer? _timer;

    private bool _disposed;

    public DiagnosticsViewModel(IPerformanceDiagnostics diagnostics, FrameTimeMonitor? frames = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        _diagnostics = diagnostics;
        _frames = frames;

        ResetCommand = new RelayCommand(Reset);
        RefreshCommand = new RelayCommand(Refresh);

        _frames?.Start();

        // Setting up a timer is unnecessary in headless tests: the tests call Refresh directly, and a
        // timer can keep a closed window alive.
        if (Dispatcher.UIThread.CheckAccess())
        {
            _timer = new DispatcherTimer { Interval = RefreshInterval };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        Refresh();
    }

    /// <summary>The command statistics, descending by total time.</summary>
    public ObservableCollection<CommandStatisticsRowViewModel> Commands { get; } = [];

    /// <summary>The long-running operations currently in progress.</summary>
    public ObservableCollection<string> ActiveOperations { get; } = [];

    public bool HasActiveOperations => ActiveOperations.Count > 0;

    public bool IsEmpty => Commands.Count == 0;

    /// <summary>The total number of git calls.</summary>
    public int TotalCommandCount { get; private set; }

    /// <summary>The total git time.</summary>
    public string TotalCommandDuration { get; private set; } = "0 ms";

    public string ManagedMemory { get; private set; } = "—";

    public string ProcessMemory { get; private set; } = "—";

    /// <summary>The GC collection counts per generation.</summary>
    public string Collections { get; private set; } = "—";

    public string Uptime { get; private set; } = "—";

    /// <summary>Is there a frame measurement? Without one, the related rows are hidden.</summary>
    public bool HasFrameStatistics => _frames is not null;

    public string AverageFrameTime { get; private set; } = "—";

    public string WorstFrameTime { get; private set; } = "—";

    public string DroppedFrames { get; private set; } = "—";

    /// <summary>
    /// Was the frame budget exceeded? Shown as a warning in the UI.
    /// </summary>
    public bool HasDroppedFrames { get; private set; }

    public IRelayCommand ResetCommand { get; }

    public IRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Re-reads all the indicators.
    /// </summary>
    public void Refresh()
    {
        RefreshCommands();
        RefreshActiveOperations();
        RefreshMemory();
        RefreshFrames();

        Uptime = FormatUptime(_diagnostics.Uptime);
        OnPropertyChanged(nameof(Uptime));
    }

    private void RefreshCommands()
    {
        Commands.Clear();

        int totalCount = 0;
        TimeSpan totalDuration = TimeSpan.Zero;

        foreach (GitCommandStatistics statistics in _diagnostics.CommandStatistics)
        {
            Commands.Add(new CommandStatisticsRowViewModel { Statistics = statistics });

            totalCount += statistics.Count;
            totalDuration += statistics.TotalDuration;
        }

        TotalCommandCount = totalCount;
        TotalCommandDuration = totalDuration.TotalSeconds >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{totalDuration.TotalSeconds:0.00} sn")
            : string.Create(CultureInfo.InvariantCulture, $"{totalDuration.TotalMilliseconds:0} ms");

        OnPropertyChanged(nameof(TotalCommandCount));
        OnPropertyChanged(nameof(TotalCommandDuration));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RefreshActiveOperations()
    {
        ActiveOperations.Clear();

        foreach (string operation in _diagnostics.ActiveOperations)
        {
            ActiveOperations.Add(operation);
        }

        OnPropertyChanged(nameof(HasActiveOperations));
    }

    private void RefreshMemory()
    {
        MemorySnapshot memory = _diagnostics.Memory;

        ManagedMemory = FormatBytes(memory.ManagedBytes);
        ProcessMemory = memory.ProcessBytes > 0 ? FormatBytes(memory.ProcessBytes) : "—";
        Collections = string.Create(
            CultureInfo.InvariantCulture,
            $"Gen0 {memory.Gen0} · Gen1 {memory.Gen1} · Gen2 {memory.Gen2}");

        OnPropertyChanged(nameof(ManagedMemory));
        OnPropertyChanged(nameof(ProcessMemory));
        OnPropertyChanged(nameof(Collections));
    }

    private void RefreshFrames()
    {
        if (_frames is null || _frames.SampleCount == 0)
        {
            return;
        }

        AverageFrameTime = string.Create(
            CultureInfo.InvariantCulture,
            $"{_frames.AverageMilliseconds:0.0} ms ({_frames.AverageFramesPerSecond:0} FPS)");

        WorstFrameTime = string.Create(CultureInfo.InvariantCulture, $"{_frames.WorstMilliseconds:0.0} ms");

        int dropped = _frames.DroppedFrames;
        DroppedFrames = string.Create(CultureInfo.InvariantCulture, $"{dropped} / {_frames.SampleCount}");
        HasDroppedFrames = dropped > 0;

        OnPropertyChanged(nameof(AverageFrameTime));
        OnPropertyChanged(nameof(WorstFrameTime));
        OnPropertyChanged(nameof(DroppedFrames));
        OnPropertyChanged(nameof(HasDroppedFrames));
    }

    private void Reset()
    {
        _diagnostics.Reset();
        _frames?.Reset();
        Refresh();
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    /// <summary>
    /// Converts a byte count into a readable unit.
    /// </summary>
    internal static string FormatBytes(long bytes)
    {
        const long kilobyte = 1024;
        const long megabyte = kilobyte * 1024;
        const long gigabyte = megabyte * 1024;

        return bytes switch
        {
            >= gigabyte => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)gigabyte:0.00} GB"),
            >= megabyte => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)megabyte:0.0} MB"),
            >= kilobyte => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)kilobyte:0} KB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
        };
    }

    /// <summary>
    /// Converts the uptime into a readable form.
    /// </summary>
    internal static string FormatUptime(TimeSpan value) => value.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours} sa {value.Minutes} dk")
        : value.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalMinutes} dk {value.Seconds} sn")
            : string.Create(CultureInfo.InvariantCulture, $"{value.Seconds} sn");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_timer is not null)
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
        }

        _frames?.Dispose();
    }
}

/// <summary>The side that shows the diagnostics panel (P09-T03).</summary>
/// <remarks>
/// It takes an <see cref="IPerformanceDiagnostics"/> rather than the ViewModel: the frame measurement
/// has to attach to the main window, and that window is known only on the display side.
/// </remarks>
public interface IDiagnosticsPrompt
{
    Task ShowAsync(IPerformanceDiagnostics diagnostics);
}
