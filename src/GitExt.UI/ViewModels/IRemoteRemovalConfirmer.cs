namespace GitExt.UI.ViewModels;

/// <summary>
/// The context given to the remote deletion confirmation (P06-T05).
/// </summary>
/// <remarks>
/// 🔴 All of this information is read <b>before</b> the deletion; none of it can be read afterwards
/// (the config keys are deleted, and the remote tracking branches and their reflogs are gone).
/// </remarks>
public sealed record RemoteRemovalRequest
{
    /// <summary>The name of the remote to be deleted.</summary>
    public required string Name { get; init; }

    /// <summary>The number of remote tracking branches that will be deleted with it.</summary>
    public int TrackingBranchCount { get; init; }

    /// <summary>Local branches whose upstream points at this remote and will lose their link.</summary>
    public IReadOnlyList<string> AffectedBranches { get; init; } = [];

    /// <summary>Does <c>remote.pushDefault</c> name this remote?</summary>
    public bool IsPushDefault { get; init; }

    /// <summary>
    /// The recovery commands the user can run.
    /// </summary>
    /// <remarks>
    /// ⚠️ These commands <b>do not bring the objects back</b>: only commits living on remote tracking
    /// branches return with a <c>fetch</c>, which means the <b>remote must still be reachable</b>. That
    /// is the difference from deleting a branch (P06-T03), and the dialog says so.
    /// </remarks>
    public IReadOnlyList<string> RecoveryCommands { get; init; } = [];
}

/// <summary>
/// The side that asks for the remote deletion confirmation (P06-T05).
/// </summary>
public interface IRemoteRemovalConfirmer
{
    /// <summary>Did the user confirm the deletion?</summary>
    Task<bool> ConfirmAsync(RemoteRemovalRequest request);
}
