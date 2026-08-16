namespace GitExt.UI.ViewModels;

/// <summary>
/// The context handed to the create branch dialog (P06-T01).
/// </summary>
public sealed record CreateBranchRequest
{
    /// <summary>
    /// The starting point (a commit hash or a ref name). <c>HEAD</c> when <see langword="null"/>.
    /// </summary>
    public string? StartPoint { get; init; }

    /// <summary>The description of the starting point to show the user.</summary>
    public required string StartPointLabel { get; init; }

    /// <summary>
    /// Are there uncommitted changes in the working tree?
    /// </summary>
    /// <remarks>
    /// For the <b>warning</b> only: measured, <c>git switch -c</c> usually carries the changes across on
    /// a dirty tree. Information, not a block.
    /// </remarks>
    public bool HasLocalChanges { get; init; }
}

/// <summary>
/// The user's decision in the create branch dialog (P06-T01).
/// </summary>
public sealed record CreateBranchDecision
{
    /// <summary>Did the user confirm?</summary>
    public bool Confirmed { get; init; }

    /// <summary>The branch name entered.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Should the branch be checked out after creating it?</summary>
    public bool Checkout { get; init; } = true;

    /// <summary>A cancelled decision.</summary>
    public static CreateBranchDecision Cancelled { get; } = new();
}

/// <summary>
/// The side that shows the create branch dialog (P06-T01).
/// </summary>
/// <remarks>
/// The same reasoning as <see cref="IDestructiveActionConfirmer"/>: the dialog needs an owner window,
/// and that is only known at the moment it opens; the ViewModel knowing about <c>Window</c> would break
/// the layering rule.
/// </remarks>
public interface ICreateBranchPrompt
{
    /// <summary>Shows the dialog and returns the decision.</summary>
    Task<CreateBranchDecision> RequestAsync(CreateBranchRequest request);
}
