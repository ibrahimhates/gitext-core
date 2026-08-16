using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Computes the <b>intra-line</b> difference between two lines (P04-T05).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why is git's <c>--word-diff</c> not used?</b> The plan suggested starting with it
/// ("free and correct"). It was measured, and it turned out <b>not to be correct</b>:
/// </para>
/// <list type="number">
/// <item>With the default word separator, reassembling the line from the pieces adds a
/// <b>phantom space</b> to the end of the old side.</item>
/// <item>A character-level separator fixes that, but a bigger problem remains:
/// <b>for an added/removed blank line git emits nothing but a bare <c>~</c></b>, and which side
/// the line belongs to is <b>not in the output at all</b>. Scanning 150 commits in a real
/// repository, this put 5,701 lines on the wrong side.</item>
/// <item>On top of that, word diff <b>replaces</b> the line-based output, meaning it needs an
/// extra <c>git</c> invocation.</item>
/// </list>
/// <para>
/// So the intra-line difference is computed <b>locally</b>, over the <b>exact</b> line texts the
/// parser already produces. There is no fidelity risk: the input is already the correct lines.
/// </para>
/// </remarks>
public static class InlineDiff
{
    /// <summary>
    /// No intra-line difference is computed for lines longer than this.
    /// </summary>
    /// <remarks>
    /// In minified JS or single-line JSON, lines can run to tens of thousands of characters.
    /// Highlighting inside such a line is unreadable anyway, and computing it is wasted work.
    /// </remarks>
    public const int MaximumLineLength = 4000;

    /// <summary>
    /// The largest middle segment that will still be resolved word by word.
    /// </summary>
    /// <remarks>
    /// Above this, trimming the common prefix/suffix is considered enough. Without a limit, long
    /// lines would produce a quadratic running time.
    /// </remarks>
    private const int MaximumMiddleLength = 400;

    /// <summary>
    /// Above this many candidate line pairs, the best matching is not searched for.
    /// </summary>
    /// <remarks>
    /// Searching for a matching is O(n·m); left unbounded it costs quadratic time on large hunks.
    /// The limit, and falling back to positional matching once it is exceeded, is the same as
    /// <b>GitExtensions</b>' solution to the same problem
    /// (<c>GitUI/Editor/Diff/LinesMatcher.cs</c>).
    /// </remarks>
    private const int MaximumPairCombinations = 100 * 100;

    /// <summary>
    /// The lowest similarity at which a match still counts as meaningful.
    /// </summary>
    /// <remarks>Scores below this are noise; GitExtensions uses the same threshold.</remarks>
    private const double InsignificantScore = 0.1;

    /// <summary>
    /// Adds intra-line segments to a hunk's lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Matching:</b> consecutive runs of removed and added lines are matched recursively,
    /// anchored on the <b>pair sharing the most words</b>; what comes before and after the anchor
    /// is split the same way. Matching positionally (i-th ↔ i-th) is right when one line is added
    /// and one removed, but compares the <b>wrong lines</b> when the counts differ.
    /// </para>
    /// <para>
    /// This approach was taken from <b>GitExtensions</b>' solution
    /// (<c>GitUI/Editor/Diff/LinesMatcher.cs</c>): the score is
    /// <i>total length of shared words ÷ the greater of the two lines' word length</i>, and
    /// matches below the threshold are ignored and fall back to positional matching.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DiffLine> Annotate(IReadOnlyList<DiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        DiffLine[] result = [.. lines];

        int index = 0;

        while (index < result.Length)
        {
            if (result[index].Kind != DiffLineKind.Removed)
            {
                index++;
                continue;
            }

            int removedStart = index;

            while (index < result.Length && result[index].Kind == DiffLineKind.Removed)
            {
                index++;
            }

            int addedStart = index;

            while (index < result.Length && result[index].Kind == DiffLineKind.Added)
            {
                index++;
            }

            int removedCount = addedStart - removedStart;
            int addedCount = index - addedStart;

            foreach ((int Removed, int Added) pair in FindPairs(result, removedStart, removedCount, addedStart, addedCount))
            {
                DiffLine oldLine = result[pair.Removed];
                DiffLine newLine = result[pair.Added];

                (IReadOnlyList<DiffSegment> oldSegments, IReadOnlyList<DiffSegment> newSegments) =
                    Compute(oldLine.Content, newLine.Content);

                result[pair.Removed] = oldLine with { Segments = oldSegments };
                result[pair.Added] = newLine with { Segments = newSegments };
            }
        }

        return result;
    }

    /// <summary>
    /// Matches removed and added line texts to one another.
    /// </summary>
    /// <returns>
    /// Index pairs into <paramref name="removed"/> and <paramref name="added"/>, in increasing
    /// order of the left index. Unmatched lines <b>do not appear at all</b> in the result.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>This is the very matching the intra-line highlighting uses</b>, and it is exposed
    /// deliberately: the side-by-side view (P04-T10) takes its alignment from here. Had a second
    /// matching been written, the highlighted pair and the pair shown side by side <b>could
    /// differ</b> — two contradictory answers to the user on the same screen.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(int Removed, int Added)> MatchLines(
        IReadOnlyList<string> removed,
        IReadOnlyList<string> added)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(added);

        return Match(
            removed.Count,
            added.Count,
            i => removed[i],
            i => added[i],
            removedStart: 0,
            addedStart: 0);
    }

    /// <summary>
    /// Matches the removed/added runs within a hunk (with absolute indices).
    /// </summary>
    private static List<(int Removed, int Added)> FindPairs(
        DiffLine[] lines,
        int removedStart,
        int removedCount,
        int addedStart,
        int addedCount) =>
        Match(
            removedCount,
            addedCount,
            i => lines[removedStart + i].Content,
            i => lines[addedStart + i].Content,
            removedStart,
            addedStart);

    private static List<(int Removed, int Added)> Match(
        int removedCount,
        int addedCount,
        Func<int, string> removedContent,
        Func<int, string> addedContent,
        int removedStart,
        int addedStart)
    {
        List<(int, int)> pairs = [];

        if (removedCount == 0 || addedCount == 0)
        {
            return pairs;
        }

        // A single-line side, or a very large hunk: match positionally.
        if (removedCount == 1 || addedCount == 1
            || removedCount * addedCount > MaximumPairCombinations)
        {
            int count = Math.Min(removedCount, addedCount);

            for (int i = 0; i < count; i++)
            {
                pairs.Add((removedStart + i, addedStart + i));
            }

            return pairs;
        }

        string[][] removedWords = new string[removedCount][];
        string[][] addedWords = new string[addedCount][];

        for (int i = 0; i < removedCount; i++)
        {
            removedWords[i] = Words(removedContent(i));
        }

        for (int i = 0; i < addedCount; i++)
        {
            addedWords[i] = Words(addedContent(i));
        }

        Pair(0, removedCount, 0, addedCount);

        pairs.Sort((left, right) => left.Item1.CompareTo(right.Item1));

        return pairs;

        void Pair(int removedFrom, int removedTo, int addedFrom, int addedTo)
        {
            if (removedFrom >= removedTo || addedFrom >= addedTo)
            {
                return;
            }

            (int bestRemoved, int bestAdded, double score) =
                FindBestMatch(removedWords, addedWords, removedFrom, removedTo, addedFrom, addedTo);

            if (score <= InsignificantScore)
            {
                // No meaningful anchor: match the rest positionally.
                int count = Math.Min(removedTo - removedFrom, addedTo - addedFrom);

                for (int i = 0; i < count; i++)
                {
                    pairs.Add((removedStart + removedFrom + i, addedStart + addedFrom + i));
                }

                return;
            }

            Pair(removedFrom, bestRemoved, addedFrom, bestAdded);

            pairs.Add((removedStart + bestRemoved, addedStart + bestAdded));

            Pair(bestRemoved + 1, removedTo, bestAdded + 1, addedTo);
        }
    }

    /// <summary>
    /// Finds the pair of lines sharing the most words.
    /// </summary>
    /// <remarks>
    /// Score: <i>total length of shared words ÷ the greater of the two lines' word length</i>.
    /// The same measure as GitExtensions' <c>LinesMatcher.GetWordMatchScore</c>.
    /// </remarks>
    private static (int Removed, int Added, double Score) FindBestMatch(
        string[][] removedWords,
        string[][] addedWords,
        int removedFrom,
        int removedTo,
        int addedFrom,
        int addedTo)
    {
        int bestRemoved = removedFrom;
        int bestAdded = addedFrom;
        double best = -1;

        for (int r = removedFrom; r < removedTo; r++)
        {
            for (int a = addedFrom; a < addedTo; a++)
            {
                double score = Score(removedWords[r], addedWords[a]);

                if (score <= best)
                {
                    continue;
                }

                best = score;
                bestRemoved = r;
                bestAdded = a;

                if (best >= 1)
                {
                    return (bestRemoved, bestAdded, best);
                }
            }
        }

        return (bestRemoved, bestAdded, best);
    }

    private static double Score(string[] left, string[] right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return -1;
        }

        int leftLength = left.Sum(w => w.Length);
        int rightLength = right.Sum(w => w.Length);

        HashSet<string> shared = new(left, StringComparer.Ordinal);
        shared.IntersectWith(right);

        int common = shared.Sum(w => w.Length);

        return (double)common / Math.Max(leftLength, rightLength);
    }

    /// <summary>The words used in scoring: runs of letters and digits only.</summary>
    private static string[] Words(string text) =>
        [.. Tokenize(text).Where(t => t.Length > 0 && char.IsLetterOrDigit(t[0]))];

    /// <summary>
    /// Computes the segments of two lines.
    /// </summary>
    /// <returns>The old and new line's segment lists; joined, they give back the input.</returns>
    public static (IReadOnlyList<DiffSegment> Old, IReadOnlyList<DiffSegment> New) Compute(
        string oldLine,
        string newLine)
    {
        ArgumentNullException.ThrowIfNull(oldLine);
        ArgumentNullException.ThrowIfNull(newLine);

        if (oldLine.Length > MaximumLineLength || newLine.Length > MaximumLineLength)
        {
            return (Whole(oldLine, DiffLineKind.Removed), Whole(newLine, DiffLineKind.Added));
        }

        if (string.Equals(oldLine, newLine, StringComparison.Ordinal))
        {
            return (Whole(oldLine, DiffLineKind.Context), Whole(newLine, DiffLineKind.Context));
        }

        // The common prefix and suffix are trimmed: in a typical edit the work ends here and the
        // remaining middle segment is short.
        int prefix = CommonPrefixLength(oldLine, newLine);
        int suffix = CommonSuffixLength(oldLine, newLine, prefix);

        string oldMiddle = oldLine[prefix..(oldLine.Length - suffix)];
        string newMiddle = newLine[prefix..(newLine.Length - suffix)];

        (string[] oldTokens, string[] newTokens) = (Tokenize(oldMiddle), Tokenize(newMiddle));

        List<DiffSegment> oldSegments = [];
        List<DiffSegment> newSegments = [];

        Append(oldSegments, DiffLineKind.Context, oldLine[..prefix]);
        Append(newSegments, DiffLineKind.Context, newLine[..prefix]);

        if (oldTokens.Length <= MaximumMiddleLength && newTokens.Length <= MaximumMiddleLength)
        {
            AppendTokenDiff(oldSegments, newSegments, oldTokens, newTokens);
        }
        else
        {
            // The middle segment is very long: the whole of it counts as changed. Crude but
            // correct — highlighting broadly beats highlighting the wrong place.
            Append(oldSegments, DiffLineKind.Removed, oldMiddle);
            Append(newSegments, DiffLineKind.Added, newMiddle);
        }

        Append(oldSegments, DiffLineKind.Context, oldLine[(oldLine.Length - suffix)..]);
        Append(newSegments, DiffLineKind.Context, newLine[(newLine.Length - suffix)..]);

        return (oldSegments, newSegments);
    }

    /// <summary>
    /// Matches the middle tokens with a longest common subsequence (LCS) and splits them into
    /// segments.
    /// </summary>
    private static void AppendTokenDiff(
        List<DiffSegment> oldSegments,
        List<DiffSegment> newSegments,
        string[] oldTokens,
        string[] newTokens)
    {
        int[,] lengths = new int[oldTokens.Length + 1, newTokens.Length + 1];

        for (int i = oldTokens.Length - 1; i >= 0; i--)
        {
            for (int j = newTokens.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(oldTokens[i], newTokens[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        int x = 0;
        int y = 0;

        while (x < oldTokens.Length && y < newTokens.Length)
        {
            if (string.Equals(oldTokens[x], newTokens[y], StringComparison.Ordinal))
            {
                Append(oldSegments, DiffLineKind.Context, oldTokens[x]);
                Append(newSegments, DiffLineKind.Context, newTokens[y]);
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                Append(oldSegments, DiffLineKind.Removed, oldTokens[x++]);
            }
            else
            {
                Append(newSegments, DiffLineKind.Added, newTokens[y++]);
            }
        }

        while (x < oldTokens.Length)
        {
            Append(oldSegments, DiffLineKind.Removed, oldTokens[x++]);
        }

        while (y < newTokens.Length)
        {
            Append(newSegments, DiffLineKind.Added, newTokens[y++]);
        }
    }

    /// <summary>
    /// Splits the line into tokens: runs of letters/digits are one token, every other character
    /// stands alone.
    /// </summary>
    /// <remarks>
    /// Splitting character by character produces noisy highlighting; keeping words whole improves
    /// readability markedly. Because punctuation is a separate token,
    /// <c>foo(bar)</c> → <c>foo</c> <c>(</c> <c>bar</c> <c>)</c>.
    /// </remarks>
    private static string[] Tokenize(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        List<string> tokens = [];
        int start = 0;

        while (start < text.Length)
        {
            if (char.IsLetterOrDigit(text[start]))
            {
                int end = start;

                while (end < text.Length && char.IsLetterOrDigit(text[end]))
                {
                    end++;
                }

                tokens.Add(text[start..end]);
                start = end;
            }
            else
            {
                tokens.Add(text[start].ToString());
                start++;
            }
        }

        return [.. tokens];
    }

    /// <summary>
    /// Appends the segment to the list; consecutive segments of the same kind are <b>merged</b>.
    /// </summary>
    /// <remarks>
    /// Without merging, every token would be its own segment — meaning hundreds of needless
    /// drawing elements on the UI side.
    /// </remarks>
    private static void Append(List<DiffSegment> segments, DiffLineKind kind, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (segments.Count > 0 && segments[^1].Kind == kind)
        {
            segments[^1] = segments[^1] with { Text = segments[^1].Text + text };
            return;
        }

        segments.Add(new DiffSegment(kind, text));
    }

    private static IReadOnlyList<DiffSegment> Whole(string text, DiffLineKind kind) =>
        text.Length == 0 ? [] : [new DiffSegment(kind, text)];

    private static int CommonPrefixLength(string left, string right)
    {
        int limit = Math.Min(left.Length, right.Length);
        int index = 0;

        while (index < limit && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static int CommonSuffixLength(string left, string right, int prefix)
    {
        int limit = Math.Min(left.Length, right.Length) - prefix;
        int index = 0;

        while (index < limit && left[left.Length - 1 - index] == right[right.Length - 1 - index])
        {
            index++;
        }

        return index;
    }
}
