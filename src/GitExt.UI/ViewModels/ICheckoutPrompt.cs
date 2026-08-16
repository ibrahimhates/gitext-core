using GitExt.Core;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The context given to the checkout branch dialog (P06-T02).
/// </summary>
public sealed record CheckoutRequest
{
    /// <summary>The target: a branch name or a commit hash.</summary>
    public required string Target { get; init; }

    /// <summary>The description of the target to show the user.</summary>
    public required string TargetLabel { get; init; }

    /// <summary>
    /// Is the target not a branch but a commit directly? (it will become a detached HEAD)
    /// </summary>
    public bool IsDetached { get; init; }

    /// <summary>Are there uncommitted changes in the working tree?</summary>
    /// <remarks>
    /// Without them the whole "local changes" group is meaningless; it is not shown.
    /// </remarks>
    public bool HasLocalChanges { get; init; }
}

/// <summary>
/// The user's decision in the checkout branch dialog (P06-T02).
/// </summary>
public sealed record CheckoutDecision
{
    public bool Confirmed { get; init; }

    /// <summary>What is to be done with the local changes?</summary>
    public LocalChangesAction LocalChanges { get; init; } = LocalChangesAction.Keep;

    /// <summary>A cancelled decision.</summary>
    public static CheckoutDecision Cancelled { get; } = new();
}

/// <summary>
/// The side that shows the checkout branch dialog (P06-T02).
/// </summary>
public interface ICheckoutPrompt
{
    Task<CheckoutDecision> RequestAsync(CheckoutRequest request);
}
