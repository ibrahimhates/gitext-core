using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Diagnostics;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Storage;
using GitExt.UI.Settings;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A recently opened repository entry on the welcome screen (P03-T16).
/// </summary>
public sealed class RecentRepositoryItem
{
    public RecentRepositoryItem(string path, ICommand openCommand)
    {
        Path = path;
        OpenCommand = openCommand;

        // The folder name makes the list easy to scan; the full path stays on the second line.
        Name = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(Name))
        {
            Name = path;
        }
    }

    public string Path { get; }

    public string Name { get; }

    public ICommand OpenCommand { get; }

    /// <summary>
    /// Does the folder still exist?
    /// </summary>
    /// <remarks>
    /// Lost entries are <b>not removed</b> from the list, they are shown dimmed: an unmounted
    /// disk or a temporarily unreachable network path is not reason enough to permanently
    /// prune the user's list.
    /// </remarks>
    public bool Exists => Directory.Exists(Path);

    public override string ToString() => Path;
}

/// <summary>
/// ViewModel of the main window.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IRecentRepositoryStore _recentStore;

    private readonly IStatusReader? _statusReader;
    private readonly IStagingWriter? _staging;
    private readonly ICommitWriter? _commitWriter;
    private readonly IDiffReader? _diffReader;
    private readonly ICommitMessageReader? _messageReader;
    private readonly ICommitMessageStore? _messageStore;
    private readonly IRepositoryWatcher? _watcher;
    private readonly IWorkingTreeWriter? _workingTreeWriter;
    private readonly IBranchWriter? _branchWriter;
    private readonly IInProgressOperationReader? _operations;

    /// <summary>Faz 07 servisleri; bkz. <see cref="AdvancedOperationServices"/>.</summary>
    private readonly AdvancedOperationServices _advanced;
    private readonly IRemoteReader? _remoteReader;
    private readonly IRemoteWriter? _remoteWriter;
    private readonly IFetchWriter? _fetchWriter;
    private readonly IPullWriter? _pullWriter;
    private readonly IPushWriter? _pushWriter;
    private readonly IAuthenticationDiagnostics? _authDiagnostics;
    private readonly IMergeWriter? _mergeWriter;
    private readonly IGitCommandLog? _commandLog;
    private readonly IGitConfigReader? _configReader;
    private readonly IPerformanceDiagnostics? _diagnostics;

    /// <summary>
    /// Creates a new ViewModel for the commit screen (P05-T09).
    /// </summary>
    /// <remarks>
    /// In GitExtensions this screen opens <b>modally</b> via <c>FormCommit</c> and
    /// <c>ShowDialog</c> (<c>GitUICommands.StartCommitDialog</c>); the way it opens is
    /// followed just like the layout is (CLAUDE.md § 9).
    /// <para>
    /// <b>Opening</b> the window is the view's job; this only sets up what will be shown
    /// (the same pattern as the comparison window in P04-T16).
    /// </para>
    /// </remarks>
    public WorkingTreeViewModel? CreateWorkingTree()
    {
        if (_statusReader is null || _staging is null || _commitWriter is null || _diffReader is null)
        {
            return null;
        }

        return new WorkingTreeViewModel(
            _statusReader,
            _staging,
            _commitWriter,
            new DiffViewModel(_diffReader),
            _messageReader,
            _messageStore,
            _watcher,
            _workingTreeWriter,
            _configReader);
    }

    public MainWindowViewModel(
        CommitListViewModel commits,
        IRecentRepositoryStore recentStore,
        IStatusReader? statusReader = null,
        IStagingWriter? staging = null,
        ICommitWriter? commitWriter = null,
        IDiffReader? diffReader = null,
        ICommitMessageReader? messageReader = null,
        ICommitMessageStore? messageStore = null,
        IRepositoryWatcher? watcher = null,
        IWorkingTreeWriter? workingTreeWriter = null,
        IBranchWriter? branchWriter = null,
        IInProgressOperationReader? operations = null,
        IRemoteReader? remoteReader = null,
        IRemoteWriter? remoteWriter = null,
        IFetchWriter? fetchWriter = null,
        IPullWriter? pullWriter = null,
        IPushWriter? pushWriter = null,
        IAuthenticationDiagnostics? authenticationDiagnostics = null,
        IMergeWriter? mergeWriter = null,
        IGitCommandLog? commandLog = null,
        IGitConfigReader? configReader = null,
        IPerformanceDiagnostics? diagnostics = null,
        AdvancedOperationServices? advanced = null)
    {
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(recentStore);

        _statusReader = statusReader;
        _staging = staging;
        _commitWriter = commitWriter;
        _diffReader = diffReader;
        _messageReader = messageReader;
        _messageStore = messageStore;
        _watcher = watcher;
        _workingTreeWriter = workingTreeWriter;
        _branchWriter = branchWriter;
        _operations = operations;
        _remoteReader = remoteReader;
        _remoteWriter = remoteWriter;
        _fetchWriter = fetchWriter;
        _pullWriter = pullWriter;
        _pushWriter = pushWriter;
        _authDiagnostics = authenticationDiagnostics;
        _mergeWriter = mergeWriter;
        _commandLog = commandLog;
        _configReader = configReader;
        _diagnostics = diagnostics;
        _advanced = advanced ?? new AdvancedOperationServices();

        if (_watcher is not null)
        {
            _watcher.Changed += OnRepositoryChanged;
        }

        Commits = commits;
        _recentStore = recentStore;

        OpenRecentCommand = new AsyncRelayCommand<string>(
            async path =>
            {
                if (!string.IsNullOrEmpty(path))
                {
                    await OpenRepositoryAsync(path).ConfigureAwait(true);
                }
            });

        CancelLoadingCommand = new AsyncRelayCommand(Commits.CancelLoadingAsync);

        // Menu commands (P08-T26). In GitExtensions "Refresh" exists in both the Dashboard and
        // the Repository menu; "Close (go to Dashboard)" is the last item of the Repository menu.
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CloseRepositoryCommand = new RelayCommand(CloseRepository);
        CreateBranchCommand = new AsyncRelayCommand(CreateBranchAsync, () => CanCreateBranch);
        CheckoutCommand = new AsyncRelayCommand(CheckoutAsync, () => CanCheckout);
        RenameBranchCommand = new AsyncRelayCommand(RenameBranchAsync, () => CanEditBranch);
        DeleteBranchCommand = new AsyncRelayCommand(DeleteBranchAsync, () => CanEditBranch);
        ManageRemotesCommand = new AsyncRelayCommand(ManageRemotesAsync, () => CanManageRemotes);
        PullCommand = new AsyncRelayCommand(PullAsync, () => CanPull);
        PushCommand = new AsyncRelayCommand(PushAsync, () => CanPush);
        MergeCommand = new AsyncRelayCommand(MergeAsync, () => CanMerge);
        ShowCommandLogCommand = new AsyncRelayCommand(ShowCommandLogAsync, () => CanShowCommandLog);
        ShowDiagnosticsCommand = new AsyncRelayCommand(ShowDiagnosticsAsync, () => CanShowDiagnostics);
        AbortMergeCommand = new AsyncRelayCommand(AbortMergeAsync, () => CanAbortMerge);

        // ---------------------------------------------------------- Faz 07
        AbortOperationCommand = new AsyncRelayCommand(AbortOperationAsync, () => CanAbortOperation);
        ResolveConflictsCommand = new AsyncRelayCommand(ResolveConflictsAsync, () => CanResolveConflicts);
        ShowStashCommand = new AsyncRelayCommand(ShowStashAsync, () => CanShowStash);
        ShowReflogCommand = new AsyncRelayCommand(ShowReflogAsync, () => CanShowReflog);
        ResetCommand = new AsyncRelayCommand(ResetAsync, () => CanReset);
        CherryPickCommand = new AsyncRelayCommand(CherryPickAsync, () => CanCherryPick);
        RevertCommand = new AsyncRelayCommand(RevertAsync, () => CanRevert);
        RebaseCommand = new AsyncRelayCommand(RebaseAsync, () => CanRebase);

        // The dashboard shares the store and the open command with the Start menu: two lists
        // reading the file separately would sooner or later show two different things.
        Dashboard = new DashboardViewModel(_recentStore, OpenRecentCommand);

        CheckoutBranchCommand = new AsyncRelayCommand<string>(
            async name =>
            {
                if (!string.IsNullOrEmpty(name))
                {
                    await CheckoutRefAsync(name).ConfigureAwait(true);
                }
            });

        RecentRepositories.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasRecentRepositories));

        // 🔴 EVERYTHING that depends on the repository is notified from here. A single place is
        // essential: the repository is opened and closed through four separate paths (explicit
        // open, drag and drop, silent attempt at startup, close) and putting the notification
        // into each path one by one would make forgetting one a silent bug — which is exactly
        // what happened: `HasRepository` was notified only on CLOSE, never on open. The
        // `_Repository` and `_Commands` menus stayed dimmed even with a repository open.
        Commits.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CommitListViewModel.IsLoading)
                or nameof(CommitListViewModel.Repository))
            {
                OnPropertyChanged(nameof(ShowWelcome));
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(RepositoryLabel));
                NotifyRepositoryDependents();
            }

            // Phase 07: reset/cherry-pick/revert operate on the SELECTED commit. Without a
            // notification when the selection changes the menu items would stay dimmed even
            // though they are actually available — the same bug lived through with
            // `HasRepository` in P06.
            if (e.PropertyName is nameof(CommitListViewModel.SelectedIndex)
                or nameof(CommitListViewModel.SelectedRow))
            {
                NotifySelectionDependents();
                RememberSelection();
            }
        };
    }

    /// <summary>
    /// Writes the selected commit into the session (P08-T16).
    /// </summary>
    /// <remarks>
    /// Called on every selection change, but not written to disk every time: the settings
    /// store saves with a delay. A user walking the list with the arrow keys changes the
    /// selection dozens of times a second and writing each one meant writing files nonstop.
    /// </remarks>
    private void RememberSelection()
    {
        if (Session is { } session
            && Commits.Repository is { } repository
            && Commits.SelectedRow is { } row)
        {
            session.RememberSelectedCommit(repository.WorkingDirectory, row.Commit.Id.Value);
        }
    }

    /// <summary>Commit history list.</summary>
    public CommitListViewModel Commits { get; }

    /// <summary>
    /// The dashboard shown when no repository is open (P12-T03).
    /// </summary>
    /// <remarks>
    /// Its counterpart in GitExtensions is the <c>Dashboard</c> control: it is not a placeholder
    /// screen but the way into the application — the repository is picked from there.
    /// </remarks>
    public DashboardViewModel Dashboard { get; }

    /// <summary>Recently opened repositories, newest first.</summary>
    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = [];

    /// <summary>
    /// Is there any recently opened repository to show?
    /// </summary>
    /// <remarks>
    /// A separate <see cref="bool"/> is required: binding <c>Count</c> directly to
    /// <c>IsVisible</c> <b>does not work</b> — Avalonia does not convert <c>int</c> to
    /// <c>bool</c> and the section silently never showed (caught while rendering).
    /// </remarks>
    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    public ICommand OpenRecentCommand { get; }

    public ICommand CancelLoadingCommand { get; }

    /// <summary>Re-reads the open repository (P08-T26).</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Closes the repository and returns to the welcome screen (P08-T26).</summary>
    public ICommand CloseRepositoryCommand { get; }

    /// <summary>Opens the create-branch dialog (P06-T01).</summary>
    public IAsyncRelayCommand CreateBranchCommand { get; }

    /// <summary>
    /// The party that shows the dialog. The view supplies it: the dialog wants an owner
    /// window and that is only known at open time (same rationale as
    /// <see cref="IDestructiveActionConfirmer"/> in P05-T15).
    /// </summary>
    public ICreateBranchPrompt? BranchPrompt { get; set; }

    /// <summary>Can a branch be created? (Open repository + writer + dialog required.)</summary>
    public bool CanCreateBranch =>
        _branchWriter is not null
        && BranchPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Checks out the selected commit / branch (P06-T02).</summary>
    public IAsyncRelayCommand CheckoutCommand { get; }

    /// <summary>The party that shows the checkout dialog (P06-T02).</summary>
    public ICheckoutPrompt? CheckoutPrompt { get; set; }

    /// <summary>Can a checkout be performed?</summary>
    public bool CanCheckout =>
        _branchWriter is not null
        && CheckoutPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Renames the selected branch (P06-T03).</summary>
    public IAsyncRelayCommand RenameBranchCommand { get; }

    /// <summary>Deletes the selected branch (P06-T03).</summary>
    public IAsyncRelayCommand DeleteBranchCommand { get; }

    /// <summary>The party that shows the branch editing dialogs (P06-T03).</summary>
    public IBranchEditPrompt? BranchEditPrompt { get; set; }

    /// <summary>Opens the remote management screen (P06-T05).</summary>
    public IAsyncRelayCommand ManageRemotesCommand { get; }

    /// <summary>The party that shows the remote management screen (P06-T05).</summary>
    public IRemotesPrompt? RemotesPrompt { get; set; }

    /// <summary>Opens the Pull/Fetch screen (P06-T06 + P06-T07).</summary>
    public IAsyncRelayCommand PullCommand { get; }

    /// <summary>The party that shows the Pull/Fetch screen.</summary>
    public IPullPrompt? PullPrompt { get; set; }

    /// <summary>Opens the Push screen (P06-T08).</summary>
    public IAsyncRelayCommand PushCommand { get; }

    /// <summary>The party that shows the Push screen.</summary>
    public IPushPrompt? PushPrompt { get; set; }

    /// <summary>The party that shows the authentication screen (P06-T09).</summary>
    public IAuthenticationPrompt? AuthenticationPrompt { get; set; }

    /// <summary>Dal paneli (P06-T13).</summary>
    public RefTreeViewModel RefTree { get; } = new();

    /// <summary>
    /// Checks out a branch on a double click in the panel (P06-T13).
    /// </summary>
    /// <remarks>
    /// The dialog flow is <b>the same</b> as <see cref="CheckoutCommand"/>: the dirty-tree
    /// warning and the options live in one place. Writing a second checkout path meant one
    /// of them silently ending up unguarded (P06-T02's rule: no path may lose changes).
    /// </remarks>
    public Task CheckoutRefAsync(string refName) => CheckoutCoreAsync(refName);

    // ------------------------------------------------------------- P12-T06: the main toolbar

    /// <summary>
    /// The window title: repository and branch, then the application name.
    /// </summary>
    /// <remarks>
    /// GitExtensions' own title carries the same three parts. It is the only place the repository
    /// is named while the window is not focused — in the task bar and in the window switcher —
    /// and it is why the separate subtitle strip could go: it was saying the same thing twice.
    /// </remarks>
    public string WindowTitle =>
        Commits.Repository is { } repository
            ? $"{RepositoryLabel} ({CurrentBranchLabel}) - {Loc.T("main.gitext_core")}"
            : Loc.T("main.gitext_core");

    /// <summary>The open repository's folder name; empty when none is open.</summary>
    public string RepositoryLabel =>
        Commits.Repository is { WorkingDirectory: { Length: > 0 } directory }
            ? System.IO.Path.GetFileName(directory.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar)) is { Length: > 0 } name
                ? name
                : directory
            : string.Empty;

    /// <summary>
    /// What the toolbar's branch button shows.
    /// </summary>
    /// <remarks>
    /// A detached HEAD is not left blank — an empty button would read as "still loading". The
    /// same wording as the dashboard's tiles, so the two never disagree.
    /// </remarks>
    public string CurrentBranchLabel =>
        Commits.Refs?.Head switch
        {
            { IsUnborn: true } => Loc.T("main.unborn_branch"),
            { IsDetached: true } => Loc.T("dashboard.detached_head"),
            { BranchName: { Length: > 0 } branch } => branch,
            _ => Loc.T("dashboard.detached_head"),
        };

    /// <summary>The local branches, for the toolbar's branch dropdown.</summary>
    /// <remarks>
    /// Local branches only — GitExtensions' branch button switches branches, and checking out a
    /// remote-tracking ref detaches HEAD instead. That is a different operation and belongs to a
    /// deliberate menu item, not to the button people use dozens of times a day.
    /// </remarks>
    public ObservableCollection<ToolbarBranchItem> Branches { get; } = [];

    /// <summary>Rebuilds the toolbar's branch list from the last ref read.</summary>
    private void UpdateBranches()
    {
        Branches.Clear();

        if (Commits.Refs is not { } refs)
        {
            return;
        }

        string? current = refs.Head.BranchName;

        foreach (BranchInfo branch in refs.LocalBranches.OrderBy(b => b.Name, StringComparer.CurrentCulture))
        {
            Branches.Add(new ToolbarBranchItem(
                branch.Name,
                string.Equals(branch.Name, current, StringComparison.Ordinal),
                CheckoutBranchCommand));
        }
    }

    /// <summary>Checks out the branch named by the parameter (the toolbar dropdown).</summary>
    public IAsyncRelayCommand<string> CheckoutBranchCommand { get; }

    /// <summary>
    /// The branch chosen in the filter toolbar's branch box (P12-T07).
    /// </summary>
    /// <remarks>
    /// Picking one switches the history to <see cref="BranchFilterMode.FilteredBranches"/> —
    /// otherwise the box would look like it did nothing, which is exactly how a control gets a
    /// reputation for being broken.
    /// </remarks>
    public ToolbarBranchItem? SelectedBranchFilter
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged();

            Commits.BranchFilter = value?.Name;

            if (value is not null)
            {
                Commits.BranchMode = BranchFilterMode.FilteredBranches;
                _ = Commits.ApplyFilterAsync();
            }
        }
    }

    /// <summary>Refreshes the enablement of the commands tied to the selected commit (P07-T06 … T08).</summary>
    private void NotifySelectionDependents()
    {
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(CanCherryPick));
        OnPropertyChanged(nameof(CanRevert));
        ResetCommand.NotifyCanExecuteChanged();
        CherryPickCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    // ===================================================== Phase 07 commands

    /// <summary>The party that shows the conflict resolution screen (P07-T03).</summary>
    public IConflictPrompt? ConflictPrompt { get; set; }

    /// <summary>The party that shows the stash screen (P07-T13).</summary>
    public IStashPrompt? StashPrompt { get; set; }

    /// <summary>The party that shows the reflog browser (P07-T14).</summary>
    public IReflogPrompt? ReflogPrompt { get; set; }

    /// <summary>The party that shows the reset dialog (P07-T06).</summary>
    public IResetPrompt? ResetPrompt { get; set; }

    /// <summary>The party that shows the cherry-pick / revert dialog (P07-T07, P07-T08).</summary>
    public ISequencerPrompt? SequencerPrompt { get; set; }

    /// <summary>The party that shows the rebase screen (P07-T09, P07-T10).</summary>
    public IRebasePrompt? RebasePrompt { get; set; }

    public IAsyncRelayCommand AbortOperationCommand { get; }

    public IAsyncRelayCommand ResolveConflictsCommand { get; }

    public IAsyncRelayCommand ShowStashCommand { get; }

    public IAsyncRelayCommand ShowReflogCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IAsyncRelayCommand CherryPickCommand { get; }

    public IAsyncRelayCommand RevertCommand { get; }

    public IAsyncRelayCommand RebaseCommand { get; }

    private string? RepositoryPathOrNull =>
        Commits.Repository?.WorkingDirectory is { Length: > 0 } path ? path : null;

    private string? SelectedCommitId => Commits.SelectedRow?.Commit.Id.ToString();

    /// <summary>
    /// Can <b>any</b> operation in progress be aborted? (P07-T11)
    /// </summary>
    /// <remarks>
    /// In P06-T12 this was for merge only; aborting rebase/cherry-pick/revert are different
    /// commands so they were deliberately left out. In phase 07
    /// <see cref="IConflictResolver"/> picks the right verb <b>from the state files</b>,
    /// so all of them can now be offered.
    /// </remarks>
    public bool CanAbortOperation =>
        _advanced.Resolver is not null
        && RepositoryPathOrNull is not null
        && CurrentOperation is not (InProgressOperation.None or InProgressOperation.Bisect);

    /// <summary>Can the conflict resolution screen be opened? (P07-T03)</summary>
    public bool CanResolveConflicts =>
        _advanced.Conflicts is not null
        && _advanced.Resolver is not null
        && ConflictPrompt is not null
        && RepositoryPathOrNull is not null;

    public bool CanShowStash =>
        _advanced.Stash is not null && StashPrompt is not null && RepositoryPathOrNull is not null;

    public bool CanShowReflog =>
        _advanced.Reflog is not null && ReflogPrompt is not null && RepositoryPathOrNull is not null;

    public bool CanReset =>
        _advanced.Reset is not null
        && ResetPrompt is not null
        && RepositoryPathOrNull is not null
        && SelectedCommitId is not null;

    public bool CanCherryPick =>
        _advanced.Sequencer is not null
        && SequencerPrompt is not null
        && RepositoryPathOrNull is not null
        && SelectedCommitId is not null;

    public bool CanRevert => CanCherryPick;

    public bool CanRebase =>
        _advanced.Rebase is not null && RebasePrompt is not null && RepositoryPathOrNull is not null;

    /// <summary>
    /// Aborts the operation in progress (P07-T11).
    /// </summary>
    /// <remarks>
    /// 🔑 Confirmation is essential: the abort returns the working tree to its state BEFORE
    /// the operation, so everything written while resolving conflicts is lost (measured in
    /// P06-T12). The confirmation screen lists the unresolved files.
    /// </remarks>
    private async Task AbortOperationAsync()
    {
        if (_advanced.Resolver is not { } resolver || RepositoryPathOrNull is not { } path)
        {
            return;
        }

        IReadOnlyList<string> conflicted = await ReadConflictedAsync(path).ConfigureAwait(true);

        if (MergeAbortConfirmer is { } confirmer
            && !await confirmer.ConfirmAsync(conflicted).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            using (_watcher?.Suspend())
            {
                await resolver.AbortAsync(path).ConfigureAwait(true);
            }

            BranchNotice = Loc.T("main.the_operation_was_cancelled_the_working_tree");
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
        }
        catch (InvalidOperationException error)
        {
            BranchNotice = error.Message;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the conflict resolution screen (P07-T03, P07-T05).</summary>
    private async Task ResolveConflictsAsync()
    {
        if (_advanced.Conflicts is not { } reader
            || _advanced.Resolver is not { } resolver
            || ConflictPrompt is not { } prompt
            || RepositoryPathOrNull is not { } path)
        {
            return;
        }

        ConflictViewModel model = new(path, reader, resolver, _advanced.MergeTools);
        await model.RefreshAsync().ConfigureAwait(true);

        using (_watcher?.Suspend())
        {
            await prompt.ShowAsync(model).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the stash screen (P07-T13).</summary>
    private async Task ShowStashAsync()
    {
        if (_advanced.Stash is not { } stash
            || StashPrompt is not { } prompt
            || RepositoryPathOrNull is not { } path)
        {
            return;
        }

        StashViewModel model = new(path, stash);
        await model.RefreshAsync().ConfigureAwait(true);

        using (_watcher?.Suspend())
        {
            await prompt.ShowAsync(model).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the reflog browser (P07-T14).</summary>
    private async Task ShowReflogAsync()
    {
        if (_advanced.Reflog is not { } reflog
            || ReflogPrompt is not { } prompt
            || RepositoryPathOrNull is not { } path)
        {
            return;
        }

        ReflogViewModel model = new(path, reflog, _advanced.Reset);
        await model.RefreshAsync().ConfigureAwait(true);

        using (_watcher?.Suspend())
        {
            await prompt.ShowAsync(model).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the reset dialog (P07-T06).</summary>
    private async Task ResetAsync()
    {
        if (_advanced.Reset is not { } reset
            || ResetPrompt is not { } prompt
            || RepositoryPathOrNull is not { } path
            || SelectedCommitId is not { } commit)
        {
            return;
        }

        ResetViewModel model = new(path, reset, commit);
        await model.LoadAsync().ConfigureAwait(true);

        using (_watcher?.Suspend())
        {
            await prompt.ShowAsync(model).ConfigureAwait(true);
        }

        if (model.Result is { Length: > 0 } notice)
        {
            BranchNotice = notice;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private Task CherryPickAsync() => RunSequencerAsync(SequencerOperation.CherryPick);

    private Task RevertAsync() => RunSequencerAsync(SequencerOperation.Revert);

    /// <summary>Opens the cherry-pick / revert dialog (P07-T07, P07-T08).</summary>
    private async Task RunSequencerAsync(SequencerOperation operation)
    {
        if (_advanced.Sequencer is not { } sequencer
            || SequencerPrompt is not { } prompt
            || RepositoryPathOrNull is not { } path
            || SelectedCommitId is not { } commit)
        {
            return;
        }

        SequencerViewModel model = new(path, sequencer, operation, [commit]);
        await model.LoadAsync().ConfigureAwait(true);

        using (_watcher?.Suspend())
        {
            await prompt.ShowAsync(model).ConfigureAwait(true);
        }

        if (model.Result is { Length: > 0 } notice)
        {
            BranchNotice = notice;
        }

        await RefreshAsync().ConfigureAwait(true);

        // If it stopped on a conflict we take the user to the resolution screen: forcing them
        // to find a half-finished operation and hunt for the way out breaks the phase's rule.
        if (model.HasConflicts)
        {
            await ResolveConflictsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Opens the rebase screen (P07-T09, P07-T10).</summary>
    private async Task RebaseAsync()
    {
        if (_advanced.Rebase is not { } rebase
            || RebasePrompt is not { } prompt
            || RepositoryPathOrNull is not { } path)
        {
            return;
        }

        // Default target: the selected commit if there is one, otherwise the current branch's upstream is empty.
        RebaseViewModel model = new(path, rebase, SelectedCommitId ?? string.Empty);
        await model.LoadAsync().ConfigureAwait(true);

        using (_watcher?.Suspend())
        {
            await prompt.ShowAsync(model).ConfigureAwait(true);
        }

        if (model.Result is { Length: > 0 } notice)
        {
            BranchNotice = notice;
        }

        await RefreshAsync().ConfigureAwait(true);

        if (model.HasConflicts)
        {
            await ResolveConflictsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Opens the git command log (P06-T16).</summary>
    public IAsyncRelayCommand ShowCommandLogCommand { get; }

    /// <summary>The party that shows the command log panel (P06-T16).</summary>
    public ICommandLogPrompt? CommandLogPrompt { get; set; }

    /// <summary>
    /// Can the log be opened?
    /// </summary>
    /// <remarks>
    /// NOT tied to the repository: the log also shows the commands that run without an open
    /// repository (repository discovery, version check) and the problem may be exactly there.
    /// </remarks>
    public bool CanShowCommandLog => _commandLog is not null && CommandLogPrompt is not null;

    /// <summary>Opens the performance diagnostics panel (P09-T03).</summary>
    public IAsyncRelayCommand ShowDiagnosticsCommand { get; }

    /// <summary>The party that shows the diagnostics panel (P09-T03).</summary>
    public IDiagnosticsPrompt? DiagnosticsPrompt { get; set; }

    /// <summary>
    /// Can diagnostics be opened?
    /// </summary>
    /// <remarks>
    /// Like the log this is NOT tied to the repository: if startup itself is slow, the timing
    /// of the commands before the repository opens is exactly the information wanted.
    /// </remarks>
    public bool CanShowDiagnostics => _diagnostics is not null && DiagnosticsPrompt is not null;

    /// <summary>Opens the merge screen (P06-T11).</summary>
    public IAsyncRelayCommand MergeCommand { get; }

    /// <summary>The party that shows the merge screen.</summary>
    public IMergePrompt? MergePrompt { get; set; }

    /// <summary>Aborts the merge in progress (P06-T12).</summary>
    public IAsyncRelayCommand AbortMergeCommand { get; }

    /// <summary>Merge iptalini onaylatan taraf (P06-T12).</summary>
    public IMergeAbortConfirmer? MergeAbortConfirmer { get; set; }

    /// <summary>The party that confirms a drag-and-drop merge (P06-T15).</summary>
    public IMergeDropConfirmer? MergeDropConfirmer { get; set; }

    /// <summary>
    /// Called when a branch is dropped onto another branch (P06-T15).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔑 <b>The target MUST be the CURRENT branch.</b> GitExtensions also allows dropping
    /// onto another branch, but to do that it first has to <b>check out</b> that branch —
    /// meaning a hidden second operation behind a single drag. This project has no hidden
    /// operations: if the target is not the current branch the merge is not performed and
    /// the reason is written out.
    /// </para>
    /// <para>
    /// Confirmation is <b>always</b> asked for (an item of the plan) and the command that
    /// will run is written out verbatim on the confirmation screen.
    /// </para>
    /// </remarks>
    public async Task MergeDroppedAsync(string source, string target)
    {
        if (_mergeWriter is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        string current = Commits.Refs?.CurrentBranch?.Name ?? string.Empty;

        if (!string.Equals(target, current, StringComparison.Ordinal))
        {
            BranchNotice = $"You can only merge into the branch you are on. "
                + $"Switch to branch \"{target}\" first.";
            return;
        }

        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            return;
        }

        MergeOptions options = new() { Source = source };

        if (MergeDropConfirmer is not { } confirmer
            || !await confirmer
                .ConfirmAsync(new MergeDropRequest(source, target, MergeWriter.Describe(options)))
                .ConfigureAwait(true))
        {
            return;
        }

        try
        {
            MergeResult result;

            using (_watcher?.Suspend())
            {
                result = await _mergeWriter.MergeAsync(path, options).ConfigureAwait(true);
            }

            BranchNotice = result.Outcome switch
            {
                MergeOutcome.AlreadyUpToDate => Loc.T("main.already_up_to_date"),
                MergeOutcome.FastForward => $"\"{source}\" was fast-forwarded.",
                MergeOutcome.MergeCommit => $"\"{source}\" was merged.",
                MergeOutcome.Staged => Loc.T("main.the_changes_were_staged_but_not_committed"),
                _ => $"The merge stopped with conflicts: {result.ConflictedPaths.Count} files "
                    + Loc.T("main.are_unresolved"),
            };
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Can a merge be performed?</summary>
    public bool CanMerge =>
        _mergeWriter is not null
        && MergePrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>
    /// Can a merge in progress be aborted?
    /// </summary>
    /// <remarks>
    /// For <b>merge</b> only: aborting rebase/cherry-pick/revert are other commands and the
    /// subject of phase 07. Offering the wrong command would wreck a half-finished job.
    /// </remarks>
    public bool CanAbortMerge =>
        _mergeWriter is not null
        && CurrentOperation == InProgressOperation.Merge
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Can a push be performed?</summary>
    public bool CanPush =>
        _pushWriter is not null
        && _remoteReader is not null
        && PushPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Can a pull/fetch be performed?</summary>
    public bool CanPull =>
        _fetchWriter is not null
        && _pullWriter is not null
        && _remoteReader is not null
        && PullPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Can remotes be managed?</summary>
    public bool CanManageRemotes =>
        _remoteReader is not null
        && _remoteWriter is not null
        && RemotesPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Can a branch be edited?</summary>
    public bool CanEditBranch =>
        _branchWriter is not null
        && BranchEditPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>
    /// Is HEAD detached? (P06-T04)
    /// </summary>
    [ObservableProperty]
    public partial bool IsDetachedHead { get; private set; }

    /// <summary>Multi-step operation in progress (P06-T04).</summary>
    [ObservableProperty]
    public partial InProgressOperation CurrentOperation { get; private set; }

    /// <summary>
    /// Should the detached HEAD banner be shown?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>NOT SHOWN while an operation is in progress.</b> MEASURED: during rebase and
    /// bisect HEAD really is detached; a plain warning would pop up there too and say
    /// <i>"create a branch from here"</i> — whereas the user is knowingly in the middle of an
    /// operation and creating a branch is not what they should do. In that case
    /// <see cref="OperationText"/> is shown instead.
    /// </remarks>
    public bool ShowDetachedBanner =>
        IsDetachedHead && CurrentOperation == InProgressOperation.None;

    /// <summary>Should the operation-in-progress banner be shown?</summary>
    public bool ShowOperationBanner => CurrentOperation != InProgressOperation.None;

    /// <summary>Human-readable name of the operation in progress.</summary>
    public string OperationText => CurrentOperation switch
    {
        InProgressOperation.Rebase => Loc.T("main.a_rebase_is_in_progress_finish_it_or_abort"),
        InProgressOperation.ApplyMailbox => Loc.T("main.a_patch_is_being_applied_git_am_finish_it_or"),
        InProgressOperation.Merge => Loc.T("main.the_merge_stopped_with_conflicts_resolve_the"),
        InProgressOperation.CherryPick => Loc.T("main.a_cherry_pick_is_in_progress_finish_it_or_ab"),
        InProgressOperation.Revert => Loc.T("main.a_revert_is_in_progress_finish_it_or_abort"),
        InProgressOperation.Bisect => Loc.T("main.a_bisect_is_in_progress_finish_it_or_reset"),
        _ => string.Empty,
    };

    /// <summary>Result of the last branch operation; shown as a banner in the UI.</summary>
    [ObservableProperty]
    public partial string? BranchNotice { get; set; }

    /// <summary>Is a repository open? Menu item enablement depends on this.</summary>
    /// <remarks>
    /// ⚠️ A computed property: its value is always correct but the <b>notification</b> does
    /// not arrive by itself. Updating the binding depends on <see cref="NotifyRepositoryDependents"/>.
    /// </remarks>
    public bool HasRepository => Commits.Repository is not null;

    /// <summary>
    /// Notifies <b>all</b> the bindings and command states that change when a repository opens or closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 The absence of this was a real bug: <c>HasRepository</c> was notified only on close,
    /// never on open → <c>IsEnabled="{Binding HasRepository}"</c> froze at its first value
    /// (<see langword="false"/>) and <b>two whole sections of the main menu</b>
    /// (<i>Repository</i>, <i>Commands</i>) stayed dimmed even with a repository open.
    /// </para>
    /// <para>
    /// ⚠️ <b>The commands must be notified too:</b> the <c>CanExecute</c> delegates look at
    /// <c>Commits.Repository</c>, but a menu item that has already been created does not ask
    /// again until <c>CanExecuteChanged</c> arrives. Submenu items were not affected because
    /// the menu is rebuilt <b>every time it opens</b>; on persistent bindings such as the
    /// toolbar and the shortcuts they would have stayed silently dead.
    /// </para>
    /// <para>
    /// When a property/command is added it must be added here too — because forgetting that is
    /// a silent bug, <c>MainWindowBindingTests</c> checks it through a real window.
    /// </para>
    /// </remarks>
    private void NotifyRepositoryDependents()
    {
        OnPropertyChanged(nameof(HasRepository));
        OnPropertyChanged(nameof(CanCreateBranch));
        OnPropertyChanged(nameof(CanCheckout));
        OnPropertyChanged(nameof(CanEditBranch));
        OnPropertyChanged(nameof(CanManageRemotes));
        OnPropertyChanged(nameof(CanPull));
        OnPropertyChanged(nameof(CanPush));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(CanAbortMerge));
        OnPropertyChanged(nameof(CanAbortOperation));
        OnPropertyChanged(nameof(CanResolveConflicts));
        OnPropertyChanged(nameof(CanShowStash));
        OnPropertyChanged(nameof(CanShowReflog));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(CanCherryPick));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(CanRebase));
        AbortOperationCommand.NotifyCanExecuteChanged();
        ResolveConflictsCommand.NotifyCanExecuteChanged();
        ShowStashCommand.NotifyCanExecuteChanged();
        ShowReflogCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        CherryPickCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        RebaseCommand.NotifyCanExecuteChanged();

        CreateBranchCommand.NotifyCanExecuteChanged();
        CheckoutCommand.NotifyCanExecuteChanged();
        RenameBranchCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
        ManageRemotesCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
        AbortMergeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Re-reads the open repository from scratch.
    /// </summary>
    /// <remarks>
    /// The path is supplied again: the <c>git</c> state may have changed from outside (a commit
    /// made on the command line, for instance), so the cache is not trusted.
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        string? path = Commits.Repository?.WorkingDirectory;

        if (!string.IsNullOrEmpty(path))
        {
            await OpenRepositoryAsync(path, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Handles a change reported by the watcher (P05-T14).
    /// </summary>
    /// <remarks>
    /// Only <see cref="RepositoryChangeKind.Repository"/> concerns the commit list; the commit
    /// window listens for working tree changes itself. Re-reading the commit history on every
    /// file save would be a job taking seconds in a large repository
    /// (measured: git/git 2.1 s, Linux 31.6 s).
    /// </remarks>
    private void OnRepositoryChanged(object? sender, RepositoryChangedEventArgs e)
    {
        if (e.Kind != RepositoryChangeKind.Repository)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = AutoRefreshAsync());
    }

    private async Task AutoRefreshAsync()
    {
        if (Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        // Events produced by our own reads must not trigger another refresh.
        using IDisposable? suspension = _watcher?.Suspend();

        await OpenRepositoryAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Branch creation flow (P06-T01).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a confirmation dialog but a setup dialog.</b> Creating a branch is not a
    /// destructive operation — P05-T15's "irreversible operation" rule does not apply here;
    /// the dialog exists to collect a name and options.
    /// </para>
    /// <para>
    /// ⚠️ The <b>selection in the commit list</b> is used as the starting point, not HEAD:
    /// in GitExtensions this command sits in the commit context menu and is labelled
    /// <i>"Create branch at this revision"</i> — ignoring the selected commit and creating
    /// from HEAD would be silently doing something else.
    /// </para>
    /// </remarks>
    private async Task CreateBranchAsync()
    {
        if (_branchWriter is null
            || BranchPrompt is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        string? startPoint = Commits.SelectedRow?.Commit.Id.Value;

        CreateBranchDecision decision = await BranchPrompt
            .RequestAsync(new CreateBranchRequest
            {
                StartPoint = startPoint,
                StartPointLabel = DescribeStartPoint(startPoint),
                HasLocalChanges = await HasLocalChangesAsync(path).ConfigureAwait(true),
            })
            .ConfigureAwait(true);

        if (!decision.Confirmed)
        {
            return;
        }

        try
        {
            // Events produced by our own write must not trigger an extra refresh; we already
            // refresh manually below (P05-T14's `Suspend()` rule).
            BranchCreateResult result;

            using (_watcher?.Suspend())
            {
                result = await _branchWriter
                    .CreateAsync(
                        path,
                        new BranchCreateOptions
                        {
                            Name = decision.Name,
                            StartPoint = startPoint,
                            Checkout = decision.Checkout,
                        })
                    .ConfigureAwait(true);
            }

            BranchNotice = Describe(result);
        }
        catch (GitException error)
        {
            // Raw stderr is not shown as the primary message (the rationale of GitFailureKind).
            BranchNotice = Loc.GitError(error);
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }


    /// <summary>
    /// Checkout flow for a branch / commit (P06-T02).
    /// </summary>
    /// <remarks>
    /// <b>Target selection:</b> if the selected commit has a <b>local branch</b>, that branch
    /// is checked out; otherwise the commit itself (detached). GitExtensions has two separate
    /// items in the commit menu too (<i>Checkout branch</i> · <i>Checkout this commit</i>);
    /// merging the two into one command, which one it will be is <b>stated plainly in the
    /// dialog</b>, never chosen silently.
    /// </remarks>
    private Task CheckoutAsync() => CheckoutCoreAsync(null);

    /// <param name="refName">
    /// Ref name coming from the panel; when <see langword="null"/> the selected commit is used.
    /// </param>
    private async Task CheckoutCoreAsync(string? refName)
    {
        if (_branchWriter is null
            || CheckoutPrompt is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        CheckoutTarget? resolved = refName is { Length: > 0 }
            ? new CheckoutTarget(refName, refName, IsDetached: false)
            : ResolveCheckoutTarget();

        if (resolved is not { } target)
        {
            BranchNotice = Loc.T("main.select_a_commit_to_check_out");
            return;
        }

        CheckoutDecision decision = await CheckoutPrompt
            .RequestAsync(new CheckoutRequest
            {
                Target = target.Value,
                TargetLabel = target.Label,
                IsDetached = target.IsDetached,
                HasLocalChanges = await HasLocalChangesAsync(path).ConfigureAwait(true),
            })
            .ConfigureAwait(true);

        if (!decision.Confirmed)
        {
            return;
        }

        try
        {
            BranchSwitchResult result;

            using (_watcher?.Suspend())
            {
                result = await _branchWriter
                    .SwitchAsync(
                        path,
                        new BranchSwitchOptions
                        {
                            Target = target.Value,
                            Detach = target.IsDetached,
                            LocalChanges = decision.LocalChanges,

                            // The confirmation itself comes from the dialog; the Core side
                            // still wants an explicit flag (the P05-T15 pattern).
                            UserConfirmed = decision.LocalChanges == LocalChangesAction.Discard,
                        })
                    .ConfigureAwait(true);
            }

            BranchNotice = Describe(result, target);
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
            return;
        }
        catch (InvalidOperationException error)
        {
            BranchNotice = error.Message;
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }


    /// <summary>
    /// Renames the local branch at the selected commit (P06-T03).
    /// </summary>
    private async Task RenameBranchAsync()
    {
        if (_branchWriter is null
            || BranchEditPrompt is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        if (SelectedLocalBranch is not { } branch)
        {
            BranchNotice = Loc.T("main.select_a_branch_to_rename");
            return;
        }

        RenameBranchDecision decision = await BranchEditPrompt
            .RequestRenameAsync(new RenameBranchRequest { CurrentName = branch })
            .ConfigureAwait(true);

        if (!decision.Confirmed || decision.NewName == branch)
        {
            return;
        }

        try
        {
            using (_watcher?.Suspend())
            {
                await _branchWriter
                    .RenameAsync(path, branch, decision.NewName)
                    .ConfigureAwait(true);
            }

            BranchNotice = $"Renamed '{branch}' to '{decision.NewName}'.";
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
            return;
        }
        catch (ArgumentException error)
        {
            BranchNotice = error.Message;
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Deletes the local branch at the selected commit (P06-T03).
    /// </summary>
    /// <remarks>
    /// <b>A two-round flow.</b> First an ordinary confirmation is asked and <c>git branch -d</c>
    /// is attempted; if git refuses because the branch is unmerged the dialog opens a second
    /// time, this time <b>with the recovery command</b>. We do not compute mergedness up
    /// front: measured, <c>-d</c> deletes a branch that is merged into its <b>upstream</b>
    /// rather than into HEAD; our own calculation would raise a false "unmerged" alarm on
    /// those branches.
    /// </remarks>
    private async Task DeleteBranchAsync()
    {
        if (_branchWriter is null
            || BranchEditPrompt is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        if (SelectedLocalBranch is not { } branch)
        {
            BranchNotice = Loc.T("main.select_a_branch_to_delete");
            return;
        }

        DeleteBranchDecision decision = await BranchEditPrompt
            .RequestDeleteAsync(new DeleteBranchRequest { Name = branch })
            .ConfigureAwait(true);

        if (!decision.Confirmed)
        {
            return;
        }

        BranchDeleteResult result;

        try
        {
            using (_watcher?.Suspend())
            {
                result = await _branchWriter.DeleteAsync(path, branch).ConfigureAwait(true);
            }
        }
        catch (BranchNotMergedException unmerged)
        {
            // Second round: now we can show the recovery command as well.
            DeleteBranchDecision forced = await BranchEditPrompt
                .RequestDeleteAsync(new DeleteBranchRequest
                {
                    Name = branch,
                    IsUnmerged = true,
                    LastCommitId = unmerged.LastCommitId,
                })
                .ConfigureAwait(true);

            if (!forced.Confirmed || !forced.Force)
            {
                return;
            }

            try
            {
                using (_watcher?.Suspend())
                {
                    result = await _branchWriter
                        .DeleteAsync(path, branch, force: true)
                        .ConfigureAwait(true);
                }
            }
            catch (GitException error)
            {
                BranchNotice = Loc.GitError(error);
                return;
            }
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
            return;
        }

        // 🔴 The hash MUST STAY in the notice: the deleted branch's own reflog goes away too,
        // and if the branch was never checked out in this working tree there is no trace in
        // the HEAD reflog either (measured).
        BranchNotice = result.WasUnmerged
            ? $"Branch '{result.Name}' deleted. To restore it: "
              + $"git branch {result.Name} {result.LastCommitId}"
            : $"Branch '{result.Name}' deleted (tip was {Shorten(result.LastCommitId)}).";

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the remote management screen (P06-T05).
    /// </summary>
    /// <remarks>
    /// The screen has its own ViewModel and <b>does not know</b> the main window (the decision
    /// made in P04-T08): this only sets it up, opening the window is the view's job.
    /// <para>
    /// <see cref="RefreshAsync"/> on close: remote tracking branches may have been deleted or
    /// a new remote added; the badges and the branch list must reflect that.
    /// </para>
    /// </remarks>
    private async Task ManageRemotesAsync()
    {
        if (_remoteReader is null
            || _remoteWriter is null
            || RemotesPrompt is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        RemotesViewModel model = new(_remoteReader, _remoteWriter, RemotesPrompt.RemovalConfirmer);

        try
        {
            await model.LoadAsync(path).ConfigureAwait(true);
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
            return;
        }

        // The watcher is suspended during repository writes: config changes can produce a
        // storm of refreshes (P05-T14).
        using (_watcher?.Suspend())
        {
            await RemotesPrompt.ShowAsync(model).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the Pull/Fetch screen (P06-T06 + P06-T07).
    /// </summary>
    /// <remarks>
    /// Fetch has no separate screen: in GitExtensions it is an option of <c>FormPull</c> too
    /// and its place in the menu is combined ("Pull/Fetch…", § 9).
    /// <para>
    /// A refresh on close is essential: remote tracking branches may have changed, HEAD moved.
    /// </para>
    /// </remarks>
    private async Task PullAsync()
    {
        if (_fetchWriter is null
            || _pullWriter is null
            || _remoteReader is null
            || PullPrompt is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        PullViewModel model = new(
            _remoteReader, _fetchWriter, _pullWriter, _authDiagnostics, AuthenticationPrompt);

        try
        {
            await model
                .LoadAsync(
                    path,
                    Commits.Refs?.CurrentBranch?.Name ?? string.Empty,
                    [.. Commits.Refs?.RemoteBranches.Select(branch => branch.Ref) ?? []])
                .ConfigureAwait(true);
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
            return;
        }

        using (_watcher?.Suspend())
        {
            await PullPrompt.ShowAsync(model).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the Push screen (P06-T08).
    /// </summary>
    /// <remarks>
    /// A refresh on close is essential: an upstream may have been set with <c>-u</c>, remote
    /// tracking branches moved or a branch deleted — all three change the branch badges.
    /// </remarks>
    private async Task PushAsync()
    {
        if (_pushWriter is null
            || _remoteReader is null
            || PushPrompt is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        PushViewModel model = new(
            _remoteReader, _pushWriter, _authDiagnostics, AuthenticationPrompt);

        try
        {
            await model
                .LoadAsync(
                    path,
                    Commits.Refs?.CurrentBranch?.Name ?? string.Empty,
                    [.. Commits.Refs?.LocalBranches ?? []])
                .ConfigureAwait(true);
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
            return;
        }

        using (_watcher?.Suspend())
        {
            await PushPrompt.ShowAsync(model).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the merge screen (P06-T11).
    /// </summary>
    private async Task MergeAsync()
    {
        if (_mergeWriter is null
            || MergePrompt is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        MergeViewModel model = new(_mergeWriter);

        // Remote branches can be merged too; GitExtensions' list contains both as well
        // (§ 9).
        IReadOnlyList<string> sources =
        [
            .. (Commits.Refs?.LocalBranches ?? []).Select(branch => branch.Name),
            .. (Commits.Refs?.RemoteBranches ?? [])
                .Where(branch => !branch.Ref.IsSymbolic)
                .Select(branch => branch.Name),
        ];

        try
        {
            await model
                .LoadAsync(
                    path,
                    Commits.Refs?.CurrentBranch?.Name ?? string.Empty,
                    sources,
                    SelectedLocalBranch)
                .ConfigureAwait(true);
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
            return;
        }

        using (_watcher?.Suspend())
        {
            await MergePrompt.ShowAsync(model).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Aborts the merge in progress (P06-T12).
    /// </summary>
    /// <remarks>
    /// 🔑 Confirmation is essential: <c>merge --abort</c> returns the working tree to its state
    /// BEFORE the merge, so everything the user wrote while resolving conflicts is lost
    /// (measured). The confirmation screen lists the unresolved files — no confirmation is
    /// asked for without showing what will be lost.
    /// </remarks>
    private async Task AbortMergeAsync()
    {
        if (_mergeWriter is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        IReadOnlyList<string> conflicted = Commits.Refs is null
            ? []
            : await ReadConflictedAsync(path).ConfigureAwait(true);

        if (MergeAbortConfirmer is { } confirmer
            && !await confirmer.ConfirmAsync(conflicted).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            using (_watcher?.Suspend())
            {
                await _mergeWriter.AbortAsync(path).ConfigureAwait(true);
            }

            BranchNotice = Loc.T("main.merge_aborted_the_working_tree_is_back_to_it");
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the git command log (P06-T16).</summary>
    private async Task ShowCommandLogAsync()
    {
        if (_commandLog is null || CommandLogPrompt is null)
        {
            return;
        }

        await CommandLogPrompt.ShowAsync(new CommandLogViewModel(_commandLog)).ConfigureAwait(true);
    }

    /// <summary>Opens the performance diagnostics panel (P09-T03).</summary>
    private async Task ShowDiagnosticsAsync()
    {
        if (_diagnostics is null || DiagnosticsPrompt is null)
        {
            return;
        }

        await DiagnosticsPrompt.ShowAsync(_diagnostics).ConfigureAwait(true);
    }

    /// <summary>Unresolved files — shown in the abort confirmation.</summary>
    private async Task<IReadOnlyList<string>> ReadConflictedAsync(string path)
    {
        if (_statusReader is null)
        {
            return [];
        }

        try
        {
            WorkingTreeStatus status = await _statusReader.ReadAsync(path).ConfigureAwait(true);

            return [.. status.Conflicted.Select(entry => entry.Path.Value)];
        }
        catch (GitException)
        {
            // The confirmation screen can be shown without the list too; blocking the abort
            // would not be right.
            return [];
        }
    }

    /// <summary>The first local branch at the selected commit.</summary>
    private string? SelectedLocalBranch =>
        Commits.SelectedRow?.Badges.FirstOrDefault(badge => badge.IsLocalBranch)?.Text;

    private static string Shorten(string id) => id.Length > 8 ? id[..8] : id;

    private sealed record CheckoutTarget(string Value, string Label, bool IsDetached);

    private CheckoutTarget? ResolveCheckoutTarget()
    {
        if (Commits.SelectedRow is not { } row)
        {
            return null;
        }

        // The same commit can carry several branches; the first local branch is chosen and
        // written on the label, so the user sees which one they switched to.
        RefBadge? branch = row.Badges.FirstOrDefault(badge => badge.IsLocalBranch);

        return branch is not null
            ? new CheckoutTarget(branch.Text, branch.Text, IsDetached: false)
            : new CheckoutTarget(row.Commit.Id.Value, $"{row.ShortId} — {row.Subject}", IsDetached: true);
    }

    private static string Describe(BranchSwitchResult result, CheckoutTarget target)
    {
        string summary = target.IsDetached
            ? $"Checked out commit {target.Label} (detached HEAD)."
            : $"Switched to branch '{result.Target}'.";

        // 🔴 A conflict is possible even with exit code 0 (the `--merge` measurement); staying
        // silent would be telling the user "checked out successfully".
        if (result.HasConflicts)
        {
            summary += Loc.T("main.some_files_are_unresolved_you_need_to_resolv");
        }

        if (result.StashCreated)
        {
            summary += Loc.T("main.your_local_changes_were_stashed_restore_them");
        }

        if (result.Backups.Count > 0)
        {
            summary += $" The discarded content of {result.Backups.Count} files was backed up.";
        }

        return summary;
    }


    private static string Describe(BranchCreateResult result)
    {
        string summary = result.CheckedOut
            ? $"Branch '{result.Name}' created and checked out."
            : $"Branch '{result.Name}' created.";

        // git set the upstream itself; a link created without the user asking must not stay silent.
        return result.Upstream is { Length: > 0 } upstream
            ? $"{summary} Tracked branch: {upstream}."
            : summary;
    }

    private string DescribeStartPoint(string? startPoint)
    {
        if (startPoint is not { Length: > 0 })
        {
            return Loc.T("main.head_tip_of_the_current_branch");
        }

        string shortId = startPoint.Length > 8 ? startPoint[..8] : startPoint;
        string? subject = Commits.SelectedRow?.Subject;

        return subject is { Length: > 0 } ? $"{shortId} — {subject}" : shortId;
    }

    private async Task<bool> HasLocalChangesAsync(string path)
    {
        if (_statusReader is null)
        {
            return false;
        }

        try
        {
            WorkingTreeStatus status = await _statusReader.ReadAsync(path).ConfigureAwait(true);

            return status.Entries.Count > 0;
        }
        catch (GitException)
        {
            // Failing to read the state needed for a warning does not block branch creation.
            return false;
        }
    }

    /// <summary>
    /// Starts watching the open repository; stops when there is none (P05-T14).
    /// </summary>
    /// <remarks>
    /// <b>A bare repository is not watched:</b> there is no working tree, so no file to watch.
    /// </remarks>
    private void UpdateWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        if (Commits.Repository is { WorkTreeRoot: { Length: > 0 } root } repository)
        {
            // ⚠️ Three separate paths: in a linked working tree HEAD/index live in its own git
            // directory, while the refs live in the common directory (CLAUDE.md § 5, item 9).
            _watcher.Start(root, repository.GitDirectory, repository.CommonDirectory);
        }
        else
        {
            _watcher.Stop();
        }
    }

    /// <summary>Closes the repository; the welcome screen comes back.</summary>
    public void CloseRepository()
    {
        Commits.Close();
        _watcher?.Stop();

        Subtitle = Loc.T("main.no_repository_open");

        // The repository was closed deliberately: the next startup must show the welcome
        // screen, not the same repository. That is what the user means by "close".
        Session?.ForgetRepository();

        // `Commits.Close()` already sets `Repository = null` and sends the subscription
        // notifications; the call here only stands to guarantee the ordering (notifying twice
        // is harmless, notifying too little is not).
        OnPropertyChanged(nameof(ShowWelcome));
        NotifyRepositoryDependents();
    }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = Loc.T("main.no_repository_open");

    /// <summary>
    /// Should the welcome screen be shown?
    /// </summary>
    /// <remarks>
    /// When there is no open repository and nothing is loading. Hiding it during loading is
    /// deliberate: otherwise the welcome screen flashes up and vanishes at startup.
    /// </remarks>
    public bool ShowWelcome => Commits.Repository is null && !Commits.IsLoading;

    /// <summary>
    /// Repository selection at application startup (P03-T16).
    /// </summary>
    /// <param name="explicitPath">
    /// Path given on the command line; <see langword="null"/> when not given.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// If the path was given explicitly, failing to open it is an <b>error</b> and is told to
    /// the user — it is the user who asked for that path.
    /// </para>
    /// <para>
    /// If it was not given, <b>the dashboard opens</b> (P12-T04). That is GitExtensions'
    /// behaviour: <c>Program.cs</c> looks at the working directory only when it was started with
    /// an argument, and reopening the last repository is a setting that is <b>off by default</b>
    /// (<c>StartWithRecentWorkingDir</c>). The repository is picked from the dashboard.
    /// </para>
    /// <para>
    /// 🔴 Until P12-T04 the current working directory was tried silently. Launched from a
    /// terminal that happened to sit inside a repository, the application went straight into it
    /// and the user never saw their list — the dashboard was only reachable by closing the
    /// repository. Passing the path (<c>gitext-core .</c>) still opens it directly.
    /// </para>
    /// </remarks>
    public async Task StartAsync(string? explicitPath, CancellationToken cancellationToken = default)
    {
        await LoadRecentAsync(cancellationToken).ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            await OpenRepositoryAsync(explicitPath, cancellationToken).ConfigureAwait(true);
            return;
        }

        // The repository open at shutdown is reopened only when the user asked for it (P08-T16,
        // now behind the P12-T04 setting).
        if (Session is { StartWithLastRepository: true, LastRepository: { Length: > 0 } last })
        {
            await TryOpenQuietlyAsync(last, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Session recorder (P08-T16). Session state is not kept when this is not supplied.
    /// </summary>
    /// <remarks>
    /// A settable property like the other view dependencies: most ViewModel tests do not care
    /// about session persistence and a mandatory dependency would require changing all of
    /// them.
    /// </remarks>
    public SessionTracker? Session { get; set; }

    /// <summary>
    /// Restores the repository's last selected commit (P08-T16).
    /// </summary>
    /// <remarks>
    /// <b>If the SHA is not found nothing is done</b> and the default selection (the newest
    /// commit) is kept. The commit may genuinely be gone: rebased, reset away or pruned.
    /// Clearing the selection for a SHA that cannot be found would leave the user with an
    /// empty details panel.
    /// </remarks>
    private void RestoreSelectedCommit(string workingDirectory)
    {
        if (Session?.SelectedCommit(workingDirectory) is not { Length: > 0 } sha)
        {
            return;
        }

        Commits.TryGoToCommit(sha);
    }

    /// <summary>
    /// Opens a repository, updates the title and adds it to the recent list.
    /// </summary>
    public async Task OpenRepositoryAsync(string path, CancellationToken cancellationToken = default)
    {
        await Commits.OpenAsync(path, cancellationToken).ConfigureAwait(true);

        Subtitle = Commits.Repository is { } repository
            ? $"{repository.WorkingDirectory} — {Commits.Rows.Count} commit"
            : Commits.ErrorMessage ?? Loc.T("main.could_not_open_the_repository");

        OnPropertyChanged(nameof(ShowWelcome));

        UpdateWatcher();

        if (Commits.Repository is { } opened)
        {
            // Not the path the user gave but the root git resolved is recorded: opening from a
            // subfolder must not create two different entries in the list.
            await AddRecentAsync(opened.WorkingDirectory, cancellationToken).ConfigureAwait(true);

            Session?.RememberRepository(opened.WorkingDirectory);
            RestoreSelectedCommit(opened.WorkingDirectory);
        }

        await UpdateHeadStateAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the detached HEAD and operation-in-progress state (P06-T04).
    /// </summary>
    /// <remarks>
    /// Whether HEAD is detached comes from <c>RefReader</c> (based on <c>symbolic-ref</c>, so a
    /// branch actually named <c>(detached)</c> is not misread); the operation in progress comes
    /// from a separate read, because it looks at the file system and is not part of the ref read.
    /// </remarks>
    private async Task UpdateHeadStateAsync(CancellationToken cancellationToken)
    {
        if (Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            IsDetachedHead = false;
            CurrentOperation = InProgressOperation.None;
        }
        else
        {
            IsDetachedHead = Commits.Refs?.Head.IsDetached == true;

            // The branch panel (P06-T13) is fed from the same ref read: writing a second read
            // meant the two panels silently diverging. The toolbar's branch button (P12-T06)
            // comes from the very same place, for the very same reason.
            RefTree.Load(await ReadPanelDataAsync(path, cancellationToken).ConfigureAwait(true));
            UpdateBranches();

            CurrentOperation = _operations is null
                ? InProgressOperation.None
                : await ReadOperationAsync(path, cancellationToken).ConfigureAwait(true);
        }

        if (Commits.Repository?.WorkingDirectory is not { Length: > 0 })
        {
            RefTree.Load(new RefTreeData());
        }

        OnPropertyChanged(nameof(ShowDetachedBanner));
        OnPropertyChanged(nameof(ShowOperationBanner));
        OnPropertyChanged(nameof(OperationText));

        // The title and the toolbar's branch button both name the current branch; they are
        // notified here, where the branch is actually known.
        OnPropertyChanged(nameof(CurrentBranchLabel));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(RepositoryLabel));
    }

    // ------------------------------------------------- P12-T14: the left panel's node actions

    /// <summary>
    /// Applies a stash entry, optionally dropping it afterwards (<c>apply</c> / <c>pop</c>).
    /// </summary>
    /// <remarks>
    /// The result is reported in the notice strip, conflicts included: <c>stash pop</c> leaves the
    /// entry in place when it conflicts (measured in P07-T12) and a user who is not told that
    /// applies it a second time.
    /// </remarks>
    public async Task ApplyStashAsync(string selector, bool drop)
    {
        if (_advanced.Stash is not { } stash
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path
            || string.IsNullOrWhiteSpace(selector))
        {
            return;
        }

        try
        {
            StashApplyResult result = await stash.ApplyAsync(path, selector, drop).ConfigureAwait(true);

            BranchNotice = result.HasConflicts
                ? Loc.F("ref_tree.stash_applied_with_conflicts", selector, result.ConflictedPaths.Count)
                : Loc.F(drop ? "ref_tree.stash_popped" : "ref_tree.stash_applied", selector);
        }
        catch (GitException exception)
        {
            BranchNotice = Loc.GitError(exception);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Drops a stash entry.</summary>
    /// <remarks>
    /// 🔴 This cannot be undone through the interface, so it asks first — the P05-T15 rule. The
    /// entry does survive in the reflog, and the message says so instead of pretending otherwise.
    /// </remarks>
    public async Task DropStashAsync(string selector)
    {
        if (_advanced.Stash is not { } stash
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path
            || string.IsNullOrWhiteSpace(selector)
            || DashboardConfirmer is not { } confirmer)
        {
            return;
        }

        bool confirmed = await confirmer.ConfirmAsync(
                Loc.T("ref_tree.drop_stash"),
                Loc.F("ref_tree.drop_stash_question", selector))
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            await stash.DropAsync(path, selector).ConfigureAwait(true);
            BranchNotice = Loc.F("ref_tree.stash_dropped", selector);
        }
        catch (GitException exception)
        {
            BranchNotice = Loc.GitError(exception);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Sets the whole working tree aside (the <i>Stashes</i> heading's "Stash all").</summary>
    public async Task StashAllAsync()
    {
        if (_advanced.Stash is not { } stash
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            bool stashed = await stash.PushAsync(path, new StashPushOptions()).ConfigureAwait(true);

            // "Nothing to stash" is an answer, not a failure — and without it the user is left
            // wondering whether the click did anything at all.
            BranchNotice = stashed
                ? Loc.T("ref_tree.stash_created")
                : Loc.T("ref_tree.nothing_to_stash");
        }
        catch (GitException exception)
        {
            BranchNotice = Loc.GitError(exception);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Fetches from every remote, optionally pruning (the <i>Remotes</i> heading).</summary>
    public async Task FetchAllRemotesAsync(bool prune)
    {
        if (_fetchWriter is null || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            // Remote = null means --all (see FetchOptions).
            FetchResult result = await _fetchWriter
                .FetchAsync(path, new FetchOptions { Remote = null, Prune = prune })
                .ConfigureAwait(true);

            // 🔴 A partial failure is REPORTED: git keeps going after one remote fails and exits
            // 0, so a silent "done" would hide the fact that half the remotes were not reached
            // (P09, the parallel-fetch finding).
            BranchNotice = result.Failures.Count > 0
                ? Loc.F("ref_tree.fetch_partly_failed", result.Changes.Count, result.Failures.Count)
                : Loc.F("ref_tree.fetch_finished", result.Changes.Count);
        }
        catch (GitException exception)
        {
            BranchNotice = Loc.GitError(exception);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Updates a submodule (<c>git submodule update --init</c>).</summary>
    public async Task UpdateSubmoduleAsync(string relativePath)
    {
        if (_advanced.Submodules is not { } submodules
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path
            || string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            await submodules
                .InitializeAsync(path, RepositoryPath.Parse(relativePath), recursive: true)
                .ConfigureAwait(true);

            BranchNotice = Loc.F("ref_tree.submodule_updated", relativePath);
        }
        catch (GitException exception)
        {
            BranchNotice = Loc.GitError(exception);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Shows the dialogs the panel needs (the drop-stash question).</summary>
    public IDashboardPrompt? DashboardConfirmer { get; set; }

    /// <summary>
    /// Reads everything the left panel shows: refs, worktrees, submodules and stashes (P12-T13).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refs are already in hand; the other three are separate reads. <b>MEASURED</b> on three
    /// repositories: <c>stash list</c> and <c>worktree list</c> cost 0,6–2 ms each — cheap enough
    /// to do on every refresh. <c>submodule status</c> costs <b>12–49 ms</b> even where there are
    /// no submodules, and <c>SubmoduleReader</c> now skips it when there is no <c>.gitmodules</c>,
    /// so the common case pays nothing.
    /// </para>
    /// <para>
    /// A failure in any of the three is <b>not</b> an error: the section stays empty. The panel is
    /// an aid, and a repository must not fail to open because a stash could not be listed.
    /// </para>
    /// </remarks>
    private async Task<RefTreeData> ReadPanelDataAsync(string path, CancellationToken cancellationToken)
    {
        return new RefTreeData
        {
            Refs = Commits.Refs,
            RootPath = path,
            WorkTrees = await SafeAsync(
                () => _advanced.WorkTrees?.ListAsync(path, cancellationToken),
                Array.Empty<WorkTree>()).ConfigureAwait(true),
            Submodules = await SafeAsync(
                () => _advanced.Submodules?.ListAsync(path, cancellationToken),
                Array.Empty<Submodule>()).ConfigureAwait(true),
            Stashes = await SafeAsync(
                () => _advanced.Stash?.ListAsync(path, cancellationToken),
                Array.Empty<StashEntry>()).ConfigureAwait(true),
        };

        static async Task<IReadOnlyList<T>> SafeAsync<T>(
            Func<Task<IReadOnlyList<T>>?> read,
            IReadOnlyList<T> fallback)
        {
            try
            {
                return read() is { } task ? await task.ConfigureAwait(true) : fallback;
            }
            catch (Exception exception) when (exception is GitException or IOException)
            {
                return fallback;
            }
        }
    }

    private async Task<InProgressOperation> ReadOperationAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _operations!.ReadAsync(path, cancellationToken).ConfigureAwait(true);
        }
        catch (GitException)
        {
            // Failing to show the banner does not block opening the repository.
            return InProgressOperation.None;
        }
    }

    /// <summary>
    /// Tries to open the paths dropped onto the window (P03-T16, drag and drop).
    /// </summary>
    /// <remarks>
    /// What is dropped may be a <b>file</b> (the user drags a file from the file manager); in
    /// that case its containing folder is tried. <c>git</c> already searches upwards for the
    /// repository root, so any file inside the repository is enough.
    /// </remarks>
    /// <returns><see langword="true"/> when opening a path was attempted.</returns>
    public async Task<bool> TryOpenDroppedAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string directory = Directory.Exists(path)
                ? path
                : System.IO.Path.GetDirectoryName(path) ?? path;

            await OpenRepositoryAsync(directory, cancellationToken).ConfigureAwait(true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries a path that may not be a repository, without reporting failure as an error.
    /// </summary>
    /// <returns><see langword="true"/> when a repository was really opened.</returns>
    private async Task<bool> TryOpenQuietlyAsync(string path, CancellationToken cancellationToken)
    {
        await Commits.OpenAsync(path, cancellationToken).ConfigureAwait(true);

        UpdateWatcher();

        bool opened = Commits.Repository is not null;

        if (Commits.Repository is { } repository)
        {
            Subtitle = $"{repository.WorkingDirectory} — {Commits.Rows.Count} commit";
            await AddRecentAsync(repository.WorkingDirectory, cancellationToken).ConfigureAwait(true);

            Session?.RememberRepository(repository.WorkingDirectory);
            RestoreSelectedCommit(repository.WorkingDirectory);
        }
        else
        {
            // The error message is cleared: the user did not ask to open this folder.
            Commits.ErrorMessage = null;
            Commits.ErrorDetails = null;
            Subtitle = Loc.T("main.no_repository_open");
        }

        await UpdateHeadStateAsync(cancellationToken).ConfigureAwait(true);

        OnPropertyChanged(nameof(ShowWelcome));

        return opened;
    }

    private async Task LoadRecentAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Storage.RecentRepository> recent;

        try
        {
            recent = await _recentStore.LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        RecentRepositories.Clear();

        foreach (Storage.RecentRepository repository in recent)
        {
            RecentRepositories.Add(new RecentRepositoryItem(repository.Path, OpenRecentCommand));
        }

        // The dashboard reads the same store; refreshing it from here keeps the menu and the
        // dashboard from drifting apart.
        await Dashboard.LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task AddRecentAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            await _recentStore.AddAsync(workingDirectory, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        await LoadRecentAsync(cancellationToken).ConfigureAwait(true);
    }
}

/// <summary>
/// One entry of the toolbar's branch dropdown (P12-T06).
/// </summary>
public sealed class ToolbarBranchItem
{
    public ToolbarBranchItem(string name, bool isCurrent, ICommand checkoutCommand)
    {
        Name = name;
        IsCurrent = isCurrent;
        CheckoutCommand = checkoutCommand;
    }

    public string Name { get; }

    /// <summary>The branch that is checked out — it is marked and cannot be checked out again.</summary>
    public bool IsCurrent { get; }

    public bool CanCheckout => !IsCurrent;

    public ICommand CheckoutCommand { get; }

    public override string ToString() => Name;
}
