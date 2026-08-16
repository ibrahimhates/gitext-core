using Avalonia.Controls;

namespace GitExt.UI.Views;

/// <summary>
/// The confirmation for aborting an in-progress merge (P06-T12).
/// </summary>
public partial class AbortMergeDialog : Window
{
    private bool _confirmed;

    public AbortMergeDialog()
    {
        InitializeComponent();
    }

    internal static async Task<bool> ShowAsync(IReadOnlyList<string> conflicted, Window owner)
    {
        ArgumentNullException.ThrowIfNull(conflicted);
        ArgumentNullException.ThrowIfNull(owner);

        AbortMergeDialog dialog = new();

        dialog.ConflictList.ItemsSource = conflicted;

        // When the list is empty the panel is not shown either: an empty heading tells the user nothing.
        dialog.ConflictPanel.IsVisible = conflicted.Count > 0;

        await dialog.ShowDialog(owner);

        return dialog._confirmed;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }
}
