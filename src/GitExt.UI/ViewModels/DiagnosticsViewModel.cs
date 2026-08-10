using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core.Diagnostics;
using GitExt.UI.Diagnostics;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Teşhis tablosundaki tek bir komut satırı (P09-T03).
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
/// Performans teşhis paneli (P09-T03).
/// </summary>
/// <remarks>
/// <para>
/// Panel <b>gizli</b>: normal kullanıcı için gürültü, ama "yavaş" şikâyeti geldiğinde
/// tek pratik teşhis yolu. Toplama her zaman açık (bkz. <see cref="IPerformanceDiagnostics"/>);
/// gizli olan yalnızca gösterim.
/// </para>
/// <para>
/// Sayılar bir zamanlayıcıyla tazeleniyor. Canlı olay akışı yerine yoklama seçildi çünkü
/// bellek ve kare süresi zaten sürekli değişen büyüklükler — her değişimde arayüzü
/// güncellemek, teşhis panelinin kendisini yavaşlığın kaynağı yapardı.
/// </para>
/// </remarks>
public sealed class DiagnosticsViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// Tazeleme aralığı. Bir saniyenin altına inmek panelin kendi maliyetini görünür kılar.
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

        // Headless testlerde zamanlayıcı kurmak gereksiz: testler Refresh'i doğrudan
        // çağırıyor ve zamanlayıcı, kapanmış bir pencereyi canlı tutabilir.
        if (Dispatcher.UIThread.CheckAccess())
        {
            _timer = new DispatcherTimer { Interval = RefreshInterval };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        Refresh();
    }

    /// <summary>Komut istatistikleri, toplam süreye göre azalan.</summary>
    public ObservableCollection<CommandStatisticsRowViewModel> Commands { get; } = [];

    /// <summary>O an devam eden uzun işler.</summary>
    public ObservableCollection<string> ActiveOperations { get; } = [];

    public bool HasActiveOperations => ActiveOperations.Count > 0;

    public bool IsEmpty => Commands.Count == 0;

    /// <summary>Toplam git çağrısı sayısı.</summary>
    public int TotalCommandCount { get; private set; }

    /// <summary>Toplam git süresi.</summary>
    public string TotalCommandDuration { get; private set; } = "0 ms";

    public string ManagedMemory { get; private set; } = "—";

    public string ProcessMemory { get; private set; } = "—";

    /// <summary>Nesil bazında GC toplama sayıları.</summary>
    public string Collections { get; private set; } = "—";

    public string Uptime { get; private set; } = "—";

    /// <summary>Kare ölçümü var mı? Yoksa ilgili satırlar gizleniyor.</summary>
    public bool HasFrameStatistics => _frames is not null;

    public string AverageFrameTime { get; private set; } = "—";

    public string WorstFrameTime { get; private set; } = "—";

    public string DroppedFrames { get; private set; } = "—";

    /// <summary>
    /// Kare bütçesi aşıldı mı? Arayüzde uyarı olarak gösteriliyor.
    /// </summary>
    public bool HasDroppedFrames { get; private set; }

    public IRelayCommand ResetCommand { get; }

    public IRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Bütün göstergeleri yeniden okur.
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
    /// Bayt sayısını okunabilir birime çevirir.
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
    /// Çalışma süresini okunabilir biçime çevirir.
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

/// <summary>Teşhis panelini gösteren taraf (P09-T03).</summary>
/// <remarks>
/// ViewModel değil <see cref="IPerformanceDiagnostics"/> alıyor: kare ölçümü ana pencereye
/// bağlanmak zorunda ve o pencere yalnızca gösterim tarafında biliniyor.
/// </remarks>
public interface IDiagnosticsPrompt
{
    Task ShowAsync(IPerformanceDiagnostics diagnostics);
}
