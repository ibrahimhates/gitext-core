using Avalonia.Controls;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// The remote removal confirmation (P06-T05).
/// </summary>
/// <remarks>
/// A <b>checkbox</b> is enough rather than a separate dialog, because the recovery commands are on
/// screen (the P05-T15 rule). <i>Remove</i> does not enable until the box is ticked.
/// </remarks>
public partial class RemoveRemoteDialog : Window
{
    private bool _confirmed;

    public RemoveRemoteDialog()
    {
        InitializeComponent();

        // `IsCheckedChanged` works even in a window not attached to the visual tree (measured in P06-T01;
        // `TextChanged` does not — the difference is misleading).
        ConfirmBox.IsCheckedChanged += (_, _) => RemoveButton.IsEnabled = ConfirmBox.IsChecked == true;

        RemoveButton.IsEnabled = false;
    }

    internal static async Task<bool> ShowAsync(RemoteRemovalRequest request, Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        RemoveRemoteDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._confirmed;
    }

    internal void Apply(RemoteRemovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MessageText.Text = $"Remote '{request.Name}' will be removed.";
        ImpactText.Text = DescribeImpact(request);
        RecoveryCommands.Text = string.Join('\n', request.RecoveryCommands);
    }

    /// <summary>
    /// States <b>concretely</b> what will be lost.
    /// </summary>
    /// <remarks>
    /// A single generic text ("are you sure?") does not give the user what they need to decide: the
    /// number of branches losing their upstream and the number of remote tracking branches to be deleted
    /// are what make the difference here (the "every option states what it will do" rule from P06-T02).
    /// </remarks>
    internal static string DescribeImpact(RemoteRemovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> parts = [];

        if (request.TrackingBranchCount > 0)
        {
            parts.Add(
                $"{request.TrackingBranchCount} remote-tracking branches (and their reflogs) will be deleted; "
                + Loc.T("remove_remote_dialog.axaml.commits_that_live_only_on_them_will_no_longe"));
        }

        if (request.AffectedBranches.Count > 0)
        {
            parts.Add(
                $"these local branches will lose their upstream link: "
                + string.Join(", ", request.AffectedBranches));
        }

        if (request.IsPushDefault)
        {
            parts.Add(Loc.T("remove_remote_dialog.axaml.this_remote_is_set_as_the_default_push_targe"));
        }

        return parts.Count == 0
            ? Loc.T("remove_remote_dialog.axaml.no_local_branch_or_remote_tracking_branch_is")
            : char.ToUpperInvariant(parts[0][0]) + parts[0][1..]
              + (parts.Count > 1 ? " · " + string.Join(" · ", parts.Skip(1)) : string.Empty)
              + ".";
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnRemoveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ConfirmBox.IsChecked != true)
        {
            return;
        }

        _confirmed = true;
        Close();
    }
}
