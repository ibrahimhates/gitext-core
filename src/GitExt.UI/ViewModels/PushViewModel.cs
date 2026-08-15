using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>Push ekranındaki sekme (P06-T08).</summary>
/// <remarks>
/// Sıra GitExtensions <c>FormPush</c>'un <c>TabControlTagBranch</c>'inden (§ 9):
/// <i>Push branches · Push tags · Push multiple branches</i>.
/// </remarks>
public enum PushTab
{
    /// <summary>Tek bir dal.</summary>
    Branch,

    /// <summary>Etiketler.</summary>
    Tag,

    /// <summary>Birden çok dal — <b>uzak dal silme</b> de burada.</summary>
    MultipleBranches,
}

/// <summary>
/// "Birden çok dal" sekmesindeki bir satır (P06-T08).
/// </summary>
/// <remarks>
/// Sütunlar GitExtensions'ın <c>BranchGrid</c>'inden: <i>Local Branch · Remote Branch ·
/// Ahead/Behind · Push · Force · Delete Remote Branch</i>.
/// </remarks>
public sealed class PushBranchRowViewModel : ViewModelBase
{
    private bool _push;
    private bool _delete;

    public required string LocalBranch { get; init; }

    public required string RemoteBranch { get; init; }

    /// <summary>Upstream'e göre konum; upstream yoksa boş.</summary>
    public required string AheadBehind { get; init; }

    /// <summary>Uzakta bu dal var mı? Yoksa silinemez.</summary>
    public required bool ExistsOnRemote { get; init; }

    /// <summary>Kira çıpası — uzak izleme ref'inin ekran açılırkenki ucu.</summary>
    public string? RemoteTipObjectId { get; init; }

    public bool Push
    {
        get => _push;
        set
        {
            if (SetProperty(ref _push, value) && value)
            {
                Delete = false;
            }
        }
    }

    /// <summary>
    /// Uzaktaki dalı sil (<c>--delete</c>).
    /// </summary>
    /// <remarks>
    /// Gönderme ile aynı anda işaretlenemez: git tek refspec için ikisini birden kabul
    /// etmiyor ve kullanıcının niyeti de belirsiz kalırdı.
    /// </remarks>
    public bool Delete
    {
        get => _delete;
        set
        {
            if (SetProperty(ref _delete, value) && value)
            {
                Push = false;
            }
        }
    }

    public bool CanDelete => ExistsOnRemote;
}

/// <summary>
/// Push ekranı (P06-T08).
/// </summary>
/// <remarks>
/// <para>
/// 🔑 <b>Çıplak <c>--force</c> yok.</b> Plan kararı: başkasının commit'lerini sessizce
/// siler. Ekranda GitExtensions'taki gibi bir <i>Force push</i> kutusu <b>duruyor</b> ama
/// devre dışı ve nedeni yazılı — yerini boş bırakmak kullanıcıya "bu program zorlayamıyor"
/// dedirtirdi, oysa <see cref="ForceWithLease"/> tam da onun güvenli hâli.
/// </para>
/// <para>
/// 🔴 <b>Kira çıpası ekran açılırken donuyor.</b> <see cref="LeaseNotice"/> kullanıcıya
/// hangi uca göre karar verileceğini söylüyor. Gerekçe:
/// <see cref="PushOptions.ForceWithLease"/> — çıplak <c>--force-with-lease</c> araya giren
/// bir fetch'ten sonra korumayı bırakıyor ve bu projede fetch kullanıcı istemeden de
/// olabiliyor (otomatik tazeleme).
/// </para>
/// </remarks>
public sealed class PushViewModel : ViewModelBase
{
    private readonly IRemoteReader _remotes;
    private readonly IPushWriter _push;
    private readonly IAuthenticationDiagnostics? _diagnostics;
    private readonly IAuthenticationPrompt? _authentication;

    private string _workingDirectory = string.Empty;
    private string? _selectedRemote;
    private string _currentBranch = string.Empty;
    private PushTab _tab;
    private string? _sourceBranch;
    private string _remoteBranch = string.Empty;
    private string? _selectedTag;
    private bool _allTags;
    private bool _setUpstream;
    private bool _forceWithLease;
    private bool _showOptions;
    private bool _isBusy;
    private string? _notice;
    private string? _warning;
    private string? _advice;
    private PushPlan? _plan;
    private IReadOnlyList<BranchInfo> _localBranches = [];
    private string? _progressText;
    private double? _progressPercent;
    private CancellationTokenSource? _cancellation;

    public PushViewModel(
        IRemoteReader remotes,
        IPushWriter push,
        IAuthenticationDiagnostics? diagnostics = null,
        IAuthenticationPrompt? authentication = null)
    {
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(push);

        _remotes = remotes;
        _push = push;
        _diagnostics = diagnostics;
        _authentication = authentication;

        RunCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
        CancelCommand = new RelayCommand(Cancel, () => CanCancel);
    }

    /// <summary>Yapılandırılmış uzak depolar.</summary>
    public ObservableCollection<string> Remotes { get; } = [];

    /// <summary>Yerel dallar (kaynak seçimi).</summary>
    public ObservableCollection<string> SourceBranches { get; } = [];

    /// <summary>Gönderilebilecek etiketler.</summary>
    public ObservableCollection<string> Tags { get; } = [];

    /// <summary>"Birden çok dal" sekmesinin satırları.</summary>
    public ObservableCollection<PushBranchRowViewModel> Rows { get; } = [];

    public string? SelectedRemote
    {
        get => _selectedRemote;
        set
        {
            if (SetProperty(ref _selectedRemote, value))
            {
                ReloadPlan();
            }
        }
    }

    public string CurrentBranch
    {
        get => _currentBranch;
        private set => SetProperty(ref _currentBranch, value);
    }

    public PushTab Tab
    {
        get => _tab;
        set
        {
            if (SetProperty(ref _tab, value))
            {
                OnPropertyChanged(nameof(IsBranchTab));
                OnPropertyChanged(nameof(IsTagTab));
                OnPropertyChanged(nameof(IsMultipleTab));
                RaisePreview();
            }
        }
    }

    public bool IsBranchTab
    {
        get => Tab == PushTab.Branch;
        set { if (value) { Tab = PushTab.Branch; } }
    }

    public bool IsTagTab
    {
        get => Tab == PushTab.Tag;
        set { if (value) { Tab = PushTab.Tag; } }
    }

    public bool IsMultipleTab
    {
        get => Tab == PushTab.MultipleBranches;
        set { if (value) { Tab = PushTab.MultipleBranches; } }
    }

    /// <summary>Gönderilecek yerel dal.</summary>
    public string? SourceBranch
    {
        get => _sourceBranch;
        set
        {
            if (SetProperty(ref _sourceBranch, value))
            {
                if (value is { Length: > 0 })
                {
                    // Hedef adı kaynakla birlikte gidiyor; kullanıcı isterse değiştirir.
                    RemoteBranch = value;
                }

                ReloadPlan();
            }
        }
    }

    /// <summary>Uzaktaki hedef dal adı (GitExtensions'ta da serbest metin).</summary>
    public string RemoteBranch
    {
        get => _remoteBranch;
        set
        {
            if (SetProperty(ref _remoteBranch, value))
            {
                OnPropertyChanged(nameof(LeaseNotice));
                OnPropertyChanged(nameof(HasLeaseNotice));
                RaisePreview();
            }
        }
    }

    public string? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (SetProperty(ref _selectedTag, value))
            {
                RaisePreview();
            }
        }
    }

    /// <summary><c>--tags</c>: tek etiket yerine hepsi.</summary>
    public bool AllTags
    {
        get => _allTags;
        set
        {
            if (SetProperty(ref _allTags, value))
            {
                RaisePreview();
            }
        }
    }

    /// <summary>
    /// <c>--set-upstream</c> (GitExtensions'ta <i>"Replace tracking reference"</i>).
    /// </summary>
    public bool SetUpstream
    {
        get => _setUpstream;
        set
        {
            if (SetProperty(ref _setUpstream, value))
            {
                RaisePreview();
            }
        }
    }

    public bool ForceWithLease
    {
        get => _forceWithLease;
        set
        {
            if (SetProperty(ref _forceWithLease, value))
            {
                OnPropertyChanged(nameof(LeaseNotice));
                OnPropertyChanged(nameof(HasLeaseNotice));
                RaisePreview();
            }
        }
    }

    /// <summary>GitExtensions'taki <i>"Show options"</i> bağlantısı.</summary>
    public bool ShowOptions
    {
        get => _showOptions;
        set => SetProperty(ref _showOptions, value);
    }

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

    /// <summary>Red durumunda ne yapılacağını söyleyen satır.</summary>
    /// <remarks>
    /// GitExtensions reddi <b>insan-okunur çıktıya düzenli ifade uygulayarak</b> tespit
    /// ediyor (<c>FormPush.cs</c>). Bizde sebep porcelain satırından geliyor; öneri de
    /// sebebe göre değişiyor, "push başarısız" demekle yetinmiyoruz.
    /// </remarks>
    public string? Advice
    {
        get => _advice;
        private set
        {
            if (SetProperty(ref _advice, value))
            {
                OnPropertyChanged(nameof(HasAdvice));
            }
        }
    }

    public bool HasAdvice => !string.IsNullOrEmpty(Advice);

    /// <summary>Uzakta yeni bir dal oluşacak mı?</summary>
    public bool WouldCreateRemoteBranch =>
        _plan is not null && !_plan.RemoteBranches.Contains(RemoteBranch, StringComparer.Ordinal);

    /// <summary>Kiranın hangi uca göre karar vereceği.</summary>
    public string? LeaseNotice
    {
        get
        {
            if (!ForceWithLease)
            {
                return null;
            }

            string? anchor = LeaseAnchor;

            return anchor is null
                ? "This branch does not exist on the remote; there is nothing to force."
                : $"The decision is based on the remote tip as you see it right now: "
                  + $"{anchor[..Math.Min(10, anchor.Length)]}. If someone else "
                  + "pushes something, the push is rejected.";
        }
    }

    public bool HasLeaseNotice => LeaseNotice is not null;

    /// <summary>
    /// Çıplak <c>--force</c> neden yok?
    /// </summary>
    public static string ForceDisabledReason =>
        "A bare force is not offered: it deletes remote commits without checking. "
        + "\"Force with lease\" does the same thing but stops if someone else got in between.";

    /// <summary>Çalıştırılacak komut ("komutu göster" ilkesi).</summary>
    public string CommandPreview =>
        SelectedRemote is not { Length: > 0 } ? string.Empty : PushWriter.Describe(BuildOptions());

    public bool CanRun => !IsBusy
        && SelectedRemote is { Length: > 0 }
        && Tab switch
        {
            PushTab.Branch => SourceBranch is { Length: > 0 } && RemoteBranch.Length > 0,
            PushTab.Tag => AllTags || SelectedTag is { Length: > 0 },
            _ => Rows.Any(row => row.Push || row.Delete),
        };

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
        IReadOnlyList<BranchInfo> localBranches,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(localBranches);

        _workingDirectory = workingDirectory;
        _localBranches = localBranches;
        CurrentBranch = currentBranch;

        Remotes.Clear();

        foreach (GitRemote remote in await _remotes
                     .ReadAllAsync(workingDirectory, cancellationToken).ConfigureAwait(true))
        {
            Remotes.Add(remote.Name);
        }

        SourceBranches.Clear();

        foreach (BranchInfo branch in localBranches)
        {
            SourceBranches.Add(branch.Name);
        }

        _selectedRemote = Remotes.FirstOrDefault(name =>
            string.Equals(name, "origin", StringComparison.Ordinal)) ?? Remotes.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedRemote));

        SourceBranch = SourceBranches.FirstOrDefault(name =>
            string.Equals(name, currentBranch, StringComparison.Ordinal))
            ?? SourceBranches.FirstOrDefault();

        await ReloadPlanAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Seçim değişince planı tazeler.
    /// </summary>
    /// <remarks>
    /// 🔑 Çıpa <b>seçili dala</b> ait olmak zorunda. Tazelenmezse
    /// <see cref="LeaseAnchor"/> güvenli tarafa düşer (çıpa yok → kira bayrağı yazılmaz →
    /// zorlama denenmez), ama kullanıcı işaretlediği kutunun çalışmadığını göremezdi.
    /// </remarks>
    private void ReloadPlan() => _ = ReloadPlanSafeAsync();

    private async Task ReloadPlanSafeAsync()
    {
        try
        {
            await ReloadPlanAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Warning = Loc.GitError(error);
        }
    }

    /// <summary>Kira çıpasını ve satırları tazeler.</summary>
    private async Task ReloadPlanAsync(CancellationToken cancellationToken)
    {
        if (SelectedRemote is not { Length: > 0 } remote || SourceBranch is not { Length: > 0 } branch)
        {
            return;
        }

        _plan = await _push
            .PlanAsync(_workingDirectory, remote, branch, cancellationToken)
            .ConfigureAwait(true);

        // Kullanıcının en sık istediği şey: upstream'i olmayan bir dal gönderiliyorsa
        // kutusu kendiliğinden işaretli gelsin (GitExtensions da böyle yapıyor). Aksi
        // hâlde çıplak `git push` bir daha çalışmazdı — ölçüldü, çıkış kodu 128.
        SetUpstream = !_plan.HasUpstream;

        Tags.Clear();

        foreach (string tag in _plan.Tags)
        {
            Tags.Add(tag);
        }

        SelectedTag = Tags.FirstOrDefault();

        BuildRows();

        OnPropertyChanged(nameof(LeaseNotice));
        OnPropertyChanged(nameof(HasLeaseNotice));
        OnPropertyChanged(nameof(WouldCreateRemoteBranch));
        RaisePreview();
    }

    private void BuildRows()
    {
        Rows.Clear();

        if (_plan is null)
        {
            return;
        }

        foreach (BranchInfo branch in _localBranches)
        {
            bool exists = _plan.RemoteBranches.Contains(branch.Name, StringComparer.Ordinal);

            Rows.Add(new PushBranchRowViewModel
            {
                LocalBranch = branch.Name,
                RemoteBranch = branch.Upstream is { Length: > 0 } upstream
                    ? upstream
                    : $"{_plan.Remote}/{branch.Name}",
                AheadBehind = DescribeTracking(branch.Tracking, branch.Upstream),
                ExistsOnRemote = exists,
            });
        }
    }

    private static string DescribeTracking(UpstreamTracking tracking, string? upstream)
    {
        if (upstream is not { Length: > 0 })
        {
            return "takip yok";
        }

        if (tracking.IsGone)
        {
            return "upstream was deleted";
        }

        return tracking.IsUpToDate ? "up to date" : $"↑{tracking.Ahead} ↓{tracking.Behind}";
    }

    /// <summary>Kira çıpası: seçili hedefin uzak izleme ref'inin donmuş ucu.</summary>
    private string? LeaseAnchor =>
        _plan is not null && string.Equals(_plan.RemoteBranch, RemoteBranch, StringComparison.Ordinal)
            ? _plan.RemoteTipObjectId
            : null;

    private PushOptions BuildOptions()
    {
        List<PushSpec> refs = [];
        PushTagMode tags = PushTagMode.None;

        switch (Tab)
        {
            case PushTab.Branch:
                if (SourceBranch is { Length: > 0 } source && RemoteBranch.Length > 0)
                {
                    refs.Add(new PushSpec(source, RemoteBranch)
                    {
                        ExpectedRemoteObjectId = LeaseAnchor,
                    });
                }

                break;

            case PushTab.Tag:
                if (AllTags)
                {
                    tags = PushTagMode.All;
                }
                else if (SelectedTag is { Length: > 0 } tag)
                {
                    refs.Add(new PushSpec($"refs/tags/{tag}", $"refs/tags/{tag}"));
                }

                break;

            case PushTab.MultipleBranches:
            default:
                foreach (PushBranchRowViewModel row in Rows)
                {
                    if (row.Delete)
                    {
                        refs.Add(new PushSpec(string.Empty, row.LocalBranch, Delete: true));
                    }
                    else if (row.Push)
                    {
                        refs.Add(new PushSpec(row.LocalBranch, row.LocalBranch));
                    }
                }

                break;
        }

        return new PushOptions
        {
            Remote = SelectedRemote ?? string.Empty,
            Refs = refs,
            SetUpstream = SetUpstream && Tab == PushTab.Branch,
            ForceWithLease = ForceWithLease && Tab == PushTab.Branch,
            Tags = tags,
        };
    }

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(CommandPreview));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(WouldCreateRemoteBranch));
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
        Advice = null;
        ProgressText = null;
        ProgressPercent = null;

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        try
        {
            PushResult result = await _push
                .PushAsync(
                    _workingDirectory,
                    BuildOptions() with { Progress = CreateProgress() },
                    _cancellation.Token)
                .ConfigureAwait(true);

            Report(result);
        }
        catch (OperationCanceledException)
        {
            Notice = "The operation was cancelled.";
        }
        catch (GitException error) when (error.Kind == GitFailureKind.AuthenticationRequired)
        {
            await HandleAuthenticationAsync(error).ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Warning = Loc.GitError(error);
        }
        finally
        {
            IsBusy = false;
            ProgressText = null;
            ProgressPercent = null;
        }
    }

    /// <summary>
    /// Kimlik doğrulama hatasını ele alır (P06-T09).
    /// </summary>
    /// <remarks>
    /// 🔑 Ham <c>stderr</c> gösterilmiyor: aynı satır hem eksik SSH anahtarında hem
    /// çözülemeyen sunucu adında yazılıyor (ölçüldü). Teşhis <b>ortama</b> bakıyor ve
    /// yalnızca HTTPS'te kimlik sorup tekrar deniyor — SSH'ta istenen şey bir anahtar,
    /// diyalogla çözülmez.
    /// </remarks>
    private async Task HandleAuthenticationAsync(GitException error)
    {
        if (_diagnostics is null || _authentication is null)
        {
            Warning = Loc.GitError(error);
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
            Advice = diagnosis.Suggestions.Count > 0
                ? "Deneyebilecekleriniz: " + string.Join(" · ", diagnosis.Suggestions)
                : null;

            return;
        }

        try
        {
            PushResult result = await _push
                .PushAsync(_workingDirectory, BuildOptions() with { Credentials = credentials })
                .ConfigureAwait(true);

            Report(result);
        }
        catch (GitException retry)
        {
            Warning = Loc.GitError(retry);
        }
    }

    private void Report(PushResult result)
    {
        Notice = Summarize(result);

        if (result.Rejected.Count == 0)
        {
            return;
        }

        // 🔴 Kısmi başarıda hem gideni hem reddedileni söylüyoruz: "push başarısız" demek
        // kullanıcıyı gitmiş bir gönderimi tekrarlamaya iterdi (ölçüldü — çıkış kodu 1
        // olsa da diğer dal gerçekten gitmişti).
        Warning = (result.IsPartial
                ? "Some were pushed, some were rejected: "
                : "The push was rejected: ")
            + string.Join(", ", result.Rejected.Select(row => row.ShortDestination));

        Advice = DescribeAdvice(result);
    }

    private static string DescribeAdvice(PushResult result)
    {
        PushRejectionKind kind = result.Rejected[0].Rejection ?? PushRejectionKind.Unknown;

        string advice = kind switch
        {
            PushRejectionKind.Behind =>
                "The remote has commits you do not have. Fetch or pull them first; "
                + "if you rewrote history deliberately, use the \"Force with lease\" option.",
            PushRejectionKind.StaleLease =>
                "The remote branch changed since you opened this screen — that is exactly what the protection is for. "
                + "Fetch, see what changed, then try again.",
            PushRejectionKind.RemoteRejected =>
                "The remote rejected it (it may be a protected branch or a permissions issue).",
            _ => "The reason for the rejection was not recognised; see git's output.",
        };

        return result.RemoteMessages.Count > 0
            ? advice + " Uzak depo diyor ki: " + string.Join(" · ", result.RemoteMessages)
            : advice;
    }

    private static string Summarize(PushResult result)
    {
        if (result.Refs.Count == 0)
        {
            return "No changes.";
        }

        List<string> parts = [];

        foreach (IGrouping<PushRefStatus, PushRefResult> group in result.Refs.GroupBy(row => row.Status))
        {
            string label = group.Key switch
            {
                PushRefStatus.Created => "yeni",
                PushRefStatus.FastForward => "updated",
                PushRefStatus.Forced => "force-updated",
                PushRefStatus.Deleted => "silindi",
                PushRefStatus.UpToDate => "already up to date",
                _ => "reddedildi",
            };

            parts.Add($"{group.Count()} {label} ({string.Join(", ", group.Select(row => row.ShortDestination))})");
        }

        return (result.DryRun ? "Deneme — " : string.Empty) + string.Join(" · ", parts);
    }
}
