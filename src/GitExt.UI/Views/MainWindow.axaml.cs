using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Ana pencere. Sürükle-bırak ile depo açma burada bağlanır (P03-T16).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // Yalnızca dosya/klasör kabul edilir; metin sürüklendiğinde imleç "yasak" göstersin.
        // NOT: Avalonia 12'de API değişti — `e.Data`/`DataFormats.Files` yerine
        // `e.DataTransfer`/`DataFormat.File` (ölçüldü).
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Link
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IStorageItem[]? items = e.DataTransfer.TryGetFiles();

        if (items is null)
        {
            return;
        }

        // Uzak/sanal konumların yerel yolu yoktur; git yerel yol ister, onlar elenir.
        List<string> paths = [.. items
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)];

        if (paths.Count > 0)
        {
            await viewModel.TryOpenDroppedAsync(paths);
        }
    }
}
