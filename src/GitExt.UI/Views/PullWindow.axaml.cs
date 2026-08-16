using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The Pull / Fetch window (P06-T06 + P06-T07).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormPull</c> (§ 9), and it opens modally there too.
/// Fetch has no screen of its own; "fetch only" is an option on this window.
/// </remarks>
public partial class PullWindow : Window
{
    /// <summary>The side that opens the remotes screen when "Manage…" is pressed.</summary>
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
