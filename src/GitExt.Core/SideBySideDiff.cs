using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// A single row in the side-by-side view (P04-T10).
/// </summary>
/// <remarks>
/// One side can be <see langword="null"/>: a <b>filler</b> is placed opposite a line with no
/// counterpart. On a hunk header row both sides are <see langword="null"/>.
/// </remarks>
public sealed record SideBySideRow
{
    /// <summary>The left side (the old state): a context or removed line.</summary>
    public DiffLine? Left { get; init; }

    /// <summary>The right side (the new state): a context or added line.</summary>
    public DiffLine? Right { get; init; }

    /// <summary>The hunk header; <see langword="null"/> on a content row.</summary>
    public string? HunkHeader { get; init; }

    /// <summary>
    /// The index of the hunk the row belongs to; <c>-1</c> when a single hunk is being converted.
    /// </summary>
    /// <remarks>
    /// Partial staging (P05-T11) uses this when turning a selection in side-by-side mode into a
    /// <see cref="PatchSelection"/>. The row order on screen is not enough: one side-by-side row can
    /// carry <b>two different</b> unified lines.
    /// </remarks>
    public int HunkIndex { get; init; } = -1;

    /// <summary>The left line's index within the hunk; <c>-1</c> when the left side is empty.</summary>
    public int LeftIndex { get; init; } = -1;

    /// <summary>The right line's index within the hunk; <c>-1</c> when the right side is empty.</summary>
    public int RightIndex { get; init; } = -1;

    public bool IsHunkHeader => HunkHeader is not null;

    public override string ToString() =>
        IsHunkHeader ? HunkHeader! : $"{Left?.Content} │ {Right?.Content}";
}

/// <summary>
/// Converts unified diff lines into a <b>side-by-side</b> layout (P04-T10).
/// </summary>
/// <remarks>
/// <para>
/// This conversion is deliberately in the <b>core layer</b>: it is a pure data transformation with no
/// UI dependency, and its test can be written without setting up any UI.
/// </para>
/// <para>
/// <b>The alignment uses the SAME matching as the intra-line highlighting</b>
/// (<see cref="InlineDiff.MatchLines"/>). Writing a second matching would mean the highlighted pair
/// and the pair shown side by side could differ — two contradictory answers on the same screen.
/// </para>
/// <para>
/// <b>No reference:</b> GitExtensions has <b>no</b> built-in side-by-side view; it hands the job to an
/// external difftool (difftastic) and parses its two-column output. The one thing taken from there is
/// this: a side <b>may have no line</b>, meaning a filler is a real state.
/// </para>
/// </remarks>
public static class SideBySideDiff
{
    /// <summary>Converts all of a file's hunks into a side-by-side layout.</summary>
    public static IReadOnlyList<SideBySideRow> Build(FileDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        List<SideBySideRow> rows = [];

        for (int hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
        {
            DiffHunk hunk = diff.Hunks[hunkIndex];

            rows.Add(new SideBySideRow { HunkHeader = hunk.Header, HunkIndex = hunkIndex });
            rows.AddRange(Build(hunk, hunkIndex));
        }

        return rows;
    }

    /// <summary>Converts a single hunk into a side-by-side layout (no header row is produced).</summary>
    public static IReadOnlyList<SideBySideRow> Build(DiffHunk hunk, int hunkIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(hunk);

        List<SideBySideRow> rows = [];
        IReadOnlyList<DiffLine> lines = hunk.Lines;

        int index = 0;

        while (index < lines.Count)
        {
            DiffLine line = lines[index];

            if (line.Kind == DiffLineKind.Context)
            {
                // A context line is the same on both sides; a single object is enough (it carries both
                // line numbers).
                rows.Add(new SideBySideRow
                {
                    Left = line,
                    Right = line,
                    HunkIndex = hunkIndex,
                    LeftIndex = index,
                    RightIndex = index,
                });

                index++;
                continue;
            }

            // In a git unified diff a change block always arrives as "removals first, then additions";
            // the block boundary is a context line.
            int removedStart = index;

            while (index < lines.Count && lines[index].Kind == DiffLineKind.Removed)
            {
                index++;
            }

            int addedStart = index;

            while (index < lines.Count && lines[index].Kind == DiffLineKind.Added)
            {
                index++;
            }

            AppendBlock(
                rows,
                lines,
                removedStart,
                addedStart - removedStart,
                addedStart,
                index - addedStart,
                hunkIndex);
        }

        return rows;
    }

    /// <summary>
    /// Lays a removed/added block out into rows.
    /// </summary>
    /// <remarks>
    /// <b>Unmatched lines are NOT put side by side.</b> The matching algorithm has already decided
    /// those two lines are not counterparts; putting them side by side anyway would be telling the
    /// user "these correspond". Correctness is not given up to save space — a filler row is left
    /// instead.
    /// </remarks>
    private static void AppendBlock(
        List<SideBySideRow> rows,
        IReadOnlyList<DiffLine> lines,
        int removedStart,
        int removedCount,
        int addedStart,
        int addedCount,
        int hunkIndex)
    {
        if (removedCount == 0 || addedCount == 0)
        {
            // A one-sided block: the opposite side is left empty.
            for (int i = 0; i < removedCount; i++)
            {
                rows.Add(Left(lines, removedStart + i, hunkIndex));
            }

            for (int i = 0; i < addedCount; i++)
            {
                rows.Add(Right(lines, addedStart + i, hunkIndex));
            }

            return;
        }

        string[] removed = new string[removedCount];
        string[] added = new string[addedCount];

        for (int i = 0; i < removedCount; i++)
        {
            removed[i] = lines[removedStart + i].Content;
        }

        for (int i = 0; i < addedCount; i++)
        {
            added[i] = lines[addedStart + i].Content;
        }

        IReadOnlyList<(int Removed, int Added)> pairs = InlineDiff.MatchLines(removed, added);

        int nextRemoved = 0;
        int nextAdded = 0;

        foreach ((int pairedRemoved, int pairedAdded) in pairs)
        {
            // The unmatched lines before the pair: removals first, then additions.
            while (nextRemoved < pairedRemoved)
            {
                rows.Add(Left(lines, removedStart + nextRemoved++, hunkIndex));
            }

            while (nextAdded < pairedAdded)
            {
                rows.Add(Right(lines, addedStart + nextAdded++, hunkIndex));
            }

            rows.Add(new SideBySideRow
            {
                Left = lines[removedStart + pairedRemoved],
                Right = lines[addedStart + pairedAdded],
                HunkIndex = hunkIndex,
                LeftIndex = removedStart + pairedRemoved,
                RightIndex = addedStart + pairedAdded,
            });

            nextRemoved = pairedRemoved + 1;
            nextAdded = pairedAdded + 1;
        }

        while (nextRemoved < removedCount)
        {
            rows.Add(Left(lines, removedStart + nextRemoved++, hunkIndex));
        }

        while (nextAdded < addedCount)
        {
            rows.Add(Right(lines, addedStart + nextAdded++, hunkIndex));
        }
    }

    private static SideBySideRow Left(IReadOnlyList<DiffLine> lines, int index, int hunkIndex) =>
        new() { Left = lines[index], HunkIndex = hunkIndex, LeftIndex = index };

    private static SideBySideRow Right(IReadOnlyList<DiffLine> lines, int index, int hunkIndex) =>
        new() { Right = lines[index], HunkIndex = hunkIndex, RightIndex = index };
}
