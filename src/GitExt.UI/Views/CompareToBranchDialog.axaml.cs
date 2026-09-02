using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GitExt.UI.Views;

/// <summary>
/// The branch picker for "Compare to branch…" (P14-T04) — GitExtensions' <c>FormCompareToBranch</c>.
/// </summary>
public partial class CompareToBranchDialog : Window
{
    private string? _branch;

    public CompareToBranchDialog()
    {
        InitializeComponent();

        Loaded += (_, _) => BranchBox.Focus();
    }

    internal static async Task<string?> ShowAsync(IReadOnlyList<string> branches, Window owner)
    {
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(owner);

        CompareToBranchDialog dialog = new();
        dialog.Apply(branches);

        await dialog.ShowDialog(owner);

        return dialog._branch;
    }

    internal void Apply(IReadOnlyList<string> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);

        BranchBox.ItemsSource = branches;
        BranchBox.SelectedIndex = branches.Count > 0 ? 0 : -1;
        CompareButton.IsEnabled = branches.Count > 0;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnCompareClick(object? sender, RoutedEventArgs e)
    {
        // GitExtensions keeps the dialog open when nothing is selected rather than comparing
        // against an empty ref.
        if (BranchBox.SelectedItem is not string { Length: > 0 } branch)
        {
            return;
        }

        _branch = branch;

        Close();
    }
}
