using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// A multi-step operation in progress in the repository (P06-T04).
/// </summary>
public enum InProgressOperation
{
    /// <summary>No operation in progress.</summary>
    None,

    /// <summary>A rebase (interactive or not).</summary>
    Rebase,

    /// <summary>A patch being applied with <c>git am</c>.</summary>
    ApplyMailbox,

    /// <summary>A merge stopped on a conflict.</summary>
    Merge,

    /// <summary>A cherry-pick in progress.</summary>
    CherryPick,

    /// <summary>A revert in progress.</summary>
    Revert,

    /// <summary>A bisect in progress.</summary>
    Bisect,
}

/// <summary>
/// Reads the operation in progress in the repository (P06-T04).
/// </summary>
public interface IInProgressOperationReader
{
    Task<InProgressOperation> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Determines the operation in progress by looking at the state files in the git directory (P06-T04).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why is this needed?</b> MEASURED: <b>during a rebase and a bisect, HEAD is detached</b>
/// (<c>symbolic-ref</c> exit code 1, <c>--porcelain=v2</c> says <c>(detached)</c>).
/// A plain "detached HEAD" warning would pop up in both of those cases; the user is deliberately in
/// the middle of an operation, and what they need to be told is not "create a branch here" but
/// <b>which operation is in progress</b>.
/// </para>
/// <para>
/// The file names are the same as the ones <see cref="RepositoryChangeClassifier"/> watches — those
/// are exactly what counts there as "the repository state changed" (P05-T14).
/// </para>
/// <para>
/// ⚠️ The path is obtained with <c>--absolute-git-dir</c>: <c>--git-path</c> returns a <b>relative</b>
/// path that depends on the working directory (P05-T13, item 20e). This is also the right thing in a
/// linked working tree — these files live not in the common directory but in that worktree's <b>own</b>
/// directory.
/// </para>
/// </remarks>
public sealed class InProgressOperationReader : IInProgressOperationReader
{
    private readonly IGitProcessRunner _runner;

    public InProgressOperationReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<InProgressOperation> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--absolute-git-dir"],
            },
            cancellationToken).ConfigureAwait(false);

        string gitDirectory = result.GetStandardOutputText().Trim();

        return gitDirectory.Length == 0 ? InProgressOperation.None : Classify(gitDirectory);
    }

    /// <summary>Looks at the state files in the git directory.</summary>
    /// <remarks>
    /// The order matters: <c>MERGE_HEAD</c> can appear during a rebase as well, but the context that
    /// matters to the user is the rebase.
    /// </remarks>
    internal static InProgressOperation Classify(string gitDirectory)
    {
        if (Directory.Exists(Path.Combine(gitDirectory, "rebase-merge")))
        {
            return InProgressOperation.Rebase;
        }

        // `rebase-apply` is used by both `rebase --apply` and `git am`;
        // the `applying` file inside it makes the distinction.
        string applyDirectory = Path.Combine(gitDirectory, "rebase-apply");

        if (Directory.Exists(applyDirectory))
        {
            return File.Exists(Path.Combine(applyDirectory, "applying"))
                ? InProgressOperation.ApplyMailbox
                : InProgressOperation.Rebase;
        }

        if (File.Exists(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD")))
        {
            return InProgressOperation.CherryPick;
        }

        if (File.Exists(Path.Combine(gitDirectory, "REVERT_HEAD")))
        {
            return InProgressOperation.Revert;
        }

        if (File.Exists(Path.Combine(gitDirectory, "MERGE_HEAD")))
        {
            return InProgressOperation.Merge;
        }

        return File.Exists(Path.Combine(gitDirectory, "BISECT_LOG"))
            ? InProgressOperation.Bisect
            : InProgressOperation.None;
    }
}
