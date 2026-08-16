using System.Diagnostics;
using System.Globalization;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Graph;

namespace GitExt.Desktop;

/// <summary>
/// Runs a repository through the commit graph pipeline and measures it (P03-T18).
/// </summary>
/// <remarks>
/// <para>
/// Usage: <c>gitext-core --bench &lt;repository-path&gt;</c>
/// </para>
/// <para>
/// <b>Why a separate mode?</b> The Phase 02 measurements only covered the core layer.
/// What is measured here is what the user waits for: <b>when does the first row appear</b>,
/// how long does the whole thing take, how many lanes are created and what stays in memory.
/// Avalonia is not started — no desktop session needed, it runs on CI as well.
/// </para>
/// <para>
/// The retained memory is measured with the rows <b>kept alive</b> and after a forced garbage
/// collection; otherwise what is measured is allocated rather than retained memory — we fell into
/// this trap once in Phase 03.
/// </para>
/// </remarks>
internal static class GraphBenchmark
{
    internal const string Flag = "--bench";

    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                      ?? Directory.GetCurrentDirectory();

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        GitProcessRunner runner = new(executable);

        RepositoryLocation location = await new RepositoryLocator(runner)
            .LocateAsync(path, cancellationToken).ConfigureAwait(false);

        // All refs, or only HEAD? To separate out where the lane width comes from
        // (a repository can have more than 1000 tags — measured).
        bool headOnly = args.Contains("--head-only", StringComparer.Ordinal);

        Console.WriteLine($"depo        : {location.WorkingDirectory}");
        Console.WriteLine($"scope       : {(headOnly ? "HEAD only" : "all refs (--all)")}");

        await MeasureAsync(runner, location.WorkingDirectory, headOnly, cancellationToken)
            .ConfigureAwait(false);

        return 0;
    }

    private static async Task MeasureAsync(
        IGitProcessRunner runner,
        string workingDirectory,
        bool headOnly,
        CancellationToken cancellationToken)
    {
        CommitLogReader reader = new(runner);
        CommitLogQuery query = new() { IncludeAllRefs = !headOnly };

        GraphLayoutEngine engine = new();

        // The rows are kept alive: this is the only way to measure retained memory.
        List<GraphRow> rows = [];
        List<CommitInfo> commits = [];

        long beforeBytes = GetRetainedBytes();

        Stopwatch total = Stopwatch.StartNew();
        TimeSpan firstRow = TimeSpan.Zero;

        // The DISTRIBUTION of the lane count says more than the peak value: a peak seen on a few rows
        // is solved with horizontal scrolling, whereas a widespread width calls the algorithm into
        // question (P03-T18, open questions 1 and 2).
        List<int> laneCounts = [];

        // For column width the real question is this: which lanes do the commit NODES sit in?
        // If most lanes are long pass-through edges, a narrow limit does not hide the nodes (P03-T21).
        List<int> nodeLanes = [];

        // Are the lanes really full, or are there gaps between them? If there are gaps the fix is
        // compaction; if they are full, the number of concurrent branches really is high.
        List<int> occupied = [];

        await foreach (CommitInfo commit in reader
                           .StreamAsync(workingDirectory, query, cancellationToken)
                           .ConfigureAwait(false))
        {
            GraphRow row = engine.Add(ToDagCommit(commit));

            if (rows.Count == 0)
            {
                firstRow = total.Elapsed;
            }

            rows.Add(row);
            commits.Add(commit);
            laneCounts.Add(row.LaneCount);
            nodeLanes.Add(row.Lane);

            HashSet<int> used = [row.Lane];

            foreach (GraphEdge edge in row.Edges)
            {
                used.Add(edge.FromLane);
                used.Add(edge.ToLane);
            }

            occupied.Add(used.Count);
        }

        total.Stop();

        long afterBytes = GetRetainedBytes();

        // Touching rows/commits here stops the JIT from treating the lists as collectable early.
        int count = rows.Count + commits.Count - commits.Count;

        Report(count, firstRow, total.Elapsed, laneCounts, afterBytes - beforeBytes);
        ReportNodeLanes(nodeLanes);
        ReportOccupancy(laneCounts, occupied);
        ReportTextWeight(commits);

        GC.KeepAlive(rows);
        GC.KeepAlive(commits);
    }

    private static void Report(
        int rowCount,
        TimeSpan firstRow,
        TimeSpan total,
        List<int> laneCounts,
        long retainedBytes)
    {
        CultureInfo c = CultureInfo.InvariantCulture;

        Console.WriteLine($"commit      : {rowCount.ToString("N0", c)}");
        Console.WriteLine($"first row   : {firstRow.TotalMilliseconds.ToString("F1", c)} ms");
        Console.WriteLine($"total       : {total.TotalMilliseconds.ToString("F0", c)} ms");

        if (total.TotalSeconds > 0)
        {
            double perSecond = rowCount / total.TotalSeconds;
            Console.WriteLine($"rate        : {perSecond.ToString("N0", c)} commits/s");
        }

        laneCounts.Sort();

        Console.WriteLine(
            "lanes       : "
            + $"p50={Percentile(laneCounts, 0.50)}  "
            + $"p90={Percentile(laneCounts, 0.90)}  "
            + $"p99={Percentile(laneCounts, 0.99)}  "
            + $"max={(laneCounts.Count > 0 ? laneCounts[^1] : 0)}");

        double megabytes = retainedBytes / 1024.0 / 1024.0;
        Console.WriteLine($"tutulan     : {megabytes.ToString("F0", c)} MB");

        if (rowCount > 0)
        {
            double perRow = (double)retainedBytes / rowCount;
            Console.WriteLine($"per row     : {perRow.ToString("F0", c)} bytes");
        }
    }

    /// <summary>
    /// The lane index distribution of the commit nodes and the coverage ratio of possible limits.
    /// </summary>
    private static void ReportNodeLanes(List<int> nodeLanes)
    {
        nodeLanes.Sort();

        Console.WriteLine(
            "node lanes  : "
            + $"p50={Percentile(nodeLanes, 0.50)}  "
            + $"p90={Percentile(nodeLanes, 0.90)}  "
            + $"p99={Percentile(nodeLanes, 0.99)}  "
            + $"max={(nodeLanes.Count > 0 ? nodeLanes[^1] : 0)}");

        foreach (int cap in new[] { 8, 12, 16, 24, 32 })
        {
            int visible = nodeLanes.Count(l => l < cap);
            double share = nodeLanes.Count == 0 ? 0 : 100.0 * visible / nodeLanes.Count;

            Console.WriteLine(
                $"  cap {cap,2}     : {share.ToString("F2", CultureInfo.InvariantCulture)}% of nodes visible");
        }
    }

    /// <summary>
    /// How much of the allocated lanes is actually used.
    /// </summary>
    private static void ReportOccupancy(List<int> laneCounts, List<int> occupied)
    {
        if (occupied.Count == 0)
        {
            return;
        }

        // laneCounts is already sorted; since the ratio needs an unsorted match, this is derived
        // from the totals.
        double totalLanes = laneCounts.Sum(x => (double)x);
        double totalUsed = occupied.Sum(x => (double)x);

        List<int> sortedUsed = [.. occupied];
        sortedUsed.Sort();

        Console.WriteLine(
            "used lanes  : "
            + $"p50={Percentile(sortedUsed, 0.50)}  "
            + $"p90={Percentile(sortedUsed, 0.90)}  max={sortedUsed[^1]}  "
            + $"· doluluk %{(100 * totalUsed / totalLanes).ToString("F1", CultureInfo.InvariantCulture)}");
    }

    private static int Percentile(List<int> sorted, double fraction) =>
        sorted.Count == 0
            ? 0
            : sorted[Math.Clamp((int)(sorted.Count * fraction), 0, sorted.Count - 1)];

    /// <summary>
    /// Reports the share of the text fields in memory.
    /// </summary>
    /// <remarks>
    /// Trying to reduce memory without knowing what eats it is guesswork. The author name and
    /// e-mail address have a small number of unique values; what interning would gain is visible
    /// from here (Phase 09).
    /// </remarks>
    private static void ReportTextWeight(List<CommitInfo> commits)
    {
        long subject = 0;
        long body = 0;
        long people = 0;

        HashSet<string> uniquePeople = new(StringComparer.Ordinal);
        long peopleUnique = 0;

        foreach (CommitInfo commit in commits)
        {
            subject += commit.Subject.Length;
            body += commit.Body.Length;

            people += commit.Author.Name.Length + commit.Author.Email.Length
                + commit.Committer.Name.Length + commit.Committer.Email.Length;

            foreach (string value in new[]
                     {
                         commit.Author.Name, commit.Author.Email,
                         commit.Committer.Name, commit.Committer.Email,
                     })
            {
                if (uniquePeople.Add(value))
                {
                    peopleUnique += value.Length;
                }
            }
        }

        CultureInfo c = CultureInfo.InvariantCulture;

        // In .NET a char is 2 bytes.
        Console.WriteLine(
            "metin (MB)  : "
            + $"subject={Mb(subject)}  body={Mb(body)}  "
            + $"people={Mb(people)} (unique {Mb(peopleUnique)}, {uniquePeople.Count.ToString("N0", c)} values)");

        static string Mb(long chars) =>
            (chars * 2.0 / 1024 / 1024).ToString("F0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Retained memory after a forced full collection.
    /// </summary>
    /// <remarks>
    /// <c>GC.GetTotalMemory(forceFullCollection: true)</c> alone is not enough; objects awaiting
    /// finalization get mixed into the number and the measurement comes out noisy.
    /// </remarks>
    private static long GetRetainedBytes()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static DagCommit ToDagCommit(CommitInfo commit)
    {
        string[] parents = new string[commit.Parents.Count];

        for (int i = 0; i < parents.Length; i++)
        {
            parents[i] = commit.Parents[i].Value;
        }

        return new DagCommit(commit.Id.Value, parents);
    }
}
