using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// The repository's position <b>before</b> an operation that rewrites history (P07-T15).
/// </summary>
/// <remarks>
/// <para>
/// The phase rule: <i>"Before every operation that rewrites history, the reflog position is recorded
/// and the user is always given the 'how do I undo this' information."</i> This type carries that
/// information.
/// </para>
/// </remarks>
public sealed record SafetyPoint
{
    /// <summary><c>HEAD</c> before the operation (full SHA).</summary>
    public required string ObjectId { get; init; }

    /// <summary>The branch we were on; <see langword="null"/> for a detached <c>HEAD</c>.</summary>
    public string? BranchName { get; init; }

    /// <summary>The name of the operation taking the safety point — "rebase", "reset" and so on.</summary>
    public required string Operation { get; init; }

    /// <summary>
    /// Were there uncommitted changes in the working tree when the safety point was taken?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — <c>git reset --hard</c> deletes uncommitted work too.</b>
    /// A newly staged file (<c>new.txt</c>) <b>disappeared</b> from disk after the reset.
    /// So with a dirty tree, saying "to undo: <c>git reset --hard &lt;sha&gt;</c>" is an
    /// <b>incomplete</b> promise: the commit comes back, the user's current work does not.
    /// <see cref="IsFullyRecoverable"/> makes that distinction.
    /// </remarks>
    public bool HasUncommittedChanges { get; init; }

    public bool IsDetached => BranchName is null;

    /// <summary>The abbreviated SHA — the one shown on screen.</summary>
    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;

    /// <summary>
    /// The command to run in order to return to this point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>MEASURED — on a detached <c>HEAD</c>, <c>reset --hard</c> moves no branch</b>, whereas on
    /// a branch it moves <b>the branch</b>. Both are correct behaviour but they differ; because in the
    /// detached case what the user usually wants is to return to that commit, <c>checkout</c> is
    /// suggested.
    /// </para>
    /// <para>
    /// ⚠️ The <b>SHA</b> is written, not a <b>sliding</b> reference such as <c>ORIG_HEAD</c> or
    /// <c>HEAD@{1}</c>: if the user copies the command and runs it later, a sliding reference would
    /// point somewhere else entirely.
    /// </para>
    /// </remarks>
    public string RecoveryCommand => IsDetached
        ? $"git checkout {ObjectId}"
        : $"git reset --hard {ObjectId}";

    /// <summary>
    /// Does the undo command bring <b>everything</b> back?
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> when the tree is dirty: uncommitted work cannot be recovered.
    /// The screen shows that as a separate warning rather than hiding the command.
    /// </remarks>
    public bool IsFullyRecoverable => !HasUncommittedChanges;
}

/// <summary>Taking a safety point (P07-T15).</summary>
public interface ISafetyPointRecorder
{
    /// <summary>
    /// Called immediately before an operation that rewrites history.
    /// </summary>
    Task<SafetyPoint> CaptureAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Records <c>HEAD</c> and whether the working tree is clean (P07-T15).
/// </summary>
public sealed class SafetyPointRecorder : ISafetyPointRecorder
{
    private readonly IGitProcessRunner _runner;

    public SafetyPointRecorder(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<SafetyPoint> CaptureAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        GitResult head = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "HEAD"),
            cancellationToken).ConfigureAwait(false);

        // In an unborn repository `rev-parse HEAD` fails — and there is no point to return to either.
        string objectId = head.IsSuccess ? head.GetStandardOutputText().Trim() : string.Empty;

        GitResult branch = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "symbolic-ref", "--quiet", "--short", "HEAD"),
            cancellationToken).ConfigureAwait(false);

        // On a detached HEAD `symbolic-ref` gives exit code 1; that is not an error but a state.
        string? branchName = branch.IsSuccess
            ? branch.GetStandardOutputText().Trim() is { Length: > 0 } name ? name : null
            : null;

        return new SafetyPoint
        {
            ObjectId = objectId,
            BranchName = branchName,
            Operation = operation,
            HasUncommittedChanges =
                await IsDirtyAsync(workingDirectory, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Is there an uncommitted change in the working tree?
    /// </summary>
    /// <remarks>
    /// Untracked files <b>do not count</b>: <c>reset --hard</c> does not touch them (measured — only
    /// staged/tracked changes were deleted), so they do not affect recoverability.
    /// </remarks>
    private async Task<bool> IsDirtyAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["status", "--porcelain=v2", "-z", "--untracked-files=no"],
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess && result.GetStandardOutputText().Length > 0;
    }
}
