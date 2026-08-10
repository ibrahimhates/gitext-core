using BenchmarkDotNet.Attributes;
using GitExt.Graph;

namespace GitExt.Benchmarks;

/// <summary>
/// CommitDAG yerleşim (lane assignment) benchmark'ları (P09-T02).
/// </summary>
/// <remarks>
/// Farklı DAG topolojilerinde LaneAssigner'ın performansını ölçer:
/// lineer zincir, şube çizgileri, merge-ağır ve octopus merge desenleri.
/// </remarks>
public class LinearBenchmarks
{
    private IReadOnlyList<DagCommit> _linear10k = null!;
    private IReadOnlyList<DagCommit> _linear50k = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Chain: 10.000 commit, her satırda yeni commit → ebeveyn (lineer zincir).
        var sb10k = new System.Text.StringBuilder(320_000);
        for (int i = 9999; i >= 0; i--)
            sb10k.Append(i.ToString().PadLeft(5, '0')).Append(": ").Append(i == 0 ? "" : ((i - 1).ToString().PadLeft(5, '0'))).Append('\n');
        _linear10k = DagFixture.Parse(sb10k.ToString());

        // Chain: 50.000 commit (büyük depo simülasyonu).
        var sb50k = new System.Text.StringBuilder(1_600_000);
        for (int i = 49999; i >= 0; i--)
            sb50k.Append(i.ToString().PadLeft(5, '0')).Append(": ").Append(i == 0 ? "" : ((i - 1).ToString().PadLeft(5, '0'))).Append('\n');
        _linear50k = DagFixture.Parse(sb50k.ToString());
    }

    /// <summary>10.000 commit lineer zincir — temel (baseline).</summary>
    [Benchmark(Baseline = true)]
    public IReadOnlyList<GraphRow> Linear_10k() => Layout(_linear10k);

    /// <summary>50.000 commit lineer zincir — büyük depo simülasyonu.</summary>
    [Benchmark]
    public IReadOnlyList<GraphRow> Linear_50k() => Layout(_linear50k);

    private static IReadOnlyList<GraphRow> Layout(IReadOnlyList<DagCommit> commits)
    {
        var engine = new GraphLayoutEngine();
        return engine.Add(commits);
    }
}

/// <summary>
/// Çatallanıp birleşen DAG — her grup bir fan-out/fan-in deseni.
/// </summary>
public class BranchedBenchmarks
{
    private IReadOnlyList<DagCommit> _branched10k = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Her grup = 3 commit, çatallanan sonra birleşen bir desen:
        //
        //   MM: CC BB    → BB dalını ve gövdeyi birleştiren merge
        //   BB: CC       → CC'den ayrılan paralel dal
        //   CC:          → grubun kökü
        //
        // Sıra ADR-0007'ye uyuyor: çocuk metinde ebeveyninden önce geliyor, kök en sonda.
        // Gruplar arası bağ yok — ileri doğru üretimde geriye referans vermek ebeveyni
        // çocuğundan öne alır ve doğrulama haklı olarak reddeder.

        var sb = new System.Text.StringBuilder(320_000);
        for (int g = 0; g < 3333; g++)
        {
            int baseId = g * 3;
            sb.Append($"{baseId:D5}: ").Append($"{baseId + 2:D5}").Append(' ').Append($"{baseId + 1:D5}").Append('\n');   // MM: CC BB
            sb.Append($"{baseId + 1:D5}: ").Append($"{baseId + 2:D5}").Append('\n');                                      // BB: CC
            sb.Append($"{baseId + 2:D5}:").Append('\n');                                                                  // CC: kök
        }
        _branched10k = DagFixture.Parse(sb.ToString());
    }

    /// <summary>Çatallanan DAG — 9.999 commit, merge başına iki şerit.</summary>
    [Benchmark]
    public IReadOnlyList<GraphRow> Branched_10k() => Layout();

    private IReadOnlyList<GraphRow> Layout()
    {
        var engine = new GraphLayoutEngine();
        return engine.Add(_branched10k);
    }
}

/// <summary>
/// Merge-ağır DAG — her grup bir merge cycle (şube → branch → merge back).
/// </summary>
public class MergedBbenchmarks
{
    private IReadOnlyList<DagCommit> _merged10k = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Her merge cycle = 4 satır:
        //   MM: BB CB → merge commit (2 parent) — child FIRST in topo order
        //   CB: XX    → branch tip C
        //   BB: XX    → branch tip B
        //   XX:       → backbone, root node.
        //
        // Topo order (ADR-0007): children FIRST in text, parents LAST.
        // MM is child of BB and CB → MM comes first.
        // BB, CB are children of XX → come after MM but before XX.
        // XX is parent of all → comes last (topo root).

        var sb = new System.Text.StringBuilder(320_000);
        for (int g = 0; g < 2500; g++)
        {
            int baseId = g * 4;
            // Topo order: MM → BB → XX.
            sb.Append($"{baseId:D5}: ").Append($"{baseId + 1:D5}").Append(' ').Append($"{baseId + 2:D5}").Append('\n'); // MM: BB CB (child FIRST)
            sb.Append($"{baseId + 1:D5}: ").Append($"{baseId + 3:D5}").Append('\n');   // BB: XX
            sb.Append($"{baseId + 2:D5}: ").Append($"{baseId + 3:D5}").Append('\n');   // CB: XX
            sb.Append($"{baseId + 3:D5}:").Append('\n');                               // XX: no parent (topo root)
        }
        _merged10k = DagFixture.Parse(sb.ToString());
    }

    /// <summary>Merge-ağır DAG — 10.000 commit, grup başına bir merge.</summary>
    [Benchmark]
    public IReadOnlyList<GraphRow> Merged_10k() => Layout();

    private IReadOnlyList<GraphRow> Layout()
    {
        var engine = new GraphLayoutEngine();
        return engine.Add(_merged10k);
    }
}

/// <summary>
/// Octopus merge DAG — her grup bir commit'in 4 şubeden gelen ebeveynleri olduğu desen.
/// </summary>
public class MultiMergeBenchmarks
{
    private IReadOnlyList<DagCommit> _multiMerge10k = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Her grup = 6 satır, topo order (ADR-0007: children FIRST, parents LAST):
        //   MM: AA BB CC DD → octopus merge (4 parent), child FIRST in text
        //   AA: XX          → branch A, child of XX
        //   BB: XX          → branch B, child of XX
        //   CC: XX          → branch C, child of XX
        //   DD: XX          → branch D, child of XX
        //   XX:             → backbone root.
        //
        // Her cycle = 6 commit, ~5 unique node (MM + AA+BB+CC+DD).
        // No cross-group links — each group is an independent component for lane variety.

        var sb = new System.Text.StringBuilder(320_000);
        for (int g = 0; g < 1667; g++)
        {
            int baseId = g * 6;
            // Topo order: MM → branches(AA,BB,CC,DD) → XX.
            sb.Append($"{baseId:D5}: ").Append($"{baseId + 4:D5}").Append(' ').Append($"{baseId + 3:D5}")
                .Append(' ').Append($"{baseId + 2:D5}").Append(' ').Append($"{baseId + 1:D5}").Append('\n'); // MM: AA BB CC DD (child FIRST)
            sb.Append($"{baseId + 4:D5}: ").Append($"{baseId + 5:D5}").Append('\n');        // AA: XX
            sb.Append($"{baseId + 3:D5}: ").Append($"{baseId + 5:D5}").Append('\n');        // BB: XX
            sb.Append($"{baseId + 2:D5}: ").Append($"{baseId + 5:D5}").Append('\n');        // CC: XX
            sb.Append($"{baseId + 1:D5}: ").Append($"{baseId + 5:D5}").Append('\n');        // DD: XX
            sb.Append($"{baseId + 5:D5}:").Append('\n');                                    // XX: no parent (topo root)
        }
        _multiMerge10k = DagFixture.Parse(sb.ToString());
    }

    /// <summary>Octopus DAG — 10.002 commit, merge başına dört ebeveyn.</summary>
    [Benchmark]
    public IReadOnlyList<GraphRow> MultiMerge_10k() => Layout();

    private IReadOnlyList<GraphRow> Layout()
    {
        var engine = new GraphLayoutEngine();
        return engine.Add(_multiMerge10k);
    }
}
