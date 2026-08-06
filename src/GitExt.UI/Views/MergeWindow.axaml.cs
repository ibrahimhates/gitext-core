using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Merge penceresi (P06-T11).
/// </summary>
/// <remarks>GitExtensions'ta karşılığı <c>FormMergeBranch</c> (§ 9); orada da modal.</remarks>
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
