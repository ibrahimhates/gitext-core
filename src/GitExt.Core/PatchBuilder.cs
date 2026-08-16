using System.Text;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Which direction the patch will be applied in (P05-T04).
/// </summary>
public enum PatchDirection
{
    /// <summary>
    /// Forward: move the working tree change into the index (<c>git apply --cached</c>).
    /// </summary>
    Stage,

    /// <summary>
    /// Backward: undo the change in the index (<c>git apply --cached --reverse</c>).
    /// </summary>
    /// <remarks>
    /// In this direction the patch must come from <c>git diff --cached</c> — that is, the difference
    /// between the index and <c>HEAD</c>.
    /// </remarks>
    Unstage,
}

/// <summary>
/// Which lines are selected in a file diff (P05-T04).
/// </summary>
public sealed class PatchSelection
{
    private readonly HashSet<(int Hunk, int Line)> _lines;

    private PatchSelection(HashSet<(int Hunk, int Line)> lines)
    {
        _lines = lines;
    }

    /// <summary>Selects specific lines.</summary>
    public static PatchSelection Lines(IEnumerable<(int Hunk, int Line)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new PatchSelection([.. lines]);
    }

    /// <summary>Selects <b>all</b> the change lines of the given hunks.</summary>
    public static PatchSelection Hunks(FileDiff diff, params int[] hunkIndexes)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(hunkIndexes);

        HashSet<(int, int)> lines = [];

        foreach (int hunkIndex in hunkIndexes)
        {
            DiffHunk hunk = diff.Hunks[hunkIndex];

            for (int line = 0; line < hunk.Lines.Count; line++)
            {
                if (hunk.Lines[line].Kind != DiffLineKind.Context)
                {
                    lines.Add((hunkIndex, line));
                }
            }
        }

        return new PatchSelection(lines);
    }

    /// <summary>Selects every change line in the file.</summary>
    public static PatchSelection All(FileDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return Hunks(diff, [.. Enumerable.Range(0, diff.Hunks.Count)]);
    }

    public bool IsSelected(int hunkIndex, int lineIndex) => _lines.Contains((hunkIndex, lineIndex));

    public int Count => _lines.Count;
}

/// <summary>
/// Produces a patch that can be handed to <c>git apply</c> from the selected lines (P05-T04).
/// </summary>
/// <remarks>
/// <para>
/// <b>The riskiest code in this phase.</b> The risk distribution, as measurement made clear:
/// </para>
/// <list type="table">
/// <item><term>The numbers in the hunk header are wrong</term>
/// <description><c>error: corrupt patch</c> — git <b>rejects it</b>.</description></item>
/// <item><term>A context or removed line does not match the file</term>
/// <description><c>error: patch failed</c> — git <b>rejects it</b>.</description></item>
/// <item><term><b>The selection logic is wrong</b></term>
/// <description>Because the patch is <b>valid</b>, git accepts it and <b>silently wrong content</b>
/// is staged. Measured: unless an unselected <c>-</c> line is turned into context, that line
/// disappears from the index. <b>This is what the tests are really aimed at.</b></description></item>
/// </list>
/// <para>
/// <b><c>--recount</c> is deliberately NOT USED.</b> Measured: it corrects wrong numbers and gets
/// the patch accepted. That would switch off the one validation git offers us — and a wrong count is
/// usually the symptom of a deeper logic error.
/// </para>
/// </remarks>
public static class PatchBuilder
{
    /// <summary>
    /// Produces a patch from the selected lines; <see langword="null"/> when there is nothing to
    /// select.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The line conversion rules — <b>symmetric by direction</b>:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Stage:</b> an unselected <c>+</c> line is <b>skipped</b> (it is not in
    /// the index yet, and must not end up there), an unselected <c>-</c> line is <b>turned into
    /// context</b> (it is in the index, and must stay).</description></item>
    /// <item><description><b>Unstage:</b> exactly the reverse — an unselected <c>-</c> line is
    /// skipped, an unselected <c>+</c> line is turned into context. Because the patch will be
    /// applied in reverse and the "old/new" roles swap.</description></item>
    /// </list>
    /// </remarks>
    public static string? Build(FileDiff diff, PatchSelection selection, PatchDirection direction)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Count == 0 || !diff.HasHunks)
        {
            return null;
        }

        StringBuilder body = new();
        int emitted = 0;
        int delta = 0;

        for (int hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
        {
            DiffHunk hunk = diff.Hunks[hunkIndex];

            List<(DiffLineKind Kind, DiffLine Line)> lines = [];
            bool hasChange = false;

            for (int lineIndex = 0; lineIndex < hunk.Lines.Count; lineIndex++)
            {
                DiffLine line = hunk.Lines[lineIndex];

                if (line.Kind == DiffLineKind.Context)
                {
                    lines.Add((DiffLineKind.Context, line));
                    continue;
                }

                if (selection.IsSelected(hunkIndex, lineIndex))
                {
                    lines.Add((line.Kind, line));
                    hasChange = true;
                    continue;
                }

                if (KeepAsContext(line.Kind, direction))
                {
                    lines.Add((DiffLineKind.Context, line));
                }

                // Otherwise the line is skipped entirely.
            }

            if (!hasChange)
            {
                // Nothing was selected from this hunk; it must not go into the patch.
                continue;
            }

            int oldCount = lines.Count(entry => entry.Kind != DiffLineKind.Added);
            int newCount = lines.Count(entry => entry.Kind != DiffLineKind.Removed);

            body.Append(FormatHunkHeader(hunk.OldStart, oldCount, hunk.OldStart + delta, newCount));
            body.Append('\n');

            foreach ((DiffLineKind kind, DiffLine line) in lines)
            {
                body.Append(Prefix(kind));
                body.Append(line.Content);
                body.Append('\n');

                // ⚠️ The marker comes AFTER the line and belongs to the line it refers to (measured
                // in P04-T01). If it is skipped, git either rejects the patch or adds a newline at
                // the end of the file.
                if (line.EndsWithoutNewline)
                {
                    body.Append("\\ No newline at end of file\n");
                }
            }

            delta += newCount - oldCount;
            emitted++;
        }

        if (emitted == 0)
        {
            return null;
        }

        return Header(diff) + body.ToString();
    }

    /// <summary>
    /// Should an unselected change line be preserved as context?
    /// </summary>
    private static bool KeepAsContext(DiffLineKind kind, PatchDirection direction) =>
        direction == PatchDirection.Stage
            ? kind == DiffLineKind.Removed
            : kind == DiffLineKind.Added;

    private static char Prefix(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added => '+',
        DiffLineKind.Removed => '-',
        _ => ' ',
    };

    /// <summary>
    /// Formats the hunk header.
    /// </summary>
    /// <remarks>
    /// For single-line sides git omits the count (<c>@@ -1 +1 @@</c>); the same format is produced
    /// so the patch can be compared against git's own output.
    /// </remarks>
    private static string FormatHunkHeader(int oldStart, int oldCount, int newStart, int newCount)
    {
        // An empty side is 0 lines long and its start is written one less (that is what git does).
        string old = oldCount == 1 ? $"-{oldStart}" : $"-{(oldCount == 0 ? oldStart - 1 : oldStart)},{oldCount}";
        string @new = newCount == 1 ? $"+{newStart}" : $"+{(newCount == 0 ? newStart - 1 : newStart)},{newCount}";

        return $"@@ {old} {@new} @@";
    }

    /// <summary>
    /// Produces the file header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MEASURED:</b> <c>git apply</c> accepts paths as <b>raw UTF-8</b>; the quoting/octal
    /// escaping from the reading side (P04-T02) is <b>not needed</b>. Names with spaces work
    /// unquoted too.
    /// </para>
    /// <para>
    /// <b>A patch is a SINGLE PIECE of Unicode text.</b> <c>DiffLine.Content</c> arrives from the
    /// parser already <b>decoded</b> with <c>DiffOptions.ContentEncoding</c> (P04-T07), and so does
    /// the path. The patch is therefore encoded to bytes with the file's encoding <b>once</b>.
    /// </para>
    /// <para>
    /// ⚠️ This was learned by measurement: when the content was assumed lossless (byte-per-character)
    /// and encoded with Latin-1, characters like <c>ı</c> were corrupted and <c>git apply</c>
    /// rejected the patch with <c>error: while searching for: ilk satir</c>.
    /// </para>
    /// </remarks>
    private static string Header(FileDiff diff)
    {
        string oldPath = (diff.OldPath ?? diff.Path).Value;
        string newPath = diff.Path.Value;

        return $"diff --git a/{oldPath} b/{newPath}\n--- a/{oldPath}\n+++ b/{newPath}\n";
    }
}
