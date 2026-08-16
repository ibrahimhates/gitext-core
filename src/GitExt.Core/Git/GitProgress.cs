using System.Globalization;

namespace GitExt.Core.Git;

/// <summary>
/// The progress of a long-running git operation (P06-T10).
/// </summary>
/// <param name="Phase">The phase name (<c>Counting objects</c>, <c>Receiving objects</c>…).</param>
/// <param name="Percent">The percentage; <see langword="null"/> when git gives none.</param>
/// <param name="Current">The number of objects processed.</param>
/// <param name="Total">The total number of objects.</param>
/// <param name="IsRemote">Is the phase running on the remote server (the <c>remote:</c> prefix)?</param>
/// <param name="IsDone">Has the phase completed (<c>, done.</c>)?</param>
public sealed record GitProgress(
    string Phase,
    double? Percent,
    long? Current,
    long? Total,
    bool IsRemote = false,
    bool IsDone = false)
{
    /// <summary>The single line to show the user.</summary>
    public string Describe()
    {
        string prefix = IsRemote ? "Sunucu: " : string.Empty;

        if (IsDone)
        {
            return $"{prefix}{Phase} — tamam";
        }

        return Percent is { } percent
            ? string.Create(CultureInfo.InvariantCulture, $"{prefix}{Phase} — %{percent:0}")
            : $"{prefix}{Phase}…";
    }
}

/// <summary>
/// Parses <c>git --progress</c> lines (P06-T10).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>MEASURED — progress lines are separated by <c>\r</c>, NOT by <c>\n</c>.</b>
/// In a real clone, 404 carriage returns (<c>\r</c>) were counted against 7 line endings (<c>\n</c>):
/// git writes over the same line. A reader using <c>ReadLineAsync</c> or <c>Split('\n')</c> would show
/// the user <b>nothing at all</b> until the operation finished — meaning the "progress bar" would stay
/// empty at exactly the moment it is needed.
/// </para>
/// <para>
/// The measured formats:
/// <code>
/// remote: Counting objects:   5% (207/4125)
/// remote: Enumerating objects: 16201, done.
/// Receiving objects:  47% (7615/16201), 4.10 MiB | 8.20 MiB/s
/// Resolving deltas: 100% (11603/11603), done.
/// </code>
/// The space padding at the end of a line comes from git too (to erase the older, longer line).
/// </para>
/// </remarks>
public static class GitProgressParser
{
    private const string RemotePrefix = "remote: ";

    /// <summary>
    /// Parses a single line; <see langword="null"/> when it is not a progress line.
    /// </summary>
    public static GitProgress? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        ReadOnlySpan<char> span = line.AsSpan().Trim();

        if (span.IsEmpty)
        {
            return null;
        }

        bool remote = span.StartsWith(RemotePrefix, StringComparison.Ordinal);

        if (remote)
        {
            span = span[RemotePrefix.Length..].Trim();
        }

        int colon = span.IndexOf(':');

        if (colon <= 0)
        {
            return null;
        }

        string phase = span[..colon].ToString().Trim();
        ReadOnlySpan<char> rest = span[(colon + 1)..].Trim();

        if (phase.Length == 0 || rest.IsEmpty)
        {
            return null;
        }

        // Lines such as `Cloning into 'x'...` can contain a colon too; what marks a progress line out
        // is that it starts with a number.
        if (!char.IsAsciiDigit(rest[0]))
        {
            return null;
        }

        bool done = rest.EndsWith("done.", StringComparison.Ordinal);
        double? percent = null;
        long? current = null;
        long? total = null;

        int percentSign = rest.IndexOf('%');

        if (percentSign > 0
            && double.TryParse(
                rest[..percentSign].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed))
        {
            percent = parsed;
        }

        int open = rest.IndexOf('(');
        int close = rest.IndexOf(')');

        if (open >= 0 && close > open)
        {
            ReadOnlySpan<char> pair = rest[(open + 1)..close];
            int slash = pair.IndexOf('/');

            if (slash > 0)
            {
                if (long.TryParse(pair[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out long a))
                {
                    current = a;
                }

                if (long.TryParse(pair[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long b))
                {
                    total = b;
                }
            }
        }
        else if (percent is null)
        {
            // `Enumerating objects: 16201, done.` — no percentage, only a counter.
            int comma = rest.IndexOf(',');
            ReadOnlySpan<char> number = comma > 0 ? rest[..comma] : rest;

            if (long.TryParse(number.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long count))
            {
                current = count;
            }
        }

        return new GitProgress(phase, percent, current, total, remote, done);
    }

    /// <summary>
    /// Splits a chunk of text into lines on <b>both <c>\r</c> and <c>\n</c></b>.
    /// </summary>
    /// <remarks>
    /// The last piece is handed back when it saw no line ending: while reading a stream, a line can be
    /// split across two chunks, and parsing half of it would produce a wrong percentage.
    /// </remarks>
    public static (IReadOnlyList<string> Lines, string Remainder) SplitLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> lines = [];
        int start = 0;

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                continue;
            }

            if (index > start)
            {
                lines.Add(text[start..index]);
            }

            start = index + 1;
        }

        return (lines, text[start..]);
    }
}
