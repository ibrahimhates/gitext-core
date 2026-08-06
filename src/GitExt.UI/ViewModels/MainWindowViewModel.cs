using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Storage;

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
    private readonly IRemoteReader? _remoteReader;
    private readonly IRemoteWriter? _remoteWriter;
    private readonly IFetchWriter? _fetchWriter;
    private readonly IPullWriter? _pullWriter;

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
        IPullWriter? pullWriter = null)
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
        };
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
        InProgressOperation.Rebase => "Rebase sürüyor. Tamamlayın ya da iptal edin.",
        InProgressOperation.ApplyMailbox => "Yama uygulanıyor (git am). Tamamlayın ya da iptal edin.",
        InProgressOperation.Merge => "Merge çakışmayla durdu. Çakışmaları çözün ya da iptal edin.",
        InProgressOperation.CherryPick => "Cherry-pick sürüyor. Tamamlayın ya da iptal edin.",
        InProgressOperation.Revert => "Revert sürüyor. Tamamlayın ya da iptal edin.",
        InProgressOperation.Bisect => "Bisect sürüyor. Sonuçlandırın ya da sıfırlayın.",
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

        CreateBranchCommand.NotifyCanExecuteChanged();
        CheckoutCommand.NotifyCanExecuteChanged();
        RenameBranchCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
        ManageRemotesCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
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
            BranchNotice = error.Message;
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
    private async Task CheckoutAsync()
    {
        if (_branchWriter is null
            || CheckoutPrompt is null
            || Commits.Repository?.WorkingDirectory is not { Length: > 0 } path)
        {
            return;
        }

        if (ResolveCheckoutTarget() is not { } target)
        {
            BranchNotice = "Geçilecek bir commit seçin.";
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
            BranchNotice = error.Message;
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
            BranchNotice = "Yeniden adlandırılacak bir dal seçin.";
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

            BranchNotice = $"'{branch}' → '{decision.NewName}' olarak yeniden adlandırıldı.";
        }
        catch (GitException error)
        {
            BranchNotice = error.Message;
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
            BranchNotice = "Silinecek bir dal seçin.";
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
                BranchNotice = error.Message;
                return;
            }
        }
        catch (GitException error)
        {
            BranchNotice = error.Message;
            return;
        }

        // 🔴 Hash bildirimde KALMALI: silinen dalın kendi reflog'u da gidiyor ve dal bu
        // çalışma ağacında hiç checkout edilmemişse HEAD reflog'unda da iz yok (ölçüldü).
        BranchNotice = result.WasUnmerged
            ? $"'{result.Name}' dalı silindi. Geri getirmek için: "
              + $"git branch {result.Name} {result.LastCommitId}"
            : $"'{result.Name}' dalı silindi (ucu {Shorten(result.LastCommitId)}).";

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
            BranchNotice = error.Message;
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

        PullViewModel model = new(_remoteReader, _fetchWriter, _pullWriter);

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
            BranchNotice = error.Message;
            return;
        }

        using (_watcher?.Suspend())
        {
            await PullPrompt.ShowAsync(model).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
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
        string başlangıç = target.IsDetached
            ? $"{target.Label} commit'ine geçildi (HEAD ayrık)."
            : $"'{result.Target}' dalına geçildi.";

        // 🔴 Çıkış kodu 0 olsa bile çakışma olabiliyor (`--merge` ölçümü); sessiz kalmak
        // kullanıcıya "başarıyla geçildi" demek olurdu.
        if (result.HasConflicts)
        {
            başlangıç += " ⚠️ Bazı dosyalar ÇÖZÜLMEMİŞ durumda — çakışmaları elle çözmeniz gerekiyor.";
        }

        if (result.StashCreated)
        {
            başlangıç += " Yerel değişiklikleriniz stash'e alındı (`git stash pop` ile geri alınır).";
        }

        if (result.Backups.Count > 0)
        {
            başlangıç += $" {result.Backups.Count} dosyanın atılan içeriği yedeklendi.";
        }

        return başlangıç;
    }


    private static string Describe(BranchCreateResult result)
    {
        string başlangıç = result.CheckedOut
            ? $"'{result.Name}' dalı oluşturuldu ve geçildi."
            : $"'{result.Name}' dalı oluşturuldu.";

        // Upstream'i git kendisi kurdu; kullanıcı istemeden kurulan bir bağ sessiz kalmamalı.
        return result.Upstream is { Length: > 0 } upstream
            ? $"{başlangıç} Takip edilen dal: {upstream}."
            : başlangıç;
    }

    private string DescribeStartPoint(string? startPoint)
    {
        if (startPoint is not { Length: > 0 })
        {
            return "HEAD (mevcut dalın ucu)";
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

        Subtitle = "Depo açılmadı.";

        // `Commits.Close()` zaten `Repository = null` yapıyor ve abonelik bildirimleri
        // gönderiyor; buradaki çağrı yalnızca sıranın garantisi için duruyor (ikinci kez
        // bildirmek zararsız, eksik bildirmek değil).
        OnPropertyChanged(nameof(ShowWelcome));
        NotifyRepositoryDependents();
    }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "Depo açılmadı.";

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

        await TryOpenQuietlyAsync(Directory.GetCurrentDirectory(), cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Bir depoyu açar, başlığı günceller ve son açılanlara ekler.
    /// </summary>
    public async Task OpenRepositoryAsync(string path, CancellationToken cancellationToken = default)
    {
        await Commits.OpenAsync(path, cancellationToken).ConfigureAwait(true);

        Subtitle = Commits.Repository is { } repository
            ? $"{repository.WorkingDirectory} — {Commits.Rows.Count} commit"
            : Commits.ErrorMessage ?? "Depo açılamadı.";

        OnPropertyChanged(nameof(ShowWelcome));

        UpdateWatcher();

        if (Commits.Repository is { } opened)
        {
            // Kullanıcının verdiği yol değil, git'in çözdüğü kök kaydedilir: alt klasörden
            // açıldığında listede iki farklı girdi oluşmasın.
            await AddRecentAsync(opened.WorkingDirectory, cancellationToken).ConfigureAwait(true);
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

            CurrentOperation = _operations is null
                ? InProgressOperation.None
                : await ReadOperationAsync(path, cancellationToken).ConfigureAwait(true);
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
    private async Task TryOpenQuietlyAsync(string path, CancellationToken cancellationToken)
    {
        await Commits.OpenAsync(path, cancellationToken).ConfigureAwait(true);

        UpdateWatcher();

        if (Commits.Repository is { } repository)
        {
            Subtitle = $"{repository.WorkingDirectory} — {Commits.Rows.Count} commit";
            await AddRecentAsync(repository.WorkingDirectory, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            // Hata mesajı temizleniyor: kullanıcı bu klasörü açmayı istemedi.
            Commits.ErrorMessage = null;
            Commits.ErrorDetails = null;
            Subtitle = "Depo açılmadı.";
        }

        await UpdateHeadStateAsync(cancellationToken).ConfigureAwait(true);

        OnPropertyChanged(nameof(ShowWelcome));
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
