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
/// Karşılama ekranındaki son açılan depo girdisi (P03-T16).
/// </summary>
public sealed class RecentRepositoryItem
{
    public RecentRepositoryItem(string path, ICommand openCommand)
    {
        Path = path;
        OpenCommand = openCommand;

        // Klasör adı listeyi taramayı kolaylaştırır; tam yol ikinci satırda kalır.
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
    /// Klasör hâlâ duruyor mu?
    /// </summary>
    /// <remarks>
    /// Kayıp girdiler listeden <b>silinmiyor</b>, soluk gösteriliyor: bağlı olmayan bir disk
    /// veya geçici olarak erişilemeyen bir ağ yolu, kullanıcının listesini kalıcı olarak
    /// budamak için yeterli sebep değil.
    /// </remarks>
    public bool Exists => Directory.Exists(Path);

    public override string ToString() => Path;
}

/// <summary>
/// Ana pencerenin ViewModel'ı.
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
    private readonly IPerformanceDiagnostics? _diagnostics;

    /// <summary>
    /// Commit ekranı için yeni bir ViewModel üretir (P05-T09).
    /// </summary>
    /// <remarks>
    /// GitExtensions'ta bu ekran <c>FormCommit</c> ve <c>ShowDialog</c> ile <b>modal</b>
    /// açılıyor (<c>GitUICommands.StartCommitDialog</c>); yerleşim gibi açılış biçimi de
    /// takip ediliyor (CLAUDE.md § 9).
    /// <para>
    /// Pencereyi <b>açmak</b> görünümün işi; burası yalnızca ne gösterileceğini kuruyor
    /// (P04-T16'daki karşılaştırma penceresiyle aynı desen).
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
            _workingTreeWriter);
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

        // Menü komutları (P08-T26). GitExtensions'ta "Refresh" hem Dashboard hem Repository
        // menüsünde var; "Close (go to Dashboard)" Repository menüsünün son öğesi.
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

        RecentRepositories.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasRecentRepositories));

        // 🔴 Depoya bağlı HER ŞEY buradan haber alıyor. Tek yer olması şart: depo dört ayrı
        // yoldan açılıp kapanıyor (açık yol, sürükle-bırak, açılışta sessiz deneme, kapatma)
        // ve bildirimi tek tek o yollara koymak, birini unutmayı sessiz bir hata yapardı —
        // nitekim öyle olmuştu: `HasRepository` yalnızca KAPANIŞTA bildiriliyordu, açılışta
        // hiç. `_Depo` ve `_Komutlar` menüleri depo açıkken de soluk kalıyordu.
        Commits.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CommitListViewModel.IsLoading)
                or nameof(CommitListViewModel.Repository))
            {
                OnPropertyChanged(nameof(ShowWelcome));
                NotifyRepositoryDependents();
            }

            // Faz 07: reset/cherry-pick/revert SEÇİLİ commit üzerinde çalışıyor. Seçim
            // değiştiğinde bildirmezsek menü öğeleri, gerçekte kullanılabilir oldukları
            // hâlde soluk kalırdı — P06'da `HasRepository` ile yaşanan hatanın aynısı.
            if (e.PropertyName is nameof(CommitListViewModel.SelectedIndex)
                or nameof(CommitListViewModel.SelectedRow))
            {
                NotifySelectionDependents();
                RememberSelection();
            }
        };
    }

    /// <summary>
    /// Seçili commit'i oturuma yazar (P08-T16).
    /// </summary>
    /// <remarks>
    /// Her seçim değişiminde çağrılıyor ama diske her seferinde yazılmıyor: ayar deposunun
    /// kaydı gecikmeli. Ok tuşuyla listede gezinen kullanıcı saniyede onlarca seçim
    /// değiştiriyor ve her birini yazmak sürekli dosya yazmak demekti.
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

    /// <summary>Commit geçmişi listesi.</summary>
    public CommitListViewModel Commits { get; }

    /// <summary>Son açılan depolar, en yeni ilk sırada.</summary>
    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = [];

    /// <summary>
    /// Gösterilecek son açılan depo var mı?
    /// </summary>
    /// <remarks>
    /// Ayrı bir <see cref="bool"/> gerekli: <c>IsVisible</c>'a doğrudan <c>Count</c> bağlamak
    /// <b>çalışmıyor</b> — Avalonia <c>int</c>'i <c>bool</c>'a çevirmiyor ve bölüm sessizce
    /// hiç görünmüyordu (render'da yakalandı).
    /// </remarks>
    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    public ICommand OpenRecentCommand { get; }

    public ICommand CancelLoadingCommand { get; }

    /// <summary>Açık depoyu yeniden okur (P08-T26).</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Depoyu kapatıp karşılama ekranına döner (P08-T26).</summary>
    public ICommand CloseRepositoryCommand { get; }

    /// <summary>Dal oluşturma diyaloğunu açar (P06-T01).</summary>
    public IAsyncRelayCommand CreateBranchCommand { get; }

    /// <summary>
    /// Diyaloğu gösteren taraf. Görünüm kuruyor: diyalog bir sahip pencere istiyor ve o
    /// ancak açılış anında biliniyor (P05-T15'teki <see cref="IDestructiveActionConfirmer"/>
    /// ile aynı gerekçe).
    /// </summary>
    public ICreateBranchPrompt? BranchPrompt { get; set; }

    /// <summary>Dal oluşturulabilir mi? (Açık depo + yazıcı + diyalog gerekiyor.)</summary>
    public bool CanCreateBranch =>
        _branchWriter is not null
        && BranchPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Seçili commit'e / dala geçer (P06-T02).</summary>
    public IAsyncRelayCommand CheckoutCommand { get; }

    /// <summary>Dala geçme diyaloğunu gösteren taraf (P06-T02).</summary>
    public ICheckoutPrompt? CheckoutPrompt { get; set; }

    /// <summary>Geçiş yapılabilir mi?</summary>
    public bool CanCheckout =>
        _branchWriter is not null
        && CheckoutPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Seçili dalı yeniden adlandırır (P06-T03).</summary>
    public IAsyncRelayCommand RenameBranchCommand { get; }

    /// <summary>Seçili dalı siler (P06-T03).</summary>
    public IAsyncRelayCommand DeleteBranchCommand { get; }

    /// <summary>Dal düzenleme diyaloglarını gösteren taraf (P06-T03).</summary>
    public IBranchEditPrompt? BranchEditPrompt { get; set; }

    /// <summary>Uzak depo yönetimi ekranını açar (P06-T05).</summary>
    public IAsyncRelayCommand ManageRemotesCommand { get; }

    /// <summary>Uzak depo yönetimi ekranını gösteren taraf (P06-T05).</summary>
    public IRemotesPrompt? RemotesPrompt { get; set; }

    /// <summary>Pull/Fetch ekranını açar (P06-T06 + P06-T07).</summary>
    public IAsyncRelayCommand PullCommand { get; }

    /// <summary>Pull/Fetch ekranını gösteren taraf.</summary>
    public IPullPrompt? PullPrompt { get; set; }

    /// <summary>Push ekranını açar (P06-T08).</summary>
    public IAsyncRelayCommand PushCommand { get; }

    /// <summary>Push ekranını gösteren taraf.</summary>
    public IPushPrompt? PushPrompt { get; set; }

    /// <summary>Kimlik doğrulama ekranını gösteren taraf (P06-T09).</summary>
    public IAuthenticationPrompt? AuthenticationPrompt { get; set; }

    /// <summary>Dal paneli (P06-T13).</summary>
    public RefTreeViewModel RefTree { get; } = new();

    /// <summary>
    /// Panelden çift tıklamayla dala geçer (P06-T13).
    /// </summary>
    /// <remarks>
    /// Diyalog akışı <see cref="CheckoutCommand"/>'la <b>aynı</b>: kirli ağaç uyarısı ve
    /// seçenekler tek yerde. İkinci bir geçiş yolu yazmak, birinin sessizce korumasız
    /// kalması demekti (P06-T02'nin kuralı: değişiklikleri kaybettirecek hiçbir yol olmamalı).
    /// </remarks>
    public Task CheckoutRefAsync(string refName) => CheckoutCoreAsync(refName);

    /// <summary>Seçili commit'e bağlı komutların etkinliğini yeniler (P07-T06 … T08).</summary>
    private void NotifySelectionDependents()
    {
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(CanCherryPick));
        OnPropertyChanged(nameof(CanRevert));
        ResetCommand.NotifyCanExecuteChanged();
        CherryPickCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    // ===================================================== Faz 07 komutları

    /// <summary>Çakışma çözüm ekranını gösteren taraf (P07-T03).</summary>
    public IConflictPrompt? ConflictPrompt { get; set; }

    /// <summary>Stash ekranını gösteren taraf (P07-T13).</summary>
    public IStashPrompt? StashPrompt { get; set; }

    /// <summary>Reflog tarayıcısını gösteren taraf (P07-T14).</summary>
    public IReflogPrompt? ReflogPrompt { get; set; }

    /// <summary>Reset diyaloğunu gösteren taraf (P07-T06).</summary>
    public IResetPrompt? ResetPrompt { get; set; }

    /// <summary>Cherry-pick / revert diyaloğunu gösteren taraf (P07-T07, P07-T08).</summary>
    public ISequencerPrompt? SequencerPrompt { get; set; }

    /// <summary>Rebase ekranını gösteren taraf (P07-T09, P07-T10).</summary>
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
    /// Süren <b>herhangi bir</b> işlem iptal edilebilir mi? (P07-T11)
    /// </summary>
    /// <remarks>
    /// P06-T12'de bu yalnızca merge içindi; rebase/cherry-pick/revert'in iptali farklı
    /// komutlar olduğu için bilinçli olarak dışarıda bırakılmıştı. Faz 07'de
    /// <see cref="IConflictResolver"/> doğru fiili <b>durum dosyalarından</b> seçiyor,
    /// dolayısıyla artık hepsi sunulabiliyor.
    /// </remarks>
    public bool CanAbortOperation =>
        _advanced.Resolver is not null
        && RepositoryPathOrNull is not null
        && CurrentOperation is not (InProgressOperation.None or InProgressOperation.Bisect);

    /// <summary>Çakışma çözüm ekranı açılabilir mi? (P07-T03)</summary>
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
    /// Süren işlemi iptal eder (P07-T11).
    /// </summary>
    /// <remarks>
    /// 🔑 Onay şart: iptal çalışma ağacını işlem ÖNCESİNE döndürüyor, yani çakışmaları
    /// çözerken yazılan her şey gider (P06-T12'de ölçüldü). Onay ekranında çözülmemiş
    /// dosyalar listeleniyor.
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

            BranchNotice = "The operation was cancelled; the working tree is back to its previous state.";
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

    /// <summary>Çakışma çözüm ekranını açar (P07-T03, P07-T05).</summary>
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

    /// <summary>Stash ekranını açar (P07-T13).</summary>
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

    /// <summary>Reflog tarayıcısını açar (P07-T14).</summary>
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

    /// <summary>Reset diyaloğunu açar (P07-T06).</summary>
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

    /// <summary>Cherry-pick / revert diyaloğunu açar (P07-T07, P07-T08).</summary>
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

        // Çakışmayla durduysa kullanıcıyı çözüm ekranına götürüyoruz: yarım kalmış bir
        // işlemi bulup çıkış yolunu aramak zorunda bırakmak, fazın kuralına aykırı.
        if (model.HasConflicts)
        {
            await ResolveConflictsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Rebase ekranını açar (P07-T09, P07-T10).</summary>
    private async Task RebaseAsync()
    {
        if (_advanced.Rebase is not { } rebase
            || RebasePrompt is not { } prompt
            || RepositoryPathOrNull is not { } path)
        {
            return;
        }

        // Varsayılan hedef: seçili commit varsa o, yoksa mevcut dalın yukarısı boş.
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

    /// <summary>Git komut günlüğünü açar (P06-T16).</summary>
    public IAsyncRelayCommand ShowCommandLogCommand { get; }

    /// <summary>Komut günlüğü panelini gösteren taraf (P06-T16).</summary>
    public ICommandLogPrompt? CommandLogPrompt { get; set; }

    /// <summary>
    /// Günlük açılabilir mi?
    /// </summary>
    /// <remarks>
    /// Depoya bağlı DEĞİL: günlük depo açılmadan çalışan komutları da (depo arama,
    /// sürüm kontrolü) gösteriyor ve sorun tam da orada olabilir.
    /// </remarks>
    public bool CanShowCommandLog => _commandLog is not null && CommandLogPrompt is not null;

    /// <summary>Performans teşhis panelini açar (P09-T03).</summary>
    public IAsyncRelayCommand ShowDiagnosticsCommand { get; }

    /// <summary>Teşhis panelini gösteren taraf (P09-T03).</summary>
    public IDiagnosticsPrompt? DiagnosticsPrompt { get; set; }

    /// <summary>
    /// Teşhis açılabilir mi?
    /// </summary>
    /// <remarks>
    /// Günlük gibi bu da depoya bağlı DEĞİL: açılışın kendisi yavaşsa, depo açılmadan
    /// önceki komutların süresi tam da aranan bilgidir.
    /// </remarks>
    public bool CanShowDiagnostics => _diagnostics is not null && DiagnosticsPrompt is not null;

    /// <summary>Merge ekranını açar (P06-T11).</summary>
    public IAsyncRelayCommand MergeCommand { get; }

    /// <summary>Merge ekranını gösteren taraf.</summary>
    public IMergePrompt? MergePrompt { get; set; }

    /// <summary>Süren merge'i iptal eder (P06-T12).</summary>
    public IAsyncRelayCommand AbortMergeCommand { get; }

    /// <summary>Merge iptalini onaylatan taraf (P06-T12).</summary>
    public IMergeAbortConfirmer? MergeAbortConfirmer { get; set; }

    /// <summary>Sürükle-bırak birleştirmesini onaylatan taraf (P06-T15).</summary>
    public IMergeDropConfirmer? MergeDropConfirmer { get; set; }

    /// <summary>
    /// Bir dalı başka bir dalın üstüne bırakınca çağrılır (P06-T15).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔑 <b>Hedef MEVCUT dal olmak zorunda.</b> GitExtensions başka bir dalın üstüne
    /// bırakmaya da izin veriyor ama bunun için önce o dala <b>geçmesi</b> gerekiyor —
    /// yani tek bir sürüklemenin arkasında gizli ikinci bir işlem oluyor. Bu projede
    /// gizli işlem yok: hedef mevcut dal değilse birleştirme yapılmıyor ve sebebi
    /// yazılıyor.
    /// </para>
    /// <para>
    /// Onay <b>her zaman</b> soruluyor (planın maddesi) ve onay ekranında çalıştırılacak
    /// komut birebir yazılı.
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
                MergeOutcome.AlreadyUpToDate => "Already up to date.",
                MergeOutcome.FastForward => $"\"{source}\" was fast-forwarded.",
                MergeOutcome.MergeCommit => $"\"{source}\" was merged.",
                MergeOutcome.Staged => "The changes were staged but NOT committed.",
                _ => $"The merge stopped with conflicts: {result.ConflictedPaths.Count} files "
                    + "are unresolved.",
            };
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Birleştirme yapılabilir mi?</summary>
    public bool CanMerge =>
        _mergeWriter is not null
        && MergePrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>
    /// Süren bir merge iptal edilebilir mi?
    /// </summary>
    /// <remarks>
    /// Yalnızca <b>merge</b> için: rebase/cherry-pick/revert'in iptali başka komutlar ve
    /// Faz 07'nin konusu. Yanlış komutu sunmak yarım kalmış bir işi bozardı.
    /// </remarks>
    public bool CanAbortMerge =>
        _mergeWriter is not null
        && CurrentOperation == InProgressOperation.Merge
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Push yapılabilir mi?</summary>
    public bool CanPush =>
        _pushWriter is not null
        && _remoteReader is not null
        && PushPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Pull/Fetch yapılabilir mi?</summary>
    public bool CanPull =>
        _fetchWriter is not null
        && _pullWriter is not null
        && _remoteReader is not null
        && PullPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Uzak depolar yönetilebilir mi?</summary>
    public bool CanManageRemotes =>
        _remoteReader is not null
        && _remoteWriter is not null
        && RemotesPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>Dal düzenlenebilir mi?</summary>
    public bool CanEditBranch =>
        _branchWriter is not null
        && BranchEditPrompt is not null
        && Commits.Repository?.WorkingDirectory is { Length: > 0 };

    /// <summary>
    /// HEAD ayrık mı? (P06-T04)
    /// </summary>
    [ObservableProperty]
    public partial bool IsDetachedHead { get; private set; }

    /// <summary>Süren çok adımlı işlem (P06-T04).</summary>
    [ObservableProperty]
    public partial InProgressOperation CurrentOperation { get; private set; }

    /// <summary>
    /// Ayrık HEAD şeridi gösterilsin mi?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Süren bir işlem varsa GÖSTERİLMİYOR.</b> ÖLÇÜLDÜ: rebase ve bisect sırasında
    /// HEAD gerçekten ayrık; düz bir uyarı orada da açılır ve <i>"buradan dal oluştur"</i>
    /// derdi — oysa kullanıcı bilerek bir işlemin ortasında ve dal oluşturmak yapması
    /// gereken şey değil. O durumda <see cref="OperationText"/> gösteriliyor.
    /// </remarks>
    public bool ShowDetachedBanner =>
        IsDetachedHead && CurrentOperation == InProgressOperation.None;

    /// <summary>Süren işlem şeridi gösterilsin mi?</summary>
    public bool ShowOperationBanner => CurrentOperation != InProgressOperation.None;

    /// <summary>Süren işlemin insan-okunur adı.</summary>
    public string OperationText => CurrentOperation switch
    {
        InProgressOperation.Rebase => "A rebase is in progress. Finish it or abort.",
        InProgressOperation.ApplyMailbox => "A patch is being applied (git am). Finish it or abort.",
        InProgressOperation.Merge => "The merge stopped with conflicts. Resolve them or abort.",
        InProgressOperation.CherryPick => "A cherry-pick is in progress. Finish it or abort.",
        InProgressOperation.Revert => "A revert is in progress. Finish it or abort.",
        InProgressOperation.Bisect => "A bisect is in progress. Finish it or reset.",
        _ => string.Empty,
    };

    /// <summary>Son dal işleminin sonucu; arayüzde şerit olarak gösterilir.</summary>
    [ObservableProperty]
    public partial string? BranchNotice { get; set; }

    /// <summary>Bir depo açık mı? Menü öğelerinin etkinliği buna bağlı.</summary>
    /// <remarks>
    /// ⚠️ Hesaplanan özellik: değeri her zaman doğru ama <b>bildirimi</b> kendiliğinden
    /// gelmiyor. Bağlamanın güncellenmesi <see cref="NotifyRepositoryDependents"/>'a bağlı.
    /// </remarks>
    public bool HasRepository => Commits.Repository is not null;

    /// <summary>
    /// Depo açılıp kapandığında değişen <b>tüm</b> bağlamaları ve komut durumlarını bildirir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Bunun eksikliği gerçek bir hataydı: <c>HasRepository</c> yalnızca kapanışta
    /// bildiriliyordu, açılışta hiç → <c>IsEnabled="{Binding HasRepository}"</c> ilk
    /// değerinde (<see langword="false"/>) donuyor ve <b>ana menünün iki bölümü birden</b>
    /// (<i>Depo</i>, <i>Komutlar</i>) depo açıkken de soluk kalıyordu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Komutların da bildirilmesi gerekiyor:</b> <c>CanExecute</c> temsilcileri
    /// <c>Commits.Repository</c>'ye bakıyor ama bir kez oluşturulmuş menü öğesi
    /// <c>CanExecuteChanged</c> gelmedikçe sormuyor. Alt menü öğeleri menü <b>her açılışta</b>
    /// yeniden kurulduğu için orada fark edilmiyordu; araç çubuğu ve kısayollar gibi
    /// kalıcı bağlamalarda ise sessizce ölü kalırdı.
    /// </para>
    /// <para>
    /// Bir özellik/komut eklendiğinde buraya da eklenmeli — bunu unutmak sessiz bir hata
    /// olduğu için <c>MainWindowBindingTests</c> gerçek pencere üzerinden kontrol ediyor.
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
    /// Açık depoyu baştan okur.
    /// </summary>
    /// <remarks>
    /// Yol yeniden veriliyor: <c>git</c> durumu dışarıdan değişmiş olabilir (komut satırında
    /// commit atılması gibi), bu yüzden önbelleğe güvenilmiyor.
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
    /// İzleyicinin bildirdiği değişikliği ele alır (P05-T14).
    /// </summary>
    /// <remarks>
    /// Yalnızca <see cref="RepositoryChangeKind.Repository"/> commit listesini ilgilendirir;
    /// çalışma ağacı değişimini commit penceresi kendi dinliyor. Her dosya kaydedişinde
    /// commit geçmişini yeniden okumak, büyük depoda saniyeler süren bir iş olurdu
    /// (ölçüm: git/git 2,1 sn, Linux 31,6 sn).
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

        // Kendi okumalarımızın ürettiği olaylar yeni bir tazeleme doğurmasın.
        using IDisposable? suspension = _watcher?.Suspend();

        await OpenRepositoryAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Dal oluşturma akışı (P06-T01).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Onay diyaloğu değil, kurulum diyaloğu.</b> Dal oluşturmak yıkıcı bir işlem değil —
    /// P05-T15'in "geri alınamaz işlem" kuralı burada geçerli değil; diyalog ad ve seçenek
    /// almak için var.
    /// </para>
    /// <para>
    /// ⚠️ Başlangıç noktası olarak <b>commit listesindeki seçim</b> kullanılıyor, HEAD değil:
    /// GitExtensions'ta bu komut commit sağ tık menüsünde ve etiketi
    /// <i>"Create branch at this revision"</i> — seçili commit'i yok sayıp HEAD'den
    /// oluşturmak sessizce başka bir şey yapmak olurdu.
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
            // Kendi yazmamızın ürettiği olaylar ayrıca bir tazeleme doğurmasın; aşağıda
            // zaten elle tazeliyoruz (P05-T14'ün `Suspend()` kuralı).
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
            // Ham stderr birincil mesaj olarak gösterilmiyor (GitFailureKind'ın gerekçesi).
            BranchNotice = Loc.GitError(error);
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }


    /// <summary>
    /// Dala / commit'e geçme akışı (P06-T02).
    /// </summary>
    /// <remarks>
    /// <b>Hedef seçimi:</b> seçili commit'te <b>yerel bir dal</b> varsa o dala geçilir;
    /// yoksa commit'in kendisine (detached). GitExtensions'ta da commit menüsünde iki ayrı
    /// öğe var (<i>Checkout branch</i> · <i>Checkout this commit</i>); ikisini tek komutta
    /// birleştirirken hangisinin olacağı <b>diyalogda açıkça yazılıyor</b>, sessizce
    /// seçilmiyor.
    /// </remarks>
    private Task CheckoutAsync() => CheckoutCoreAsync(null);

    /// <param name="refName">
    /// Panelden gelen ref adı; <see langword="null"/> ise seçili commit kullanılır.
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
            BranchNotice = "Select a commit to check out.";
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

                            // Onayın kendisi diyalogdan geliyor; Core tarafı yine de
                            // açık bayrak istiyor (P05-T15 deseni).
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
    /// Seçili commit'teki yerel dalı yeniden adlandırır (P06-T03).
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
            BranchNotice = "Select a branch to rename.";
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
    /// Seçili commit'teki yerel dalı siler (P06-T03).
    /// </summary>
    /// <remarks>
    /// <b>İki turlu akış.</b> Önce sıradan onay sorulup <c>git branch -d</c> deneniyor;
    /// git dalı birleştirilmemiş diye reddederse diyalog ikinci kez, bu kez <b>kurtarma
    /// komutuyla</b> açılıyor. Birleşmişliği önden hesaplamıyoruz: ölçüldü, <c>-d</c>
    /// dalı HEAD'e değil <b>upstream'ine</b> birleşmiş olsa da siliyor; kendi hesabımız
    /// o dallarda yanlış "birleştirilmemiş" alarmı üretirdi.
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
            BranchNotice = "Select a branch to delete.";
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
            // İkinci tur: artık kurtarma komutunu da gösterebiliyoruz.
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

        // 🔴 Hash bildirimde KALMALI: silinen dalın kendi reflog'u da gidiyor ve dal bu
        // çalışma ağacında hiç checkout edilmemişse HEAD reflog'unda da iz yok (ölçüldü).
        BranchNotice = result.WasUnmerged
            ? $"Branch '{result.Name}' deleted. To restore it: "
              + $"git branch {result.Name} {result.LastCommitId}"
            : $"Branch '{result.Name}' deleted (tip was {Shorten(result.LastCommitId)}).";

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Uzak depo yönetimi ekranını açar (P06-T05).
    /// </summary>
    /// <remarks>
    /// Ekranın kendi ViewModel'ı var ve ana pencereyi <b>tanımıyor</b> (P04-T08'de verilen
    /// karar): burası yalnızca onu kuruyor, pencereyi açmak görünümün işi.
    /// <para>
    /// Kapanışta <see cref="RefreshAsync"/>: uzak izleme dalları silinmiş veya yeni bir
    /// remote eklenmiş olabilir; rozetler ve dal listesi bunu yansıtmalı.
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

        // Depo yazma işlemleri sırasında izleyici askıya alınıyor: config değişiklikleri
        // tazeleme fırtınası üretebiliyor (P05-T14).
        using (_watcher?.Suspend())
        {
            await RemotesPrompt.ShowAsync(model).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Pull/Fetch ekranını açar (P06-T06 + P06-T07).
    /// </summary>
    /// <remarks>
    /// Fetch'in ayrı bir ekranı yok: GitExtensions'ta da <c>FormPull</c>'un bir seçeneği
    /// ve menüdeki yer birleşik ("Pull/Fetch…", § 9).
    /// <para>
    /// Kapanışta tazeleme şart: uzak izleme dalları değişmiş, HEAD ilerlemiş olabilir.
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
    /// Push ekranını açar (P06-T08).
    /// </summary>
    /// <remarks>
    /// Kapanışta tazeleme şart: <c>-u</c> ile upstream kurulmuş, uzak izleme dalları
    /// ilerlemiş ya da bir dal silinmiş olabilir — üçü de dal rozetlerini değiştiriyor.
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
    /// Merge ekranını açar (P06-T11).
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

        // Uzak dallar da birleştirilebilir; GitExtensions'ın listesi de ikisini birden
        // içeriyor (§ 9).
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
    /// Süren merge'i iptal eder (P06-T12).
    /// </summary>
    /// <remarks>
    /// 🔑 Onay şart: <c>merge --abort</c> çalışma ağacını merge ÖNCESİNE döndürüyor, yani
    /// kullanıcının çakışmaları çözerken yazdığı her şey gider (ölçüldü). Onay ekranında
    /// çözülmemiş dosyalar listeleniyor — neyin kaybolacağı görünmeden onay istenmiyor.
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

            BranchNotice = "Merge aborted; the working tree is back to its previous state.";
        }
        catch (GitException error)
        {
            BranchNotice = Loc.GitError(error);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Git komut günlüğünü açar (P06-T16).</summary>
    private async Task ShowCommandLogAsync()
    {
        if (_commandLog is null || CommandLogPrompt is null)
        {
            return;
        }

        await CommandLogPrompt.ShowAsync(new CommandLogViewModel(_commandLog)).ConfigureAwait(true);
    }

    /// <summary>Performans teşhis panelini açar (P09-T03).</summary>
    private async Task ShowDiagnosticsAsync()
    {
        if (_diagnostics is null || DiagnosticsPrompt is null)
        {
            return;
        }

        await DiagnosticsPrompt.ShowAsync(_diagnostics).ConfigureAwait(true);
    }

    /// <summary>Çözülmemiş dosyalar — iptal onayında gösteriliyor.</summary>
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
            // Onay ekranı listesiz de gösterilebilir; iptali engellemek doğru olmazdı.
            return [];
        }
    }

    /// <summary>Seçili commit'teki ilk yerel dal.</summary>
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

        // Aynı commit'te birden çok dal olabilir; ilk yerel dal seçiliyor ve etikette
        // yazılıyor, böylece kullanıcı hangisine geçtiğini görüyor.
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

        // 🔴 Çıkış kodu 0 olsa bile çakışma olabiliyor (`--merge` ölçümü); sessiz kalmak
        // kullanıcıya "başarıyla geçildi" demek olurdu.
        if (result.HasConflicts)
        {
            summary += " ⚠️ Some files are UNRESOLVED — you need to resolve the conflicts by hand.";
        }

        if (result.StashCreated)
        {
            summary += " Your local changes were stashed (restore them with `git stash pop`).";
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

        // Upstream'i git kendisi kurdu; kullanıcı istemeden kurulan bir bağ sessiz kalmamalı.
        return result.Upstream is { Length: > 0 } upstream
            ? $"{summary} Tracked branch: {upstream}."
            : summary;
    }

    private string DescribeStartPoint(string? startPoint)
    {
        if (startPoint is not { Length: > 0 })
        {
            return "HEAD (tip of the current branch)";
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
            // Uyarı gösterebilmek için durum okuyamamak, dal oluşturmayı engellemez.
            return false;
        }
    }

    /// <summary>
    /// Açık depo için izlemeyi başlatır; depo yoksa durdurur (P05-T14).
    /// </summary>
    /// <remarks>
    /// <b>Bare depo izlenmiyor:</b> çalışma ağacı yok, izlenecek dosya da yok.
    /// </remarks>
    private void UpdateWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        if (Commits.Repository is { WorkTreeRoot: { Length: > 0 } root } repository)
        {
            // ⚠️ Üç ayrı yol: bağlı çalışma ağacında HEAD/index kendi git dizininde,
            // ref'ler ortak dizinde (CLAUDE.md § 5, madde 9).
            _watcher.Start(root, repository.GitDirectory, repository.CommonDirectory);
        }
        else
        {
            _watcher.Stop();
        }
    }

    /// <summary>Depoyu kapatır; karşılama ekranı geri gelir.</summary>
    public void CloseRepository()
    {
        Commits.Close();
        _watcher?.Stop();

        Subtitle = "No repository open.";

        // Depo bilerek kapatıldı: sonraki açılışta karşılama ekranı gelmeli, aynı depo
        // değil. Kullanıcı "kapat" derken bunu kastediyor.
        Session?.ForgetRepository();

        // `Commits.Close()` zaten `Repository = null` yapıyor ve abonelik bildirimleri
        // gönderiyor; buradaki çağrı yalnızca sıranın garantisi için duruyor (ikinci kez
        // bildirmek zararsız, eksik bildirmek değil).
        OnPropertyChanged(nameof(ShowWelcome));
        NotifyRepositoryDependents();
    }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "No repository open.";

    /// <summary>
    /// Karşılama ekranı gösterilsin mi?
    /// </summary>
    /// <remarks>
    /// Açık depo yoksa ve bir şey yüklenmiyorsa. Yükleme sırasında gizlenmesi kasıtlı:
    /// aksi halde açılışta karşılama ekranı bir an görünüp kaybolur.
    /// </remarks>
    public bool ShowWelcome => Commits.Repository is null && !Commits.IsLoading;

    /// <summary>
    /// Uygulama açılışındaki depo seçimi (P03-T16).
    /// </summary>
    /// <param name="explicitPath">
    /// Komut satırında verilen yol; verilmediyse <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    /// <remarks>
    /// <para>
    /// Yol açıkça verildiyse açılamaması bir <b>hatadır</b> ve kullanıcıya söylenir — o yolu
    /// isteyen kullanıcıdır.
    /// </para>
    /// <para>
    /// Verilmediyse çalışma dizini <b>sessizce</b> denenir. Uygulama masaüstünden veya
    /// menüden başlatıldığında çalışma dizini rastgele bir yerdir; "burası depo değil"
    /// hatası göstermek anlamsız olur. Depo değilse karşılama ekranı açılır.
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

        // Kapanışta açık olan depo yeniden açılıyor (P08-T16). Çalışma dizininden ÖNCE
        // deneniyor: masaüstünden başlatıldığında çalışma dizini rastgele bir yer, oysa
        // son depo kullanıcının bilerek açtığı yer.
        if (Session?.LastRepository is { Length: > 0 } last
            && await TryOpenQuietlyAsync(last, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        await TryOpenQuietlyAsync(Directory.GetCurrentDirectory(), cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Oturum hatırlayıcısı (P08-T16). Verilmezse oturum durumu tutulmaz.
    /// </summary>
    /// <remarks>
    /// Diğer görünüm bağımlılıkları gibi ayarlanabilir bir özellik: ViewModel testlerinin
    /// çoğu oturum kalıcılığını umursamıyor ve zorunlu bir bağımlılık hepsini değiştirmeyi
    /// gerektirirdi.
    /// </remarks>
    public SessionTracker? Session { get; set; }

    /// <summary>
    /// Deponun son seçili commit'ini geri yükler (P08-T16).
    /// </summary>
    /// <remarks>
    /// <b>SHA bulunamazsa hiçbir şey yapılmıyor</b> ve varsayılan seçim (en yeni commit)
    /// korunuyor. Commit gerçekten kaybolmuş olabilir: rebase'lenmiş, sıfırlanmış ya da
    /// budanmış. Bulunamayan bir SHA için seçimi temizlemek, kullanıcıyı boş bir detay
    /// paneliyle bırakırdı.
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
    /// Bir depoyu açar, başlığı günceller ve son açılanlara ekler.
    /// </summary>
    public async Task OpenRepositoryAsync(string path, CancellationToken cancellationToken = default)
    {
        await Commits.OpenAsync(path, cancellationToken).ConfigureAwait(true);

        Subtitle = Commits.Repository is { } repository
            ? $"{repository.WorkingDirectory} — {Commits.Rows.Count} commit"
            : Commits.ErrorMessage ?? "Could not open the repository.";

        OnPropertyChanged(nameof(ShowWelcome));

        UpdateWatcher();

        if (Commits.Repository is { } opened)
        {
            // Kullanıcının verdiği yol değil, git'in çözdüğü kök kaydedilir: alt klasörden
            // açıldığında listede iki farklı girdi oluşmasın.
            await AddRecentAsync(opened.WorkingDirectory, cancellationToken).ConfigureAwait(true);

            Session?.RememberRepository(opened.WorkingDirectory);
            RestoreSelectedCommit(opened.WorkingDirectory);
        }

        await UpdateHeadStateAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Ayrık HEAD ve süren işlem durumunu okur (P06-T04).
    /// </summary>
    /// <remarks>
    /// Ayrık olma bilgisi <c>RefReader</c>'dan geliyor (<c>symbolic-ref</c> tabanlı, yani
    /// <c>(detached)</c> adlı dalı yanlış okumuyor); süren işlem ayrı bir okumayla, çünkü
    /// dosya sistemine bakıyor ve ref okumasının parçası değil.
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

            // Dal paneli (P06-T13) aynı ref okumasından besleniyor: ikinci bir okuma
            // yazmak, iki panelin sessizce ayrışması demekti.
            RefTree.Load(Commits.Refs);

            CurrentOperation = _operations is null
                ? InProgressOperation.None
                : await ReadOperationAsync(path, cancellationToken).ConfigureAwait(true);
        }

        if (Commits.Repository?.WorkingDirectory is not { Length: > 0 })
        {
            RefTree.Load(null);
        }

        OnPropertyChanged(nameof(ShowDetachedBanner));
        OnPropertyChanged(nameof(ShowOperationBanner));
        OnPropertyChanged(nameof(OperationText));
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
            // Şerit gösterememek depoyu açmayı engellemez.
            return InProgressOperation.None;
        }
    }

    /// <summary>
    /// Pencereye bırakılan yolları açmayı dener (P03-T16, sürükle-bırak).
    /// </summary>
    /// <remarks>
    /// Bırakılan şey bir <b>dosya</b> olabilir (kullanıcı dosya yöneticisinden bir dosyayı
    /// sürükler); o zaman bulunduğu klasör denenir. <c>git</c> zaten üst klasörlere doğru
    /// depo kökünü arar, yani deponun içindeki herhangi bir dosya yeterlidir.
    /// </remarks>
    /// <returns>Bir yol açılmaya çalışıldıysa <see langword="true"/>.</returns>
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
    /// Depo olmayabilecek bir yolu, başarısızlığı hata olarak göstermeden dener.
    /// </summary>
    /// <returns>Depo gerçekten açıldıysa <see langword="true"/>.</returns>
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
            // Hata mesajı temizleniyor: kullanıcı bu klasörü açmayı istemedi.
            Commits.ErrorMessage = null;
            Commits.ErrorDetails = null;
            Subtitle = "No repository open.";
        }

        await UpdateHeadStateAsync(cancellationToken).ConfigureAwait(true);

        OnPropertyChanged(nameof(ShowWelcome));

        return opened;
    }

    private async Task LoadRecentAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> recent;

        try
        {
            recent = await _recentStore.LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        RecentRepositories.Clear();

        foreach (string path in recent)
        {
            RecentRepositories.Add(new RecentRepositoryItem(path, OpenRecentCommand));
        }
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
