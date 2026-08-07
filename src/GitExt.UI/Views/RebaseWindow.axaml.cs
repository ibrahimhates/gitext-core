using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>Rebase penceresi (P07-T09, P07-T10).</summary>
public partial class RebaseWindow : Window
{
    public RebaseWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    internal static async Task ShowAsync(RebaseViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        RebaseWindow window = new() { DataContext = model };

        await window.ShowDialog(owner);
    }


    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
