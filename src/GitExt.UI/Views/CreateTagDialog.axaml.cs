using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExt.Core;
using GitExt.UI.Localization;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The create tag dialog (P14-T03).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormCreateTag</c>; the field order and the texts were
/// taken from there.
/// </remarks>
public partial class CreateTagDialog : Window
{
    private CreateTagDecision _decision = CreateTagDecision.Cancelled;
    private IReadOnlyList<string> _existingTags = [];

    public CreateTagDialog()
    {
        InitializeComponent();

        // ⚠️ A property change rather than `TextChanged`: `TextChanged` does not fire in a window that
        // is not attached to the visual tree (measured in P06-T01), and the validation silently never
        // ran.
        TagNameTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Revalidate();
            }
        };

        KindBox.SelectionChanged += (_, _) => UpdateKind();
        ForceBox.IsCheckedChanged += (_, _) => Revalidate();

        Loaded += (_, _) => TagNameTextBox.Focus();

        UpdateKind();
        Revalidate();
    }

    internal static async Task<CreateTagDecision> ShowAsync(CreateTagRequest request, Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        CreateTagDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    internal void Apply(CreateTagRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TargetText.Text = request.TargetLabel;
        _existingTags = request.ExistingTags;

        Revalidate();
    }

    internal TagKind SelectedKind => KindBox.SelectedIndex switch
    {
        1 => TagKind.Annotated,
        2 => TagKind.SignWithDefaultKey,
        _ => TagKind.Lightweight,
    };

    /// <summary>
    /// A lightweight tag carries no message — GitExtensions' <c>TagOperation.CanProvideMessage</c>.
    /// </summary>
    private void UpdateKind()
    {
        bool providesMessage = SelectedKind != TagKind.Lightweight;

        MessageLabel.IsEnabled = providesMessage;
        MessageTextBox.IsEnabled = providesMessage;

        Revalidate();
    }

    /// <summary>
    /// Writes <b>why</b> the name is refused and disables the button.
    /// </summary>
    /// <remarks>
    /// 🔴 MEASURED — <c>git tag &lt;existing&gt;</c> fails with <c>fatal: tag 'v1.0' already
    /// exists</c> (exit 128) and <c>--force</c> is what turns that into
    /// <c>Updated tag 'v1.0' (was …)</c>. Since the tags already loaded say which names are taken,
    /// the collision is answered before git is started — but only as a block on <c>Force</c> being
    /// off, which is the switch that makes the overwrite deliberate.
    /// </remarks>
    private void Revalidate()
    {
        string name = TagNameTextBox.Text ?? string.Empty;
        BranchNameProblem? problem = TagNameRules.Validate(name);

        bool duplicate = problem is null
            && ForceBox.IsChecked != true
            && _existingTags.Contains(name, StringComparer.Ordinal);

        CreateButton.IsEnabled = problem is null && !duplicate;

        bool show = problem is not null and not BranchNameProblem.Empty;

        ValidationText.IsVisible = show || duplicate;
        ValidationText.Text = duplicate
            ? Loc.T("create_tag_dialog.axaml.a_tag_with_this_name_already_exists_tick_for")
            : show ? Describe(problem!.Value) : string.Empty;

        UpdateForceWarning();
    }

    private void UpdateForceWarning()
    {
        bool warn = ForceBox.IsChecked == true
            && _existingTags.Contains(TagNameTextBox.Text ?? string.Empty, StringComparer.Ordinal);

        ForceWarning.IsVisible = warn;
        ForceWarning.Text = warn
            ? Loc.T("create_tag_dialog.axaml.the_existing_tag_will_be_moved_to_this_commi")
            : string.Empty;
    }

    internal static string Describe(BranchNameProblem problem) => problem switch
    {
        BranchNameProblem.Empty => Loc.T("create_tag_dialog.axaml.a_tag_name_cannot_be_empty"),
        BranchNameProblem.NestedRefsPrefix =>
            Loc.T("create_tag_dialog.axaml.do_not_type_the_refs_tags_prefix_git_does_no"),
        BranchNameProblem.RevisionSyntax =>
            Loc.T("create_branch_dialog.axaml.is_revision_syntax_for_git_a_branch_name_oth"),
        BranchNameProblem.LeadingDash => Loc.T("create_tag_dialog.axaml.a_tag_name_cannot_start_with"),
        BranchNameProblem.ReservedHead => Loc.T("create_branch_dialog.axaml.head_is_a_name_reserved_by_git"),
        BranchNameProblem.ForbiddenCharacter =>
            Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_contain_spaces_or_these"),
        BranchNameProblem.InvalidSegment =>
            Loc.T("create_branch_dialog.axaml.components_cannot_start_with_or_end_with_loc"),
        BranchNameProblem.EmptySegment => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_start_or_end_with_or_co"),
        BranchNameProblem.InvalidDot => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_contain_or_end_with"),
        _ => Loc.T("create_tag_dialog.axaml.invalid_tag_name"),
    };

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        string name = TagNameTextBox.Text ?? string.Empty;

        if (!TagNameRules.IsValid(name))
        {
            return;
        }

        _decision = new CreateTagDecision
        {
            Confirmed = true,
            Name = name,
            Kind = SelectedKind,
            Message = MessageTextBox.Text ?? string.Empty,
            Force = ForceBox.IsChecked == true,
        };

        Close();
    }
}
