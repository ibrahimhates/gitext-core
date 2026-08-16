using BenchmarkDotNet.Attributes;
using GitExt.Graph;

namespace GitExt.Benchmarks;

/// <summary>
/// CommitDAG layout (lane assignment) benchmarks (P09-T02).
/// </summary>
/// <remarks>
/// Measures LaneAssigner's performance across different DAG topologies:
/// a linear chain, branch lines, merge-heavy and octopus merge patterns.
/// </remarks>
public class LinearBenchmarks
{
    private IReadOnlyList<DagCommit> _linear10k = null!;
    private IReadOnlyList<DagCommit> _linear50k = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Chain: 10,000 commits, a new commit → parent on every row (a linear chain).
        var sb10k = new System.Text.StringBuilder(320_000);
        for (int i = 9999; i >= 0; i--)
            sb10k.Append(i.ToString().PadLeft(5, '0')).Append(": ").Append(i == 0 ? "" : ((i - 1).ToString().PadLeft(5, '0'))).Append('\n');
        _linear10k = DagFixture.Parse(sb10k.ToString());

        // Chain: 50,000 commits (a large repository simulation).
        var sb50k = new System.Text.StringBuilder(1_600_000);
        for (int i = 49999; i >= 0; i--)
            sb50k.Append(i.ToString().PadLeft(5, '0')).Append(": ").Append(i == 0 ? "" : ((i - 1).ToString().PadLeft(5, '0'))).Append('\n');
        _linear50k = DagFixture.Parse(sb50k.ToString());
    }

    /// <summary>10.000 commit lineer zincir — temel (baseline).</summary>
    [Benchmark(Baseline = true)]
    public IReadOnlyList<GraphRow> Linear_10k() => Layout(_linear10k);

    /// <summary>A 50,000-commit linear chain — a large repository simulation.</summary>
    [Benchmark]
    public IReadOnlyList<GraphRow> Linear_50k() => Layout(_linear50k);

    private static IReadOnlyList<GraphRow> Layout(IReadOnlyList<DagCommit> commits)
    {
        var engine = new GraphLayoutEngine();
        return engine.Add(commits);
    }
}

/// <summary>
/// A DAG that forks and joins — each group is one fan-out/fan-in pattern.
/// </summary>
public class BranchedBenchmarks
{
    private IReadOnlyList<DagCommit> _branched10k = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Each group = 3 commits, a pattern that forks then joins:
        //
        //   MM: CC BB    → the merge joining branch BB and the trunk
        //   BB: CC       → the parallel branch splitting off CC
        //   CC:          → the group's root
        //
        // The order follows ADR-0007: the child comes before its parent in the text, the root last.
        // There are no links between groups — referring backwards while generating forwards would put a
        // parent ahead of its child, and validation would rightly reject it.

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

    /// <summary>A forking DAG — 9,999 commits, two lanes per merge.</summary>
    [Benchmark]
    public IReadOnlyList<GraphRow> Branched_10k() => Layout();

    private IReadOnlyList<GraphRow> Layout()
    {
        var engine = new GraphLayoutEngine();
        return engine.Add(_branched10k);
    }
}

/// <summary>
/// A merge-heavy DAG — each group is one merge cycle (fork → branch → merge back).
/// </summary>
public class MergedBbenchmarks
{
    private IReadOnlyList<DagCommit> _merged10k = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Each merge cycle = 4 rows:
        //   MM: BB CB → the merge commit (2 parents) — child FIRST in topo order
        //   CB: XX    → branch tip C
        //   BB: XX    → branch tip B
        //   XX:       → the backbone, root node.
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

    /// <summary>A merge-heavy DAG — 10,000 commits, one merge per group.</summary>
    [Benchmark]
    public IReadOnlyList<GraphRow> Merged_10k() => Layout();

    private IReadOnlyList<GraphRow> Layout()
    {
        var engine = new GraphLayoutEngine();
        return engine.Add(_merged10k);
    }
}

/// <summary>
/// An octopus merge DAG — each group is a pattern where one commit has parents from 4 branches.
/// </summary>
public class MultiMergeBenchmarks
{
    private IReadOnlyList<DagCommit> _multiMerge10k = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Each group = 6 rows, in topo order (ADR-0007: children FIRST, parents LAST):
        //   MM: AA BB CC DD → the octopus merge (4 parents), child FIRST in the text
        //   AA: XX          → branch A, child of XX
        //   BB: XX          → branch B, child of XX
        //   CC: XX          → branch C, child of XX
        //   DD: XX          → branch D, child of XX
        //   XX:             → the backbone root.
        //
        // Each cycle = 6 commits, ~5 unique nodes (MM + AA+BB+CC+DD).
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

    /// <summary>An octopus DAG — 10,002 commits, four parents per merge.</summary>
    [Benchmark]
    public IReadOnlyList<GraphRow> MultiMerge_10k() => Layout();

    private IReadOnlyList<GraphRow> Layout()
    {
        var engine = new GraphLayoutEngine();
        return engine.Add(_multiMerge10k);
    }
}
