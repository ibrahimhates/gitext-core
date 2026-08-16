using Avalonia.Controls;
using GitExt.Core;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// The create branch dialog (P06-T01).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormCreateBranch</c>; the layout and the order were taken
/// from there (§ 9).
/// </remarks>
public partial class CreateBranchDialog : Window
{
    private CreateBranchDecision _decision = CreateBranchDecision.Cancelled;

    public CreateBranchDialog()
    {
        InitializeComponent();

        // ⚠️ A property change rather than `TextChanged`: `TextChanged` does not fire in a window that is
        // not attached to the visual tree (measured in a headless test), and the validation was silently
        // never running.
        BranchNameTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Revalidate();
            }
        };
        CheckoutAfterCreateBox.IsCheckedChanged += (_, _) => UpdateDirtyWarning();

        Loaded += (_, _) => BranchNameTextBox.Focus();

        Revalidate();
    }

    /// <summary>Opens the dialog modally and returns the user's decision.</summary>
    internal static async Task<CreateBranchDecision> ShowAsync(
        CreateBranchRequest request,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        CreateBranchDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    /// <summary>Reflects the request onto the dialog.</summary>
    internal void Apply(CreateBranchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        StartPointText.Text = request.StartPointLabel;
        StartPointLabel.Text = request.StartPoint is null
            ? Loc.T("create_branch_dialog.axaml.create_branch_at_this_revision_current_head")
            : Loc.T("create_branch_dialog.axaml.create_branch_at_this_revision");

        _hasLocalChanges = request.HasLocalChanges;

        UpdateDirtyWarning();
        Revalidate();
    }

    private bool _hasLocalChanges;

    /// <summary>
    /// When the name is invalid, writes <b>why</b> and disables the button.
    /// </summary>
    /// <remarks>
    /// The validation is done with <see cref="BranchName"/>; it is pure so as not to start a
    /// <c>git</c> process on every keystroke. That the rules stay the same as git's is pinned down by
    /// the differential test in <c>BranchNameTests</c>.
    /// </remarks>
    private void Revalidate()
    {
        string name = BranchNameTextBox.Text ?? string.Empty;
        BranchNameProblem? problem = BranchName.Validate(name);

        CreateButton.IsEnabled = problem is null;

        // Showing an error message on an empty box is telling off a user who has not done anything yet.
        bool show = problem is not null and not BranchNameProblem.Empty;

        ValidationText.IsVisible = show;
        ValidationText.Text = show ? Describe(problem!.Value) : string.Empty;
    }

    /// <summary>
    /// Warns when checkout is ticked and the working tree is dirty.
    /// </summary>
    /// <remarks>
    /// MEASURED: on a dirty tree <c>git switch -c</c> usually <b>carries</b> the changes across, but
    /// <b>refuses</b> when there is a conflict (and then does not create the branch either). So this is
    /// not a block but information: we tell the user what may happen and leave the decision to them.
    /// </remarks>
    private void UpdateDirtyWarning()
    {
        bool warn = _hasLocalChanges && CheckoutAfterCreateBox.IsChecked == true;

        DirtyWarning.IsVisible = warn;
        DirtyWarning.Text = warn
            ? Loc.T("create_branch_dialog.axaml.there_are_uncommitted_changes_in_the_working")
              + Loc.T("create_branch_dialog.axaml.if_there_is_a_conflict_that_cannot_be_carrie")
            : string.Empty;
    }

    internal static string Describe(BranchNameProblem problem) => problem switch
    {
        BranchNameProblem.Empty => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_be_empty"),
        BranchNameProblem.NestedRefsPrefix =>
            Loc.T("create_branch_dialog.axaml.do_not_type_the_refs_heads_prefix_git_does_n"),
        BranchNameProblem.RevisionSyntax =>
            Loc.T("create_branch_dialog.axaml.is_revision_syntax_for_git_a_branch_name_oth"),
        BranchNameProblem.LeadingDash => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_start_with"),
        BranchNameProblem.ReservedHead => Loc.T("create_branch_dialog.axaml.head_is_a_name_reserved_by_git"),
        BranchNameProblem.ForbiddenCharacter =>
            Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_contain_spaces_or_these"),
        BranchNameProblem.InvalidSegment =>
            Loc.T("create_branch_dialog.axaml.components_cannot_start_with_or_end_with_loc"),
        BranchNameProblem.EmptySegment => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_start_or_end_with_or_co"),
        BranchNameProblem.InvalidDot => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_contain_or_end_with"),
        _ => Loc.T("create_branch_dialog.axaml.invalid_branch_name"),
    };

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnCreateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string name = BranchNameTextBox.Text ?? string.Empty;

        if (!BranchName.IsValid(name))
        {
            return;
        }

        _decision = new CreateBranchDecision
        {
            Confirmed = true,
            Name = name,
            Checkout = CheckoutAfterCreateBox.IsChecked == true,
        };

        Close();
    }
}
