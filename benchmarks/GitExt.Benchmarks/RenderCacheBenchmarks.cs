using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;

namespace GitExt.Benchmarks;

/// <summary>
/// The access cost of the Pen/Brush cache on the drawing path (P09-T09).
/// </summary>
/// <remarks>
/// <para>
/// <c>CommitGraphCell.Render</c> takes a pen from the cache for every edge, and the access is under a
/// <c>lock</c>. In a 500k-commit repository there are ~50 rows on screen with a few edges each; that is
/// hundreds of lock acquisitions per frame.
/// </para>
/// <para>
/// It is here <b>to measure before optimising</b>: if the lock's cost is at the noise level, moving to
/// a lock-free structure only adds risk. Even though Avalonia doing its drawing on a single thread
/// makes the lock look unnecessary, the cache is static and the data race would be real if it were
/// reached from somewhere else.
/// </para>
/// <para>
/// It cannot be measured with the real types (<c>Pen</c>, <c>SolidColorBrush</c>): this project does
/// not depend on Avalonia (ADR-0003). What is measured is the access <b>pattern</b> itself — a
/// dictionary lookup plus the synchronisation.
/// </para>
/// </remarks>
public class RenderCacheBenchmarks
{
    /// <summary>The pen/brush lookups made per frame (~50 rows × a few edges).</summary>
    private const int LookupsPerFrame = 300;

    private readonly Dictionary<(uint Color, double Thickness), object> _locked = [];
    private readonly ConcurrentDictionary<(uint Color, double Thickness), object> _concurrent = new();
    private readonly Lock _gate = new();

    private (uint Color, double Thickness)[] _keys = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Eight lane colours — the palette's size (GraphPalettes).
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

    /// <summary>The current implementation: a dictionary plus a <c>lock</c>.</summary>
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
