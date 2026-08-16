using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The reset dialog (P07-T06).
/// </summary>
/// <remarks>
/// <para>
/// The plan asks for it explicitly: <i>"A dialog that states clearly what each mode will do: which
/// commits will be lost, what will happen to the working directory."</i> That is why the mode
/// selection is not a dropdown but three options that each <b>state what they do</b>.
/// </para>
/// <para>
/// The layout comes from GitExtensions <c>FormResetCurrentBranch</c> (§ 9): radio buttons, an
/// explanation below them, and the confirmation at the bottom.
/// </para>
/// </remarks>
public sealed class ResetViewModel : ViewModelBase
{
    private readonly string _workingDirectory;
    private readonly IResetWriter _reset;

    private ResetMode _mode = ResetMode.Mixed;
    private ResetPreview? _preview;
    private bool _confirmed;
    private string? _error;
    private string? _result;
    private bool _isBusy;

    public ResetViewModel(string workingDirectory, IResetWriter reset, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(reset);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        _workingDirectory = workingDirectory;
        _reset = reset;
        Target = target;

        ResetCommand = new AsyncRelayCommand(RunAsync, CanReset);
    }

    /// <summary>The commit to return to.</summary>
    public string Target { get; }

    public ResetMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsSoft));
                OnPropertyChanged(nameof(IsMixed));
                OnPropertyChanged(nameof(IsHard));
                OnPropertyChanged(nameof(ModeDescription));
                OnPropertyChanged(nameof(LosesWork));
                OnPropertyChanged(nameof(RequiresConfirmation));
                OnPropertyChanged(nameof(CommandLine));
                ResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // So the radio buttons can be bound two-way.
    public bool IsSoft
    {
        get => Mode == ResetMode.Soft;
        set
        {
            if (value)
            {
                Mode = ResetMode.Soft;
            }
        }
    }

    public bool IsMixed
    {
        get => Mode == ResetMode.Mixed;
        set
        {
            if (value)
            {
                Mode = ResetMode.Mixed;
            }
        }
    }

    public bool IsHard
    {
        get => Mode == ResetMode.Hard;
        set
        {
            if (value)
            {
                Mode = ResetMode.Hard;
            }
        }
    }

    /// <summary>What the selected mode will do — a plain statement of the measured behaviour.</summary>
    public string ModeDescription => Mode switch
    {
        ResetMode.Soft =>
            "Moves the branch only. Your changes stay on disk and stay staged, ready to be "
            + "committed again.",
        ResetMode.Hard =>
            "Moves the branch, the index and the working tree. Every uncommitted change is "
            + "discarded and cannot be recovered — the reflog only keeps commits.",
        ResetMode.Keep =>
            "Moves the branch but keeps local changes. Refuses if they would conflict.",
        _ =>
            "Moves the branch and the index. Your changes stay on disk but are no longer "
            + "staged.",
    };

    public ResetPreview? Preview
    {
        get => _preview;
        private set
        {
            if (SetProperty(ref _preview, value))
            {
                OnPropertyChanged(nameof(DroppedText));
                OnPropertyChanged(nameof(LosesWork));
                OnPropertyChanged(nameof(RequiresConfirmation));
                OnPropertyChanged(nameof(IsTargetValid));
                ResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsTargetValid => Preview?.IsTargetValid != false;

    /// <summary>How many commits will drop, and which ones.</summary>
    public string DroppedText => Preview is null || Preview.DroppedCount == 0
        ? "No commits will be dropped."
        : $"{Preview.DroppedCount} commit(s) will be dropped: "
          + string.Join(", ", Preview.DroppedCommits.Take(5))
          + (Preview.DroppedCount > 5 ? "…" : string.Empty);

    /// <summary>
    /// Does this choice lead to an <b>irrecoverable</b> loss?
    /// </summary>
    /// <remarks>
    /// The dropped commits are still in the reflog; what really cannot be recovered are the uncommitted
    /// changes <c>--hard</c> deletes.
    /// </remarks>
    public bool LosesWork => Preview?.LosesUncommittedWork(Mode) == true;

    /// <summary>
    /// Should the extra confirmation checkbox be shown?
    /// </summary>
    /// <remarks>
    /// The lesson of P05-T15: when the recovery command is on screen a checkbox is enough and no
    /// separate warning window is needed. But <c>--hard</c> on a dirty tree produces an irrecoverable
    /// loss — the checkbox belongs exactly there.
    /// </remarks>
    public bool RequiresConfirmation => LosesWork;

    public bool Confirmed
    {
        get => _confirmed;
        set
        {
            if (SetProperty(ref _confirmed, value))
            {
                ResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The command that will run (the "show the command" principle).</summary>
    public string CommandLine =>
        ResetWriter.Describe(new ResetOptions { Target = Target, Mode = Mode });

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>The "to undo this…" information shown after the operation.</summary>
    public string? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Has the operation finished? (The window uses this to close.)</summary>
    public bool IsCompleted { get; private set; }

    public IAsyncRelayCommand ResetCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default) =>
        Preview = await _reset
            .PreviewAsync(_workingDirectory, Target, cancellationToken)
            .ConfigureAwait(true);

    private bool CanReset() =>
        !IsBusy && IsTargetValid && (!RequiresConfirmation || Confirmed);

    private async Task RunAsync()
    {
        IsBusy = true;
        Error = null;

        try
        {
            SafetyPoint point = await _reset
                .ResetAsync(_workingDirectory, new ResetOptions { Target = Target, Mode = Mode })
                .ConfigureAwait(true);

            // The phase rule: the undo information is ALWAYS offered.
            Result = point.IsFullyRecoverable
                ? $"To undo: {point.RecoveryCommand}"
                : $"To undo: {point.RecoveryCommand} — note that uncommitted changes were "
                  + "discarded and are not restored by this command.";

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

/// <summary>The side that shows the reset dialog (P07-T06).</summary>
public interface IResetPrompt
{
    Task ShowAsync(ResetViewModel model);
}
