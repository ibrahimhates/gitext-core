using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The merge window (P06-T11).
/// </summary>
/// <remarks>Its counterpart in GitExtensions is <c>FormMergeBranch</c> (§ 9); modal there too.</remarks>
public partial class MergeWindow : Window
{
    public MergeWindow()
    {
        InitializeComponent();
    }

    internal static async Task ShowAsync(MergeViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        MergeWindow window = new() { DataContext = model };

        await window.ShowDialog(owner);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
