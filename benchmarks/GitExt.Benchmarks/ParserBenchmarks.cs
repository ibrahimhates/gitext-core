using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using GitExt.Core;
using GitExt.Core.Model;

namespace GitExt.Benchmarks;

/// <summary>
/// Core layer parser micro-benchmarks (P09-T02).
/// </summary>
/// <remarks>
/// Measures <see cref="CommitLogReader"/>'s static parsing methods without a real git process. The
/// inputs are simulated to look the way they would in `git log -z --format=...` output.
/// </remarks>
public class ParserBenchmarks
{
    // ── Simulated inputs (NUL-separated field sequences) ──────────────────────

    /// <summary>12 alan: id, parent(s), authorName, authorEmail, authorDate, committerName, committerEmail, committerDate, refs, encoding, subject, body.</summary>
    private string[] _singleCommitFields = null!;

    [GlobalSetup]
    public void Setup()
    {
        // 12 fields for a single merge commit (the real git output format).
        string id = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        string parents = "prevId0000000000000000000000000000000 mergeParentId00000000000000000000000";
        string authorName = "İbrahim Hates";
        string authorEmail = "ibrahim@example.com";
        string authorDate = "2026-08-07T14:30:00+03:00";
        string committerName = "CI Bot";
        string committerEmail = "ci@gitext.io";
        string committerDate = "2026-08-07T14:30:05+03:00";
        string refs = "HEAD -> main, tag: v0.3.0";
        string encoding = "";
        string subject = "feat(perf): optimize commit graph layout engine";
        string body = """
            - Reduce lane allocation overhead by reusing free slots.
            - Cache color index lookup to avoid repeated HashSet scans.

            Benchmarks show 15-20% improvement on DAGs with >500 nodes.
            """;

        _singleCommitFields = [id, parents, authorName, authorEmail, authorDate,
                               committerName, committerEmail, committerDate,
                               refs, encoding, subject, body];

        // Pre-compute 10.000 CommitIds for CompareAll / ShortenAll benchmarks.
        _commitIds = new CommitId[10_000];
        for (int i = 0; i < _commitIds.Length; i++)
            _commitIds[i] = GenerateFakeSha(i);
    }

    // ── The CommitLogReader.ParseRecord equivalent ─────────────────────────────

    [Benchmark(Baseline = true)]
    public CommitInfo ParseSingleRecord() => ParseRecord(_singleCommitFields);

    /// <summary>
    /// A verbatim copy of <see cref="CommitLogReader"/>'s `ParseRecord` method — isolated so its
    /// performance characteristics can be measured.
    /// </summary>
    private static CommitInfo ParseRecord(ReadOnlySpan<string> fields) => new()
    {
        Id = CommitId.Parse(fields[0]),
        Parents = ParseParents(fields[1]),
        Author = new Signature(fields[2], fields[3], ParseTimestamp(fields[4])),
        Committer = new Signature(fields[5], fields[6], ParseTimestamp(fields[7])),
        Refs = ParseRefs(fields[8]),
        Encoding = fields[9],
        Subject = fields[10],
        Body = fields[11].TrimEnd('\n'),
    };

    // ── The sub-parsing methods (identical to the real implementations) ───────

    private static IReadOnlyList<CommitId> ParseParents(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<CommitId> parents = new(parts.Length);

        foreach (string part in parts)
        {
            if (CommitId.TryParse(part, out CommitId id))
            {
                parents.Add(id);
            }
        }

        return parents;
    }

    private static IReadOnlyList<string> ParseRefs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UnixEpoch;

    // ── CommitId operations ───────────────────────────────────────────────────

    private CommitId[] _commitIds = null!;

    private static readonly string[] HexPalette = GenerateHexPalette();

    private static string[] GenerateHexPalette()
    {
        var palette = new string[10_000];
        ReadOnlySpan<char> chars = "0123456789abcdef";

        for (int i = 0; i < palette.Length; i++)
        {
            char[] buf = new char[40];

            for (int j = 0; j < 40; j++)
            {
                buf[j] = chars[(j + i) % chars.Length];
            }

            palette[i] = new string(buf);
        }

        return palette;
    }

    private static CommitId GenerateFakeSha(int index) =>
        CommitId.Parse(HexPalette[index]);

    [Benchmark]
    public int CompareAll()
    {
        var ids = _commitIds;
        int sum = 0;

        for (int i = 1; i < ids.Length; i++)
        {
            sum += ids[i].CompareTo(ids[i - 1]);
        }

        return sum;
    }

    [Benchmark]
    public string ShortenAll()
    {
        var ids = _commitIds;
        var builder = new StringBuilder();

        for (int i = 0; i < ids.Length; i++)
        {
            builder.Append(ids[i].ToShortString());
        }

        return builder.ToString();
    }

    [Benchmark]
    public bool TryParseAll()
    {
        // HexPalette is pre-computed at static constructor time — zero allocation here.
        var strings = HexPalette;

        int count = 0;

        foreach (string s in strings)
        {
            if (CommitId.TryParse(s, out _))
            {
                count++;
            }
        }

        return count == 10_000;
    }
}
