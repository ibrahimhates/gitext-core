using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Bir stash girdisi (P07-T12).
/// </summary>
public sealed record StashEntry
{
    /// <summary>The selector — <c>refs/stash@{0}</c>.</summary>
    public required string Selector { get; init; }

    /// <summary>The stash commit's full SHA.</summary>
    public required string ObjectId { get; init; }

    /// <summary>The entry's message — <c>On main: my stash</c>.</summary>
    public required string Message { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The ordinal — the N in <c>stash@{N}</c>.</summary>
    public int Index { get; init; }

    /// <summary>
    /// Are untracked files included in this stash as well?
    /// </summary>
    /// <remarks>
    /// MEASURED: a stash taken with <c>-u</c> has a <b>third parent</b> — the commit of the untracked
    /// files. That is what the distinction is made from; looking at the message would be unreliable
    /// (the user writes it freely).
    /// </remarks>
    public bool IncludesUntracked { get; init; }

    /// <summary><c>stash@{N}</c>, for short display.</summary>
    public string ShortSelector =>
        $"stash@{{{Index.ToString(CultureInfo.InvariantCulture)}}}";
}

/// <summary>Options for creating a stash (P07-T12).</summary>
public sealed record StashPushOptions
{
    /// <summary>The entry's message.</summary>
    public string? Message { get; init; }

    /// <summary><c>--include-untracked</c>.</summary>
    public bool IncludeUntracked { get; init; }

    /// <summary><c>--keep-index</c>: leave what is staged in the working tree.</summary>
    public bool KeepIndex { get; init; }

    /// <summary>Only these paths; all of them when empty.</summary>
    public IReadOnlyList<RepositoryPath> Paths { get; init; } = [];
}

/// <summary>Stash uygulama sonucu (P07-T12).</summary>
public sealed record StashApplyResult
{
    public required bool HasConflicts { get; init; }

    public IReadOnlyList<RepositoryPath> ConflictedPaths { get; init; } = [];

    /// <summary>
    /// Did the entry stay in the list?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — when <c>pop</c> conflicts the entry IS NOT DROPPED</b>
    /// (<i>"The stash entry is kept in case you need it again."</i>, rc=1). Unless the user is told,
    /// they either apply the change twice or lose it while deleting it by hand.
    /// </remarks>
    public required bool EntryKept { get; init; }

    /// <summary>
    /// Could the staged/unstaged distinction be preserved?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — in its default form <c>pop</c> silently LOSES this distinction.</b>
    /// With one file staged and one not, after the pop <b>both</b> are unstaged. <c>--index</c>
    /// preserves the distinction; but <c>--index</c> cannot be applied in every case (git refuses it
    /// when there is a conflict), so the outcome is reported.
    /// </remarks>
    public required bool IndexRestored { get; init; }
}

/// <summary>Stash operations (P07-T12).</summary>
public interface IStashWriter
{
    Task<IReadOnlyList<StashEntry>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <returns><see langword="false"/> when there is nothing to set aside.</returns>
    Task<bool> PushAsync(
        string workingDirectory,
        StashPushOptions options,
        CancellationToken cancellationToken = default);

    Task<StashApplyResult> ApplyAsync(
        string workingDirectory,
        string selector,
        bool drop,
        CancellationToken cancellationToken = default);

    Task DropAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default);

    /// <summary>Opens the stash onto a new branch (<c>git stash branch</c>).</summary>
    Task BranchAsync(
        string workingDirectory,
        string selector,
        string branchName,
        CancellationToken cancellationToken = default);

    /// <summary>Produces the entry's diff.</summary>
    Task<string> ShowAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The <c>git stash</c> wrapper (P07-T12).
/// </summary>
public sealed class StashWriter : IStashWriter
{
    private const string Format = "%x1e%gD%x00%H%x00%ct%x00%gs%x00%P";

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public StashWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<IReadOnlyList<StashEntry>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "stash", "list", $"--format={Format}"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    /// <summary>Parses the NUL-separated <c>stash list</c> output.</summary>
    /// <remarks>
    /// The separator is NUL: the stash message is written by the <b>user</b> and may contain a tab
    /// (the same trap measured on the reflog in P07-T14).
    /// </remarks>
    internal static IReadOnlyList<StashEntry> Parse(string output)
    {
        List<StashEntry> entries = [];
        int index = 0;

        foreach (string record in output.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Trim('\n', '\r').Split('\0');

            if (fields.Length < 5 || fields[0].Length == 0)
            {
                continue;
            }

            // Ebeveynler: <HEAD> <index-commit> [<untracked-commit>]
            int parents = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            entries.Add(new StashEntry
            {
                Selector = fields[0],
                ObjectId = fields[1],
                Timestamp = long.TryParse(fields[2], CultureInfo.InvariantCulture, out long seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : default,
                Message = fields[3],
                Index = index++,
                IncludesUntracked = parents >= 3,
            });
        }

        return entries;
    }

    public async Task<bool> PushAsync(
        string workingDirectory,
        StashPushOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> arguments = ["stash", "push"];

        if (options.IncludeUntracked)
        {
            arguments.Add("--include-untracked");
        }

        if (options.KeepIndex)
        {
            arguments.Add("--keep-index");
        }

        if (options.Message is { Length: > 0 } message)
        {
            arguments.Add("-m");
            arguments.Add(message);
        }

        if (options.Paths.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(options.Paths.Select(path => path.Value));
        }

        GitResult result = await _writer
            .RunAsync(workingDirectory, arguments, cancellationToken)
            .ConfigureAwait(false);

        // When there is nothing to set aside, git says "No local changes to save" and returns 0.
        // Reporting that as "stashed" would send the user looking for an entry that does not exist.
        return !result.GetStandardOutputText()
            .Contains("No local changes", StringComparison.Ordinal);
    }

    /// <remarks>
    /// <c>--index</c> is tried <b>first</b>: in the measurement, the default <c>pop</c> left what was
    /// staged unstaged. <c>--index</c> cannot be applied in every case (git refuses it when there is
    /// a conflict), so on failure it falls back to the plain form and
    /// <see cref="StashApplyResult.IndexRestored"/> <b>says what happened</b>.
    /// </remarks>
    public async Task<StashApplyResult> ApplyAsync(
        string workingDirectory,
        string selector,
        bool drop,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        string verb = drop ? "pop" : "apply";
        bool indexRestored = true;
        GitException? failure = null;

        try
        {
            await _writer
                .RunAsync(workingDirectory, ["stash", verb, "--index", selector], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException)
        {
            indexRestored = false;

            try
            {
                await _writer
                    .RunAsync(workingDirectory, ["stash", verb, selector], cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GitException error)
            {
                // A conflict lands here too; whether it was a real error or a conflict, the index will say.
                failure = error;
            }
        }

        IReadOnlyList<RepositoryPath> conflicts =
            await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        if (failure is not null && conflicts.Count == 0)
        {
            // With no conflict this was a real error (unknown selector, dirty tree…); saying "done"
            // silently would be wrong.
            throw failure;
        }

        IReadOnlyList<StashEntry> remaining =
            await ListAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        return new StashApplyResult
        {
            HasConflicts = conflicts.Count > 0,
            ConflictedPaths = conflicts,
            EntryKept = !drop || remaining.Any(entry =>
                string.Equals(entry.Selector, selector, StringComparison.Ordinal)
                || conflicts.Count > 0),
            IndexRestored = indexRestored,
        };
    }

    public Task DropAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default) =>
        _writer.RunAsync(workingDirectory, ["stash", "drop", selector], cancellationToken);

    public Task BranchAsync(
        string workingDirectory,
        string selector,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        return _writer.RunAsync(
            workingDirectory,
            ["stash", "branch", branchName, selector],
            cancellationToken);
    }

    public async Task<string> ShowAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory,
                "stash", "show", "--patch", "--no-color", selector),
            cancellationToken).ConfigureAwait(false);

        // Read losslessly: the diff content is the repository's own bytes, not in any single encoding
        // (the lesson of P04).
        return result.IsSuccess ? result.GetStandardOutputLossless() : string.Empty;
    }

    private async Task<IReadOnlyList<RepositoryPath>> ReadConflictsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return [];
        }

        List<RepositoryPath> paths = [];

        foreach (string value in result.GetStandardOutputText()
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (RepositoryPath.TryParse(value, out RepositoryPath path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}
