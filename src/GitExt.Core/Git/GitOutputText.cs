using System.Text;

namespace GitExt.Core.Git;

/// <summary>
/// Converts a git command's diagnostic output (<c>stderr</c>) into <b>displayable</b> text (P05-T07).
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED:</b> git also redirects the hooks' <c>stdout</c> to <c>stderr</c>
/// (<c>stdout_to_stderr</c>): the lines of a <c>pre-commit</c> hook that writes with <c>echo</c>
/// come out on <c>stderr</c>. That is, all of the hook output is collected in a single channel
/// and <see cref="GitResult.StandardError"/> carries it <b>in full</b>.
/// </para>
/// <para>
/// ⚠️ <b>Hook output and git's own output CANNOT be told apart.</b> There is a single interleaved
/// stream and git puts no marker on the hook lines. That is why the UI must present this text as
/// the command's output, not as "hook output". (Measured: on a successful commit without hooks
/// <c>stderr</c> is <b>completely empty</b> — in practice a non-empty <c>stderr</c> points to a
/// hook, but this is not a guarantee.)
/// </para>
/// <para>
/// The text arrives raw: hooks can produce ANSI colour codes and <c>\r</c> progress output that
/// overwrites the line (measured, git passes both through as they are). Both look unreadable in
/// a text box.
/// </para>
/// </remarks>
public static class GitOutputText
{
    /// <summary>
    /// Upper bound on the number of displayable lines; for output above it the <b>tail</b> is kept.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED:</b> a hook writing 60,000 lines produced a 4.1 MB <c>stderr</c> and all of it
    /// was captured without trouble (167 ms, no deadlock). Capturing is not the problem;
    /// <b>displaying</b> is — putting 4 MB into a text box freezes the UI.
    /// The reason the tail is kept: hooks write the summary and the actual error line at the end.
    /// </remarks>
    public const int MaximumDisplayLines = 1000;

    /// <summary>
    /// Prepares the raw output for display and, if it exceeds the
    /// <see cref="MaximumDisplayLines"/> limit, keeps its <b>tail</b> and reports the number of
    /// dropped lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ANSI sequences are removed, <c>\r</c> is applied as overwriting, trailing whitespace on
    /// lines is trimmed. Trailing empty lines are dropped; leading ones are <b>preserved</b>
    /// (they may be the hook's own formatting).
    /// </para>
    /// <para>
    /// ⚠️ <b>Truncation happens BEFORE cleaning</b> and this is not a micro-optimisation:
    /// measured, on a 60,000-line (4 MB) hook output, cleaning first and truncating afterwards
    /// consumed <b>60 ms and 59.6 MB</b> — and that while showing an error, <b>on the UI
    /// thread</b>. Truncating first drops the same result to <b>2.1 ms and 1.0 MB</b>; 98% of it
    /// was being spent on lines that were going to be thrown away anyway.
    /// </para>
    /// </remarks>
    /// <param name="rawOutput">The raw <c>stderr</c> text.</param>
    /// <param name="droppedLines">Number of dropped lines; 0 if nothing was truncated.</param>
    public static string CleanForDisplay(string? rawOutput, out int droppedLines)
    {
        droppedLines = 0;

        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> tail = TakeLastLines(rawOutput, MaximumDisplayLines, out droppedLines);

        return CleanLines(tail);
    }

    /// <summary>
    /// Returns the last <paramref name="maximumLines"/> lines of the text without making a copy.
    /// </summary>
    /// <remarks>
    /// A single backwards pass counting newlines. No line <b>objects</b> are produced — that was
    /// the real cost.
    /// </remarks>
    private static ReadOnlySpan<char> TakeLastLines(
        string text,
        int maximumLines,
        out int droppedLines)
    {
        droppedLines = 0;

        int seen = 0;

        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            // A trailing newline produces an artificial empty line; we do not count it.
            if (i == text.Length - 1)
            {
                continue;
            }

            seen++;

            if (seen == maximumLines)
            {
                droppedLines = CountLines(text.AsSpan(0, i));
                return text.AsSpan(i + 1);
            }
        }

        return text.AsSpan();
    }

    private static int CountLines(ReadOnlySpan<char> text)
    {
        int lines = 1;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    /// <summary>
    /// Cleans the lines and joins them, dropping trailing empty lines.
    /// </summary>
    /// <remarks>
    /// No separate conversion is done for <c>\r\n</c>: <see cref="CleanLine"/> already treats
    /// <c>\r</c> as a cursor reset, so a <c>\r</c> at the end of a line falls away on its own.
    /// Measured — on 4 MB of CRLF output it meant one more full copy of the string.
    /// </remarks>
    private static string CleanLines(ReadOnlySpan<char> text)
    {
        List<string> lines = [];

        while (true)
        {
            int newline = text.IndexOf('\n');

            if (newline < 0)
            {
                lines.Add(CleanLine(text));
                break;
            }

            lines.Add(CleanLine(text[..newline]));
            text = text[(newline + 1)..];
        }

        // Trailing empty lines: git and hooks leave a separator line at the end.
        int end = lines.Count;
        while (end > 0 && lines[end - 1].Length == 0)
        {
            end--;
        }

        return string.Join('\n', lines.ToArray(), 0, end);
    }

    /// <summary>
    /// Cleans ANSI escape sequences and <c>\r</c> overwriting out of a single line.
    /// </summary>
    private static string CleanLine(ReadOnlySpan<char> line)
    {
        StringBuilder buffer = new(line.Length);
        int cursor = 0;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '\u001b')
            {
                i = SkipEscape(line, i);
                continue;
            }

            if (c == '\r')
            {
                // Terminal behaviour: the cursor returns to the start of the line and the
                // following characters write OVER the previous ones. Truncating the tail would
                // be wrong — a progress line saying "done" would disappear entirely.
                cursor = 0;
                continue;
            }

            if (c == '\b')
            {
                cursor = Math.Max(0, cursor - 1);
                continue;
            }

            // Other C0 control characters show up as garbage in a text box; tab is preserved.
            if (char.IsControl(c) && c != '\t')
            {
                continue;
            }

            if (cursor < buffer.Length)
            {
                buffer[cursor] = c;
            }
            else
            {
                buffer.Append(c);
            }

            cursor++;
        }

        return buffer.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns the index of the <b>last</b> character of the ESC sequence at <paramref name="start"/>.
    /// </summary>
    private static int SkipEscape(ReadOnlySpan<char> line, int start)
    {
        int i = start + 1;

        if (i >= line.Length)
        {
            return start;
        }

        char introducer = line[i];

        if (introducer == '[')
        {
            // CSI: ESC [ parameters final-byte(@-~). Colour codes (SGR) are in this group.
            i++;
            while (i < line.Length && line[i] is >= ' ' and <= '?')
            {
                i++;
            }

            return i < line.Length ? i : line.Length - 1;
        }

        if (introducer is ']' or 'P' or 'X' or '^' or '_')
        {
            // OSC and friends: terminated by BEL or ESC \. If it does not terminate within the
            // line the whole line is swallowed — showing the rest would mean showing half an
            // escape sequence.
            i++;
            while (i < line.Length)
            {
                if (line[i] == '\u0007')
                {
                    return i;
                }

                if (line[i] == '\u001b' && i + 1 < line.Length && line[i + 1] == '\\')
                {
                    return i + 1;
                }

                i++;
            }

            return line.Length - 1;
        }

        // A simple two-character escape (such as ESC c).
        return i;
    }
}
