using GitExt.Core;
using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The side that carries out the partial staging actions in the diff panel (P05-T10).
/// </summary>
/// <remarks>
/// <see cref="DiffViewModel"/> is deliberately <b>standalone</b> (P04-T08): staging is meaningless in
/// commit history and in the comparison window. This interface is plugged in from outside at the one
/// place staging does make sense (the working directory view).
/// </remarks>
public interface IPartialStagingHost
{
    /// <summary>Can the side being shown be staged (the working tree side)?</summary>
    bool CanStage { get; }

    /// <summary>Can the side being shown be unstaged (the index side)?</summary>
    bool CanUnstage { get; }

    /// <summary>
    /// Applies the selection and refreshes the lists.
    /// </summary>
    /// <param name="diff">The file diff the selection belongs to.</param>
    /// <param name="selection">The line selection to apply.</param>
    /// <param name="stage">
    /// When <see langword="true"/> it stages, otherwise it takes it back out of the index.
    /// </param>
    Task ApplyAsync(FileDiff diff, PatchSelection selection, bool stage);

    /// <summary>
    /// <b>Discards the changes on the selected lines from the working tree</b> (P05-T15).
    /// </summary>
    /// <remarks>
    /// A separate method from stage/unstage: this is a <b>destructive</b> operation, it asks for
    /// confirmation and it is backed up. Added to the same call as a third flag, a caller could pass it
    /// by mistake.
    /// </remarks>
    Task DiscardAsync(FileDiff diff, PatchSelection selection);
}
