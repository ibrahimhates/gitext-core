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
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// The main window. Opening a repository by drag and drop (P03-T16) and the main menu
/// (P08-T26) are wired up here.
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
    /// Panel navigation order (P08-T05).
    /// </summary>
    /// <remarks>
    /// The order follows the layout on screen: left to right, top to bottom. Keyboard navigation
    /// taking the same path as the eye makes memorising the order unnecessary.
    /// </remarks>
    private readonly PanelNavigator _panels = new();

    private IAppearanceService? _appearance;

    private IGitConfigWriter? _configWriter;

    /// <summary>
    /// Supplies the settings screen's dependencies (P08-T15).
    /// </summary>
    /// <remarks>
    /// They cannot be passed to the constructor: <see cref="MainWindow"/>'s parameterless
    /// constructor is a requirement of the XAML designer. The wiring is still set up in the
    /// composition root (ADR-0004).
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

        // 🔴 An invisible panel is skipped: giving focus to a hidden panel means losing focus —
        // the keys go nowhere and nothing on screen changes.
        _panels
            .Add(PanelBranches, () => BranchPanel.IsEffectivelyVisible, () => BranchPanel.FocusPanel())
            .Add(PanelCommits, () => CommitList.IsEffectivelyVisible, () => CommitList.FocusPanel())
            .Add(PanelDetails, () => Details.IsEffectivelyVisible, () => Details.FocusPanel())
            .Add(PanelDiff, () => ChangesDiff.IsEffectivelyVisible, () => ChangesDiff.FocusPanel());

        // The dialog needs an owner window; the ViewModel knowing about `Window` would break the
        // layering rule (the same pattern as the confirmation dialog in P05-T15).
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

                // A double-click in the panel calls the SAME flow as checkout in the menu
                // (P06-T13): a second path meant one of them ending up unguarded.
                BranchPanel.Checkout = model.CheckoutRefAsync;
                BranchPanel.Commands = model;
                BranchPanel.MergeDropped = model.MergeDroppedAsync;
                model.MergeDropConfirmer = new DialogMergeDropConfirmer(this);
                model.CommandLogPrompt = new DialogCommandLogPrompt(this);
                model.DiagnosticsPrompt = new DialogDiagnosticsPrompt(this);
                model.MergeAbortConfirmer = new DialogMergeAbortConfirmer(this);

                // Phase 07 screens.
                model.ConflictPrompt = new DialogConflictPrompt(this);
                model.StashPrompt = new DialogStashPrompt(this);
                model.ReflogPrompt = new DialogReflogPrompt(this);
                model.ResetPrompt = new DialogResetPrompt(this);
                model.SequencerPrompt = new DialogSequencerPrompt(this);
                model.RebasePrompt = new DialogRebasePrompt(this);

                // The dashboard (P12-T03): its dialogs and "Show in folder" also need the window.
                model.Dashboard.Prompt = new DialogDashboardPrompt(this);
                model.Dashboard.OpenFolderInShell = OpenFolderInShell;
            }

            BuildShortcuts();
        };
    }

    /// <summary>
    /// Sets up the global shortcuts (P08-T01).
    /// </summary>
    /// <remarks>
    /// Registration comes here rather than to the constructor: <see cref="MainWindow"/>'s
    /// parameterless constructor is a requirement of the XAML designer. DI calls this explicitly,
    /// so the wiring is still in the composition root and no Service Locator is used (ADR-0004).
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

        // The menu labels are fed from the registry too: measured in P08-T00/M03, `InputGesture`
        // DOES NOT RUN the command — it is only text. Unless they come from the same source, the
        // shortcut written in the menu and the shortcut that works silently diverge.
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

        // The toolbar buttons come from the same registry: the tooltip's shortcut and the key
        // that actually works cannot drift apart (P12-T06).
        shortcuts
            .BindButton(CommandIds.RepositoryRefresh, ToolRefresh)
            .BindButton(CommandIds.ViewToggleLeftPanel, ToolToggleLeftPanel)
            .BindButton(CommandIds.ViewToggleBottomPanel, ToolToggleBottomPanel)
            .BindButton(CommandIds.RemotePull, ToolPull)
            .BindButton(CommandIds.RemotePush, ToolPush)
            .BindButton(CommandIds.CommitShow, ToolCommit)
            .BindButton(CommandIds.StashManage, ToolStash)
            .BindButton(CommandIds.ToolsSettings, ToolSettings);

        shortcuts.Apply();

        _shortcuts = shortcuts;

        // Panel shortcuts are bound to the panel itself, NOT to the window (P08-T00/M11: a window
        // binding steals the key from the focused control unconditionally). The panels' dispatchers
        // are registered with the router as well: the command palette must be able to run them too.
        shortcuts.Router.Register(CommitList.AttachShortcuts(registry));
        shortcuts.Router.Register(ChangesDiff.AttachShortcuts(registry));
        shortcuts.Router.Register(BranchPanel.AttachShortcuts(registry));
    }

    /// <summary>Whether the panel currently holds focus.</summary>
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
    /// Opens the settings screen (P08-T15).
    /// </summary>
    /// <remarks>
    /// The open repository's path is passed in: local git settings can only be written inside a
    /// repository (measured — `--local` is `fatal` outside one).
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

    /// <summary>
    /// Opens a folder in the system's file manager (P12-T03, "Show in folder").
    /// </summary>
    /// <remarks>
    /// <c>UseShellExecute</c> is essential: without it .NET takes the path for an executable and
    /// errors out. A failure is swallowed — a message box because a file manager could not be
    /// started would be a bigger nuisance than the action is worth.
    /// </remarks>
    private static void OpenFolderInShell(string path)
    {
        try
        {
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
                or System.PlatformNotSupportedException
                or InvalidOperationException)
        {
        }
    }

    /// <summary>Shows the dashboard's small dialogs in real windows (P12-T03).</summary>
    private sealed class DialogDashboardPrompt : IDashboardPrompt
    {
        private readonly Window _owner;

        public DialogDashboardPrompt(Window owner) => _owner = owner;

        public Task<string?> AskCategoryNameAsync(IReadOnlyList<string> existingCategories, string? currentName) =>
            DashboardCategoryDialog.ShowAsync(existingCategories, currentName, _owner);

        public Task<bool> ConfirmAsync(string caption, string question) =>
            ConfirmDialog.ShowAsync(caption, question, _owner);
    }

    /// <summary>Shows the conflict resolution screen in a real window (P07-T03).</summary>
    private sealed class DialogConflictPrompt : IConflictPrompt
    {
        private readonly Window _owner;

        public DialogConflictPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(ConflictViewModel model) => ConflictWindow.ShowAsync(model, _owner);
    }

    /// <summary>Shows the stash screen in a real window (P07-T13).</summary>
    private sealed class DialogStashPrompt : IStashPrompt
    {
        private readonly Window _owner;

        public DialogStashPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(StashViewModel model) => StashWindow.ShowAsync(model, _owner);
    }

    /// <summary>Shows the reflog browser in a real window (P07-T14).</summary>
    private sealed class DialogReflogPrompt : IReflogPrompt
    {
        private readonly Window _owner;

        public DialogReflogPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(ReflogViewModel model) => ReflogWindow.ShowAsync(model, _owner);
    }

    /// <summary>Shows the reset dialog in a real window (P07-T06).</summary>
    private sealed class DialogResetPrompt : IResetPrompt
    {
        private readonly Window _owner;

        public DialogResetPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(ResetViewModel model) => ResetDialog.ShowAsync(model, _owner);
    }

    /// <summary>Shows the cherry-pick / revert dialog (P07-T07, P07-T08).</summary>
    private sealed class DialogSequencerPrompt : ISequencerPrompt
    {
        private readonly Window _owner;

        public DialogSequencerPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(SequencerViewModel model) => SequencerDialog.ShowAsync(model, _owner);
    }

    /// <summary>Shows the rebase screen in a real window (P07-T09, P07-T10).</summary>
    private sealed class DialogRebasePrompt : IRebasePrompt
    {
        private readonly Window _owner;

        public DialogRebasePrompt(Window owner) => _owner = owner;

        public Task ShowAsync(RebaseViewModel model) => RebaseWindow.ShowAsync(model, _owner);
    }

    /// <summary>Shows the pull/fetch screen in a real window (P06-T06, T07).</summary>
    private sealed class DialogPullPrompt : IPullPrompt
    {
        private readonly Window _owner;
        private readonly MainWindowViewModel _model;

        public DialogPullPrompt(Window owner, MainWindowViewModel model)
        {
            _owner = owner;
            _model = model;
        }

        // The "Manage…" button opens the remotes screen — GitExtensions has an `AddRemote` button
        // in `FormPull` as well (§ 9). Not a second path, a shortcut to the same command.
        public Task ShowAsync(PullViewModel model) =>
            PullWindow.ShowAsync(
                model,
                _owner,
                () => _model.ManageRemotesCommand.ExecuteAsync(null));
    }

    /// <summary>Shows the merge screen in a real window (P06-T11).</summary>
    private sealed class DialogMergePrompt : IMergePrompt
    {
        private readonly Window _owner;

        public DialogMergePrompt(Window owner) => _owner = owner;

        public Task ShowAsync(MergeViewModel model) => MergeWindow.ShowAsync(model, _owner);
    }

    /// <summary>Shows the command log in a real window (P06-T16).</summary>
    private sealed class DialogCommandLogPrompt : ICommandLogPrompt
    {
        private readonly Window _owner;

        public DialogCommandLogPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(CommandLogViewModel model) => CommandLogWindow.ShowAsync(model, _owner);
    }

    /// <summary>Shows the diagnostics panel in a real window (P09-T03).</summary>
    /// <remarks>
    /// The main window is given as the owner; the frame measurement attaches to it — what is meant
    /// to be measured is scrolling the graph, not the diagnostics panel drawing itself.
    /// </remarks>
    private sealed class DialogDiagnosticsPrompt : IDiagnosticsPrompt
    {
        private readonly Window _owner;

        public DialogDiagnosticsPrompt(Window owner) => _owner = owner;

        public Task ShowAsync(IPerformanceDiagnostics diagnostics) =>
            DiagnosticsWindow.ShowAsync(diagnostics, _owner);
    }

    /// <summary>Asks for confirmation of a drag-and-drop merge (P06-T15).</summary>
    private sealed class DialogMergeDropConfirmer : IMergeDropConfirmer
    {
        private readonly Window _owner;

        public DialogMergeDropConfirmer(Window owner) => _owner = owner;

        public Task<bool> ConfirmAsync(MergeDropRequest request) =>
            MergeDropDialog.ShowAsync(request, _owner);
    }

    /// <summary>Asks for merge abort confirmation in a real window (P06-T12).</summary>
    private sealed class DialogMergeAbortConfirmer : IMergeAbortConfirmer
    {
        private readonly Window _owner;

        public DialogMergeAbortConfirmer(Window owner) => _owner = owner;

        public Task<bool> ConfirmAsync(IReadOnlyList<string> conflicted) =>
            AbortMergeDialog.ShowAsync(conflicted, _owner);
    }

    /// <summary>Shows the authentication screen in a real window (P06-T09).</summary>
    private sealed class DialogAuthenticationPrompt : IAuthenticationPrompt
    {
        private readonly Window _owner;

        public DialogAuthenticationPrompt(Window owner) => _owner = owner;

        public Task<GitCredentials?> ShowAsync(AuthenticationViewModel model) =>
            AuthenticationWindow.ShowAsync(model, _owner);
    }

    /// <summary>Shows the push screen in a real window (P06-T08).</summary>
    private sealed class DialogPushPrompt : IPushPrompt
    {
        private readonly Window _owner;
        private readonly MainWindowViewModel _model;

        public DialogPushPrompt(Window owner, MainWindowViewModel model)
        {
            _owner = owner;
            _model = model;
        }

        // The "Pull…" button on the bottom row comes from GitExtensions' `FormPush` (§ 9): after a
        // rejected push, that is where the user is going anyway.
        public Task ShowAsync(PushViewModel model) =>
            PushWindow.ShowAsync(
                model,
                _owner,
                () => _model.ManageRemotesCommand.ExecuteAsync(null),
                () => _model.PullCommand.ExecuteAsync(null));
    }

    /// <summary>Shows the remote management screen in a real window (P06-T05).</summary>
    private sealed class DialogRemotesPrompt : IRemotesPrompt, IRemoteRemovalConfirmer
    {
        private readonly Window _owner;

        public DialogRemotesPrompt(Window owner) => _owner = owner;

        public IRemoteRemovalConfirmer RemovalConfirmer => this;

        public Task ShowAsync(RemotesViewModel model) => RemotesWindow.ShowAsync(model, _owner);

        public Task<bool> ConfirmAsync(RemoteRemovalRequest request) =>
            RemoveRemoteDialog.ShowAsync(request, _owner);
    }

    /// <summary>Shows the branch editing dialogs in a real window (P06-T03).</summary>
    private sealed class DialogBranchEditPrompt : IBranchEditPrompt
    {
        private readonly Window _owner;

        public DialogBranchEditPrompt(Window owner) => _owner = owner;

        public Task<RenameBranchDecision> RequestRenameAsync(RenameBranchRequest request) =>
            RenameBranchDialog.ShowAsync(request, _owner);

        public Task<DeleteBranchDecision> RequestDeleteAsync(DeleteBranchRequest request) =>
            DeleteBranchDialog.ShowAsync(request, _owner);
    }

    /// <summary>Shows the checkout branch dialog in a real window (P06-T02).</summary>
    private sealed class DialogCheckoutPrompt : ICheckoutPrompt
    {
        private readonly Window _owner;

        public DialogCheckoutPrompt(Window owner) => _owner = owner;

        public Task<CheckoutDecision> RequestAsync(CheckoutRequest request) =>
            CheckoutBranchDialog.ShowAsync(request, _owner);
    }

    /// <summary>Shows the create branch dialog in a real window (P06-T01).</summary>
    private sealed class DialogBranchPrompt : ICreateBranchPrompt
    {
        private readonly Window _owner;

        public DialogBranchPrompt(Window owner) => _owner = owner;

        public Task<CreateBranchDecision> RequestAsync(CreateBranchRequest request) =>
            CreateBranchDialog.ShowAsync(request, _owner);
    }

    /// <summary>
    /// Opening a repository from the menu. The same flow as the one on the welcome screen.
    /// </summary>
    /// <remarks>
    /// The folder picker lives in the code-behind: <c>IStorageProvider</c> is bound to the window,
    /// and the ViewModel knowing about the window would break the layering rule.
    /// </remarks>
    private async Task OpenRepositoryAsync()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = Loc.T("main_window.axaml.open_a_git_repository"), AllowMultiple = false });

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
        // A full "About" window is Phase 08's job (P08-T21); for now the version is in the title.
        Title = $"gitext-core — {typeof(MainWindow).Assembly.GetName().Version}";
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // Only files/folders are accepted; when text is dragged the cursor should show "forbidden".
        // NOTE: the API changed in Avalonia 12 — `e.DataTransfer`/`DataFormat.File` instead of
        // `e.Data`/`DataFormats.Files` (measured).
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

        // Remote/virtual locations have no local path; git wants a local path, so they are filtered out.
        List<string> paths = [.. items
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)];

        if (paths.Count > 0)
        {
            await viewModel.TryOpenDroppedAsync(paths);
        }
    }

    // ----------------------------------------------------------- P12-T06: the toolbar

    // 🔴 The panel toggles have NO Click handler of their own. They had one at first, on top of
    // the command that `BindButton` attaches from the registry — so a single click ran the toggle
    // TWICE and the panel appeared not to move at all. The command is the only path: it is the
    // same one the shortcut and the menu use, so the layout is saved the same way from all three.

    /// <summary>Opens the repository in the system's file manager (`toolStripFileExplorer`).</summary>
    private void OnFileExplorerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Commits.Repository.WorkingDirectory: { Length: > 0 } path })
        {
            OpenFolderInShell(path);
        }
    }

    /// <summary>
    /// Enter applies the filter in the toolbar's filter box.
    /// </summary>
    /// <remarks>
    /// On Enter, not while typing: every application starts a fresh <c>git log</c>, and the
    /// diff-content filter has to diff every commit to answer. Filtering per keystroke would
    /// start — and cancel — a process for every letter.
    /// </remarks>
    private void OnRevisionFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainWindowViewModel model)
        {
            return;
        }

        e.Handled = true;
        model.Commits.ApplyFilterCommand.Execute(null);
    }

    private void OnDismissBranchNoticeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel model)
        {
            model.BranchNotice = null;
        }
    }

    /// <summary>
    /// Opens the commit screen (P05-T09).
    /// </summary>
    /// <remarks>
    /// Its place in GitExtensions: <i>Commands → Commit</i>, the <b>first</b> item of the menu
    /// (<c>commandsToolStripMenuItem.DropDownItems</c>). It opens modally; on closing, the commit
    /// list is refreshed because a new commit may have been created on that screen.
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
