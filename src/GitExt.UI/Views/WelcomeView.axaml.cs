using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using GitExt.UI.Localization;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The dashboard shown when no repository is open (P03-T16, layout P08-T25, rebuilt in P12-T03).
/// </summary>
/// <remarks>
/// The folder picker is opened here, in the code-behind: <c>IStorageProvider</c> is bound to the window
/// (<see cref="TopLevel"/>) and the ViewModel knowing about the window would break the layering rule.
/// The ViewModel only receives the "open this path" command.
/// </remarks>
public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
    }

    private DashboardViewModel? Dashboard =>
        (DataContext as MainWindowViewModel)?.Dashboard;

    private async void OnOpenFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;

        if (storage is null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = Loc.T("welcome_view.axaml.open_a_git_repository"), AllowMultiple = false });

        if (folders.Count == 0)
        {
            return;
        }

        // Without a local file path (a remote/virtual location) it cannot be opened; git wants a local path.
        string? path = folders[0].TryGetLocalPath();

        if (!string.IsNullOrEmpty(path))
        {
            await viewModel.OpenRepositoryAsync(path);
        }
    }

    /// <summary>
    /// The context menu acts on the tile it was opened on.
    /// </summary>
    /// <remarks>
    /// GitExtensions does the same thing (<c>_rightClickedItem</c>): the menu items read the
    /// selection rather than taking a parameter, because the "Categories" submenu has to be built
    /// for that one repository <b>before</b> it opens.
    /// </remarks>
    private void OnTileContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: DashboardRepositoryItem item })
        {
            item.Owner.SelectedItem = item;
        }
    }

    /// <summary>
    /// Enter opens the first result, ↓ moves into the list.
    /// </summary>
    /// <remarks>
    /// Taken from GitExtensions' <c>TextBoxSearch_KeyDown</c>: typing part of a name and pressing
    /// Enter is the fastest way into a repository, and it works without ever touching the mouse.
    /// </remarks>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (Dashboard is not { } dashboard)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (dashboard.FirstItem is { } first && first.OpenCommand.CanExecute(first.Path))
            {
                first.OpenCommand.Execute(first.Path);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Down)
        {
            // The first tile takes the focus; from there the arrow keys walk the list.
            RepositoryGroups.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
    }

    private void OnDevelopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        OpenBrowser("https://github.com/ibrahimhates/gitext-core");

    private void OnTranslateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        OpenBrowser("https://github.com/ibrahimhates/gitext-core/tree/main/src/GitExt.UI/Locales");

    private void OnIssuesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        OpenBrowser("https://github.com/ibrahimhates/gitext-core/issues");

    /// <summary>
    /// Opens the link in the system's browser.
    /// </summary>
    /// <remarks>
    /// <c>UseShellExecute</c> is essential: without it .NET takes the file for an executable and errors
    /// out. When the browser cannot be opened we stay silent — interrupting the welcome screen with an
    /// error box would be a bigger nuisance than this link is worth.
    /// </remarks>
    private static void OpenBrowser(string url)
    {
        try
        {
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
                or System.PlatformNotSupportedException
                or InvalidOperationException)
        {
        }
    }
}
