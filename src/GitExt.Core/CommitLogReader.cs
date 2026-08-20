using System.Globalization;
using System.Runtime.CompilerServices;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Query for reading commit history (P02-T08).
/// </summary>
public sealed record CommitLogQuery
{
    /// <summary>
    /// Starting point: branch name, tag, SHA, or <c>HEAD</c>. If empty, the current <c>HEAD</c>.
    /// </summary>
    public string? Revision { get; init; }

    /// <summary>Read history from all refs (<c>--all</c>).</summary>
    public bool IncludeAllRefs { get; init; }

    /// <summary>Follow only the first parent on merges (<c>--first-parent</c>).</summary>
    public bool FirstParentOnly { get; init; }

    /// <summary>
    /// Topological order (<c>--topo-order</c>): every child comes <b>before</b> its parent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Defaults to <see langword="true"/> and must stay that way.</b> <c>git log</c>'s
    /// default (date) order doesn't give this guarantee: in a repository with skewed dates a
    /// parent can come before its child. Measured — rebase, imports, and clock drift produce
    /// this in real repositories.
    /// </para>
    /// <para>
    /// Graph layout (ADR-0007) does a single-pass forward scan and <b>depends on</b> this
    /// order; if violated, edges point upward. Turning it off should only happen when order
    /// truly doesn't matter (e.g. listing a single file's history) and deliberately.
    /// </para>
    /// <para>
    /// Cost: git has to walk the entire graph. If the repository has a <c>commit-graph</c>
    /// file, this cost is practically zero (600 ms → 1 ms at 200k commits, measured).
    /// </para>
    /// </remarks>
    public bool TopologicalOrder { get; init; } = true;

    /// <summary>Maximum number of commits to read. <see langword="null"/> means unlimited.</summary>
    public int? MaxCount { get; init; }

    /// <summary>Number of commits to skip from the start.</summary>
    public int Skip { get; init; }

    /// <summary>Only commits affecting these paths.</summary>
    public IReadOnlyList<RepositoryPath> Paths { get; init; } = [];

    /// <summary>Search within the commit message (<c>--grep</c>).</summary>
    public string? MessageContains { get; init; }

    /// <summary>Filter by author (<c>--author</c>).</summary>
    public string? Author { get; init; }

    /// <summary>Filter by committer (<c>--committer</c>).</summary>
    /// <remarks>
    /// The author wrote the change, the committer put it on this branch; after a rebase or a
    /// cherry-pick they are different people. GitExtensions offers both, so the filter can answer
    /// "what did I write" and "what did I land" separately.
    /// </remarks>
    public string? Committer { get; init; }

    /// <summary>
    /// Commits whose <b>diff</b> contains the pattern (<c>-G</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>-G</c>, not <c>-S</c> — the same choice GitExtensions makes. MEASURED on a repository
    /// where a line containing the word was <b>moved</b>: <c>-S</c> misses that commit (the number
    /// of occurrences did not change) while <c>-G</c> finds it. For someone asking "which commit
    /// touched this text", the moved line is a hit, not a miss.
    /// </para>
    /// <para>
    /// This is the slow filter: git has to produce a diff for every commit. GitExtensions writes
    /// "(SLOW)" next to it in the menu for that reason.
    /// </para>
    /// </remarks>
    public string? DiffContains { get; init; }

    /// <summary>
    /// Should the patterns match regardless of case (<c>--regexp-ignore-case</c>)?
    /// </summary>
    /// <remarks>
    /// <b>On by default.</b> MEASURED: git's pattern filters are case-SENSITIVE without it —
    /// <c>--committer=GRACE</c> returns nothing in a repository full of commits by <c>grace</c>.
    /// In a filter box the user types what they remember, not what was typed originally.
    /// </remarks>
    public bool IgnoreCase { get; init; } = true;

    /// <summary>
    /// Are the patterns plain text rather than regular expressions (<c>--fixed-strings</c>)?
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 This is <b>query-wide and cannot be otherwise</b>: git's <c>--fixed-strings</c> applies
    /// to every limiting pattern at once — <c>--grep</c>, <c>--author</c> and <c>--committer</c>
    /// alike. MEASURED: <c>--fixed-strings --author="a.a"</c> finds nothing where
    /// <c>--author="a.a"</c> finds two commits.
    /// </para>
    /// <para>
    /// The earlier version added <c>--fixed-strings</c> whenever a message filter was present, so
    /// switching on a message filter silently turned an existing author filter into literal
    /// matching. One switch for the whole query at least makes the rule visible.
    /// </para>
    /// <para>
    /// It does <b>not</b> reach <see cref="DiffContains"/>: <c>-G</c> stays a regular expression
    /// under <c>--fixed-strings</c> (measured).
    /// </para>
    /// </remarks>
    public bool LiteralPatterns { get; init; }
}

/// <summary>
/// Reads commit history.
/// </summary>
public interface ICommitLogReader
{
    /// <summary>
    /// Reads the entire history and returns it as a list.
    /// </summary>
    Task<IReadOnlyList<CommitInfo>> ReadAsync(
        string workingDirectory,
        CommitLogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads history as a stream — the first commits are produced before <c>git</c> finishes.
    /// </summary>
    /// <remarks>
    /// So the UI can render its first screen right away in large repositories (P02-T04).
    /// </remarks>
    IAsyncEnumerable<CommitInfo> StreamAsync(
        string workingDirectory,
        CommitLogQuery query,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICommitLogReader"/>
public sealed class CommitLogReader : ICommitLogReader
{
    private readonly IGitProcessRunner _runner;

    public CommitLogReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <summary>
    /// Field order. <b>This order must change together with <see cref="FieldCount"/> and the parser.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Field separator is <c>%x00</c>; with <c>-z</c> records are also separated by NUL. This
    /// creates no ambiguity because <b>no field can contain a NUL</b> — git explicitly rejects
    /// a NUL byte in a commit message (<c>a NUL byte in commit log message not allowed</c>,
    /// measured). So the whole stream is a flat sequence of NUL-separated chunks and can safely
    /// be grouped by a fixed field count.
    /// </para>
    /// <para>
    /// <c>%aI</c> / <c>%cI</c> are strict ISO-8601, with a timezone offset:
    /// the local time the commit was made is preserved.
    /// </para>
    /// </remarks>
    private const string Format =
        "%H%x00%P%x00%an%x00%ae%x00%aI%x00%cn%x00%ce%x00%cI%x00%D%x00%e%x00%s%x00%b";

    private const int FieldCount = 12;

    public async Task<IReadOnlyList<CommitInfo>> ReadAsync(
        string workingDirectory,
        CommitLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        GitResult result = await _runner
            .RunCheckedAsync(BuildCommand(workingDirectory, query), cancellationToken)
            .ConfigureAwait(false);

        // A split that PRESERVES empty chunks is required: a commit with no body produces an
        // empty field, and dropping it would shift every field after it.
        string[] fields = result.SplitStandardOutputAtNulPreservingEmpty();

        List<CommitInfo> commits = new(fields.Length / FieldCount);
        StringPool pool = new();

        for (int offset = 0; offset + FieldCount <= fields.Length; offset += FieldCount)
        {
            commits.Add(ParseRecord(fields.AsSpan(offset, FieldCount), pool));
        }

        return commits;
    }

    public async IAsyncEnumerable<CommitInfo> StreamAsync(
        string workingDirectory,
        CommitLogQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string[] window = new string[FieldCount];
        StringPool pool = new();
        int filled = 0;

        await foreach (string field in _runner
                           .StreamNulSeparatedAsync(BuildCommand(workingDirectory, query), cancellationToken)
                           .ConfigureAwait(false))
        {
            window[filled++] = field;

            if (filled < FieldCount)
            {
                continue;
            }

            filled = 0;
            yield return ParseRecord(window, pool);
        }

        // If filled > 0 the stream ended with a partial record. This means the format string
        // and FieldCount don't match — report it instead of silently swallowing it.
        if (filled > 0)
        {
            throw new InvalidOperationException(
                $"The git log output ended with an incomplete record ({filled}/{FieldCount} fields). "
                + "The format string and the field count may not match.");
        }
    }

    /// <summary>
    /// Is there any pattern filter at all?
    /// </summary>
    /// <remarks>
    /// The two global switches are only added when something is actually being matched. They are
    /// harmless on their own, but an argument list that carries them without a pattern is a
    /// puzzle for whoever reads the command log.
    /// </remarks>
    private static bool HasPatternFilter(CommitLogQuery query) =>
        !string.IsNullOrWhiteSpace(query.MessageContains)
        || !string.IsNullOrWhiteSpace(query.Author)
        || !string.IsNullOrWhiteSpace(query.Committer)
        || !string.IsNullOrWhiteSpace(query.DiffContains);

    private static GitCommand BuildCommand(string workingDirectory, CommitLogQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        List<string> arguments = ["log", "-z", $"--format={Format}"];

        if (query.TopologicalOrder)
        {
            arguments.Add("--topo-order");
        }

        if (query.IncludeAllRefs)
        {
            arguments.Add("--all");
        }

        if (query.FirstParentOnly)
        {
            arguments.Add("--first-parent");
        }

        if (query.MaxCount is { } maxCount)
        {
            arguments.Add($"--max-count={maxCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (query.Skip > 0)
        {
            arguments.Add($"--skip={query.Skip.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            arguments.Add($"--author={query.Author}");
        }

        if (!string.IsNullOrWhiteSpace(query.Committer))
        {
            arguments.Add($"--committer={query.Committer}");
        }

        if (!string.IsNullOrWhiteSpace(query.MessageContains))
        {
            arguments.Add($"--grep={query.MessageContains}");
        }

        // -G rather than -S: a commit that only MOVES a line containing the text is a hit for the
        // person searching for it, and -S misses exactly that case (measured).
        if (!string.IsNullOrWhiteSpace(query.DiffContains))
        {
            arguments.Add($"-G{query.DiffContains}");
        }

        // 🔴 Both switches are query-wide in git; they are added once, after the patterns, so it
        // is visible that they govern all of them. Adding --fixed-strings next to a single filter
        // was how the author filter silently changed meaning.
        if (query.LiteralPatterns && HasPatternFilter(query))
        {
            arguments.Add("--fixed-strings");
        }

        if (query.IgnoreCase && HasPatternFilter(query))
        {
            arguments.Add("--regexp-ignore-case");
        }

        if (!string.IsNullOrWhiteSpace(query.Revision))
        {
            arguments.Add(query.Revision);
        }

        // The `--` separator is mandatory: file paths starting with a dash or colliding with a
        // ref name would otherwise be mistaken for a revision.
        if (query.Paths.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(query.Paths.Select(path => path.Value));
        }

        return new GitCommand
        {
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            // Large histories can take a while; the default 2-minute timeout may not be enough.
            Timeout = TimeSpan.FromMinutes(10),
        };
    }

    private static CommitInfo ParseRecord(ReadOnlySpan<string> fields, StringPool pool) => new()
    {
        Id = CommitId.Parse(fields[0]),
        Parents = ParseParents(fields[1]),
        Author = new Signature(pool.Intern(fields[2]), pool.Intern(fields[3]), ParseTimestamp(fields[4])),
        Committer = new Signature(pool.Intern(fields[5]), pool.Intern(fields[6]), ParseTimestamp(fields[7])),
        Refs = ParseRefs(fields[8]),
        Encoding = pool.Intern(fields[9]),
        Subject = fields[10],
        // git puts the record separator after the last field; the body can end with a newline.
        Body = fields[11].TrimEnd('\n'),
    };

    /// <summary>
    /// Collapses short strings repeated over the course of a read to a single instance (P09-T08).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the measurement showed this was needed:</b> in a repository with 500,000 commits,
    /// the author and committer fields held <b>46 MB</b>, but the number of unique values was
    /// only <b>2</b> (P09-T04 measurement, the "text (MB)" line of the `--bench` output). In
    /// real repositories this number is on the order of tens too — a project's authors don't
    /// vary as much as its commits.
    /// </para>
    /// <para>
    /// ⚠️ <c>string.Intern</c> is NOT USED: the runtime's intern pool lives for the entire
    /// process lifetime and is never freed. Putting text there that should be freed when the
    /// repository closes would produce a worse version of the thing we're trying to fix — an
    /// unrecoverable leak. This pool becomes garbage once the read is done.
    /// </para>
    /// <para>
    /// Subject and body are not interned: they are genuinely unique per commit, and the pool
    /// would just be a dictionary with zero hits.
    /// </para>
    /// </remarks>
    private sealed class StringPool
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string Intern(string value)
        {
            if (value.Length == 0)
            {
                return string.Empty;
            }

            if (_values.TryGetValue(value, out string? existing))
            {
                return existing;
            }

            _values[value] = value;
            return value;
        }
    }

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

    /// <summary>
    /// Parses the <c>%D</c> field — comma-separated ref names.
    /// </summary>
    /// <remarks>
    /// Example: <c>HEAD -> main, origin/main, tag: v1.0</c>. The symbolic arrow and the
    /// <c>tag:</c> prefix are display-only; the raw name is preserved here, interpretation is
    /// Phase 03's job.
    /// </remarks>
    private static IReadOnlyList<string> ParseRefs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        // %aI produces strict ISO-8601. If it can't be parsed, we return the Unix epoch instead
        // of throwing: failing to read the entire history over one bad date is worse than that
        // one commit's date looking wrong. (Real repositories do have bad dates.)
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UnixEpoch;
    }
}
