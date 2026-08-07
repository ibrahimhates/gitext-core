using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>Reset diyaloğu (P07-T06).</summary>
public partial class ResetDialog : Window
{
    public ResetDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    internal static async Task ShowAsync(ResetViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        ResetDialog window = new() { DataContext = model };

        await window.ShowDialog(owner);
    }


    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
