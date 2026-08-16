using System.Text;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// The <b>display</b> settings for diff text (P04-T13).
/// </summary>
/// <remarks>
/// Affects only what appears on screen; the model content does not change (in Phase 05 the patch has
/// to be handed back to <c>git apply</c> verbatim).
/// </remarks>
public sealed record DiffTextOptions
{
    public static DiffTextOptions Default { get; } = new();

    /// <summary>How many columns a tab advances.</summary>
    public int TabWidth { get; init; } = 4;

    /// <summary>Should spaces and tabs be shown with visible characters?</summary>
    /// <remarks>
    /// GitExtensions has <b>a single switch</b> here too: spaces and tabs are turned on together rather
    /// than separately (<c>ShowSpaces = ShowTabs = show</c>).
    /// </remarks>
    public bool ShowWhitespace { get; init; }

    /// <summary>Is no transformation needed at all?</summary>
    internal bool IsIdentity => !ShowWhitespace && TabWidth <= 0;
}

/// <summary>
/// Prepares diff lines for display: expands tabs and optionally makes whitespace visible (P04-T13).
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED:</b> in Avalonia's <c>TextBlock</c> a tab is <b>not a tab stop</b>, it is drawn at a
/// fixed width of four spaces (<c>"ab\tc"</c> and <c>"ab    c"</c> come out the same width; with a
/// real tab stop it would be two spaces). There is also no property to set the tab width. That is why
/// the transformation is done <b>here</b>.
/// </para>
/// <para>
/// The transformation <b>preserves the segment boundaries</b> and the column counter carries on
/// across segments: tab stops are computed from the start of the line, not from the start of a
/// segment. Otherwise, tabs would align differently on lines that have intra-line highlighting.
/// </para>
/// </remarks>
public static class DiffTextFormatter
{
    /// <summary>The space marker (a middle dot).</summary>
    public const char SpaceMarker = '·';

    /// <summary>The tab marker (a double arrow) — the same as ICSharpCode/GitExtensions.</summary>
    public const char TabMarker = '»';

    /// <summary>Prepares a line's segments for display.</summary>
    public static IReadOnlyList<DiffSegment> Format(
        IReadOnlyList<DiffSegment> segments,
        DiffTextOptions options)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsIdentity || segments.Count == 0)
        {
            return segments;
        }

        DiffSegment[] result = new DiffSegment[segments.Count];
        int column = 0;

        for (int i = 0; i < segments.Count; i++)
        {
            string text = Expand(segments[i].Text, options, ref column);
            result[i] = segments[i] with { Text = text };
        }

        return result;
    }

    /// <summary>Prepares a single text for display (starting from the beginning of the line).</summary>
    public static string Format(string text, DiffTextOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsIdentity)
        {
            return text;
        }

        int column = 0;
        return Expand(text, options, ref column);
    }

    /// <summary>
    /// Expands the text and advances <paramref name="column"/>.
    /// </summary>
    private static string Expand(string text, DiffTextOptions options, ref int column)
    {
        if (text.Length == 0)
        {
            return text;
        }

        // The common case: no tab, and no whitespace display requested. It is checked first to avoid
        // producing a new string — the line count can run into the tens of thousands.
        if (!options.ShowWhitespace && !text.Contains('\t'))
        {
            column += text.Length;
            return text;
        }

        StringBuilder builder = new(text.Length + 8);

        foreach (char character in text)
        {
            if (character == '\t' && options.TabWidth > 0)
            {
                // Tab stop: pad up to the next multiple (at least 1, rather than not advancing at all).
                int width = options.TabWidth - (column % options.TabWidth);

                builder.Append(options.ShowWhitespace ? TabMarker : ' ');
                builder.Append(' ', width - 1);

                column += width;
                continue;
            }

            if (character == ' ' && options.ShowWhitespace)
            {
                builder.Append(SpaceMarker);
                column++;
                continue;
            }

            builder.Append(character);
            column++;
        }

        return builder.ToString();
    }
}
