using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The push window (P06-T08).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormPush</c> (§ 9), and it opens modally there too. The
/// "Pull…" button on the bottom row comes from there as well: after a rejected push, the pull screen is
/// where the user is going anyway.
/// </remarks>
public partial class PushWindow : Window
{
    /// <summary>The side that opens the remotes screen when "Manage…" is pressed.</summary>
    internal Func<Task>? ManageRemotes { get; set; }

    /// <summary>The side that opens the Pull/Fetch screen when "Pull…" is pressed.</summary>
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
