using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The branch deletion confirmation (P06-T03).
/// </summary>
/// <remarks>
/// A two-round flow: the first round asks for an ordinary confirmation. When git refuses the branch as
/// <b>unmerged</b>, the dialog opens a second time, this time <b>with the recovery command</b>.
/// We do not work out whether it is merged in advance, because it was measured — <c>git branch -d</c>
/// also deletes a branch merged into its upstream, and our own calculation would produce false alarms.
/// </remarks>
public partial class DeleteBranchDialog : Window
{
    private DeleteBranchDecision _decision = DeleteBranchDecision.Cancelled;
    private bool _isUnmerged;

    public DeleteBranchDialog()
    {
        InitializeComponent();

        ForceBox.IsCheckedChanged += (_, _) => UpdateButton();

        UpdateButton();
    }

    internal static async Task<DeleteBranchDecision> ShowAsync(
        DeleteBranchRequest request,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        DeleteBranchDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    internal void Apply(DeleteBranchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _isUnmerged = request.IsUnmerged;

        MessageText.Text = request.IsUnmerged
            ? $"Could not delete branch '{request.Name}'."
            : $"Are you sure you want to delete branch '{request.Name}'?";

        UnmergedPanel.IsVisible = request.IsUnmerged;

        // 🔴 This command is the only reliable route to recovery: measured, the deleted branch's OWN
        // reflog goes too, and if the branch was never checked out in this working tree there is no
        // trace of it in HEAD's reflog either.
        RecoveryCommand.Text = request.LastCommitId is { Length: > 0 } id
            ? $"git branch {request.Name} {id}"
            : string.Empty;

        UpdateButton();
    }

    /// <summary>
    /// On an unmerged branch, <b>Delete</b> does not enable until the box is ticked.
    /// </summary>
    /// <remarks>
    /// A checkbox is enough and no separate dialog is needed: the recovery command is on screen, which
    /// means the operation can be undone (P05-T15's rule that "a dialog is only for irrecoverable
    /// operations").
    /// </remarks>
    private void UpdateButton() =>
        DeleteButton.IsEnabled = !_isUnmerged || ForceBox.IsChecked == true;

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isUnmerged && ForceBox.IsChecked != true)
        {
            return;
        }

        _decision = new DeleteBranchDecision { Confirmed = true, Force = _isUnmerged };

        Close();
    }
}
