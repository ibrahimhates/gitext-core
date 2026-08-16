using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// Reports the state of the repository's <c>commit-graph</c> file (P09-T07).
/// </summary>
/// <remarks>
/// <para>
/// <c>commit-graph</c> keeps commit headers and generation numbers in a precomputed file; queries
/// that walk history, such as <c>--topo-order</c>, do not have to read the object database when they
/// can use it.
/// </para>
/// <para>
/// <b>The measured gain (a 500,000-commit repository, the same machine as the P09-T04 baseline):</b>
/// </para>
/// <list type="bullet">
///   <item>The graph's <b>first row</b>: 1,281 ms → <b>7.8 ms</b> (~164×)</item>
///   <item>Reading all of it: 3,411 ms → 2,867 ms</item>
///   <item>Writing the file: ~1.7 s (once)</item>
/// </list>
/// <para>
/// The difference on the first row is what the user sees directly: without generation numbers,
/// <c>--topo-order</c> has to walk the entire history before it can print the first row.
/// </para>
/// <para>
/// 🔒 <b>The file is NOT written by itself.</b> Adding a file to the user's repository without
/// permission is not right — least of all when that repository is a shared working copy. The class
/// only reports the state; the decision to write is the user's.
/// </para>
/// </remarks>
public interface ICommitGraphAdvisor
{
    /// <summary>Deponun commit-graph durumunu okur.</summary>
    Task<CommitGraphStatus> InspectAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the <c>commit-graph</c> file when the user approves.
    /// </summary>
    /// <remarks>
    /// Must only be called from an explicit user action.
    /// </remarks>
    Task WriteAsync(string workingDirectory, CancellationToken cancellationToken = default);
}

/// <summary>
/// A repository's <c>commit-graph</c> state.
/// </summary>
/// <param name="Exists">Does the file exist?</param>
/// <param name="CommitCount">The number of commits in the repository.</param>
/// <param name="IsWorthwhile">Should the suggestion be shown?</param>
public readonly record struct CommitGraphStatus(bool Exists, int CommitCount, bool IsWorthwhile)
{
    /// <summary>
    /// The lower bound at which the suggestion is worth showing.
    /// </summary>
    /// <remarks>
    /// In small repositories the gain is not measurable: a 10,000-commit repository is already read in
    /// full in 99 ms (P09-T04). Showing the suggestion in every repository would mean bothering the
    /// user over an operation that gains them nothing.
    /// </remarks>
    public const int RecommendedThreshold = 50_000;
}

/// <inheritdoc cref="ICommitGraphAdvisor"/>
public sealed class CommitGraphAdvisor : ICommitGraphAdvisor
{
    private readonly IGitProcessRunner _runner;

    public CommitGraphAdvisor(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<CommitGraphStatus> InspectAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        bool exists = await ExistsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        // When the file exists there is no need to count commits: the suggestion will not be shown
        // anyway, and counting itself takes seconds in a large repository.
        if (exists)
        {
            return new CommitGraphStatus(Exists: true, CommitCount: 0, IsWorthwhile: false);
        }

        int count = await CountCommitsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        return new CommitGraphStatus(
            Exists: false,
            CommitCount: count,
            IsWorthwhile: count >= CommitGraphStatus.RecommendedThreshold);
    }

    public async Task WriteAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "commit-graph", "write", "--reachable"),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Does the commit-graph file exist?
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file system is consulted <b>directly</b> rather than asking git: <c>commit-graph verify</c>
    /// validates the whole file and takes seconds in a large repository — not a cost to pay on opening
    /// a repository.
    /// </para>
    /// <para>
    /// Two places are checked: the single-file <c>commit-graph</c> and the chained form's
    /// <c>commit-graphs/commit-graph-chain</c>. In repositories written with <c>--split</c> only the
    /// second exists; looking at the first alone would say "no file" and the suggestion would be shown
    /// again and again with no need.
    /// </para>
    /// </remarks>
    private async Task<bool> ExistsAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        string objectsDirectory = await ResolveObjectsDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return File.Exists(Path.Combine(objectsDirectory, "info", "commit-graph"))
            || File.Exists(Path.Combine(objectsDirectory, "info", "commit-graphs", "commit-graph-chain"));
    }

    private async Task<string> ResolveObjectsDirectoryAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string path = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "--git-path", "objects"),
            cancellationToken).ConfigureAwait(false);

        return Path.GetFullPath(path.Trim(), workingDirectory);
    }

    private async Task<int> CountCommitsAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        string output = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--count", "--all"),
            cancellationToken).ConfigureAwait(false);

        return int.TryParse(output.Trim(), out int count) ? count : 0;
    }
}
