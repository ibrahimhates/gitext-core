using System.Globalization;
using System.Text;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Parses <c>git diff --raw -z --patch</c> output (P04-T02).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why both <c>--raw</c> and <c>--patch</c> in a single call?</b> Measured:
/// the <c>diff --git a/… b/…</c> header <b>cannot be parsed</b> in general — for paths
/// containing spaces there's no safe way to split the two paths (<c>a/sub dir/b -&gt; c.txt
/// b/sub dir/b -&gt; c.txt</c>), and non-ASCII names get quoted with C-style octal escapes. So
/// paths, modes, blobs, and the change type are read <b>only from the <c>--raw -z</c>
/// section</b>; the patch section is parsed <b>only for hunk content</b>.
/// </para>
/// <para>
/// <b>Matching happens in two stages.</b> When the counts match, matching is done <b>by
/// order</b> — confirmed by measurement: scanned <b>700 commits</b> in the git/git repository,
/// the counts matched every single time and the ordering came out the same. When the counts
/// don't match (measured cause: <c>--ignore-blank-lines</c> leaves the file in the raw section
/// but produces no patch block), matching falls back to the <b>blob ids</b> in the
/// <c>index</c> line. If neither works, <see cref="DiffParseException"/> — attaching hunks to
/// the wrong file is silent data corruption.
/// </para>
/// <para>
/// <b>Encoding (P04-T07).</b> The input must have been read <b>losslessly</b>
/// (<see cref="Git.GitResult.GetStandardOutputLossless"/>): <c>git diff</c> output is not in a
/// single encoding — headers are ASCII, line content is in the <b>file's own bytes</b>.
/// Paths are re-decoded as UTF-8, line content with the given encoding.
/// </para>
/// </remarks>
public static class DiffParser
{
    /// <summary>
    /// Fixed fields of a record in the raw section: old mode, new mode, old blob, new blob, status.
    /// </summary>
    private const int RawFieldCount = 5;

    /// <summary>
    /// Converts combined <c>--raw -z --patch</c> output into file diffs.
    /// </summary>
    /// <param name="output">Combined <c>git diff</c> output.</param>
    /// <param name="inlineSegments">
    /// Should inline segments also be computed (P04-T05)?
    /// </param>
    /// <param name="maximumChangedLines">
    /// A file with more changed lines than this <b>has its content skipped</b>; 0 or negative
    /// means no limit (P04-T06).
    /// </param>
    /// <param name="contentEncoding">
    /// Encoding of line content; <see langword="null"/> means UTF-8 (P04-T07).
    /// </param>
    public static IReadOnlyList<FileDiff> Parse(
        string output,
        bool inlineSegments = false,
        int maximumChangedLines = 0,
        Encoding? contentEncoding = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        Encoding content = contentEncoding ?? Encoding.UTF8;

        if (output.Length == 0)
        {
            return [];
        }

        (List<RawRecord> records, int position) = ParseRawSection(output);

        if (records.Count == 0)
        {
            return [];
        }

        // When `--numstat` was requested it comes right after the raw section and gives the
        // number of changed lines per file without producing content — the size guard relies
        // on this.
        (List<NumStatRecord> stats, int patchStart) = ParseNumStatSection(output, position);

        AttachStats(records, stats);

        List<PatchBlock> blocks = SplitPatchBlocks(
            output, patchStart, inlineSegments, records, maximumChangedLines, content);

        return blocks.Count == records.Count
            ? MatchByOrder(records, blocks, maximumChangedLines)
            : MatchByBlob(records, blocks, maximumChangedLines);
    }

    /// <summary>
    /// Reads the <c>--numstat -z</c> section.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED — the format differs for renames.</b> A normal record is a single token
    /// shaped like <c>added⇥removed⇥path</c>, whereas for a rename/copy the <b>path field is
    /// left empty</b> and the old and new paths arrive as <b>separate NUL tokens</b>
    /// (<c>0⇥0⇥</c> + <c>old.txt</c> + <c>new.txt</c>). Counts are <c>-</c> for a binary file.
    /// </remarks>
    private static (List<NumStatRecord> Stats, int PatchStart) ParseNumStatSection(
        string output,
        int position)
    {
        List<NumStatRecord> stats = [];

        while (position < output.Length && output[position] != '\0')
        {
            int probe = position;

            if (!TryReadToken(output, ref probe, out string token))
            {
                break;
            }

            string[] fields = token.Split('\t');

            if (fields.Length < 3)
            {
                // numstat was not requested: from here on it's the patch section.
                break;
            }

            position = probe;

            if (fields[2].Length == 0)
            {
                // Rename: the two paths are separate tokens.
                TryReadToken(output, ref position, out _);
                TryReadToken(output, ref position, out _);
            }

            stats.Add(new NumStatRecord(ParseCount(fields[0]), ParseCount(fields[1])));
        }

        while (position < output.Length && output[position] == '\0')
        {
            position++;
        }

        return (stats, position);
    }

    /// <summary>Comes as <c>-</c> for a binary file; means there's no count.</summary>
    private static int? ParseCount(string value) =>
        int.TryParse(value, CultureInfo.InvariantCulture, out int count) ? count : null;

    /// <summary>
    /// Matches numstat records to raw records.
    /// </summary>
    /// <remarks>
    /// Both arrive in the same order. If the counts don't agree, no matching is done — a
    /// fabricated alignment would mean writing the wrong line count against the wrong file.
    /// </remarks>
    private static void AttachStats(List<RawRecord> records, List<NumStatRecord> stats)
    {
        if (stats.Count != records.Count)
        {
            return;
        }

        for (int i = 0; i < records.Count; i++)
        {
            records[i] = records[i] with { Added = stats[i].Added, Removed = stats[i].Removed };
        }
    }

    /// <summary>
    /// Matches by order when the counts agree.
    /// </summary>
    /// <remarks>
    /// This is the common case, <b>confirmed on 700 real commits</b>: the raw record count and
    /// the patch block count matched every time, and the ordering came out the same.
    /// </remarks>
    private static FileDiff[] MatchByOrder(
        List<RawRecord> records,
        List<PatchBlock> blocks,
        int maximumChangedLines)
    {
        FileDiff[] diffs = new FileDiff[records.Count];

        for (int i = 0; i < records.Count; i++)
        {
            diffs[i] = Build(records[i], blocks[i], maximumChangedLines);
        }

        return diffs;
    }

    /// <summary>
    /// Matches by blob ids when the counts don't agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MEASURED:</b> with <c>--ignore-blank-lines</c>, git leaves a file whose only change is
    /// blank lines <b>in the raw section but produces no patch block for it</b>. (This differs
    /// from <c>-w</c>: there, once the file becomes identical it drops out of both sections, so
    /// the counts stay aligned.)
    /// </para>
    /// <para>
    /// Trusting order in that situation would attach hunks to the <b>wrong file</b>. The
    /// <c>index &lt;old&gt;..&lt;new&gt;</c> line in the patch block carries the same ids as the
    /// blobs in the raw record; matching is done from there. An unmatched record ends up with
    /// no hunks (meaning the change was genuinely ignored); if there's an unmatched <b>block</b>
    /// instead, processing stops.
    /// </para>
    /// </remarks>
    private static FileDiff[] MatchByBlob(
        List<RawRecord> records,
        List<PatchBlock> blocks,
        int maximumChangedLines)
    {
        PatchBlock?[] matched = new PatchBlock?[records.Count];

        foreach (PatchBlock block in blocks)
        {
            int target = -1;

            for (int r = 0; r < records.Count; r++)
            {
                if (matched[r] is not null || !block.MatchesBlobs(records[r].OldBlob, records[r].NewBlob))
                {
                    continue;
                }

                target = r;
                break;
            }

            if (target < 0)
            {
                throw new DiffParseException(
                    $"A patch block could not be matched to any raw record ({records.Count} records, "
                    + $"{blocks.Count} blocks). Stopping is better than attaching hunks to the wrong file.");
            }

            matched[target] = block;
        }

        FileDiff[] diffs = new FileDiff[records.Count];

        for (int i = 0; i < records.Count; i++)
        {
            diffs[i] = Build(records[i], matched[i] ?? PatchBlock.Empty, maximumChangedLines);
        }

        return diffs;
    }

    /// <summary>
    /// Reads the NUL-delimited raw section and returns the index where the patch section starts.
    /// </summary>
    /// <remarks>
    /// Manual traversal is needed instead of splitting (<c>Split</c>): knowing where the patch
    /// text starts requires preserving the <b>byte position</b>. Searching for "where the first
    /// <c>diff --git</c> occurs" would be wrong — a file name could contain that text.
    /// </remarks>
    private static (List<RawRecord> Records, int PatchStart) ParseRawSection(string output)
    {
        List<RawRecord> records = [];
        int position = 0;

        while (position < output.Length && output[position] == ':')
        {
            if (!TryReadToken(output, ref position, out string meta))
            {
                break;
            }

            string[] fields = meta.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < RawFieldCount)
            {
                // Unexpected format: stop rather than silently producing wrong data.
                throw new DiffParseException($"Could not parse the raw record: '{meta}'");
            }

            string status = fields[4];

            // Rename and copy carry TWO paths (measured: `R100<NUL>old<NUL>new`).
            int pathCount = status[0] is 'R' or 'C' ? 2 : 1;

            string[] paths = new string[pathCount];

            for (int i = 0; i < pathCount; i++)
            {
                if (!TryReadToken(output, ref position, out paths[i]))
                {
                    throw new DiffParseException("The raw record paths are incomplete.");
                }
            }

            records.Add(new RawRecord(
                OldMode: fields[0].TrimStart(':'),
                NewMode: fields[1],
                OldBlob: fields[2],
                NewBlob: fields[3],
                Status: status,
                Paths: paths));
        }

        // The empty token(s) separating the raw section from the patch.
        while (position < output.Length && output[position] == '\0')
        {
            position++;
        }

        return (records, position);
    }

    private static bool TryReadToken(string text, ref int position, out string token)
    {
        int end = text.IndexOf('\0', position);

        if (end < 0)
        {
            token = string.Empty;
            return false;
        }

        token = text[position..end];
        position = end + 1;
        return true;
    }

    /// <summary>
    /// Splits the patch section into blocks at <c>diff --git</c> lines.
    /// </summary>
    private static List<PatchBlock> SplitPatchBlocks(
        string output,
        int start,
        bool inlineSegments,
        List<RawRecord> records,
        int maximumChangedLines,
        Encoding contentEncoding)
    {
        List<PatchBlock> blocks = [];

        if (start >= output.Length)
        {
            return blocks;
        }

        string[] lines = output[start..].Split('\n');

        List<string>? current = null;

        foreach (string line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal)
                || line.StartsWith("diff --cc ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    blocks.Add(Create(current));
                }

                current = [];
                continue;
            }

            current?.Add(line);
        }

        if (current is not null)
        {
            blocks.Add(Create(current));
        }

        return blocks;

        // A file over the limit gets NO lines produced at all: creating 800,000 DiffLine
        // objects is exactly what we want to avoid (per-object overhead measured in Phase 03).
        PatchBlock Create(List<string> lines)
        {
            int index = blocks.Count;

            bool skip = maximumChangedLines > 0
                && index < records.Count
                && records[index].ChangedLines > maximumChangedLines;

            return new PatchBlock(lines, inlineSegments, skip, contentEncoding);
        }
    }

    private static FileDiff Build(RawRecord record, PatchBlock block, int maximumChangedLines)
    {
        // Paths come ONLY from the raw section; paths in the patch header cannot be parsed.
        // Paths are UTF-8: git gives raw bytes with `-z` and file names in the repository are UTF-8.
        RepositoryPath newPath = RepositoryPath.Parse(Reencode(record.Paths[^1], Encoding.UTF8));
        RepositoryPath? oldPath = record.Paths.Length > 1
            ? RepositoryPath.Parse(Reencode(record.Paths[0], Encoding.UTF8))
            : null;

        // A deleted file has no "new path"; the field carries the old path instead (documented on the model).
        return new FileDiff
        {
            Path = newPath,
            OldPath = oldPath,
            Change = ParseStatus(record.Status),
            SimilarityScore = ParseSimilarity(record.Status),
            OldMode = record.OldMode,
            NewMode = record.NewMode,
            OldBlob = ParseBlob(record.OldBlob),
            NewBlob = ParseBlob(record.NewBlob),
            IsBinary = block.IsBinary,
            StatAdded = record.Added,
            StatRemoved = record.Removed,
            IsTooLarge = maximumChangedLines > 0 && record.ChangedLines > maximumChangedLines,
            Hunks = block.Hunks,
        };
    }

    /// <summary>
    /// Re-decodes losslessly read text with the target encoding.
    /// </summary>
    /// <remarks>
    /// The input must have been produced with <see cref="Git.GitResult.GetStandardOutputLossless"/>:
    /// each character corresponds to exactly one byte. For ASCII text the result is unchanged,
    /// so headers and markers are unaffected.
    /// </remarks>
    private static string Reencode(string lossless, Encoding target)
    {
        if (lossless.Length == 0 || ReferenceEquals(target, Encoding.Latin1))
        {
            return lossless;
        }

        // Conversion is unnecessary when it's pure ASCII — the common case.
        bool ascii = true;

        foreach (char c in lossless)
        {
            if (c > 0x7F)
            {
                ascii = false;
                break;
            }
        }

        return ascii ? lossless : target.GetString(Encoding.Latin1.GetBytes(lossless));
    }

    private static CommitId ParseBlob(string value) =>
        // A nonexistent side comes as all zeros (`0000000`); treating that as an id would be misleading.
        value.All(c => c == '0') || !CommitId.TryParse(value, out CommitId id)
            ? default
            : id;

    private static FileChangeKind ParseStatus(string status) => status[0] switch
    {
        'A' => FileChangeKind.Added,
        'M' => FileChangeKind.Modified,
        'D' => FileChangeKind.Deleted,
        'R' => FileChangeKind.Renamed,
        'C' => FileChangeKind.Copied,
        'T' => FileChangeKind.TypeChanged,
        'U' => FileChangeKind.Unmerged,
        _ => FileChangeKind.Unmodified,
    };

    /// <summary>Similarity percentage following the status letter (<c>R100</c> → 100).</summary>
    private static int? ParseSimilarity(string status) =>
        status.Length > 1 && int.TryParse(status[1..], CultureInfo.InvariantCulture, out int score)
            ? score
            : null;

    private sealed record RawRecord(
        string OldMode,
        string NewMode,
        string OldBlob,
        string NewBlob,
        string Status,
        string[] Paths)
    {
        public int? Added { get; init; }

        public int? Removed { get; init; }

        /// <summary>Total changed lines; 0 if numstat is missing (limit cannot be applied).</summary>
        public int ChangedLines => (Added ?? 0) + (Removed ?? 0);
    }

    private readonly record struct NumStatRecord(int? Added, int? Removed);

    /// <summary>
    /// A single file's patch block — hunks and the binary flag.
    /// </summary>
    private sealed class PatchBlock
    {
        /// <summary>For records with no patch block: no hunks, not binary.</summary>
        public static PatchBlock Empty { get; } = new([], inlineSegments: false, skipContent: false, Encoding.UTF8);

        private readonly bool _inlineSegments;

        public PatchBlock(
            List<string> lines,
            bool inlineSegments,
            bool skipContent,
            Encoding contentEncoding)
        {
            _inlineSegments = inlineSegments;

            // A file over the limit gets NO lines produced at all: creating 800,000 DiffLine
            // objects is exactly what we want to avoid (per-object overhead measured in Phase 03).
            if (skipContent)
            {
                Hunks = [];

                foreach (string skipped in lines)
                {
                    if (skipped.StartsWith("Binary files ", StringComparison.Ordinal)
                        || skipped.StartsWith("GIT binary patch", StringComparison.Ordinal))
                    {
                        IsBinary = true;
                    }
                    else if (skipped.StartsWith("index ", StringComparison.Ordinal))
                    {
                        ReadIndexLine(skipped);
                    }
                }

                return;
            }

            List<DiffHunk> hunks = [];
            List<DiffLine>? currentLines = null;
            HunkHeader header = default;
            int oldLine = 0;
            int newLine = 0;


            foreach (string line in lines)
            {
                // Measured: for binary files this line appears instead of content, and there are no hunks at all.
                if (line.StartsWith("Binary files ", StringComparison.Ordinal)
                    || line.StartsWith("GIT binary patch", StringComparison.Ordinal))
                {
                    IsBinary = true;
                    continue;
                }

                if (line.StartsWith("index ", StringComparison.Ordinal))
                {
                    // `index <old>..<new> <mode>` — blob ids, used as the matching key when counts disagree.
                    ReadIndexLine(line);
                    continue;
                }

                if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    Flush(hunks, ref currentLines, header);

                    if (TryParseHunkHeader(line, out header))
                    {
                        currentLines = [];
                        oldLine = header.OldStart;
                        newLine = header.NewStart;
                    }

                    continue;
                }

                if (currentLines is null)
                {
                    continue;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                switch (line[0])
                {
                    case ' ':
                        currentLines.Add(new DiffLine(DiffLineKind.Context, Reencode(line[1..], contentEncoding))
                        {
                            OldLineNumber = oldLine++,
                            NewLineNumber = newLine++,
                        });
                        break;

                    case '+':
                        currentLines.Add(new DiffLine(DiffLineKind.Added, Reencode(line[1..], contentEncoding))
                        {
                            NewLineNumber = newLine++,
                        });
                        break;

                    case '-':
                        currentLines.Add(new DiffLine(DiffLineKind.Removed, Reencode(line[1..], contentEncoding))
                        {
                            OldLineNumber = oldLine++,
                        });
                        break;

                    case '\\':
                        // `\ No newline at end of file` — MEASURED: not a line of its own, it
                        // belongs to the PRECEDING line and can appear twice within the same hunk.
                        if (currentLines.Count > 0)
                        {
                            currentLines[^1] = currentLines[^1] with { EndsWithoutNewline = true };
                        }

                        break;

                    default:
                        // `index …`, `old mode`, `new mode`, `--- `, `+++ `, `similarity index`,
                        // `rename from/to`: this information comes from the raw section and is ignored here.
                        break;
                }
            }

            Flush(hunks, ref currentLines, header);

            Hunks = hunks;
        }

        public IReadOnlyList<DiffHunk> Hunks { get; }

        public bool IsBinary { get; }

        private string _indexOld = string.Empty;
        private string _indexNew = string.Empty;

        /// <summary>
        /// Do the blobs in this block's <c>index</c> line match the given record?
        /// </summary>
        /// <remarks>
        /// Prefix comparison is used because the abbreviation lengths can differ between the two
        /// outputs. If there's no <c>index</c> line (rename/mode change), matching can't be done.
        /// </remarks>
        public bool MatchesBlobs(string oldBlob, string newBlob) =>
            _indexOld.Length > 0
            && _indexNew.Length > 0
            && PrefixEquals(_indexOld, oldBlob)
            && PrefixEquals(_indexNew, newBlob);

        private static bool PrefixEquals(string left, string right)
        {
            int length = Math.Min(left.Length, right.Length);

            return length > 0
                && left.AsSpan(0, length).SequenceEqual(right.AsSpan(0, length));
        }

        private void ReadIndexLine(string line)
        {
            ReadOnlySpan<char> rest = line.AsSpan("index ".Length);
            int separator = rest.IndexOf("..", StringComparison.Ordinal);

            if (separator < 0)
            {
                return;
            }

            _indexOld = rest[..separator].ToString();

            ReadOnlySpan<char> tail = rest[(separator + 2)..];
            int space = tail.IndexOf(' ');

            _indexNew = (space < 0 ? tail : tail[..space]).ToString();
        }

        private void Flush(List<DiffHunk> hunks, ref List<DiffLine>? lines, HunkHeader header)
        {
            if (lines is null)
            {
                return;
            }

            // Inline segments are computed over the EXACT line text; git's --word-diff loses
            // which side an empty line belongs to (measured).
            IReadOnlyList<DiffLine> finalLines = _inlineSegments ? InlineDiff.Annotate(lines) : lines;

            hunks.Add(new DiffHunk
            {
                Header = header.Raw,
                OldStart = header.OldStart,
                OldLength = header.OldLength,
                NewStart = header.NewStart,
                NewLength = header.NewLength,
                Section = header.Section,
                Lines = finalLines,
            });

            lines = null;
        }
    }

    private readonly record struct HunkHeader(
        string Raw,
        int OldStart,
        int OldLength,
        int NewStart,
        int NewLength,
        string Section);

    /// <summary>
    /// Parses an <c>@@ -a,b +c,d @@ context</c> line.
    /// </summary>
    /// <remarks>
    /// The length <b>may be omitted</b>: for a single-line hunk git writes <c>@@ -1 +1 @@</c>,
    /// and the missing length means <c>1</c>. Defaulting to 0 would shift line numbers.
    /// </remarks>
    private static bool TryParseHunkHeader(string line, out HunkHeader header)
    {
        header = default;

        int open = line.IndexOf("@@ ", StringComparison.Ordinal);
        int close = line.IndexOf(" @@", StringComparison.Ordinal);

        if (open != 0 || close <= open)
        {
            return false;
        }

        string ranges = line[(open + 3)..close];
        string[] parts = ranges.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || parts[0][0] != '-' || parts[1][0] != '+')
        {
            return false;
        }

        (int oldStart, int oldLength) = ParseRange(parts[0][1..]);
        (int newStart, int newLength) = ParseRange(parts[1][1..]);

        // The context text in the header comes FROM THE FILE (the enclosing function line), i.e.
        // in the file's encoding. The raw form is kept; re-encoding happens on the caller's side.
        string section = line.Length > close + 3 ? line[(close + 3)..].Trim() : string.Empty;

        header = new HunkHeader(line, oldStart, oldLength, newStart, newLength, section);
        return true;
    }

    private static (int Start, int Length) ParseRange(string range)
    {
        int comma = range.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0)
        {
            // Length not written → 1 (this is how git writes a single-line hunk).
            return (ParseInt(range), 1);
        }

        return (ParseInt(range[..comma]), ParseInt(range[(comma + 1)..]));
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, CultureInfo.InvariantCulture, out int result) ? result : 0;
}

/// <summary>
/// Thrown when diff output is not in the expected format.
/// </summary>
/// <remarks>
/// Stopping was chosen over silently producing wrong data: attaching hunks to the wrong file
/// means showing the user <b>another file's changes</b>.
/// </remarks>
public sealed class DiffParseException : Exception
{
    public DiffParseException(string message)
        : base(message)
    {
    }

    public DiffParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DiffParseException()
    {
    }
}
