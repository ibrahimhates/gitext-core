using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Merge ekranı (P06-T11).
/// </summary>
/// <remarks>
/// <para>
/// Yerleşim GitExtensions <c>FormMergeBranch</c>'ten (§ 9): <i>Merge branch</i> seçimi →
/// <i>Into current branch</i> → <i>fast forward</i> / <i>always create a merge commit</i>
/// → <i>Do not commit</i> → <i>Show advanced options</i> (squash · allow unrelated
/// histories · merge message) → <i>Merge</i>.
/// </para>
/// <para>
/// 🔴 <b>"Squash" seçildiğinde sonuç ekranı bunu açıkça söylüyor:</b> git çıkış kodu 0
/// verip <c>HEAD</c>'i ilerletmiyor (ölçüldü). "Birleştirildi" deyip geçmek, kullanıcının
/// commit'lemeyi unutup dalı silmesi demekti.
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

    /// <summary>Birleştirilebilecek dallar ve etiketler.</summary>
    public ObservableCollection<string> Sources { get; } = [];

    /// <summary>Üzerinde bulunulan dal (salt okunur).</summary>
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
    /// Squash seçiliyken <b>önceden</b> gösterilen uyarı.
    /// </summary>
    /// <remarks>
    /// Sonradan söylemek de gerekiyor ama önceden söylemek daha iyi: kullanıcı hangi işe
    /// giriştiğini bilerek başlasın.
    /// </remarks>
    public string? SquashNotice => Strategy != MergeStrategy.Squash
        ? null
        : "Squash bir commit OLUŞTURMAZ: değişiklikler hazırlanır ve commit'lemek size kalır.";

    public bool HasSquashNotice => SquashNotice is not null;

    /// <summary>Seçilen dalın ne getireceği.</summary>
    public string? PreviewNotice => _preview is not { } preview
        ? null
        : !preview.HasCommonAncestor
            ? "Bu dalın ortak atası yok (ilgisiz geçmiş). Gelişmiş seçeneklerden izin vermeniz gerekir."
            : !preview.HasChanges
                ? "Bu dalda getirilecek bir şey yok."
                : preview.CanFastForward
                    ? $"{preview.Ahead} commit ileri sarılabilir."
                    : $"{preview.Ahead} commit birleştirilecek (ileri sarılamaz).";

    public bool HasPreviewNotice => PreviewNotice is not null;

    /// <summary>Çalıştırılacak komut ("komutu göster" ilkesi).</summary>
    public string CommandPreview => Source is not { Length: > 0 }
        ? string.Empty
        : MergeWriter.Describe(BuildOptions());

    public bool CanRun => !IsBusy && Source is { Length: > 0 };

    public IAsyncRelayCommand RunCommand { get; }

    /// <summary>Ekranı doldurur.</summary>
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
            // Kendini kendine birleştirmek anlamsız; listede olmaması seçim hatasını
            // baştan engelliyor.
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
            Warning = error.Message;
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
            Warning = error.Message;
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
                Notice = "Zaten güncel.";
                break;

            case MergeOutcome.FastForward:
                Notice = "İleri sarıldı; yeni bir commit oluşmadı.";
                break;

            case MergeOutcome.MergeCommit:
                Notice = "Birleştirme commit'i oluşturuldu.";
                break;

            case MergeOutcome.Staged:
                // 🔴 Ölçümün kalbi: çıkış kodu 0 ama HEAD yerinde.
                Notice = "Değişiklikler hazırlandı.";
                Warning = "Henüz commit YAPILMADI. Değişiklikler hazırlanmış durumda; "
                    + "commit ekranından tamamlamanız gerekiyor.";
                break;

            case MergeOutcome.Conflicted:
            default:
                Notice = null;
                Warning = "Birleştirme çakışmayla durdu — "
                    + $"{result.ConflictedPaths.Count} dosya ÇÖZÜLMEMİŞ durumda: "
                    + string.Join(", ", result.ConflictedPaths.Take(4))
                    + (result.ConflictedPaths.Count > 4 ? "…" : string.Empty)
                    + ". Çakışmaları çözüp commit'leyin ya da birleştirmeyi iptal edin.";
                break;
        }
    }
}

/// <summary>Merge ekranını gösteren taraf (P06-T11).</summary>
public interface IMergePrompt
{
    /// <summary>Ekranı modal gösterir ve kapanmasını bekler.</summary>
    Task ShowAsync(MergeViewModel model);
}

/// <summary>Süren merge'in iptalini onaylayan taraf (P06-T12).</summary>
public interface IMergeAbortConfirmer
{
    /// <summary>
    /// İptali onaylatır.
    /// </summary>
    /// <param name="conflicted">Çözülmemiş dosyalar — kullanıcı neyi kaybedeceğini görsün.</param>
    Task<bool> ConfirmAsync(IReadOnlyList<string> conflicted);
}
