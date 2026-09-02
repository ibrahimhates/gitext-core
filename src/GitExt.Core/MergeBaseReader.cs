using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>Finds the common ancestor of commits (P14-T21).</summary>
public interface IMergeBaseReader
{
    /// <summary>
    /// The best common ancestor of the given revisions.
    /// </summary>
    /// <returns>
    /// The commit, or <see langword="null"/> when the histories are unrelated — which is not an
    /// error.
    /// </returns>
    Task<CommitId?> FindAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        CancellationToken cancellationToken = default);
}

/// <summary><c>git merge-base</c> wrapper.</summary>
/// <remarks>
/// <para>
/// 🔴 <b>MEASURED — "no common ancestor" leaves through the exit code.</b> For unrelated histories
/// git exits <b>1</b> with empty output, exactly as it does for a real failure. Treating that as an
/// error would turn an ordinary answer ("these two share nothing") into an error dialog, so exit
/// code 1 with no output is read as <see langword="null"/> and only anything else is thrown.
/// </para>
/// <para>
/// More than two revisions need <c>--octopus</c>; without it git rejects the arguments.
/// </para>
/// </remarks>
public sealed class MergeBaseReader : IMergeBaseReader
{
    private const int NoCommonAncestorExitCode = 1;

    private readonly IGitProcessRunner _runner;

    public MergeBaseReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
    }

    public async Task<CommitId?> FindAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(revisions);

        List<string> wanted = [.. revisions.Where(revision => !string.IsNullOrWhiteSpace(revision))];

        if (wanted.Count < 2)
        {
            return null;
        }

        List<string> arguments = ["merge-base"];

        if (wanted.Count > 2)
        {
            arguments.Add("--octopus");
        }

        arguments.AddRange(wanted);

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = arguments,

                // 1 is an ANSWER here, not a failure: it is what git returns for unrelated
                // histories. Anything else still throws.
                SuccessExitCodes = [0, NoCommonAncestorExitCode],
            },
            cancellationToken).ConfigureAwait(false);

        string output = result.GetStandardOutputText().Trim();

        return CommitId.TryParse(output, out CommitId id) ? id : null;
    }
}
