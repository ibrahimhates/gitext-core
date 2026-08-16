using GitExt.Core;
using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The question put to the user for a reset confirmation (P05-T15).
/// </summary>
/// <remarks>
/// The numbers are shown in the dialog: an "are you sure?" asked without saying what will go pushes
/// the user towards clicking rather than thinking.
/// </remarks>
public sealed record ResetChangesRequest
{
    /// <summary>The tracked files whose changes will be discarded.</summary>
    public required IReadOnlyList<RepositoryPath> ModifiedPaths { get; init; }

    /// <summary>Silinebilecek takip edilmeyen dosyalar.</summary>
    public required IReadOnlyList<RepositoryPath> UntrackedPaths { get; init; }

    /// <summary>Will staged content be discarded too (<see cref="DiscardScope.All"/>)?</summary>
    public required bool IncludesStaged { get; init; }

    /// <summary>
    /// Can "do not ask again" be offered for this operation?
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> only for operations that <b>can be backed up</b>. Offering this option on
    /// an operation with no backup would open a data-loss path the user would never be warned about
    /// again.
    /// </remarks>
    public required bool CanSuppress { get; init; }
}

/// <summary>The user's answer to a reset confirmation.</summary>
public sealed record ResetChangesDecision
{
    /// <summary>The user cancelled.</summary>
    public static ResetChangesDecision Cancelled { get; } = new();

    /// <summary>Was the operation confirmed?</summary>
    public bool Confirmed { get; init; }

    /// <summary>Takip edilmeyen dosyalar da silinsin mi?</summary>
    public bool DeleteUntracked { get; init; }

    /// <summary>Do not ask about this operation again.</summary>
    public bool DoNotAskAgain { get; init; }
}

/// <summary>
/// Obtains the user's confirmation for destructive operations (P05-T15).
/// </summary>
/// <remarks>
/// Separated out as an interface so ViewModel tests can run the "the user cancelled" / "confirmed"
/// scenarios without opening a real window. The same pattern was set up with
/// <see cref="IPartialStagingHost"/> in P05-T09.
/// </remarks>
public interface IDestructiveActionConfirmer
{
    /// <summary>Asks for a reset confirmation.</summary>
    Task<ResetChangesDecision> ConfirmResetAsync(ResetChangesRequest request);
}
