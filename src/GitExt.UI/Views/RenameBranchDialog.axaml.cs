using Avalonia.Controls;
using GitExt.Core;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The branch rename dialog (P06-T03).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormRenameBranch</c> (§ 9).
/// </remarks>
public partial class RenameBranchDialog : Window
{
    private RenameBranchDecision _decision = RenameBranchDecision.Cancelled;

    public RenameBranchDialog()
    {
        InitializeComponent();

        // ⚠️ `TextChanged` does not fire in a window not attached to the visual tree (measured in
        // P06-T01); the validation would silently never run.
        NewNameTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Revalidate();
            }
        };

        Loaded += (_, _) => NewNameTextBox.Focus();

        Revalidate();
    }

    internal static async Task<RenameBranchDecision> ShowAsync(
        RenameBranchRequest request,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        RenameBranchDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    internal void Apply(RenameBranchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        CurrentNameText.Text = request.CurrentName;

        // The box arrives filled with the current name: a rename is usually a small correction (a typo,
        // adding a prefix); making them type it from scratch is needless work.
        NewNameTextBox.Text = request.CurrentName;

        Revalidate();
    }

    private void Revalidate()
    {
        string name = NewNameTextBox.Text ?? string.Empty;
        BranchNameProblem? problem = BranchName.Validate(name);

        RenameButton.IsEnabled = problem is null;

        bool show = problem is not null and not BranchNameProblem.Empty;

        ValidationText.IsVisible = show;
        ValidationText.Text = show ? CreateBranchDialog.Describe(problem!.Value) : string.Empty;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnRenameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string name = NewNameTextBox.Text ?? string.Empty;

        if (!BranchName.IsValid(name))
        {
            return;
        }

        _decision = new RenameBranchDecision { Confirmed = true, NewName = name };

        Close();
    }
}
