using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>The cherry-pick / revert dialog (P07-T07, P07-T08).</summary>
public partial class SequencerDialog : Window
{
    public SequencerDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    internal static async Task ShowAsync(SequencerViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        SequencerDialog window = new() { DataContext = model };

        await window.ShowDialog(owner);
    }


    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
