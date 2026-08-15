using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Interactive rebase todo listesindeki bir satır (P07-T10).
/// </summary>
public sealed class RebaseStepViewModel : ViewModelBase
{
    private RebaseAction _action;
    private string _newMessage = string.Empty;

    public required RebaseStep Step { get; init; }

    public string ShortId => Step.ShortId;

    public string Subject => Step.Subject;

    public RebaseAction Action
    {
        get => _action;
        set
        {
            if (SetProperty(ref _action, value))
            {
                OnPropertyChanged(nameof(IsDropped));
                OnPropertyChanged(nameof(NeedsMessage));
            }
        }
    }

    /// <summary>Bu commit çıkarılacak mı? (Listede üstü çizili gösteriliyor.)</summary>
    public bool IsDropped => Action == RebaseAction.Drop;

    /// <summary><c>reword</c> seçildiyse yeni mesaj isteniyor.</summary>
    public bool NeedsMessage => Action == RebaseAction.Reword;

    public string NewMessage
    {
        get => _newMessage;
        set => SetProperty(ref _newMessage, value);
    }

    /// <summary>Seçilebilecek eylemler — açılır listeyi dolduruyor.</summary>
    public static IReadOnlyList<RebaseAction> Actions { get; } =
    [
        RebaseAction.Pick,
        RebaseAction.Reword,
        RebaseAction.Edit,
        RebaseAction.Squash,
        RebaseAction.Fixup,
        RebaseAction.Drop,
    ];

    public RebaseStep ToStep() => Step with
    {
        Action = Action,
        NewMessage = NeedsMessage && NewMessage.Length > 0 ? NewMessage : null,
    };
}

/// <summary>
/// Rebase ekranı (P07-T09, P07-T10).
/// </summary>
/// <remarks>
/// <para>
/// Yerleşim GitExtensions <c>FormRebase</c>'ten (§ 9): üstte hedef seçimi, ortada
/// interactive todo listesi, altta eylemler.
/// </para>
/// <para>
/// Todo listesi <b>sürükle-bırak</b> ile sıralanabiliyor; sıra
/// <see cref="MoveUpCommand"/>/<see cref="MoveDownCommand"/> ile klavyeden de
/// değiştirilebilir — sürükle-bırak tek yol olsaydı klavyeyle kullanılamazdı.
/// </para>
/// </remarks>
public sealed class RebaseViewModel : ViewModelBase
{
    private readonly string _workingDirectory;
    private readonly IRebaseWriter _rebase;

    private string _upstream = string.Empty;
    private bool _isInteractive;
    private bool _autoStash = true;
    private RebaseStepViewModel? _selected;
    private string? _error;
    private string? _result;
    private bool _isBusy;

    public RebaseViewModel(string workingDirectory, IRebaseWriter rebase, string upstream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(rebase);

        _workingDirectory = workingDirectory;
        _rebase = rebase;
        _upstream = upstream;

        RunCommand = new AsyncRelayCommand(ExecuteAsync, CanRun);
        MoveUpCommand = new RelayCommand(MoveUp, () => CanMove(-1));
        MoveDownCommand = new RelayCommand(MoveDown, () => CanMove(1));
    }

    public ObservableCollection<RebaseStepViewModel> Steps { get; } = [];

    /// <summary>Üzerine yeniden oynatılacak dal.</summary>
    public string Upstream
    {
        get => _upstream;
        set
        {
            if (SetProperty(ref _upstream, value))
            {
                OnPropertyChanged(nameof(CommandLine));
                RunCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsInteractive
    {
        get => _isInteractive;
        set
        {
            if (SetProperty(ref _isInteractive, value))
            {
                OnPropertyChanged(nameof(CommandLine));
                OnPropertyChanged(nameof(ValidationError));
                RunCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary><c>--autostash</c>: kirli ağacı kenara koy.</summary>
    public bool AutoStash
    {
        get => _autoStash;
        set
        {
            if (SetProperty(ref _autoStash, value))
            {
                OnPropertyChanged(nameof(CommandLine));
            }
        }
    }

    public RebaseStepViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                MoveUpCommand.NotifyCanExecuteChanged();
                MoveDownCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Todo listesi git tarafından kabul edilir mi?
    /// </summary>
    /// <remarks>
    /// 🔴 ÖLÇÜLDÜ: boş todo <c>error: nothing to do</c> ile rc=1 veriyor ve rebase hiç
    /// başlamıyor; ilk adım <c>squash</c> olamıyor. Kullanıcıya <b>önceden</b> söylemek,
    /// "hiçbir şey olmadı" şaşkınlığından iyi.
    /// </remarks>
    public string? ValidationError => IsInteractive
        ? RebaseTodo.Validate([.. Steps.Select(step => step.ToStep())])
        : null;

    /// <summary>Çalıştırılacak komut ("komutu göster" ilkesi).</summary>
    public string CommandLine => RebaseWriter.Describe(BuildOptions());

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

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    /// <summary>Taşınacak commit'leri okuyup listeyi doldurur.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Steps.Clear();

        if (Upstream.Length == 0)
        {
            return;
        }

        IReadOnlyList<RebaseStep> steps = await _rebase
            .ReadStepsAsync(_workingDirectory, Upstream, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        foreach (RebaseStep step in steps)
        {
            Steps.Add(new RebaseStepViewModel { Step = step, Action = RebaseAction.Pick });
        }

        OnPropertyChanged(nameof(ValidationError));
        RunCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Sürükle-bırak ya da klavye ile bir adımı taşır.</summary>
    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Steps.Count
            || toIndex < 0 || toIndex >= Steps.Count
            || fromIndex == toIndex)
        {
            return;
        }

        Steps.Move(fromIndex, toIndex);
        OnPropertyChanged(nameof(ValidationError));
        RunCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanMove(int delta)
    {
        if (Selected is null)
        {
            return false;
        }

        int index = Steps.IndexOf(Selected) + delta;
        return index >= 0 && index < Steps.Count;
    }

    private void MoveUp() => MoveSelected(-1);

    private void MoveDown() => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        if (Selected is not { } selected)
        {
            return;
        }

        int index = Steps.IndexOf(selected);
        Move(index, index + delta);
        Selected = selected;
    }

    private RebaseOptions BuildOptions() => new()
    {
        Upstream = Upstream.Length > 0 ? Upstream : "HEAD",
        AutoStash = AutoStash,
        Steps = IsInteractive ? [.. Steps.Select(step => step.ToStep())] : null,
        NewMessage = IsInteractive
            ? Steps.FirstOrDefault(step => step.NeedsMessage)?.NewMessage
            : null,
    };

    private bool CanRun() => !IsBusy && Upstream.Trim().Length > 0 && ValidationError is null;

    private async Task ExecuteAsync()
    {
        IsBusy = true;
        Error = null;

        try
        {
            RebaseResult result = await _rebase
                .RebaseAsync(_workingDirectory, BuildOptions())
                .ConfigureAwait(true);

            HasConflicts = result.Outcome == RebaseOutcome.Conflicted;

            Result = result.Outcome switch
            {
                RebaseOutcome.Conflicted =>
                    $"Stopped at step {result.CurrentStep} of {result.TotalSteps}: "
                    + $"{result.ConflictedPaths.Count} file(s) conflicted.",

                // Çakışma yok ama yine de durduk — `edit` adımı.
                RebaseOutcome.StoppedForEdit =>
                    $"Stopped at step {result.CurrentStep} of {result.TotalSteps} so you can "
                    + "amend this commit. Continue when you are done.",

                RebaseOutcome.AlreadyUpToDate => "Already up to date; nothing to replay.",

                _ => $"Rebase finished. To undo: {result.SafetyPoint.RecoveryCommand}",
            };

            IsCompleted = true;
        }
        catch (GitException error)
        {
            Error = Loc.GitError(error);
        }
        catch (ArgumentException error)
        {
            // Todo doğrulaması yazıcı tarafında da yapılıyor; buraya düşerse ekranda
            // gösteriliyor.
            Error = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Rebase ekranını gösteren taraf (P07-T09, P07-T10).</summary>
public interface IRebasePrompt
{
    Task ShowAsync(RebaseViewModel model);
}
