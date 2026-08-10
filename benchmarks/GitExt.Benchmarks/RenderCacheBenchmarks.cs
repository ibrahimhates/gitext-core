using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;

namespace GitExt.Benchmarks;

/// <summary>
/// Çizim yolundaki Pen/Brush önbelleğinin erişim maliyeti (P09-T09).
/// </summary>
/// <remarks>
/// <para>
/// <c>CommitGraphCell.Render</c> her kenar için önbellekten bir kalem alıyor ve erişim
/// <c>lock</c> altında. 500k commit'lik bir depoda ekranda ~50 satır, satır başına
/// birkaç kenar var; yani kare başına yüzlerce kilit alma.
/// </para>
/// <para>
/// <b>Optimizasyondan önce ölçmek</b> için burada: kilidin maliyeti gürültü seviyesindeyse
/// kilitsiz bir yapıya geçmek yalnızca risk ekler. Avalonia'nın çizimi tek iş parçacığında
/// yapıyor olması kilidi gereksiz gösterse de, önbellek statik ve başka bir yerden
/// erişilirse veri yarışı gerçek olurdu.
/// </para>
/// <para>
/// Gerçek tiplerle (<c>Pen</c>, <c>SolidColorBrush</c>) ölçülemiyor: bu proje Avalonia'ya
/// bağımlı değil (ADR-0003). Ölçülen şey erişim <b>deseninin</b> kendisi — sözlük araması
/// artı senkronizasyon.
/// </para>
/// </remarks>
public class RenderCacheBenchmarks
{
    /// <summary>Kare başına yapılan kalem/fırça araması (~50 satır × birkaç kenar).</summary>
    private const int LookupsPerFrame = 300;

    private readonly Dictionary<(uint Color, double Thickness), object> _locked = [];
    private readonly ConcurrentDictionary<(uint Color, double Thickness), object> _concurrent = new();
    private readonly Lock _gate = new();

    private (uint Color, double Thickness)[] _keys = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Sekiz şerit rengi — paletin boyutu (GraphPalettes).
        var keys = new (uint, double)[LookupsPerFrame];

        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = ((uint)(0xFF000000 | (i % 8)), 1.5);
        }

        _keys = keys;

        foreach ((uint Color, double Thickness) key in keys)
        {
            _locked[key] = new object();
            _concurrent[key] = new object();
        }
    }

    /// <summary>Şu anki uygulama: sözlük + <c>lock</c>.</summary>
    [Benchmark(Baseline = true)]
    public int LockedDictionary()
    {
        int hits = 0;

        for (int i = 0; i < _keys.Length; i++)
        {
            lock (_gate)
            {
                if (_locked.TryGetValue(_keys[i], out object? value) && value is not null)
                {
                    hits++;
                }
            }
        }

        return hits;
    }

    /// <summary>Kilitsiz alternatif.</summary>
    [Benchmark]
    public int ConcurrentDictionary()
    {
        int hits = 0;

        for (int i = 0; i < _keys.Length; i++)
        {
            if (_concurrent.TryGetValue(_keys[i], out object? value) && value is not null)
            {
                hits++;
            }
        }

        return hits;
    }
}
