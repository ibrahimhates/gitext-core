using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The cherry-pick / revert dialog (P07-T07, P07-T08).
/// </summary>
/// <remarks>
/// One screen for both: in git they use the same sequencer, and the questions put to the user are the
/// same — which commit, should it be committed, and for a merge, which parent.
/// </remarks>
public sealed class SequencerViewModel : ViewModelBase
{
    private readonly string _workingDirectory;
    private readonly ISequencerWriter _sequencer;

    private bool _noCommit;
    private bool _recordOrigin = true;
    private int _parentCount;
    private int _mainlineParent = 1;
    private string? _error;
    private string? _result;
    private bool _isBusy;

    public SequencerViewModel(
        string workingDirectory,
        ISequencerWriter sequencer,
        SequencerOperation operation,
        IReadOnlyList<string> commits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(sequencer);
        ArgumentNullException.ThrowIfNull(commits);

        if (commits.Count == 0)
        {
            throw new ArgumentException("En az bir commit gerekli.", nameof(commits));
        }

        _workingDirectory = workingDirectory;
        _sequencer = sequencer;
        Operation = operation;
        Commits = commits;

        RunCommand = new AsyncRelayCommand(ExecuteAsync, () => !IsBusy);
    }

    public SequencerOperation Operation { get; }

    public IReadOnlyList<string> Commits { get; }

    public bool IsRevert => Operation == SequencerOperation.Revert;

    public string Title => IsRevert ? "Revert commits" : "Cherry-pick commits";

    public string Summary => Commits.Count == 1
        ? $"1 commit will be {(IsRevert ? "reverted" : "cherry-picked")}."
        : $"{Commits.Count} commits will be {(IsRevert ? "reverted" : "cherry-picked")}.";

    /// <summary><c>--no-commit</c>.</summary>
    public bool NoCommit
    {
        get => _noCommit;
        set
        {
            if (SetProperty(ref _noCommit, value))
            {
                OnPropertyChanged(nameof(CommandLine));
            }
        }
    }

    /// <summary><c>-x</c> — meaningful only for cherry-pick.</summary>
    public bool RecordOrigin
    {
        get => _recordOrigin;
        set
        {
            if (SetProperty(ref _recordOrigin, value))
            {
                OnPropertyChanged(nameof(CommandLine));
            }
        }
    }

    public bool SupportsRecordOrigin => !IsRevert;

    /// <summary>
    /// Is the selected commit a merge commit?
    /// </summary>
    /// <remarks>
    /// 🔴 MEASURED: reverting a merge commit without <c>-m</c> gives rc=128. That is why the parent
    /// selection is visible and <b>mandatory</b>.
    /// </remarks>
    public bool IsMergeCommit => _parentCount > 1;

    public int ParentCount => _parentCount;

    /// <summary>Which parent counts as the "mainline" (1-based).</summary>
    public int MainlineParent
    {
        get => _mainlineParent;
        set
        {
            if (SetProperty(ref _mainlineParent, value))
            {
                OnPropertyChanged(nameof(CommandLine));
            }
        }
    }

    /// <summary>The command that will run (the "show the command" principle).</summary>
    public string CommandLine => SequencerWriter.Describe(BuildOptions());

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public string? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    /// <summary>Did it stop on a conflict? (The caller opens the conflict screen.)</summary>
    public bool HasConflicts { get; private set; }

    public bool IsCompleted { get; private set; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RunCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand RunCommand { get; }

    /// <summary>Populates the screen: checks whether it is a merge commit.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _parentCount = await _sequencer
            .CountParentsAsync(_workingDirectory, Commits[0], cancellationToken)
            .ConfigureAwait(true);

        OnPropertyChanged(nameof(IsMergeCommit));
        OnPropertyChanged(nameof(ParentCount));
        OnPropertyChanged(nameof(CommandLine));
    }

    private SequencerOptions BuildOptions() => new()
    {
        Operation = Operation,
        Commits = Commits,
        NoCommit = NoCommit,
        RecordOrigin = RecordOrigin,

        // When it is not a merge commit, `-m` is not passed: git refuses it on a commit with a single
        // parent.
        MainlineParent = IsMergeCommit ? MainlineParent : null,
    };

    private async Task ExecuteAsync()
    {
        IsBusy = true;
        Error = null;

        try
        {
            SequencerResult result = await _sequencer
                .RunAsync(_workingDirectory, BuildOptions())
                .ConfigureAwait(true);

            HasConflicts = result.HasConflicts;

            Result = result.HasConflicts
                ? $"{result.ConflictedPaths.Count} file(s) conflicted — resolve them and continue."
                : result.RequiresCommit
                    // 🔴 `--no-commit` returns "success" but HEAD does not advance.
                    ? "Changes are staged but not committed yet."
                    : $"{result.CommitsCreated} commit(s) created. "
                      + $"To undo: {result.SafetyPoint.RecoveryCommand}";

            IsCompleted = true;
        }
        catch (GitException error)
        {
            Error = Loc.GitError(error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>The side that shows the cherry-pick / revert dialog (P07-T07, P07-T08).</summary>
public interface ISequencerPrompt
{
    Task ShowAsync(SequencerViewModel model);
}
