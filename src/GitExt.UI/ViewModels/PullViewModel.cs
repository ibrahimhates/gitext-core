using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>Ekrandaki eylem seçimi (P06-T07).</summary>
/// <remarks>
/// Sıra GitExtensions <c>FormPull</c>'un <c>GroupMergeOptions</c>'ından (§ 9):
/// <i>Merge · Rebase · Do not merge, only fetch</i>. Fetch'in ayrı bir ekranı yok —
/// bu yüzden P06-T06'nın arayüzü de burada.
/// </remarks>
public enum PullAction
{
    /// <summary>Uzak dalı mevcut dala birleştir.</summary>
    Merge,

    /// <summary>Yerel commit'leri uzak dalın üstüne taşı.</summary>
    Rebase,

    /// <summary>Yalnızca fetch: çalışma ağacına dokunma.</summary>
    FetchOnly,
}

/// <summary>
/// Pull / Fetch ekranı (P06-T06 + P06-T07).
/// </summary>
/// <remarks>
/// 🔑 <b>Ne çalışacağı her zaman ekranda.</b> Planın maddesi ("pull düğmesinin ne yaptığı
/// belirsiz kalmamalı") ve README'nin "komutu göster" ilkesi: hem çalıştırılacak komut
/// (<see cref="CommandPreview"/>) hem de stratejinin <b>nereden geldiği</b>
/// (<see cref="StrategyNotice"/>) yazılı.
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

    /// <summary>Yapılandırılmış uzak depolar.</summary>
    public ObservableCollection<string> Remotes { get; } = [];

    /// <summary>Seçili remote'un uzak dalları.</summary>
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

    /// <summary>Üzerinde bulunulan yerel dal (salt okunur gösterilir).</summary>
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

    // XAML'de radyo düğmeleri için: bileşik koşullar ViewModel'de tutuluyor (Faz 03'te
    // bağlamada hesaplama yapmanın sessizce yanlış davrandığı ölçülmüştü).
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

    /// <summary>Etiket seçimi (<c>GroupTagOptions</c>).</summary>
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
                    // GitExtensions'ta da `PruneTags` yalnızca `Prune` işaretliyken etkin;
                    // git zaten `--prune-tags`i tek başına kabul etmiyor (ölçüldü).
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
    /// Yalnızca fetch seçiliyken anlamsız: fetch çalışma ağacına dokunmuyor.
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

    /// <summary>İşlemin sonucu.</summary>
    public string? Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    /// <summary>Dikkat çekilmesi gereken durum (çakışma, kısmi başarı).</summary>
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

    /// <summary>Yapılanı geri alan komut; yalnızca <c>HEAD</c> ilerlediyse dolu.</summary>
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
    /// Kullanıcının ayarlarının ne söylediği — <b>seçim değişse bile</b> görünür kalır.
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
            _ => "No preference in your settings; merge was chosen as the default.",
        };

    /// <summary>
    /// Çalıştırılacak komut.
    /// </summary>
    /// <remarks>
    /// "Komutu göster" ilkesi: kullanıcı düğmeye basmadan <b>ne olacağını</b> okuyabilmeli.
    /// Metin gerçek argümanlardan üretiliyor, elle yazılmıyor — yoksa zamanla koddan
    /// sapardı.
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

    /// <summary>Canlı ilerleme metni; işlem yokken boş.</summary>
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

    /// <summary>Yüzde; git yüzde vermiyorsa <see langword="null"/> (belirsiz çubuk).</summary>
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
    /// Çalışan işlemi iptal eder (P06-T10).
    /// </summary>
    /// <remarks>
    /// 🔑 Süreç <b>gerçekten öldürülüyor</b> (<c>Kill(entireProcessTree: true)</c>) —
    /// yalnızca beklemeyi bırakmak, arkada çalışmaya devam eden bir git bırakırdı.
    /// Ölçüldü: yarıda kesilen bir fetch geride kilit bırakmıyor ve <c>fsck</c> temiz.
    /// </remarks>
    public IRelayCommand CancelCommand { get; }

    public bool CanCancel => IsBusy;

    private void Cancel() => _cancellation?.Cancel();

    private IProgress<GitProgress> CreateProgress() => new Progress<GitProgress>(step =>
    {
        ProgressText = step.Describe();
        ProgressPercent = step.Percent;
    });


    /// <summary>Ekranı bir depo için doldurur.</summary>
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

        // Ekran, kullanıcının ayarının söylediği seçenekle AÇILIYOR — başka bir seçenekle
        // açmak, ayarını bilen kullanıcıya sessizce farklı bir şey yaptırırdı.
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

        // Mevcut dalın adıyla aynı olan uzak dal en olası hedef.
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
        PullStrategy.FastForwardOnly => "fast-forward only",
        _ => "merge",
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
            // İptal bir hata değil; kullanıcı zaten ne olduğunu biliyor.
            Notice = "The operation was cancelled.";
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
    /// Kimlik doğrulama hatasını ele alır (P06-T09).
    /// </summary>
    /// <remarks>Gerekçe ve sınırları: <see cref="PushViewModel"/> üzerindeki not.</remarks>
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
            // 🔴 Kısmi başarı: bazı remote'lar güncellendi, bazıları güncellenmedi.
            Warning = "These remotes could not be fetched: "
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
            ? "Already up to date."
            : DescribeChanges(result.Changes);

        if (!result.AlreadyUpToDate)
        {
            RecoveryCommand = result.RecoveryCommand;
        }

        // 🔴 Çıkış kodu 0 olsa bile çakışma olabiliyor (ölçüldü); sessiz kalmak
        // "başarıyla güncellendi" demek olurdu.
        if (result.AutoStashConflict)
        {
            Warning = "The fetch succeeded, but your uncommitted changes "
                + "conflicted while being restored. Your changes are NOT lost: `git stash list` "
                + "is still there. Resolve the conflicting files and drop the stash.";
        }
        else if (result.HasConflicts)
        {
            Warning = "The merge stopped with conflicts: some files are UNRESOLVED. "
                + "Resolve the conflicts and commit, or abort the operation.";
        }
    }

    private static string DescribeChanges(IReadOnlyList<RefChange> changes)
    {
        if (changes.Count == 0)
        {
            return "No changes.";
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
            // Budama yıkıcı: sayıyı yutmak kullanıcının haberi olmadan ref kaybetmesi olurdu.
            parts.Add($"{deleted} removed");
        }

        return string.Join(" · ", parts) + $" ({string.Join(", ", changes.Take(4).Select(c => c.ShortName))}"
            + (changes.Count > 4 ? "…)" : ")");
    }
}
