using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Uzak depo yönetimi penceresi (P06-T05).
/// </summary>
/// <remarks>
/// GitExtensions'ta karşılığı <c>FormRemotes</c> (§ 9) ve orada da <b>modal</b> açılıyor
/// (<c>ShowDialog</c>) — <c>FormDiff</c>'in aksine (P04-T16) burada aynı anda birden fazla
/// pencere açık olmasının bir faydası yok, üstelik ikisi aynı config'i yazardı.
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
