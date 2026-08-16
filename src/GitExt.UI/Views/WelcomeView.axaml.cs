using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// The welcome screen shown when no repository is open (P03-T16, layout P08-T25).
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

    private void OnDevelopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        OpenBrowser("https://github.com/ibrahimhates/gitext-core");

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
