namespace GitExt.UI.ViewModels;

/// <summary>
/// The user's answer when nothing is staged (GitExtensions <c>ConfirmAndStageAllUnstaged</c>).
/// </summary>
public enum NoStagedChangesChoice
{
    Cancel,
    StageAllAndCommit,
    EmptyCommit,
}

/// <summary>
/// The user's answer on a detached HEAD (GitExtensions <c>_notOnBranch</c> task dialog).
/// </summary>
public enum NotOnBranchChoice
{
    Cancel,
    CheckoutBranch,
    CreateBranch,
    Continue,
}

/// <summary>The user's answer to the amend warning.</summary>
public sealed record AmendConfirmation
{
    public static AmendConfirmation Cancelled { get; } = new();

    public bool Confirmed { get; init; }

    /// <summary>GitExtensions' <c>DontConfirmAmend</c>; the suppressible dialog's checkbox.</summary>
    public bool DoNotAskAgain { get; init; }
}

/// <summary>
/// The gates the commit screen passes through before <c>git commit</c> runs.
/// </summary>
/// <remarks>
/// <para>
/// One member per gate in GitExtensions' <c>FormCommit.CheckForStagedAndCommit</c> /
/// <c>DoCommit</c>, in the same order they are asked. Separated out as an interface for the same
/// reason as <see cref="IDestructiveActionConfirmer"/>: the ViewModel tests must be able to walk
/// every branch of the state machine without opening a window.
/// </para>
/// <para>
/// 🔑 When no prompt is attached every question is answered with its <b>safe</b> default —
/// cancel. A commit must never happen because nobody was there to be asked.
/// </para>
/// </remarks>
public interface ICommitPrompt
{
    Task<AmendConfirmation> ConfirmAmendAsync();

    Task<bool> ConfirmEmptyMergeCommitAsync();

    Task<NoStagedChangesChoice> AskNoStagedChangesAsync(bool canStageAll);

    Task ReportMergeConflictsAsync();

    Task ReportMissingCommitMessageAsync();

    /// <summary>A commit-validation warning; <see langword="true"/> means "continue anyway".</summary>
    Task<bool> ConfirmCommitValidationAsync(string question);

    Task<NotOnBranchChoice> AskNotOnBranchAsync();
}
