using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The remote management window (P06-T05).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormRemotes</c> (§ 9) and it opens <b>modally</b> there too
/// (<c>ShowDialog</c>) — unlike <c>FormDiff</c> (P04-T16), there is no benefit to having several of
/// these open at once here, and two of them would be writing the same config.
/// </remarks>
public partial class RemotesWindow : Window
{
    public RemotesWindow()
    {
        InitializeComponent();
    }

    internal static async Task ShowAsync(RemotesViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        RemotesWindow window = new() { DataContext = model };

        await window.ShowDialog(owner);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
