using System.Collections.Concurrent;
using System.Diagnostics;
using GitExt.Core.Git;

namespace GitExt.Core.Diagnostics;

/// <summary>
/// Çalışan bir oturumun performans göstergeleri (P09-T03).
/// </summary>
/// <remarks>
/// <para>
/// Kullanıcıdan gelen "yavaş" şikâyetini teşhis etmenin tek pratik yolu, yavaşlığın
/// yaşandığı makinede ne olup bittiğini görebilmek. Benchmark'lar (P09-T02) geliştirici
/// makinesinde kontrollü girdiyle ölçüyor; buradaki sayılar gerçek depoda, gerçek
/// donanımda oluşuyor.
/// </para>
/// <para>
/// Toplayıcı <b>her zaman açık</b>: ölçüm maliyeti komut başına birkaç alan güncellemesi,
/// ama sorun ortaya çıktığında geriye dönük veri toplamanın başka yolu yok. Panelin
/// kendisi gizli (P09-T03), toplama değil.
/// </para>
/// </remarks>
public interface IPerformanceDiagnostics
{
    /// <summary>O an devam eden uzun işlerin adları.</summary>
    IReadOnlyList<string> ActiveOperations { get; }

    /// <summary>Komut adına göre toplanmış çalıştırma istatistikleri.</summary>
    IReadOnlyList<GitCommandStatistics> CommandStatistics { get; }

    /// <summary>Anlık bellek durumu.</summary>
    MemorySnapshot Memory { get; }

    /// <summary>Uygulamanın başlamasından bu yana geçen süre.</summary>
    TimeSpan Uptime { get; }

    /// <summary>
    /// Uzun süren bir işi "aktif" olarak işaretler; dönen nesne <c>Dispose</c> edilince biter.
    /// </summary>
    /// <remarks>
    /// Aktif iş listesi donmuş bir arayüzü teşhis etmenin en doğrudan yolu: ekran yanıt
    /// vermiyorsa ve burada saatlerdir duran bir <c>fetch</c> varsa, sebep bellidir.
    /// </remarks>
    IDisposable TrackOperation(string name);

    /// <summary>Toplanan tüm sayıları sıfırlar.</summary>
    void Reset();
}

/// <summary>
/// Tek bir git komutunun (alt komut adına göre) toplu istatistiği.
/// </summary>
/// <remarks>
/// Alt komut adına göre gruplanıyor, tam komut satırına göre değil: <c>log -n 50</c> ile
/// <c>log -n 100</c> ayrı satırlar olsaydı liste yüzlerce benzersiz satıra dağılır ve
/// "hangi komut pahalı" sorusu görünmez olurdu.
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
/// Bellek kullanımının bir anlık görüntüsü.
/// </summary>
/// <param name="ManagedBytes">GC'nin yönettiği yığın.</param>
/// <param name="ProcessBytes">İşletim sisteminin sürece ayırdığı çalışma kümesi.</param>
/// <param name="Gen0">Gen 0 toplama sayısı.</param>
/// <param name="Gen1">Gen 1 toplama sayısı.</param>
/// <param name="Gen2">Gen 2 toplama sayısı — pahalı olan bu.</param>
public readonly record struct MemorySnapshot(
    long ManagedBytes,
    long ProcessBytes,
    int Gen0,
    int Gen1,
    int Gen2);

/// <summary>
/// Git komut günlüğünü dinleyerek istatistik toplayan varsayılan uygulama.
/// </summary>
/// <remarks>
/// <para>
/// Kaynak olarak <see cref="IGitCommandLog"/> seçildi çünkü ADR-0002 gereği her git
/// çağrısı zaten oradan geçiyor — ayrı bir ölçüm noktası eklemek, eklenmeyi unutulan
/// yollar üretirdi.
/// </para>
/// <para>
/// ⚠️ Kayıtlar <b>havuz iş parçacıklarından</b> geliyor; bütün durum eşzamanlı
/// koleksiyonlarda ve <see cref="Interlocked"/> ile güncelleniyor.
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
            // forceFullCollection: false — teşhis paneli açıkken tam toplama tetiklemek
            // ölçtüğü şeyi değiştirir ve arayüzü duraklatır.
            long managed = GC.GetTotalMemory(forceFullCollection: false);

            long process;
            try
            {
                using Process current = Process.GetCurrentProcess();
                process = current.WorkingSet64;
            }
            catch (PlatformNotSupportedException)
            {
                // Kısıtlı ortamlarda süreç bilgisi okunamayabilir; yönetilen sayı yine değerli.
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

        // Aktif işler silinmiyor: hâlâ çalışıyorlar ve listeden düşerlerse bittikleri
        // sanılır. Sıfırlanan, tamamlanmış çalıştırmaların istatistiği.
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
    /// Komut satırından alt komut adını çıkarır: <c>git -c foo=bar log --oneline</c> → <c>log</c>.
    /// </summary>
    /// <remarks>
    /// <c>-c key=value</c> gibi genel seçenekler alt komuttan ÖNCE geliyor; ilk kelimeyi
    /// almak bu komutları <c>-c</c> diye gruplardı ve istatistik okunmaz olurdu.
    /// </remarks>
    internal static string ExtractCommandName(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return "(bilinmiyor)";
        }

        string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];

            if (part.Length == 0 || part.Equals("git", StringComparison.Ordinal))
            {
                continue;
            }

            // `-c key=value` iki parça: seçeneğin kendisi ve değeri.
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

        return "(bilinmiyor)";
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
    /// Tek bir komut adının sayaçları.
    /// </summary>
    /// <remarks>
    /// Alanlar <see cref="Interlocked"/> ile güncelleniyor; kilit almak, saniyede yüzlerce
    /// komut çalışan bir kaydırma sırasında ölçümün kendisini darboğaza çevirirdi.
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

            // En büyüğü yazana kadar dene: araya giren bir başkası daha büyük yazmışsa
            // onu ezmemek gerekiyor.
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
/// Hiçbir şey toplamayan teşhis — testlerde ve teşhis kapalıyken.
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
