using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExt.UI.Localization;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// What to do about a rejected push — GitExtensions'
/// <c>FormPush.AskForAutoPullOnPushRejectedAction</c>.
/// </summary>
/// <remarks>
/// The pull options appear <b>only for the current branch</b> (its <c>allOptions</c>): a branch that
/// is not checked out cannot be pulled into, so offering it would be a button that fails.
/// </remarks>
public partial class PushRejectedDialog : Window
{
    private PushRejectedAction _action = PushRejectedAction.Cancel;

    public PushRejectedDialog()
    {
        InitializeComponent();
    }

    internal static async Task<PushRejectedDecision> ShowAsync(
        PushRejectedRequest request,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        PushRejectedDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return new PushRejectedDecision
        {
            Action = dialog._action,
            DoNotAskAgain = dialog.SuppressBox.IsChecked == true,
        };
    }

    internal void Apply(PushRejectedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Title = string.Format(
            CultureInfo.CurrentCulture,
            Loc.T("push.push_to_remote"),
            request.Remote);

        HeadingText.Text = request.AllOptions
            ? Loc.T("push.pull_latest_changes_from_remote_repository")
            : Loc.T("push.push_rejected");

        BodyText.Text = request.AllOptions
            ? Loc.T("push.the_push_was_rejected_because_the_tip_of_you")
            : Loc.T("push.the_push_was_rejected_because_the_tip_of_cur");

        PullDefaultButton.Content = string.Format(
            CultureInfo.CurrentCulture,
            Loc.T("push.pull_with_the_default_pull_action_0"),
            request.DefaultPullActionName);

        PullOptions.IsVisible = request.AllOptions;
    }

    internal PushRejectedAction ActionForTest => _action;

    private void Choose(PushRejectedAction action)
    {
        _action = action;
        Close();
    }

    private void OnPullDefaultClick(object? sender, RoutedEventArgs e) =>
        Choose(PushRejectedAction.PullDefault);

    private void OnPullRebaseClick(object? sender, RoutedEventArgs e) =>
        Choose(PushRejectedAction.PullRebase);

    private void OnPullMergeClick(object? sender, RoutedEventArgs e) =>
        Choose(PushRejectedAction.PullMerge);

    private void OnForceClick(object? sender, RoutedEventArgs e) =>
        Choose(PushRejectedAction.ForceWithLease);

    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        Choose(PushRejectedAction.Cancel);
}
