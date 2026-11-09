using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

public partial class ArchiveDialog : Window
{
    public ArchiveDialog()
    {
        InitializeComponent();
    }

    internal Func<ArchiveViewModel, Task<string?>>? SavePathPrompt { get; set; }

    internal static async Task ShowAsync(ArchiveViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        ArchiveDialog dialog = new() { DataContext = model };

        await dialog.ShowDialog(owner);
    }

    internal async Task SaveAsync()
    {
        if (DataContext is not ArchiveViewModel model)
        {
            return;
        }

        string? path = await (SavePathPrompt ?? PickAsync)(model);

        if (path is { Length: > 0 } && await model.ArchiveAsync(path))
        {
            Close();
        }
    }

    private async Task<string?> PickAsync(ArchiveViewModel model)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = model.DefaultFileName,
            DefaultExtension = model.Extension,
            FileTypeChoices = [new FilePickerFileType(model.Extension) { Patterns = [$"*.{model.Extension}"] }],
        });

        return file?.TryGetLocalPath();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e) => await SaveAsync();
}
