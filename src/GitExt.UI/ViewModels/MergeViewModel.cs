using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The merge screen (P06-T11).
/// </summary>
/// <remarks>
/// <para>
/// The layout comes from GitExtensions <c>FormMergeBranch</c> (§ 9): the <i>Merge branch</i>
/// selection → <i>Into current branch</i> → <i>fast forward</i> / <i>always create a merge commit</i>
/// → <i>Do not commit</i> → <i>Show advanced options</i> (squash · allow unrelated histories · merge
/// message) → <i>Merge</i>.
/// </para>
/// <para>
/// 🔴 <b>When "Squash" is chosen the result screen says so explicitly:</b> git gives exit code 0 and
/// does not advance <c>HEAD</c> (measured). Saying "merged" and moving on would mean the user
/// forgetting to commit and deleting the branch.
/// </para>
/// </remarks>
public sealed class MergeViewModel : ViewModelBase
{
    private readonly IMergeWriter _merge;

    private string _workingDirectory = string.Empty;
    private string _currentBranch = string.Empty;
    private string? _source;
    private MergeStrategy _strategy;
    private bool _noCommit;
    private bool _allowUnrelatedHistories;
    private bool _showAdvanced;
    private string _message = string.Empty;
    private bool _isBusy;
    private string? _notice;
    private string? _warning;
    private string? _recoveryCommand;
    private MergePreview? _preview;

    public MergeViewModel(IMergeWriter merge)
    {
        ArgumentNullException.ThrowIfNull(merge);

        _merge = merge;

        RunCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
    }

    /// <summary>The branches and tags that can be merged.</summary>
    public ObservableCollection<string> Sources { get; } = [];

    /// <summary>The branch we are on (read-only).</summary>
    public string CurrentBranch
    {
        get => _currentBranch;
        private set => SetProperty(ref _currentBranch, value);
    }

    public string? Source
    {
        get => _source;
        set
        {
            if (SetProperty(ref _source, value))
            {
                ReloadPreview();
            }
        }
    }

    public MergeStrategy Strategy
    {
        get => _strategy;
        set
        {
            if (SetProperty(ref _strategy, value))
            {
                OnPropertyChanged(nameof(IsFastForward));
                OnPropertyChanged(nameof(IsMergeCommit));
                OnPropertyChanged(nameof(IsSquash));
                OnPropertyChanged(nameof(SquashNotice));
                OnPropertyChanged(nameof(HasSquashNotice));
                RaisePreview();
            }
        }
    }

    public bool IsFastForward
    {
        get => Strategy == MergeStrategy.Default;
        set { if (value) { Strategy = MergeStrategy.Default; } }
    }

    public bool IsMergeCommit
    {
        get => Strategy == MergeStrategy.NoFastForward;
        set { if (value) { Strategy = MergeStrategy.NoFastForward; } }
    }

    public bool IsSquash
    {
        get => Strategy == MergeStrategy.Squash;
        set { if (value) { Strategy = MergeStrategy.Squash; } }
    }

    public bool NoCommit
    {
        get => _noCommit;
        set
        {
            if (SetProperty(ref _noCommit, value))
            {
                RaisePreview();
            }
        }
    }

    public bool AllowUnrelatedHistories
    {
        get => _allowUnrelatedHistories;
        set
        {
            if (SetProperty(ref _allowUnrelatedHistories, value))
            {
                RaisePreview();
            }
        }
    }

    /// <summary>GitExtensions'taki <i>"Show advanced options"</i>.</summary>
    public bool ShowAdvanced
    {
        get => _showAdvanced;
        set => SetProperty(ref _showAdvanced, value);
    }

    public string Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
            {
                RaisePreview();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRun));
                RunCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

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
    /// The warning shown <b>in advance</b> while squash is selected.
    /// </summary>
    /// <remarks>
    /// It has to be said afterwards as well, but saying it beforehand is better: let the user start
    /// knowing what they are getting into.
    /// </remarks>
    public string? SquashNotice => Strategy != MergeStrategy.Squash
        ? null
        : Loc.T("merge.squash_does_not_create_a_commit_the_changes_");

    public bool HasSquashNotice => SquashNotice is not null;

    /// <summary>What the selected branch will bring.</summary>
    public string? PreviewNotice => _preview is not { } preview
        ? null
        : !preview.HasCommonAncestor
            ? Loc.T("merge.this_branch_has_no_common_ancestor_unrelated")
            : !preview.HasChanges
                ? Loc.T("merge.nothing_to_fetch_on_this_branch")
                : preview.CanFastForward
                    ? $"{preview.Ahead} commits can be fast-forwarded."
                    : $"{preview.Ahead} commits will be merged (cannot fast-forward).";

    public bool HasPreviewNotice => PreviewNotice is not null;

    /// <summary>The command that will run (the "show the command" principle).</summary>
    public string CommandPreview => Source is not { Length: > 0 }
        ? string.Empty
        : MergeWriter.Describe(BuildOptions());

    public bool CanRun => !IsBusy && Source is { Length: > 0 };

    public IAsyncRelayCommand RunCommand { get; }

    /// <summary>Populates the screen.</summary>
    public async Task LoadAsync(
        string workingDirectory,
        string currentBranch,
        IReadOnlyList<string> sources,
        string? preselect = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(sources);

        _workingDirectory = workingDirectory;
        CurrentBranch = currentBranch;

        Sources.Clear();

        foreach (string source in sources)
        {
            // Merging something into itself is meaningless; keeping it out of the list prevents the
            // selection mistake from the start.
            if (!string.Equals(source, currentBranch, StringComparison.Ordinal))
            {
                Sources.Add(source);
            }
        }

        Source = preselect is { Length: > 0 } && Sources.Contains(preselect)
            ? preselect
            : Sources.FirstOrDefault();

        await ReloadPreviewAsync(cancellationToken).ConfigureAwait(true);
    }

    private void ReloadPreview() => _ = ReloadPreviewSafeAsync();

    private async Task ReloadPreviewSafeAsync()
    {
        try
        {
            await ReloadPreviewAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Warning = Loc.GitError(error);
        }
    }

    private async Task ReloadPreviewAsync(CancellationToken cancellationToken)
    {
        if (_workingDirectory.Length == 0 || Source is not { Length: > 0 } source)
        {
            _preview = null;
        }
        else
        {
            _preview = await _merge
                .PreviewAsync(_workingDirectory, source, cancellationToken)
                .ConfigureAwait(true);
        }

        OnPropertyChanged(nameof(PreviewNotice));
        OnPropertyChanged(nameof(HasPreviewNotice));
        RaisePreview();
    }

    private MergeOptions BuildOptions() => new()
    {
        Source = Source ?? string.Empty,
        Strategy = Strategy,
        NoCommit = NoCommit,
        AllowUnrelatedHistories = AllowUnrelatedHistories,
        Message = Message.Length > 0 ? Message : null,
    };

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(CommandPreview));
        OnPropertyChanged(nameof(CanRun));
        RunCommand.NotifyCanExecuteChanged();
    }

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

        try
        {
            MergeResult result = await _merge
                .MergeAsync(_workingDirectory, BuildOptions())
                .ConfigureAwait(true);

            Report(result);
        }
        catch (GitException error)
        {
            Warning = Loc.GitError(error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Report(MergeResult result)
    {
        RecoveryCommand = result.RecoveryCommand;

        switch (result.Outcome)
        {
            case MergeOutcome.AlreadyUpToDate:
                Notice = Loc.T("merge.already_up_to_date");
                break;

            case MergeOutcome.FastForward:
                Notice = Loc.T("merge.fast_forwarded_no_new_commit_was_created");
                break;

            case MergeOutcome.MergeCommit:
                Notice = Loc.T("merge.merge_commit_created");
                break;

            case MergeOutcome.Staged:
                // 🔴 The heart of the measurement: exit code 0 but HEAD has not moved.
                Notice = Loc.T("merge.the_changes_were_staged");
                Warning = Loc.T("merge.nothing_was_committed_yet_the_changes_are_st")
                    + Loc.T("merge.you_need_to_finish_it_from_the_commit_screen");
                break;

            case MergeOutcome.Conflicted:
            default:
                Notice = null;
                Warning = Loc.T("merge.the_merge_stopped_with_conflicts")
                    + $"{result.ConflictedPaths.Count} files are UNRESOLVED: "
                    + string.Join(", ", result.ConflictedPaths.Take(4))
                    + (result.ConflictedPaths.Count > 4 ? "…" : string.Empty)
                    + Loc.T("merge.resolve_the_conflicts_and_commit_or_abort_th");
                break;
        }
    }
}

/// <summary>The side that shows the merge screen (P06-T11).</summary>
public interface IMergePrompt
{
    /// <summary>Shows the screen modally and waits for it to close.</summary>
    Task ShowAsync(MergeViewModel model);
}

/// <summary>The request of a drag-and-drop merge (P06-T15).</summary>
/// <param name="Source">The branch being dragged.</param>
/// <param name="Target">The branch it is dropped on — it must be <b>the current branch</b>.</param>
/// <param name="Command">The command that will run; shown verbatim on the confirmation screen.</param>
public sealed record MergeDropRequest(string Source, string Target, string Command);

/// <summary>The side that confirms a drag-and-drop merge (P06-T15).</summary>
/// <remarks>
/// 🔑 <b>Confirmation is always asked for</b> — an item from the plan. An accidental drag is a real
/// risk: moving a branch a few pixels by mistake must not start an operation that silently rewrites
/// history.
/// </remarks>
public interface IMergeDropConfirmer
{
    Task<bool> ConfirmAsync(MergeDropRequest request);
}

/// <summary>The side that confirms aborting an in-progress merge (P06-T12).</summary>
public interface IMergeAbortConfirmer
{
    /// <summary>
    /// Asks for confirmation of the abort.
    /// </summary>
    /// <param name="conflicted">The unresolved files — so the user can see what they will lose.</param>
    Task<bool> ConfirmAsync(IReadOnlyList<string> conflicted);
}
