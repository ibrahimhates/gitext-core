using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Ana pencere. Sürükle-bırak ile depo açma (P03-T16) ve ana menü (P08-T26) burada bağlanır.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Diyalog bir sahip pencere istiyor; ViewModel'ın `Window` tanıması katman kuralını
        // bozardı (P05-T15'teki onay diyaloğuyla aynı desen).
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel model)
            {
                model.BranchPrompt = new DialogBranchPrompt(this);
                model.CheckoutPrompt = new DialogCheckoutPrompt(this);
                model.BranchEditPrompt = new DialogBranchEditPrompt(this);
                model.RemotesPrompt = new DialogRemotesPrompt(this);
                model.PullPrompt = new DialogPullPrompt(this, model);
            }
        };
    }

    /// <summary>Pull/Fetch ekranını gerçek bir pencereyle gösterir (P06-T06, T07).</summary>
    private sealed class DialogPullPrompt : IPullPrompt
    {
        private readonly Window _owner;
        private readonly MainWindowViewModel _model;

        public DialogPullPrompt(Window owner, MainWindowViewModel model)
        {
            _owner = owner;
            _model = model;
        }

        // "Yönet…" düğmesi uzak depo ekranını açıyor — GitExtensions'ta da `FormPull`'da
        // `AddRemote` düğmesi var (§ 9). İkinci bir yol değil, aynı komutun kısayolu.
        public Task ShowAsync(PullViewModel model) =>
            PullWindow.ShowAsync(
                model,
                _owner,
                () => _model.ManageRemotesCommand.ExecuteAsync(null));
    }

    /// <summary>Uzak depo yönetimi ekranını gerçek bir pencereyle gösterir (P06-T05).</summary>
    private sealed class DialogRemotesPrompt : IRemotesPrompt, IRemoteRemovalConfirmer
    {
        private readonly Window _owner;

        public DialogRemotesPrompt(Window owner) => _owner = owner;

        public IRemoteRemovalConfirmer RemovalConfirmer => this;

        public Task ShowAsync(RemotesViewModel model) => RemotesWindow.ShowAsync(model, _owner);

        public Task<bool> ConfirmAsync(RemoteRemovalRequest request) =>
            RemoveRemoteDialog.ShowAsync(request, _owner);
    }

    /// <summary>Dal düzenleme diyaloglarını gerçek bir pencereyle gösterir (P06-T03).</summary>
    private sealed class DialogBranchEditPrompt : IBranchEditPrompt
    {
        private readonly Window _owner;

        public DialogBranchEditPrompt(Window owner) => _owner = owner;

        public Task<RenameBranchDecision> RequestRenameAsync(RenameBranchRequest request) =>
            RenameBranchDialog.ShowAsync(request, _owner);

        public Task<DeleteBranchDecision> RequestDeleteAsync(DeleteBranchRequest request) =>
            DeleteBranchDialog.ShowAsync(request, _owner);
    }

    /// <summary>Dala geçme diyaloğunu gerçek bir pencereyle gösterir (P06-T02).</summary>
    private sealed class DialogCheckoutPrompt : ICheckoutPrompt
    {
        private readonly Window _owner;

        public DialogCheckoutPrompt(Window owner) => _owner = owner;

        public Task<CheckoutDecision> RequestAsync(CheckoutRequest request) =>
            CheckoutBranchDialog.ShowAsync(request, _owner);
    }

    /// <summary>Dal oluşturma diyaloğunu gerçek bir pencereyle gösterir (P06-T01).</summary>
    private sealed class DialogBranchPrompt : ICreateBranchPrompt
    {
        private readonly Window _owner;

        public DialogBranchPrompt(Window owner) => _owner = owner;

        public Task<CreateBranchDecision> RequestAsync(CreateBranchRequest request) =>
            CreateBranchDialog.ShowAsync(request, _owner);
    }

    /// <summary>
    /// Menüden depo açma. Karşılama ekranındakiyle aynı akış.
    /// </summary>
    /// <remarks>
    /// Klasör seçici kod arkasında: <c>IStorageProvider</c> pencereye bağlıdır ve
    /// ViewModel'ın pencereyi tanıması katman kuralını bozardı.
    /// </remarks>
    private async void OnOpenClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Git deposu aç", AllowMultiple = false });

        if (folders.Count == 0)
        {
            return;
        }

        string? path = folders[0].TryGetLocalPath();

        if (!string.IsNullOrEmpty(path))
        {
            await viewModel.OpenRepositoryAsync(path);
        }
    }

    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnAboutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Tam "Hakkında" penceresi Faz 08'in işi (P08-T21); şimdilik sürüm bilgisi başlıkta.
        Title = $"gitext-core — {typeof(MainWindow).Assembly.GetName().Version}";
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

    private void OnDismissBranchNoticeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel model)
        {
            model.BranchNotice = null;
        }
    }

    /// <summary>
    /// Commit ekranını açar (P05-T09).
    /// </summary>
    /// <remarks>
    /// GitExtensions'taki yeri: <i>Commands → Commit</i>, menünün <b>ilk</b> öğesi
    /// (<c>commandsToolStripMenuItem.DropDownItems</c>). Modal açılıyor; kapanınca commit
    /// listesi yenileniyor çünkü bu ekranda yeni bir commit oluşmuş olabilir.
    /// </remarks>
    private async void OnCommitClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model
            || model.CreateWorkingTree() is not { } workingTree)
        {
            return;
        }

        await workingTree.OpenAsync(model.Commits.Repository?.WorkingDirectory);
        await WorkingTreeWindow.Open(workingTree, this);
        await model.RefreshAsync();
    }
}
