using Avalonia.Controls;
using GitExt.Core;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// The checkout branch dialog (P06-T02).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormCheckoutBranch</c>; the order of the "Local changes"
/// group was taken from there (§ 9): <i>Don't change · Merge · Stash · Reset</i>.
/// </remarks>
public partial class CheckoutBranchDialog : Window
{
    private CheckoutDecision _decision = CheckoutDecision.Cancelled;

    public CheckoutBranchDialog()
    {
        InitializeComponent();

        foreach (RadioButton option in Options)
        {
            option.IsCheckedChanged += (_, _) =>
            {
                UpdateHint();
                UpdateDefaultBox();
            };
        }

        UpdateHint();
        UpdateDefaultBox();
    }

    private RadioButton[] Options => [KeepOption, MergeOption, StashOption, DiscardOption];

    /// <summary>Opens the dialog modally and returns the user's decision.</summary>
    internal static async Task<CheckoutDecision> ShowAsync(CheckoutRequest request, Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        CheckoutBranchDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    /// <summary>Reflects the request onto the dialog.</summary>
    internal void Apply(CheckoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TargetText.Text = request.TargetLabel;
        TargetLabel.Text = request.IsDetached ? Loc.T("checkout_branch_dialog.axaml.commit_to_check_out") : Loc.T("checkout_branch_dialog.axaml.branch_to_switch_to");
        DetachWarning.IsVisible = request.IsDetached;

        // On a clean tree all four options give the same result; asking would be nothing but noise.
        LocalChangesGroup.IsVisible = request.HasLocalChanges;

        Select(request.DefaultAction);

        UpdateHint();
        UpdateDefaultBox();
    }

    /// <summary>
    /// States <b>what the selected action will do</b>.
    /// </summary>
    /// <remarks>
    /// The labels are not enough on their own: in the measurement "reset" <b>does not touch</b> untracked
    /// files but deletes tracked unstaged content <b>irrecoverably</b>, while "stash" preserves both. If
    /// that difference is invisible at the moment of choosing, the user takes the wrong option for an
    /// innocent one.
    /// </remarks>
    private void UpdateHint()
    {
        ActionHint.Text = SelectedAction switch
        {
            LocalChangesAction.Keep =>
                Loc.T("checkout_branch_dialog.axaml.the_changes_are_carried_to_the_new_branch_if")
                + Loc.T("checkout_branch_dialog.axaml.and_nothing_changes"),
            LocalChangesAction.Merge =>
                Loc.T("checkout_branch_dialog.axaml.the_changes_are_merged_into_the_target_if_co")
                + Loc.T("checkout_branch_dialog.axaml.happens_but_the_files_are_left_unresolved"),
            LocalChangesAction.Stash =>
                Loc.T("checkout_branch_dialog.axaml.the_changes_are_stashed_along_with_untracked")
                + Loc.T("checkout_branch_dialog.axaml.nothing_is_lost_you_can_restore_it_later"),
            LocalChangesAction.Discard =>
                Loc.T("checkout_branch_dialog.axaml.changes_in_tracked_files_are_discarded_untra")
                + Loc.T("checkout_branch_dialog.axaml.files_are_left_alone_the_discarded_content_i")
                + Loc.T("checkout_branch.recover_it_from_the_safety_point"),
            _ => string.Empty,
        };
    }

    private LocalChangesAction SelectedAction
    {
        get
        {
            if (MergeOption.IsChecked == true)
            {
                return LocalChangesAction.Merge;
            }

            if (StashOption.IsChecked == true)
            {
                return LocalChangesAction.Stash;
            }

            return DiscardOption.IsChecked == true
                ? LocalChangesAction.Discard
                : LocalChangesAction.Keep;
        }
    }

    private void Select(LocalChangesAction action)
    {
        KeepOption.IsChecked = action == LocalChangesAction.Keep;
        MergeOption.IsChecked = action == LocalChangesAction.Merge;
        StashOption.IsChecked = action == LocalChangesAction.Stash;
        DiscardOption.IsChecked = action == LocalChangesAction.Discard;
    }

    /// <summary>
    /// Keeps "discard" from ever becoming the default.
    /// </summary>
    /// <remarks>
    /// <c>FormCheckoutBranch.rbReset_CheckedChanged</c> does the same, and the reason is worth
    /// keeping: every other option is recoverable, so making one of them the default costs the user
    /// a step. Making <b>discard</b> the default would silently throw away uncommitted work on every
    /// later branch switch.
    /// </remarks>
    private void UpdateDefaultBox()
    {
        bool discarding = DiscardOption.IsChecked == true;

        DefaultActionBox.IsEnabled = !discarding;

        if (discarding)
        {
            DefaultActionBox.IsChecked = false;
        }
    }

    internal LocalChangesAction SelectedActionForTest => SelectedAction;

    internal bool DefaultBoxEnabledForTest => DefaultActionBox.IsEnabled;

    internal bool DefaultBoxCheckedForTest
    {
        get => DefaultActionBox.IsChecked == true;
        set
        {
            DefaultActionBox.IsChecked = value;
            UpdateDefaultBox();
        }
    }

    internal bool LocalChangesVisibleForTest => LocalChangesGroup.IsVisible;

    internal void SetOptionForTest(LocalChangesAction action)
    {
        Select(action);
        UpdateHint();
        UpdateDefaultBox();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnCheckoutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _decision = new CheckoutDecision
        {
            Confirmed = true,

            // When the group is hidden the tree is clean; there is no point asking about an action.
            LocalChanges = LocalChangesGroup.IsVisible ? SelectedAction : LocalChangesAction.Keep,
            RememberAction = LocalChangesGroup.IsVisible && DefaultActionBox.IsChecked == true,
        };

        Close();
    }
}
