using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>The reflog browser window (P07-T14).</summary>
public partial class ReflogWindow : Window
{
    public ReflogWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    internal static async Task ShowAsync(ReflogViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        ReflogWindow window = new() { DataContext = model };

        await window.ShowDialog(owner);
    }


    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
