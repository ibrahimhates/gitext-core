using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>Action selection on the screen (P06-T07).</summary>
/// <remarks>
/// Order follows GitExtensions' <c>FormPull</c>'s <c>GroupMergeOptions</c> (§ 9):
/// <i>Merge · Rebase · Do not merge, only fetch</i>. Fetch has no separate screen —
/// so P06-T06's UI is here too.
/// </remarks>
public enum PullAction
{
    /// <summary>Merge the remote branch into the current branch.</summary>
    Merge,

    /// <summary>Replay local commits on top of the remote branch.</summary>
    Rebase,

    /// <summary>Fetch only: don't touch the working tree.</summary>
    FetchOnly,
}

/// <summary>
/// Pull / Fetch screen (P06-T06 + P06-T07).
/// </summary>
/// <remarks>
/// 🔑 <b>What will run is always on screen.</b> The plan's point ("what the pull button does
/// must never stay ambiguous") and the README's "show the command" principle: both the command
/// that will run (<see cref="CommandPreview"/>) and <b>where the strategy came from</b>
/// (<see cref="StrategyNotice"/>) are written out.
/// </remarks>
public sealed class PullViewModel : ViewModelBase
{
    private readonly IRemoteReader _remotes;
    private readonly IFetchWriter _fetch;
    private readonly IPullWriter _pull;
    private readonly IAuthenticationDiagnostics? _diagnostics;
    private readonly IAuthenticationPrompt? _authentication;

    private string _workingDirectory = string.Empty;
    private string? _selectedRemote;
    private string? _selectedBranch;
    private string _currentBranch = string.Empty;
    private PullAction _action;
    private FetchTagMode _tags;
    private bool _prune;
    private bool _pruneTags;
    private bool _autoStash;
    private bool _isBusy;
    private string? _notice;
    private string? _warning;
    private string? _recoveryCommand;
    private ResolvedPullStrategy? _configured;
    private string? _progressText;
    private double? _progressPercent;
    private CancellationTokenSource? _cancellation;

    public PullViewModel(
        IRemoteReader remotes,
        IFetchWriter fetch,
        IPullWriter pull,
        IAuthenticationDiagnostics? diagnostics = null,
        IAuthenticationPrompt? authentication = null)
    {
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(pull);

        _remotes = remotes;
        _fetch = fetch;
        _pull = pull;
        _diagnostics = diagnostics;
        _authentication = authentication;

        RunCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
        CancelCommand = new RelayCommand(Cancel, () => CanCancel);
    }

    /// <summary>Configured remote repositories.</summary>
    public ObservableCollection<string> Remotes { get; } = [];

    /// <summary>Remote branches of the selected remote.</summary>
    public ObservableCollection<string> RemoteBranches { get; } = [];

    public string? SelectedRemote
    {
        get => _selectedRemote;
        set
        {
            if (SetProperty(ref _selectedRemote, value))
            {
                UpdateBranches();
                RaisePreview();
            }
        }
    }

    public string? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (SetProperty(ref _selectedBranch, value))
            {
                RaisePreview();
            }
        }
    }

    /// <summary>Currently checked-out local branch (shown read-only).</summary>
    public string CurrentBranch
    {
        get => _currentBranch;
        private set => SetProperty(ref _currentBranch, value);
    }

    public PullAction Action
    {
        get => _action;
        set
        {
            if (SetProperty(ref _action, value))
            {
                OnPropertyChanged(nameof(IsMerge));
                OnPropertyChanged(nameof(IsRebase));
                OnPropertyChanged(nameof(IsFetchOnly));
                OnPropertyChanged(nameof(AutoStashApplies));
                RaisePreview();
            }
        }
    }

    // For the radio buttons in XAML: compound conditions are kept in the ViewModel (it was
    // measured in Phase 03 that computing in the binding silently misbehaves).
    public bool IsMerge
    {
        get => Action == PullAction.Merge;
        set { if (value) { Action = PullAction.Merge; } }
    }

    public bool IsRebase
    {
        get => Action == PullAction.Rebase;
        set { if (value) { Action = PullAction.Rebase; } }
    }

    public bool IsFetchOnly
    {
        get => Action == PullAction.FetchOnly;
        set { if (value) { Action = PullAction.FetchOnly; } }
    }

    /// <summary>Tag selection (<c>GroupTagOptions</c>).</summary>
    public FetchTagMode Tags
    {
        get => _tags;
        set
        {
            if (SetProperty(ref _tags, value))
            {
                OnPropertyChanged(nameof(IsReachableTags));
                OnPropertyChanged(nameof(IsAllTags));
                OnPropertyChanged(nameof(IsNoTags));
                RaisePreview();
            }
        }
    }

    public bool IsReachableTags
    {
        get => Tags == FetchTagMode.Default;
        set { if (value) { Tags = FetchTagMode.Default; } }
    }

    public bool IsAllTags
    {
        get => Tags == FetchTagMode.All;
        set { if (value) { Tags = FetchTagMode.All; } }
    }

    public bool IsNoTags
    {
        get => Tags == FetchTagMode.None;
        set { if (value) { Tags = FetchTagMode.None; } }
    }

    public bool Prune
    {
        get => _prune;
        set
        {
            if (SetProperty(ref _prune, value))
            {
                if (!value)
                {
                    // In GitExtensions too `PruneTags` is only active while `Prune` is checked;
                    // git itself doesn't accept `--prune-tags` on its own (measured).
                    PruneTags = false;
                }

                OnPropertyChanged(nameof(CanPruneTags));
                RaisePreview();
            }
        }
    }

    public bool PruneTags
    {
        get => _pruneTags;
        set
        {
            if (SetProperty(ref _pruneTags, value))
            {
                RaisePreview();
            }
        }
    }

    public bool CanPruneTags => Prune;

    /// <summary>
    /// <c>--autostash</c>.
    /// </summary>
    /// <remarks>
    /// Meaningless while only fetch is selected: fetch doesn't touch the working tree.
    /// </remarks>
    public bool AutoStash
    {
        get => _autoStash;
        set
        {
            if (SetProperty(ref _autoStash, value))
            {
                RaisePreview();
            }
        }
    }

    public bool AutoStashApplies => Action != PullAction.FetchOnly;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRun));
                OnPropertyChanged(nameof(CanCancel));
                RunCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Result of the operation.</summary>
    public string? Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    /// <summary>State that needs attention (conflict, partial success).</summary>
    public string? Warning
    {
        get => _warning;
        private set
        {
            if (SetProperty(ref _warning, value))
            {
                OnPropertyChanged(nameof(HasWarning));
            }
        }
    }

    public bool HasWarning => !string.IsNullOrEmpty(Warning);

    /// <summary>Command that undoes what was done; only populated if <c>HEAD</c> advanced.</summary>
    public string? RecoveryCommand
    {
        get => _recoveryCommand;
        private set
        {
            if (SetProperty(ref _recoveryCommand, value))
            {
                OnPropertyChanged(nameof(HasRecoveryCommand));
            }
        }
    }

    public bool HasRecoveryCommand => !string.IsNullOrEmpty(RecoveryCommand);

    /// <summary>
    /// What the user's settings say — stays visible <b>even if the selection changes</b>.
    /// </summary>
    public string? StrategyNotice => _configured is not { } resolved
        ? null
        : resolved.Source switch
        {
            PullStrategySource.BranchSetting =>
                $"This branch's setting (branch.{CurrentBranch}.rebase = {resolved.ConfigValue}) "
                + $"{Describe(resolved.Strategy)} diyor.",
            PullStrategySource.PullRebaseSetting =>
                $"Your setting (pull.rebase = {resolved.ConfigValue}) says {Describe(resolved.Strategy)}.",
            PullStrategySource.PullFfSetting =>
                $"Your setting (pull.ff = {resolved.ConfigValue}) only allows fast-forward.",
            _ => Loc.T("pull.no_preference_in_your_settings_merge_was_cho"),
        };

    /// <summary>
    /// Command that will run.
    /// </summary>
    /// <remarks>
    /// "Show the command" principle: the user must be able to read <b>what will happen</b>
    /// before pressing the button. The text is generated from the actual arguments, not
    /// hand-written — otherwise it would drift from the code over time.
    /// </remarks>
    public string CommandPreview
    {
        get
        {
            List<string> parts = ["git"];

            if (Action == PullAction.FetchOnly)
            {
                parts.Add("fetch");
            }
            else
            {
                parts.Add("pull");
                parts.Add(Action == PullAction.Rebase ? "--rebase" : "--no-rebase");

                if (AutoStash)
                {
                    parts.Add("--autostash");
                }
            }

            if (Prune)
            {
                parts.Add("--prune");
            }

            if (PruneTags)
            {
                parts.Add("--prune-tags");
            }

            if (Tags == FetchTagMode.All)
            {
                parts.Add("--tags");
            }
            else if (Tags == FetchTagMode.None)
            {
                parts.Add("--no-tags");
            }

            if (SelectedRemote is { Length: > 0 } remote)
            {
                parts.Add(remote);

                if (Action != PullAction.FetchOnly && SelectedBranch is { Length: > 0 } branch)
                {
                    parts.Add(branch);
                }
            }

            return string.Join(' ', parts);
        }
    }

    public bool CanRun => !IsBusy && SelectedRemote is { Length: > 0 };

    public IAsyncRelayCommand RunCommand { get; }

    /// <summary>Live progress text; empty when there's no operation running.</summary>
    public string? ProgressText
    {
        get => _progressText;
        private set
        {
            if (SetProperty(ref _progressText, value))
            {
                OnPropertyChanged(nameof(HasProgress));
            }
        }
    }

    public bool HasProgress => !string.IsNullOrEmpty(ProgressText);

    /// <summary>Percentage; <see langword="null"/> if git doesn't report one (indeterminate bar).</summary>
    public double? ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (SetProperty(ref _progressPercent, value))
            {
                OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    public bool IsProgressIndeterminate => ProgressPercent is null;

    /// <summary>
    /// Cancels a running operation (P06-T10).
    /// </summary>
    /// <remarks>
    /// 🔑 The process is <b>actually killed</b> (<c>Kill(entireProcessTree: true)</c>) —
    /// merely giving up waiting would leave a git process running in the background.
    /// Measured: a fetch cut off mid-way leaves no lock behind and <c>fsck</c> is clean.
    /// </remarks>
    public IRelayCommand CancelCommand { get; }

    public bool CanCancel => IsBusy;

    private void Cancel() => _cancellation?.Cancel();

    private IProgress<GitProgress> CreateProgress() => new Progress<GitProgress>(step =>
    {
        ProgressText = step.Describe();
        ProgressPercent = step.Percent;
    });


    /// <summary>Populates the screen for a repository.</summary>
    public async Task LoadAsync(
        string workingDirectory,
        string currentBranch,
        IReadOnlyList<GitRef> remoteBranches,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(remoteBranches);

        _workingDirectory = workingDirectory;
        CurrentBranch = currentBranch;
        _allRemoteBranches = remoteBranches;

        Remotes.Clear();

        foreach (GitRemote remote in await _remotes
                     .ReadAllAsync(workingDirectory, cancellationToken).ConfigureAwait(true))
        {
            Remotes.Add(remote.Name);
        }

        _configured = await _pull
            .ResolveStrategyAsync(workingDirectory, PullStrategy.Default, cancellationToken)
            .ConfigureAwait(true);

        // The screen OPENS with the option the user's setting says — opening with a different
        // option would silently make a user who knows their setting get something else.
        Action = _configured.Strategy switch
        {
            PullStrategy.Rebase => PullAction.Rebase,
            _ => PullAction.Merge,
        };

        SelectedRemote = Remotes.FirstOrDefault(name =>
            string.Equals(name, "origin", StringComparison.Ordinal)) ?? Remotes.FirstOrDefault();

        OnPropertyChanged(nameof(StrategyNotice));
    }

    private IReadOnlyList<GitRef> _allRemoteBranches = [];

    private void UpdateBranches()
    {
        RemoteBranches.Clear();

        if (SelectedRemote is not { Length: > 0 } remote)
        {
            return;
        }

        string prefix = remote + "/";

        foreach (GitRef reference in _allRemoteBranches)
        {
            if (reference.ShortName.StartsWith(prefix, StringComparison.Ordinal)
                && !reference.IsSymbolic)
            {
                RemoteBranches.Add(reference.ShortName[prefix.Length..]);
            }
        }

        // The remote branch with the same name as the current branch is the most likely target.
        SelectedBranch = RemoteBranches.FirstOrDefault(name =>
            string.Equals(name, CurrentBranch, StringComparison.Ordinal))
            ?? RemoteBranches.FirstOrDefault();
    }

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(CommandPreview));
        OnPropertyChanged(nameof(CanRun));
        RunCommand.NotifyCanExecuteChanged();
    }

    private static string Describe(PullStrategy strategy) => strategy switch
    {
        PullStrategy.Rebase => "yeniden temellendirme (rebase)",
        PullStrategy.FastForwardOnly => Loc.T("pull.fast_forward_only"),
        _ => Loc.T("pull.merge"),
    };

    private async Task RunAsync()
    {
        if (!CanRun)
        {
            return;
        }

        IsBusy = true;
        Notice = null;
        Warning = null;
        RecoveryCommand = null;
        ProgressText = null;
        ProgressPercent = null;

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        try
        {
            await RunOnceAsync(null).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error; the user already knows what happened.
            Notice = Loc.T("pull.the_operation_was_cancelled");
        }
        catch (GitException error) when (error.Kind == GitFailureKind.AuthenticationRequired)
        {
            await HandleAuthenticationAsync(error).ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Notice = Loc.GitError(error);
        }
        finally
        {
            IsBusy = false;
            ProgressText = null;
            ProgressPercent = null;
        }
    }

    private Task RunOnceAsync(GitCredentials? credentials) =>
        Action == PullAction.FetchOnly
            ? RunFetchAsync(credentials)
            : RunPullAsync(credentials);

    /// <summary>
    /// Handles an authentication error (P06-T09).
    /// </summary>
    /// <remarks>Rationale and limits: see the note on <see cref="PushViewModel"/>.</remarks>
    private async Task HandleAuthenticationAsync(GitException error)
    {
        if (_diagnostics is null || _authentication is null)
        {
            Notice = Loc.GitError(error);
            return;
        }

        AuthenticationDiagnosis diagnosis = await _diagnostics
            .DiagnoseAsync(_workingDirectory, SelectedRemote)
            .ConfigureAwait(true);

        GitCredentials? credentials = await _authentication
            .ShowAsync(new AuthenticationViewModel(diagnosis))
            .ConfigureAwait(true);

        if (credentials is null)
        {
            Warning = diagnosis.Explanation;
            return;
        }

        try
        {
            await RunOnceAsync(credentials).ConfigureAwait(true);
        }
        catch (GitException retry)
        {
            Warning = Loc.GitError(retry);
        }
    }

    private async Task RunFetchAsync(GitCredentials? credentials)
    {
        FetchResult result = await _fetch.FetchAsync(
            _workingDirectory,
            new FetchOptions
            {
                Remote = SelectedRemote,
                Prune = Prune,
                PruneTags = PruneTags,
                Tags = Tags,
                Credentials = credentials,
                Progress = CreateProgress(),
            },
            _cancellation?.Token ?? CancellationToken.None).ConfigureAwait(true);

        Notice = DescribeChanges(result.Changes);

        if (result.Failures.Count > 0)
        {
            // 🔴 Partial success: some remotes were updated, some weren't.
            Warning = Loc.T("pull.these_remotes_could_not_be_fetched")
                + string.Join(", ", result.Failures.Select(failure => failure.Remote));
        }
    }

    private async Task RunPullAsync(GitCredentials? credentials)
    {
        PullResult result = await _pull.PullAsync(
            _workingDirectory,
            new PullOptions
            {
                Remote = SelectedRemote,
                Branch = SelectedBranch,
                Strategy = Action == PullAction.Rebase ? PullStrategy.Rebase : PullStrategy.Merge,
                AutoStash = AutoStash,
                Prune = Prune,
                Tags = Tags,
                Credentials = credentials,
                Progress = CreateProgress(),
            },
            _cancellation?.Token ?? CancellationToken.None).ConfigureAwait(true);

        Notice = result.AlreadyUpToDate
            ? Loc.T("pull.already_up_to_date")
            : DescribeChanges(result.Changes);

        if (!result.AlreadyUpToDate)
        {
            RecoveryCommand = result.RecoveryCommand;
        }

        // 🔴 There can be a conflict even with exit code 0 (measured); staying silent would
        // mean saying "updated successfully".
        if (result.AutoStashConflict)
        {
            Warning = Loc.T("pull.the_fetch_succeeded_but_your_uncommitted_cha")
                + Loc.T("pull.conflicted_while_being_restored_your_changes")
                + Loc.T("pull.is_still_there_resolve_the_conflicting_files");
        }
        else if (result.HasConflicts)
        {
            Warning = Loc.T("pull.the_merge_stopped_with_conflicts_some_files_")
                + Loc.T("pull.resolve_the_conflicts_and_commit_or_abort_th");
        }
    }

    private static string DescribeChanges(IReadOnlyList<RefChange> changes)
    {
        if (changes.Count == 0)
        {
            return Loc.T("pull.no_changes");
        }

        int created = changes.Count(change => change.Kind == RefChangeKind.Created);
        int updated = changes.Count(change => change.Kind == RefChangeKind.Updated);
        int deleted = changes.Count(change => change.Kind == RefChangeKind.Deleted);

        List<string> parts = [];

        if (updated > 0)
        {
            parts.Add($"{updated} updated");
        }

        if (created > 0)
        {
            parts.Add($"{created} yeni");
        }

        if (deleted > 0)
        {
            // Pruning is destructive: swallowing the count would mean the user loses refs without knowing.
            parts.Add($"{deleted} removed");
        }

        return string.Join(" · ", parts) + $" ({string.Join(", ", changes.Take(4).Select(c => c.ShortName))}"
            + (changes.Count > 4 ? "…)" : ")");
    }
}
