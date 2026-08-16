namespace GitExt.UI.ViewModels;

/// <summary>
/// The context given to the branch rename dialog (P06-T03).
/// </summary>
public sealed record RenameBranchRequest
{
    public required string CurrentName { get; init; }
}

/// <summary>
/// The rename decision (P06-T03).
/// </summary>
public sealed record RenameBranchDecision
{
    public bool Confirmed { get; init; }

    public string NewName { get; init; } = string.Empty;

    public static RenameBranchDecision Cancelled { get; } = new();
}

/// <summary>
/// The context given to the branch deletion dialog (P06-T03).
/// </summary>
public sealed record DeleteBranchRequest
{
    public required string Name { get; init; }

    /// <summary>
    /// Is the branch unmerged? Only correct on the second round (after git has refused).
    /// </summary>
    /// <remarks>
    /// 🔴 We <b>do not work this out ourselves</b>: measured, <c>git branch -d</c> deletes a branch even
    /// when it is merged into its <b>upstream</b> rather than into HEAD. Our own calculation would
    /// produce false alarms on those branches. The decision is git's.
    /// </remarks>
    public bool IsUnmerged { get; init; }

    /// <summary>The branch's tip — shown as the way back.</summary>
    public string? LastCommitId { get; init; }
}

/// <summary>
/// The deletion decision (P06-T03).
/// </summary>
public sealed record DeleteBranchDecision
{
    public bool Confirmed { get; init; }

    /// <summary>Should an unmerged branch be deleted too?</summary>
    public bool Force { get; init; }

    public static DeleteBranchDecision Cancelled { get; } = new();
}

/// <summary>
/// The side that shows the branch editing dialogs (P06-T03).
/// </summary>
public interface IBranchEditPrompt
{
    Task<RenameBranchDecision> RequestRenameAsync(RenameBranchRequest request);

    Task<DeleteBranchDecision> RequestDeleteAsync(DeleteBranchRequest request);
}
