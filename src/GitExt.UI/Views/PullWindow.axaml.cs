using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Pull / Fetch penceresi (P06-T06 + P06-T07).
/// </summary>
/// <remarks>
/// GitExtensions'ta karşılığı <c>FormPull</c> (§ 9) ve orada da modal açılıyor.
/// Fetch'in ayrı bir ekranı yok; "yalnızca getir" bu pencerenin bir seçeneği.
/// </remarks>
public partial class PullWindow : Window
{
    /// <summary>"Yönet…" düğmesine basıldığında uzak depo ekranını açan taraf.</summary>
    internal Func<Task>? ManageRemotes { get; set; }

    public PullWindow()
    {
        InitializeComponent();
    }

    internal static async Task ShowAsync(PullViewModel model, Window owner, Func<Task>? manageRemotes = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        PullWindow window = new() { DataContext = model, ManageRemotes = manageRemotes };

        await window.ShowDialog(owner);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private async void OnManageRemotesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ManageRemotes is { } open)
        {
            await open();
        }
    }
}
