using Avalonia.Controls;

namespace GitExt.UI.Diagnostics;

/// <summary>
/// Kare süresi ölçer (P09-T03).
/// </summary>
/// <remarks>
/// <para>
/// Performans bütçesi grafik kaydırma için "60 FPS, kare düşmesi yok" diyor. Ortalama FPS
/// bunu <b>doğrulamıyor</b>: saniyede bir kare 200 ms sürse ortalama hâlâ 55 FPS görünür,
/// ama kullanıcının gördüğü şey takılmadır. Bu yüzden burada asıl rapor edilen
/// <see cref="WorstMilliseconds"/> ve <see cref="DroppedFrames"/>.
/// </para>
/// <para>
/// Ölçüm <see cref="TopLevel.RequestAnimationFrame"/> ile yapılıyor — kare gerçekten
/// oluşturulduğunda tetikleniyor. Bir zamanlayıcıyla ölçmek arayüzün donduğu anları
/// kaçırırdı: donmuş arayüzde zamanlayıcı da çalışmaz.
/// </para>
/// </remarks>
public sealed class FrameTimeMonitor : IDisposable
{
    /// <summary>60 FPS'in kare bütçesi.</summary>
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

    /// <summary>Örneklenen kare sayısı.</summary>
    public int SampleCount => _samples.Count;

    /// <summary>Ortalama kare süresi (ms).</summary>
    public double AverageMilliseconds => _samples.Count == 0 ? 0 : _samples.Average();

    /// <summary>En kötü kare süresi (ms) — takılmayı gösteren sayı budur.</summary>
    public double WorstMilliseconds => _samples.Count == 0 ? 0 : _samples.Max();

    /// <summary>Bütçeyi aşan kare sayısı.</summary>
    public int DroppedFrames => _samples.Count(s => s > TargetFrameMilliseconds);

    /// <summary>Ortalamadan türetilen FPS.</summary>
    public double AverageFramesPerSecond =>
        AverageMilliseconds <= 0 ? 0 : 1000.0 / AverageMilliseconds;

    /// <summary>Ölçümü başlatır. Zaten çalışıyorsa bir şey yapmaz.</summary>
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

    /// <summary>Ölçümü durdurur; toplanan örnekler kalır.</summary>
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

            // Negatif ya da saçma büyük aralıklar: uygulama arka plana alınmış ya da
            // saat geri gitmiş olabilir. Bunları takılma diye raporlamak yanlış alarm olurdu.
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
