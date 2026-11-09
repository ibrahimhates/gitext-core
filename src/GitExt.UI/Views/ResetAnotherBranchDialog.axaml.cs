using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

public partial class ResetAnotherBranchDialog : Window
{
    private bool _confirmed;

    public ResetAnotherBranchDialog()
    {
        InitializeComponent();

        Loaded += (_, _) => BranchCombo.Focus();
    }

    internal static async Task<bool> ShowAsync(ResetAnotherBranchViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        ResetAnotherBranchDialog dialog = new() { DataContext = model };

        await dialog.ShowDialog(owner);

        return dialog._confirmed;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ResetAnotherBranchViewModel { CanReset: true })
        {
            return;
        }

        _confirmed = true;
        Close();
    }
}
