using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>Çakışma çözüm penceresi (P07-T03, P07-T05).</summary>
public partial class ConflictWindow : Window
{
    public ConflictWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    internal static async Task ShowAsync(ConflictViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        ConflictWindow window = new() { DataContext = model };

        await window.ShowDialog(owner);
    }


    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
