using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Diagnostics;
using GitExt.Core.Git;
using GitExt.UI.Themes;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Ana pencere. Sürükle-bırak ile depo açma (P03-T16) ve ana menü (P08-T26) burada bağlanır.
/// </summary>
public partial class MainWindow : Window
{
    private const string PanelBranches = "branches";
    private const string PanelCommits = "commits";
    private const string PanelDetails = "details";
    private const string PanelDiff = "diff";

    private ICommandRegistry? _registry;
    private GlobalShortcuts? _shortcuts;

    /// <summary>
    /// Panel gezinme sırası (P08-T05).
    /// </summary>
    /// <remarks>
    /// Sıra ekrandaki yerleşimi izliyor: soldan sağa, üstten aşağı. Klavye gezinmesinin
    /// göz gezinmesiyle aynı yolu takip etmesi, sırayı ezberlemeyi gereksiz kılıyor.
    /// </remarks>
    private readonly PanelNavigator _panels = new();

    private IAppearanceService? _appearance;

    private IGitConfigWriter? _configWriter;

    /// <summary>
    /// Ayarlar ekranının bağımlılıklarını verir (P08-T15).
    /// </summary>
    /// <remarks>
    /// Yapıcıya geçirilemiyor: <see cref="MainWindow"/>'un parametresiz yapıcısı XAML
    /// tasarımcısının şartı. Bağlantı yine composition root'ta kuruluyor (ADR-0004).
    /// </remarks>
    public void AttachSettings(IAppearanceService appearance, IGitConfigWriter configWriter)
    {
        _appearance = appearance;
        _configWriter = configWriter;
    }

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // 🔴 Görünmeyen panel atlanıyor: gizli bir panele odak vermek, odağın kaybolması
        // demektir — tuşlar hiçbir yere gitmez ve ekranda hiçbir şey değişmez.
        _panels
            .Add(PanelBranches, () => BranchPanel.IsEffectivelyVisible, () => BranchPanel.FocusPanel())
            .Add(PanelCommits, () => CommitList.IsEffectivelyVisible, () => CommitList.FocusPanel())
            .Add(PanelDetails, () => Details.IsEffectivelyVisible, () => Details.FocusPanel())
            .Add(PanelDiff, () => ChangesDiff.IsEffectivelyVisible, () => ChangesDiff.FocusPanel());

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
                model.PushPrompt = new DialogPushPrompt(this, model);
                model.AuthenticationPrompt = new DialogAuthenticationPrompt(this);
                model.MergePrompt = new DialogMergePrompt(this);

                // Panelden çift tıklama, menüdeki checkout ile AYNI akışı çağırıyor
                // (P06-T13): ikinci bir geçiş yolu, birinin korumasız kalması demekti.
                BranchPanel.Checkout = model.CheckoutRefAsync;
                BranchPanel.Commands = model;
                BranchPanel.MergeDropped = model.MergeDroppedAsync;
                model.MergeDropConfirmer = new DialogMergeDropConfirmer(this);
                model.CommandLogPrompt = new DialogCommandLogPrompt(this);
                model.DiagnosticsPrompt = new DialogDiagnosticsPrompt(this);
                model.MergeAbortConfirmer = new DialogMergeAbortConfirmer(this);

                // Faz 07 ekranları.
                model.ConflictPrompt = new DialogConflictPrompt(this);
                model.StashPrompt = new DialogStashPrompt(this);
                model.ReflogPrompt = new DialogReflogPrompt(this);
                model.ResetPrompt = new DialogResetPrompt(this);
                model.SequencerPrompt = new DialogSequencerPrompt(this);
                model.RebasePrompt = new DialogRebasePrompt(this);
            }

            BuildShortcuts();
        };
    }

    /// <summary>
    /// Küresel kısayolları kurar (P08-T01).
    /// </summary>
    /// <remarks>
    /// Kayıt yapıcıya değil buraya geliyor: <see cref="MainWindow"/>'un parametresiz yapıcısı
    /// XAML tasarımcısının şartı. DI bunu açıkça çağırıyor, dolayısıyla bağlantı hâlâ
    /// composition root'ta ve Service Locator kullanılmıyor (ADR-0004).
    /// </remarks>
    public void AttachShortcuts(ICommandRegistry registry)
    {
        _registry = registry;

        BuildShortcuts();
    }

    private void BuildShortcuts()
    {
        if (_registry is not { } registry || DataContext is not MainWindowViewModel model)
        {
            return;
        }

        _shortcuts?.Dispose();

        GlobalShortcuts shortcuts = new(this, registry);

        shortcuts
            .Bind(CommandIds.RepositoryOpen, new AsyncRelayCommand(OpenRepositoryAsync))
            .Bind(CommandIds.RepositoryClose, model.CloseRepositoryCommand)
            .Bind(CommandIds.RepositoryRefresh, model.RefreshCommand)
            .Bind(CommandIds.RepositoryRemotes, model.ManageRemotesCommand)
            .Bind(CommandIds.CommitShow, new AsyncRelayCommand(ShowCommitAsync))
            .Bind(CommandIds.RemotePull, model.PullCommand)
            .Bind(CommandIds.RemotePush, model.PushCommand)
            .Bind(CommandIds.BranchCreate, model.CreateBranchCommand)
            .Bind(CommandIds.BranchDelete, model.DeleteBranchCommand)
            .Bind(CommandIds.BranchCheckout, model.CheckoutCommand)
            .Bind(CommandIds.BranchMerge, model.MergeCommand)
            .Bind(CommandIds.HistoryRebase, model.RebaseCommand)
            .Bind(CommandIds.HistoryCherryPick, model.CherryPickCommand)
            .Bind(CommandIds.HistoryRevert, model.RevertCommand)
            .Bind(CommandIds.HistoryReset, model.ResetCommand)
            .Bind(CommandIds.HistoryReflog, model.ShowReflogCommand)
            .Bind(CommandIds.StashManage, model.ShowStashCommand)
            .Bind(CommandIds.ToolsCommandLog, model.ShowCommandLogCommand)
            .Bind(CommandIds.ToolsDiagnostics, model.ShowDiagnosticsCommand)
            .Bind(CommandIds.ViewToggleLeftPanel, new RelayCommand(ToggleBranchPanel))
            .Bind(CommandIds.ViewToggleBottomPanel, new RelayCommand(ToggleBottomPanel))
            .Bind(CommandIds.ViewFocusLeftPanel, new RelayCommand(() => _panels.FocusPanel(PanelBranches)))
            .Bind(CommandIds.ViewFocusCommitList, new RelayCommand(() => _panels.FocusPanel(PanelCommits)))
            .Bind(CommandIds.ViewFocusCommitDetails, new RelayCommand(() => _panels.FocusPanel(PanelDetails)))
            .Bind(CommandIds.ViewFocusDiff, new RelayCommand(() => _panels.FocusPanel(PanelDiff)))
            .Bind(CommandIds.ViewNextPanel, new RelayCommand(() => _panels.Move(HasPanelFocus, 1)))
            .Bind(CommandIds.ViewPreviousPanel, new RelayCommand(() => _panels.Move(HasPanelFocus, -1)))
            .Bind(CommandIds.ToolsSettings, new AsyncRelayCommand(ShowSettingsAsync))
            .Bind(CommandIds.ToolsCommandPalette, new AsyncRelayCommand(ShowCommandPaletteAsync))
            .Bind(CommandIds.HelpShortcuts, new AsyncRelayCommand(ShowShortcutReferenceAsync))
            .Bind(CommandIds.HelpAbout, new RelayCommand(ShowAbout))
            .Bind(CommandIds.AppExit, new RelayCommand(Close));

        // Menü etiketleri de kayıttan besleniyor: P08-T00/M03'te ölçüldü, `InputGesture`
        // komutu ÇALIŞTIRMIYOR — yalnızca yazı. Aynı kaynaktan gelmezlerse menüde yazan
        // kısayol ile çalışan kısayol sessizce ayrışır.
        shortcuts
            .BindMenu(CommandIds.RepositoryOpen, MenuOpen)
            .BindMenu(CommandIds.RepositoryClose, MenuCloseRepository)
            .BindMenu(CommandIds.RepositoryRefresh, MenuRepositoryRefresh)
            .BindMenu(CommandIds.RepositoryRemotes, MenuRemotes)
            .BindMenu(CommandIds.CommitShow, MenuCommit)
            .BindMenu(CommandIds.RemotePull, MenuPull)
            .BindMenu(CommandIds.RemotePush, MenuPush)
            .BindMenu(CommandIds.BranchCreate, MenuCreateBranch)
            .BindMenu(CommandIds.BranchDelete, MenuDeleteBranch)
            .BindMenu(CommandIds.BranchCheckout, MenuCheckoutBranch)
            .BindMenu(CommandIds.BranchMerge, MenuMerge)
            .BindMenu(CommandIds.HistoryRebase, MenuRebase)
            .BindMenu(CommandIds.HistoryCherryPick, MenuCherryPick)
            .BindMenu(CommandIds.HistoryRevert, MenuRevert)
            .BindMenu(CommandIds.HistoryReset, MenuResetToCommit)
            .BindMenu(CommandIds.HistoryReflog, MenuReflog)
            .BindMenu(CommandIds.StashManage, MenuStashes)
            .BindMenu(CommandIds.TagCreate, MenuCreateTag)
            .BindMenu(CommandIds.ToolsCommandLog, MenuCommandLog)
            .BindMenu(CommandIds.ToolsSettings, MenuSettings)
            .BindMenu(CommandIds.HelpAbout, MenuAbout)
            .BindMenu(CommandIds.AppExit, MenuExit);

        shortcuts.Apply();

        _shortcuts = shortcuts;

        // Panel kısayolları pencereye DEĞİL, panelin kendisine bağlanıyor (P08-T00/M11:
        // pencere bağlaması tuşu odaklı kontrolden koşulsuz çalar). Panellerin dağıtıcıları
        // yönlendiriciye de kaydediliyor: komut paleti onları da çalıştırabilmeli.
        shortcuts.Router.Register(CommitList.AttachShortcuts(registry));
        shortcuts.Router.Register(ChangesDiff.AttachShortcuts(registry));
        shortcuts.Router.Register(BranchPanel.AttachShortcuts(registry));
    }

    /// <summary>Panelin şu an odağı taşıyıp taşımadığı.</summary>
    private bool HasPanelFocus(string id) => id switch
    {
        PanelBranches => PanelNavigator.ContainsFocus(BranchPanel),
        PanelCommits => PanelNavigator.ContainsFocus(CommitList),
        PanelDetails => PanelNavigator.ContainsFocus(Details),
        PanelDiff => PanelNavigator.ContainsFocus(ChangesDiff),
        _ => false,
    };

    private Task ShowCommandPaletteAsync()
    {
        if (_registry is not { } registry || _shortcuts is not { } shortcuts)
        {
            return Task.CompletedTask;
        }

        return CommandPaletteWindow.ShowAsync(
            new CommandPaletteViewModel(registry, shortcuts.Router),
            this);
    }

    /// <summary>
    /// Ayarlar ekranını açar (P08-T15).
    /// </summary>
    /// <remarks>
    /// Açık deponun yolu geçiriliyor: yerel git ayarları ancak bir depo içinde yazılabiliyor
    /// (ölçüldü — `--local` depo dışında `fatal` veriyor).
    /// </remarks>
    private Task ShowSettingsAsync()
    {
        if (_registry is not { } registry
            || _settings is not { } settings
            || _appearance is not { } appearance)
        {
            return Task.CompletedTask;
        }

        string? workingDirectory = (DataContext as MainWindowViewModel)?.Commits.Repository?.WorkingDirectory;

        return SettingsWindow.ShowAsync(
            new SettingsViewModel(appearance, settings, registry, _configWriter, workingDirectory),
            this);
    }

    private Task ShowShortcutReferenceAsync() =>
        _registry is { } registry
            ? ShortcutReferenceWindow.ShowAsync(new ShortcutReferenceViewModel(registry), this)
            : Task.CompletedTask;

    /// <summary>Çakışma çözüm ekranını gerçek bir pencereyle gösterir (P07-T03).</summary>
    private sealed class DialogConflictPrompt : IConflictPrompt
    {
        private readonly Window _owner;

        public DialogConflictPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(ConflictViewModel model) => ConflictWindow.ShowAsync(model, _owner);
    }

    /// <summary>Stash ekranını gerçek bir pencereyle gösterir (P07-T13).</summary>
    private sealed class DialogStashPrompt : IStashPrompt
    {
        private readonly Window _owner;

        public DialogStashPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(StashViewModel model) => StashWindow.ShowAsync(model, _owner);
    }

    /// <summary>Reflog tarayıcısını gerçek bir pencereyle gösterir (P07-T14).</summary>
    private sealed class DialogReflogPrompt : IReflogPrompt
    {
        private readonly Window _owner;

        public DialogReflogPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(ReflogViewModel model) => ReflogWindow.ShowAsync(model, _owner);
    }

    /// <summary>Reset diyaloğunu gerçek bir pencereyle gösterir (P07-T06).</summary>
    private sealed class DialogResetPrompt : IResetPrompt
    {
        private readonly Window _owner;

        public DialogResetPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(ResetViewModel model) => ResetDialog.ShowAsync(model, _owner);
    }

    /// <summary>Cherry-pick / revert diyaloğunu gösterir (P07-T07, P07-T08).</summary>
    private sealed class DialogSequencerPrompt : ISequencerPrompt
    {
        private readonly Window _owner;

        public DialogSequencerPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(SequencerViewModel model) => SequencerDialog.ShowAsync(model, _owner);
    }

    /// <summary>Rebase ekranını gerçek bir pencereyle gösterir (P07-T09, P07-T10).</summary>
    private sealed class DialogRebasePrompt : IRebasePrompt
    {
        private readonly Window _owner;

        public DialogRebasePrompt(Window owner) => _owner = owner;

        public Task ShowAsync(RebaseViewModel model) => RebaseWindow.ShowAsync(model, _owner);
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

    /// <summary>Merge ekranını gerçek bir pencereyle gösterir (P06-T11).</summary>
    private sealed class DialogMergePrompt : IMergePrompt
    {
        private readonly Window _owner;

        public DialogMergePrompt(Window owner) => _owner = owner;

        public Task ShowAsync(MergeViewModel model) => MergeWindow.ShowAsync(model, _owner);
    }

    /// <summary>Komut günlüğünü gerçek bir pencereyle gösterir (P06-T16).</summary>
    private sealed class DialogCommandLogPrompt : ICommandLogPrompt
    {
        private readonly Window _owner;

        public DialogCommandLogPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(CommandLogViewModel model) => CommandLogWindow.ShowAsync(model, _owner);
    }

    /// <summary>Teşhis panelini gerçek bir pencereyle gösterir (P09-T03).</summary>
    /// <remarks>
    /// Sahip olarak ana pencere veriliyor; kare ölçümü ona bağlanıyor — ölçülmek istenen
    /// şey grafiğin kaydırılması, teşhis panelinin kendi çizimi değil.
    /// </remarks>
    private sealed class DialogDiagnosticsPrompt : IDiagnosticsPrompt
    {
        private readonly Window _owner;

        public DialogDiagnosticsPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(IPerformanceDiagnostics diagnostics) =>
            DiagnosticsWindow.ShowAsync(diagnostics, _owner);
    }

    /// <summary>Sürükle-bırak birleştirme onayını sorar (P06-T15).</summary>
    private sealed class DialogMergeDropConfirmer : IMergeDropConfirmer
    {
        private readonly Window _owner;

        public DialogMergeDropConfirmer(Window owner) => _owner = owner;

        public Task<bool> ConfirmAsync(MergeDropRequest request) =>
            MergeDropDialog.ShowAsync(request, _owner);
    }

    /// <summary>Merge iptal onayını gerçek bir pencereyle sorar (P06-T12).</summary>
    private sealed class DialogMergeAbortConfirmer : IMergeAbortConfirmer
    {
        private readonly Window _owner;

        public DialogMergeAbortConfirmer(Window owner) => _owner = owner;

        public Task<bool> ConfirmAsync(IReadOnlyList<string> conflicted) =>
            AbortMergeDialog.ShowAsync(conflicted, _owner);
    }

    /// <summary>Kimlik doğrulama ekranını gerçek bir pencereyle gösterir (P06-T09).</summary>
    private sealed class DialogAuthenticationPrompt : IAuthenticationPrompt
    {
        private readonly Window _owner;

        public DialogAuthenticationPrompt(Window owner) => _owner = owner;

        public Task<GitCredentials?> ShowAsync(AuthenticationViewModel model) =>
            AuthenticationWindow.ShowAsync(model, _owner);
    }

    /// <summary>Push ekranını gerçek bir pencereyle gösterir (P06-T08).</summary>
    private sealed class DialogPushPrompt : IPushPrompt
    {
        private readonly Window _owner;
        private readonly MainWindowViewModel _model;

        public DialogPushPrompt(Window owner, MainWindowViewModel model)
        {
            _owner = owner;
            _model = model;
        }

        // Alt sıradaki "Pull…" düğmesi GitExtensions `FormPush`'tan (§ 9): reddedilen bir
        // gönderimden sonra kullanıcının gideceği yer zaten orası.
        public Task ShowAsync(PushViewModel model) =>
            PushWindow.ShowAsync(
                model,
                _owner,
                () => _model.ManageRemotesCommand.ExecuteAsync(null),
                () => _model.PullCommand.ExecuteAsync(null));
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
    private async Task OpenRepositoryAsync()
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

    private void ShowAbout()
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
    private async Task ShowCommitAsync()
    {
        if (DataContext is not MainWindowViewModel model
            || model.CreateWorkingTree() is not { } workingTree)
        {
            return;
        }

        await workingTree.OpenAsync(model.Commits.Repository?.WorkingDirectory);
        await WorkingTreeWindow.Open(workingTree, this, _registry);
        await model.RefreshAsync();
    }
}
