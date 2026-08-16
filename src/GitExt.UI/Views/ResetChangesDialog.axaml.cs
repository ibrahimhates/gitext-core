using Avalonia.Controls;
using GitExt.Core.Model;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// The confirmation dialog for a destructive reset operation (P05-T15).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormResetChanges</c>; the layout and the button order were
/// taken from there (§ 9). The one difference is that the affected files are <b>listed</b>.
/// </remarks>
public partial class ResetChangesDialog : Window
{
    /// <summary>The maximum number of paths to show in the list.</summary>
    /// <remarks>
    /// A list of thousands of lines informs nobody; the first few paths answer the "which folder"
    /// question, and the count is already in the heading.
    /// </remarks>
    public const int PreviewLimit = 40;

    private ResetChangesDecision _decision = ResetChangesDecision.Cancelled;

    public ResetChangesDialog()
    {
        InitializeComponent();
    }

    /// <summary>Opens the dialog modally and returns the user's decision.</summary>
    internal static async Task<ResetChangesDecision> ShowAsync(
        ResetChangesRequest request,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        ResetChangesDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    /// <summary>Reflects the request onto the dialog.</summary>
    internal void Apply(ResetChangesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        int modified = request.ModifiedPaths.Count;
        int untracked = request.UntrackedPaths.Count;

        MessageText.Text = request.IncludesStaged
            ? $"All changes in {modified} files (including staged ones) will be reset."
            : $"Unstaged changes in {modified} files will be reset.";

        HintText.Text = Loc.T("reset_changes_dialog.axaml.this_deletes_uncommitted_work");

        AffectedList.ItemsSource = Preview(request);

        // The behaviour in GitExtensions: with no new file in the selection the box is off and disabled;
        // only when there is a new file is it on and disabled (that being the only option).
        DeleteUntrackedBox.Content = $"Also delete {untracked} new files and/or directories";
        DeleteUntrackedBox.IsEnabled = untracked > 0 && modified > 0;
        DeleteUntrackedBox.IsChecked = untracked > 0 && modified == 0;

        DoNotAskAgainBox.IsVisible = request.CanSuppress;
    }

    private static IReadOnlyList<string> Preview(ResetChangesRequest request)
    {
        List<string> lines = [];

        foreach (RepositoryPath path in request.ModifiedPaths.Take(PreviewLimit))
        {
            lines.Add($"M  {path.Value}");
        }

        foreach (RepositoryPath path in request.UntrackedPaths.Take(PreviewLimit))
        {
            lines.Add($"?  {path.Value}");
        }

        int total = request.ModifiedPaths.Count + request.UntrackedPaths.Count;

        if (total > lines.Count)
        {
            lines.Add($"… ve {total - lines.Count} dosya daha");
        }

        return lines;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnResetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _decision = new ResetChangesDecision
        {
            Confirmed = true,
            DeleteUntracked = DeleteUntrackedBox.IsChecked == true,
            DoNotAskAgain = DoNotAskAgainBox.IsChecked == true,
        };

        Close();
    }
}
