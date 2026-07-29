using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Depo açılmadığında görünen karşılama ekranı (P03-T16).
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
            new FolderPickerOpenOptions { Title = "Git deposu aç", AllowMultiple = false });

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
}
