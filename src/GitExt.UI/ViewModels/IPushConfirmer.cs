namespace GitExt.UI.ViewModels;

/// <summary>
/// A suppressible yes/no answer (P06-T08).
/// </summary>
/// <remarks>
/// The counterpart of GitExtensions' <c>MessageBoxes.ConfirmSuppressible</c>: the answer and the
/// "don't show me this message again" tick travel together, because the caller is the side that
/// knows <b>which</b> setting the tick belongs to.
/// </remarks>
public sealed record PushConfirmation
{
    /// <summary>The user said no (or closed the window).</summary>
    public static PushConfirmation Declined { get; } = new();

    /// <summary>The user said yes.</summary>
    public static PushConfirmation Accepted { get; } = new() { Confirmed = true };

    public bool Confirmed { get; init; }

    public bool DoNotAskAgain { get; init; }
}

/// <summary>
/// The answer to the tracking-reference question (P06-T08).
/// </summary>
/// <remarks>
/// Three-way on purpose — GitExtensions asks it with <c>MessageBoxButtons.YesNoCancel</c>:
/// <i>No</i> pushes without a tracking reference, <i>Cancel</i> abandons the push altogether.
/// Folding the two together would silently push when the user meant to back out.
/// </remarks>
public enum TrackingReferenceAnswer
{
    /// <summary>Add the tracking reference (<c>--set-upstream</c>).</summary>
    Yes,

    /// <summary>Push without one.</summary>
    No,

    /// <summary>Abandon the push.</summary>
    Cancel,
}

/// <summary>What the user chose after a rejected push (P06-T08).</summary>
public enum PushRejectedAction
{
    /// <summary>Do nothing.</summary>
    Cancel,

    /// <summary>Pull with whatever the configuration says.</summary>
    PullDefault,

    /// <summary>Pull with rebase.</summary>
    PullRebase,

    /// <summary>Pull with merge.</summary>
    PullMerge,

    /// <summary>Retry the push with <c>--force-with-lease</c>.</summary>
    ForceWithLease,
}

/// <summary>The question asked after a rejected push (P06-T08).</summary>
public sealed record PushRejectedRequest
{
    /// <summary>The remote the push was rejected from; goes into the caption.</summary>
    public required string Remote { get; init; }

    /// <summary>
    /// Was the rejected ref the <b>current</b> branch?
    /// </summary>
    /// <remarks>
    /// GitExtensions calls this <c>allOptions</c>: the pull buttons only appear for the current
    /// branch, because there is no way to pull into a branch that is not checked out. For any
    /// other ref only force-with-lease is offered.
    /// </remarks>
    public required bool AllOptions { get; init; }

    /// <summary>Name of the configured default pull action (<c>merge</c>, <c>rebase</c>, …).</summary>
    public required string DefaultPullActionName { get; init; }
}

/// <summary>The decision taken after a rejected push (P06-T08).</summary>
public sealed record PushRejectedDecision
{
    public static PushRejectedDecision Cancelled { get; } = new();

    public PushRejectedAction Action { get; init; } = PushRejectedAction.Cancel;

    public bool DoNotAskAgain { get; init; }
}

/// <summary>
/// The confirmation gates of the push flow (P06-T08).
/// </summary>
/// <remarks>
/// <para>
/// Every gate GitExtensions' <c>FormPush.PushChanges</c> passes through, in the same order:
/// <i>is a remote configured</i> → <i>is a branch/tag selected</i> → <i>is this branch new for
/// the remote</i> → <i>should a tracking reference be added</i> → the push itself → <i>what to do
/// about a rejection</i>.
/// </para>
/// <para>
/// It is an interface for the same reason as <see cref="IDestructiveActionConfirmer"/>: a dialog
/// needs an owner window and the ViewModel must not know about <c>Window</c>. The suppression
/// ("don't show me this message again") is handled by the <b>implementation</b>, exactly where
/// GitExtensions handles it — inside <c>ConfirmSuppressible</c> — so that no call site can forget
/// to honour it.
/// </para>
/// </remarks>
public interface IPushConfirmer
{
    /// <summary>"The branch you are about to push seems to be a new branch for the remote."</summary>
    Task<PushConfirmation> ConfirmNewBranchAsync(string remoteBranch, string remote);

    /// <summary>"The branch {0} does not have a tracking reference…"</summary>
    Task<TrackingReferenceAnswer> ConfirmTrackingReferenceAsync(string localBranch, string remoteBranch);

    /// <summary>"Please configure a remote repository first. Would you like to do it now?"</summary>
    Task<bool> ConfirmConfigureRemoteAsync();

    /// <summary>Reports a blocking error (no branch selected, no tag selected).</summary>
    Task ReportErrorAsync(string message);

    /// <summary>Asks what to do about a rejected push.</summary>
    Task<PushRejectedDecision> AskRejectedActionAsync(PushRejectedRequest request);
}

/// <summary>
/// The confirmation gates of the pull flow (P06-T07).
/// </summary>
/// <remarks>
/// Only the two gates that can actually lose something are here. <c>FormPull</c> has more
/// (rebasing a merge commit, initialising submodules, re-applying the stash), but each of those
/// needs machinery this screen does not have yet; the report lists them.
/// </remarks>
public interface IPullConfirmer
{
    /// <summary>"Warning! The fetch with prune will remove all the remote-tracking references…"</summary>
    Task<PushConfirmation> ConfirmPruneAsync(string remote);

    /// <summary>"You cannot \"pull\" when git head detached."</summary>
    Task<bool> ConfirmDetachedHeadAsync();
}
