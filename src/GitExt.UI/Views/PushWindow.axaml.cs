using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Push penceresi (P06-T08).
/// </summary>
/// <remarks>
/// GitExtensions'ta karşılığı <c>FormPush</c> (§ 9) ve orada da modal açılıyor. Alt sıradaki
/// "Pull…" düğmesi de oradan: reddedilen bir gönderimden sonra kullanıcının gideceği yer
/// zaten pull ekranı.
/// </remarks>
public partial class PushWindow : Window
{
    /// <summary>"Yönet…" düğmesine basıldığında uzak depo ekranını açan taraf.</summary>
    internal Func<Task>? ManageRemotes { get; set; }

    /// <summary>"Pull…" düğmesine basıldığında Pull/Fetch ekranını açan taraf.</summary>
    internal Func<Task>? OpenPull { get; set; }

    public PushWindow()
    {
        InitializeComponent();
    }

    internal static async Task ShowAsync(
        PushViewModel model,
        Window owner,
        Func<Task>? manageRemotes = null,
        Func<Task>? openPull = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        PushWindow window = new()
        {
            DataContext = model,
            ManageRemotes = manageRemotes,
            OpenPull = openPull,
        };

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

    private async void OnPullClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (OpenPull is { } open)
        {
            await open();
        }
    }
}
