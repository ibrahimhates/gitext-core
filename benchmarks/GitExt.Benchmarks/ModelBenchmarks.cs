using System.Globalization;
using BenchmarkDotNet.Attributes;
using GitExt.Core.Model;
using GitExt.Graph;

namespace GitExt.Benchmarks;

/// <summary>
/// Commit data model and object creation benchmarks (P09-T02).
/// </summary>
/// <remarks>
/// Measures the allocation cost of the objects created while listing commits, such as `CommitInfo`
/// and `Signature`. Provides the baseline for evaluating the "string interning" optimisation in
/// Phase 08.
/// </remarks>
public class ModelBenchmarks
{
    private readonly string[] _authorNames =
    [
        "İbrahim Hates",
        "Alice Developer",
        "Bob Engineer",
        "CI/CD Bot",
        "Dependabot",
    ];

    private readonly string[] _emails =
    [
        "ibrahim@example.com",
        "alice@company.io",
        "bob@company.io",
        "ci@gitext.io",
        "dependabot@github.com",
    ];

    // ── Memory retention — rows/commits together (the GraphBenchmark pattern) ─

    private CommitInfo[] _textWeightCommits = null!;
    private Signature[] _signatures = null!;

    [GlobalSetup]
    public void Setup()
    {
        // The ready-made commits MeasureTextWeight will run over. Generate_10k does not use them —
        // because it measures the cost of producing them, it builds its own objects.
        var textWeights = new CommitInfo[10_000];

        for (int i = 0; i < 10_000; i++)
        {
            textWeights[i] = CreateSingleCommit(i);
        }

        _textWeightCommits = textWeights;

        // Prepare 10.000 signatures for FindUniqueAuthors benchmark.
        var sigs = new Signature[10_000];
        for (int i = 0; i < 10_000; i++)
        {
            sigs[i] = new Signature(
                _authorNames[i % _authorNames.Length],
                _emails[i % _emails.Length],
                DateTimeOffset.Parse("2026-08-07T14:30:00+03:00", CultureInfo.InvariantCulture));
        }

        _signatures = sigs;
    }

    /// <summary>Creating 10,000 unique <c>CommitInfo</c>s — the allocation cost.</summary>
    /// <remarks>
    /// The objects are built here rather than in <see cref="Setup"/>: reading a ready-made array would
    /// leave the measured work out entirely and the result would come out close to zero.
    /// </remarks>
    [Benchmark(Baseline = true)]
    public CommitInfo[] Generate_10k()
    {
        var commits = new CommitInfo[10_000];

        for (int i = 0; i < commits.Length; i++)
        {
            commits[i] = CreateSingleCommit(i);
        }

        return commits;
    }

    /// <summary>Creating a single commit — the same as the final stage of `ParseRecord`.</summary>
    private CommitInfo CreateSingleCommit(int index) => new()
    {
        Id = CommitId.Parse($"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6{(index % 10000):D4}"),
        Parents = index == 0
            ? []
            : new[] { CreateParentId(index) },

        Author = new Signature(
            _authorNames[index % _authorNames.Length],
            _emails[index % _emails.Length],
            DateTimeOffset.Parse("2026-08-07T14:30:00+03:00", CultureInfo.InvariantCulture)),
        Committer = new Signature(
            index % 3 == 0 ? "CI/CD Bot" : _authorNames[index % _authorNames.Length],
            index % 3 == 0 ? "ci@gitext.io" : _emails[index % _emails.Length],
            DateTimeOffset.Parse("2026-08-07T14:30:05+03:00", CultureInfo.InvariantCulture)),
        Refs = index % 50 == 0
            ? [.. new[] { "HEAD -> main", $"tag: v{(index / 50):D1}.0.0" }]
            : ["HEAD -> main"],
        Subject = $"feat commit #{index}",
        Body = index % 10 == 0
            ? $"Body for commit {index}\n\nChanges include optimization work."
            : "",
    };

    /// <summary>Produces a parent commit ID: 40 hex characters, made unique with index-1.</summary>
    private CommitId CreateParentId(int index) =>
        CommitId.Parse("feed" + $"{(index == 0 ? 0 : index - 1).ToString().PadLeft(36, '0')}");

    /// <summary>10k commit + GraphRow birlikte tahsisi — KB cinsinden.</summary>
    [Benchmark]
    public long RetainedMemory_10k()
    {
        var commits = new List<CommitInfo>(10_000);
        var graphRows = new List<GraphRow>(10_000);

        for (int i = 0; i < 10_000; i++)
        {
            var commit = CreateSingleCommit(i);
            commits.Add(commit);

            var parents = commit.Parents.Select(p => p.Value).ToList();
            var dagCommit = new DagCommit(commit.Id.Value, parents);

            graphRows.Add(new GraphRow
            {
                Commit = dagCommit,
                Lane = i % 8,
                ColorIndex = i % 6,
                Edges = i % 3 == 0
                    ? [new GraphEdge
                        {
                            FromLane = i % 8,
                            ToLane = (i + 1) % 8,
                            Target = $"prev{i - 1:D40}",
                            ColorIndex = i % 6,
                        }]
                    : Array.Empty<GraphEdge>(),
                LaneCount = 5,
            });
        }

        GC.KeepAlive(commits);
        GC.KeepAlive(graphRows);

        return GC.GetTotalMemory(forceFullCollection: true) / 1024; // KB cinsinden
    }

    /// <summary>The total byte weight of the text fields.</summary>
    [Benchmark]
    public long MeasureTextWeight()
    {
        var commits = _textWeightCommits;
        long subjectBytes = 0;
        long bodyBytes = 0;
        long peopleBytes = 0;

        foreach (CommitInfo commit in commits)
        {
            // .NET'te char 2 bayt.
            subjectBytes += (long)commit.Subject.Length * 2;
            bodyBytes += (long)commit.Body.Length * 2;
            peopleBytes += (commit.Author.Name.Length + commit.Author.Email.Length
                + commit.Committer.Name.Length + commit.Committer.Email.Length) * 2L;
        }

        return subjectBytes + bodyBytes + peopleBytes;
    }

    // ── Refs parsing variants ─────────────────────────────────────────────────

    private const int RefParseIterations = 100_000;

    [Benchmark]
    public long ParseRefsEmpty()
    {
        long count = 0;
        for (int i = 0; i < RefParseIterations; i++)
        {
            count += Helpers.ParseRefs("").Count;
        }
        return count;
    }

    [Benchmark]
    public int ParseRefsOne()
    {
        int c = 0;
        for (int i = 0; i < RefParseIterations; i++)
        {
            c += Helpers.ParseRefs("HEAD -> main").Count;
        }
        return c;
    }

    [Benchmark]
    public int ParseRefsMany()
    {
        const string manyRefs = "HEAD -> feature/x, origin/develop, tag: v1.0, tag: v2.0-beta, remote/head/main";
        int c = 0;
        for (int i = 0; i < RefParseIterations; i++)
        {
            c += Helpers.ParseRefs(manyRefs).Count;
        }
        return c;
    }

    // ── Signature comparison (hot path: sorting/filtering by author) ──────────

    /// <summary>The number of unique authors among 10k signatures.</summary>
    [Benchmark]
    public int FindUniqueAuthors()
    {
        var sigs = _signatures;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < sigs.Length; i++)
        {
            seen.Add(sigs[i].Name);
        }

        return seen.Count;
    }
}

/// <summary>
/// CommitLogReader.ParseRefs in isolation — internal access for the benchmark.
/// </summary>
internal static class Helpers
{
    public static IReadOnlyList<string> ParseRefs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
