using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// Depo açılmadığında görünen karşılama ekranı (P03-T16, yerleşim P08-T25).
/// </summary>
/// <remarks>
/// Klasör seçici burada, kod arkasında açılıyor: <c>IStorageProvider</c> pencereye
/// (<see cref="TopLevel"/>) bağlıdır ve ViewModel'ın pencereyi tanıması katman kuralını
/// bozardı. ViewModel yalnızca "şu yolu aç" komutunu alır.
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

        // Yerel dosya yolu yoksa (uzak/sanal konum) açılamaz; git yerel yol ister.
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
    /// Bağlantıyı sistemin tarayıcısında açar.
    /// </summary>
    /// <remarks>
    /// <c>UseShellExecute</c> şart: onsuz .NET dosyayı çalıştırılabilir sanıp hata verir.
    /// Tarayıcı açılamazsa sessiz kalınıyor — karşılama ekranını bir hata kutusuyla
    /// kesmek bu bağlantının değerinden büyük bir rahatsızlık olurdu.
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
