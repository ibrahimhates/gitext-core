using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Reset diyaloğu (P07-T06).
/// </summary>
/// <remarks>
/// <para>
/// Plan açıkça istiyor: <i>"Her modun ne yapacağını açıkça anlatan bir diyalog: hangi
/// commit'ler kaybolacak, çalışma dizinine ne olacak."</i> Bu yüzden mod seçimi bir açılır
/// liste değil, her biri <b>ne yaptığını anlatan</b> üç seçenek.
/// </para>
/// <para>
/// Yerleşim GitExtensions <c>FormResetCurrentBranch</c>'ten (§ 9): radyo düğmeleri,
/// altında açıklama, en altta onay.
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

    /// <summary>Dönülecek commit.</summary>
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

    // Radyo düğmeleri iki yönlü bağlanabilsin diye.
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

    /// <summary>Seçili modun ne yapacağı — ölçülmüş davranışın düz anlatımı.</summary>
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

    /// <summary>Kaç commit düşecek ve hangileri.</summary>
    public string DroppedText => Preview is null || Preview.DroppedCount == 0
        ? "No commits will be dropped."
        : $"{Preview.DroppedCount} commit(s) will be dropped: "
          + string.Join(", ", Preview.DroppedCommits.Take(5))
          + (Preview.DroppedCount > 5 ? "…" : string.Empty);

    /// <summary>
    /// Bu seçim <b>geri alınamayacak</b> bir kayba yol açar mı?
    /// </summary>
    /// <remarks>
    /// Düşen commit'ler reflog'da duruyor; asıl geri alınamayan şey <c>--hard</c>'ın
    /// sildiği commit'lenmemiş değişiklikler.
    /// </remarks>
    public bool LosesWork => Preview?.LosesUncommittedWork(Mode) == true;

    /// <summary>
    /// Ek onay kutusu gösterilsin mi?
    /// </summary>
    /// <remarks>
    /// P05-T15'in dersi: kurtarma komutu ekranda olduğunda onay kutusu yeterli, ayrı bir
    /// uyarı penceresi gerekmiyor. Ama <c>--hard</c> kirli bir ağaçta geri alınamayan bir
    /// kayıp üretiyor — kutu tam orada.
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

    /// <summary>Çalıştırılacak komut ("komutu göster" ilkesi).</summary>
    public string CommandLine =>
        ResetWriter.Describe(new ResetOptions { Target = Target, Mode = Mode });

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>İşlem sonrası "geri almak için…" bilgisi.</summary>
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

    /// <summary>İşlem tamamlandı mı? (Pencere bunu kapanmak için kullanıyor.)</summary>
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

            // Faz kuralı: geri alma bilgisi HER ZAMAN sunulur.
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

/// <summary>Reset diyaloğunu gösteren taraf (P07-T06).</summary>
public interface IResetPrompt
{
    Task ShowAsync(ResetViewModel model);
}
