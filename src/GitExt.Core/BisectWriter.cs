using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>How the commit under test was judged (P14-T20).</summary>
public enum BisectVerdict
{
    /// <summary>The bug is not here yet.</summary>
    Good,

    /// <summary>The bug is here.</summary>
    Bad,

    /// <summary>Cannot be judged — broken build, missing dependency.</summary>
    Skip,
}

/// <summary>
/// Where a bisect stands, read from what git printed.
/// </summary>
/// <param name="Output">Git's own output, shown to the user as it is.</param>
/// <param name="RemainingRevisions">
/// Revisions still to test, from <c>Bisecting: N revisions left to test after this</c>;
/// <see langword="null"/> when git said nothing about it.
/// </param>
/// <param name="RemainingSteps">The <c>(roughly M steps)</c> part of the same line.</param>
/// <param name="FirstBadCommit">
/// The culprit, once git names it. Non-<see langword="null"/> means <b>the bisect is over</b>.
/// </param>
public sealed record BisectStatus(
    string Output,
    int? RemainingRevisions = null,
    int? RemainingSteps = null,
    string? FirstBadCommit = null)
{
    /// <summary>Has git named the first bad commit?</summary>
    public bool IsFinished => FirstBadCommit is { Length: > 0 };
}

/// <summary>Runs <c>git bisect</c> (P14-T20).</summary>
public interface IBisectWriter
{
    Task<BisectStatus> StartAsync(
        string workingDirectory,
        string? badCommit = null,
        string? goodCommit = null,
        CancellationToken cancellationToken = default);

    Task<BisectStatus> MarkAsync(
        string workingDirectory,
        BisectVerdict verdict,
        string? commit = null,
        CancellationToken cancellationToken = default);

    Task<BisectStatus> ResetAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git bisect</c> wrapper.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>MEASURED — the exit code says nothing.</b> Every step of a bisect returns 0, including the
/// last one, where git prints <c>&lt;sha&gt; is the first 'bad' commit</c>. Whether the search is
/// over can only be read out of the <b>output</b>, which is why every call here returns a parsed
/// <see cref="BisectStatus"/> rather than a bare result.
/// </para>
/// <para>
/// 🔴 <b>MEASURED — finishing does not end the bisect.</b> After the culprit is named the
/// <c>BISECT_*</c> files are still there and HEAD is still detached; only <c>git bisect reset</c>
/// clears them. So "a bisect is in progress" stays true after the answer is found — the user is
/// meant to see the result and then stop. GitExtensions behaves the same way.
/// </para>
/// </remarks>
public sealed class BisectWriter : IBisectWriter
{
    private readonly IGitWriter _writer;

    public BisectWriter(IGitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _writer = writer;
    }

    public async Task<BisectStatus> StartAsync(
        string workingDirectory,
        string? badCommit = null,
        string? goodCommit = null,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["bisect", "start"];

        // git's own order: the bad one first, then the good one. Passing them here is the same as
        // `bisect start` followed by `bisect bad`/`bisect good`, in one process.
        if (badCommit is { Length: > 0 })
        {
            arguments.Add(badCommit);

            if (goodCommit is { Length: > 0 })
            {
                arguments.Add(goodCommit);
            }
        }

        return await RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BisectStatus> MarkAsync(
        string workingDirectory,
        BisectVerdict verdict,
        string? commit = null,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["bisect", Word(verdict)];

        if (commit is { Length: > 0 })
        {
            arguments.Add(commit);
        }

        return await RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BisectStatus> ResetAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) =>
        await RunAsync(workingDirectory, ["bisect", "reset"], cancellationToken).ConfigureAwait(false);

    private static string Word(BisectVerdict verdict) => verdict switch
    {
        BisectVerdict.Good => "good",
        BisectVerdict.Bad => "bad",
        BisectVerdict.Skip => "skip",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict)),
    };

    private async Task<BisectStatus> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _writer
            .RunWithEnvironmentAsync(workingDirectory, arguments, null, null, cancellationToken)
            .ConfigureAwait(false);

        // git writes the progress line to stderr and the verdict to stdout; the user is shown one
        // console, so the two are read as one.
        return BisectOutputParser.Parse(
            GitOutputText.CleanForDisplay(
                result.GetStandardOutputText() + result.StandardError,
                out _));
    }
}

/// <summary>Reads what git said about a bisect step.</summary>
/// <remarks>
/// Deliberately tolerant: an unrecognised line only means the numbers stay
/// <see langword="null"/>, never that the operation is treated as failed. git's wording differs
/// between versions and a parser that refused unknown text would break the whole feature on the
/// next release.
/// </remarks>
public static class BisectOutputParser
{
    public static BisectStatus Parse(string? output)
    {
        string text = output ?? string.Empty;

        int? revisions = null;
        int? steps = null;
        string? firstBad = null;

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("Bisecting:", StringComparison.Ordinal))
            {
                revisions = ReadNumberBefore(trimmed, "revision");
                steps = ReadNumberBefore(trimmed, "step");
                continue;
            }

            int marker = trimmed.IndexOf(" is the first ", StringComparison.Ordinal);

            if (marker > 0 && trimmed.Contains("bad", StringComparison.Ordinal))
            {
                firstBad = trimmed[..marker].Trim();
            }
        }

        return new BisectStatus(text, revisions, steps, firstBad);
    }

    /// <summary>Reads the number in front of a word — "5 revisions", "roughly 3 steps".</summary>
    private static int? ReadNumberBefore(string line, string word)
    {
        int at = line.IndexOf(word, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        int end = at;

        while (end > 0 && line[end - 1] == ' ')
        {
            end--;
        }

        int start = end;

        while (start > 0 && char.IsAsciiDigit(line[start - 1]))
        {
            start--;
        }

        return start < end && int.TryParse(line[start..end], out int value) ? value : null;
    }
}
