using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Graph;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Bir deponun commit geçmişini artımlı olarak yükler ve grafiğe yerleştirir (P03-T13).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden sayfalama değil de artımlı akış?</b> Yerleşim algoritması tek geçişli ileri
/// taramadır (ADR-0007): 500.000'inci satırın şeridi, öncesindeki tüm satırlar işlenmeden
/// hesaplanamaz. Bu yüzden "istenen sayfayı getir" modeli doğrudan uygulanamaz; commit'ler
/// sırayla akar ve yerleşim motoru durumunu korur.
/// </para>
/// <para>
/// <b>Toplu güncelleme zorunlu</b> (ADR-0004): commit başına bir <c>CollectionChanged</c>
/// olayı yayınlamak 100 bin commit'te uygulamayı kilitler. Satırlar
/// <see cref="AvaloniaList{T}.AddRange"/> ile parti parti eklenir.
/// </para>
/// </remarks>
public sealed partial class CommitListViewModel : ViewModelBase
{
    /// <summary>
    /// Bir partide UI'a aktarılan satır sayısı.
    /// </summary>
    /// <remarks>
    /// Küçük tutmak ilk ekranın erken görünmesini sağlar; büyük tutmak olay sayısını azaltır.
    /// 256, ilk ekranı (~40 satır) hemen dolduracak kadar küçük.
    /// </remarks>
    private const int BatchSize = 256;

    private readonly IRepositoryLocator _locator;
    private readonly ICommitLogReader _logReader;
    private readonly IRefReader _refReader;
    private readonly IDiffReader _diffReader;

    /// <summary>
    /// Commit kimliğinden satır indeksine eşleme — ebeveyne atlama ve SHA ile bulma için.
    /// </summary>
    /// <remarks>
    /// Satırlar eklendikçe artımlı olarak dolar. Maliyeti 500k satırda ~20 MB; ebeveyne
    /// atlamayı taramaya bırakmak, uzak ebeveynli birleşme commit'lerinde yarım depoyu
    /// gezmek demek olurdu.
    /// </remarks>
    private readonly Dictionary<CommitId, int> _rowIndex = [];

    private CancellationTokenSource? _loading;

    public CommitListViewModel(
        IRepositoryLocator locator,
        ICommitLogReader logReader,
        IRefReader refReader,
        ICommitSignatureReader signatureReader,
        IDiffReader diffReader)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(logReader);
        ArgumentNullException.ThrowIfNull(refReader);
        ArgumentNullException.ThrowIfNull(signatureReader);
        ArgumentNullException.ThrowIfNull(diffReader);

        _locator = locator;
        _logReader = logReader;
        _refReader = refReader;
        _diffReader = diffReader;

        // Detay panelindeki ebeveyn bağlantıları listeye geri gezinir; bağımlılık tek yönlü
        // kalsın diye panele tüm ViewModel değil yalnızca bu geri çağrı veriliyor.
        Details = new CommitDetailsViewModel(signatureReader, TryGoToCommit);

        // Diff bileşeni BAĞIMSIZ: burayı tanımıyor, yalnızca "şunu göster" deniyor.
        // Aynı bileşen P04-T16'daki karşılaştırma penceresinde de kullanılacak.
        Diff = new DiffViewModel(diffReader);
    }

    /// <summary>Seçili commit'in detay paneli (P03-T15).</summary>
    public CommitDetailsViewModel Details { get; }

    /// <summary>
    /// Bir karşılaştırma penceresi için yeni ViewModel üretir (P04-T16).
    /// </summary>
    /// <remarks>
    /// <b>Her pencere kendi ViewModel'ine sahip</b> — pencereler modeless ve aynı anda
    /// birden fazla açılabiliyor, paylaşılan bir örnek olsaydı biri diğerinin içeriğini
    /// değiştirirdi.
    /// <para>
    /// Pencereyi <b>açmak</b> görünümün işi; burası yalnızca ne gösterileceğini kuruyor.
    /// </para>
    /// </remarks>
    public CompareViewModel? CreateComparison()
    {
        string? workingDirectory = Repository?.WorkingDirectory;

        return string.IsNullOrEmpty(workingDirectory)
            ? null
            : new CompareViewModel(_diffReader, workingDirectory);
    }

    /// <summary>Seçili commit'in değişen dosyaları (P04-T08).</summary>
    public DiffViewModel Diff { get; }

    /// <summary>Yüklenmiş satırlar.</summary>
    public AvaloniaList<CommitRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Seçili satırın indeksi; seçim yoksa <c>-1</c>.
    /// </summary>
    /// <remarks>
    /// Seçimin <b>tek doğruluk kaynağı</b> budur; <see cref="SelectedRow"/> buradan türetilir.
    /// İkisini de iki yönlü bağlamak, programatik gezinmede birbirini kovalayan iki bağlama
    /// demek olurdu. İndeks ayrıca gezinme için gerekli — 500 bin satırda <c>IndexOf</c>
    /// her tuş vuruşunda listeyi baştan tarardı.
    /// </remarks>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>Son okunan ref durumu; okunamadıysa <see langword="null"/> (P06-T04).</summary>
    public RepositoryRefs? Refs { get; private set; }

    /// <summary>Seçili satır; seçim yoksa <see langword="null"/>.</summary>
    public CommitRowViewModel? SelectedRow =>
        SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex] : null;

    partial void OnSelectedIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedRow));
        EnsureLaneVisible(SelectedRow?.GraphRow.Lane);
        Details.Show(SelectedRow, Repository?.WorkingDirectory);

        // Gecikme ve iptal DiffViewModel içinde; hızlı gezinmede git çalıştırılmıyor.
        _ = Diff.ShowCommitAsync(
            Repository?.WorkingDirectory,
            SelectedRow?.Commit.Id ?? default,
            SelectedRow?.Subject);
    }

    /// <summary>
    /// Grafik penceresini, verilen şerit görünür olacak şekilde kaydırır (P03-T21).
    /// </summary>
    /// <remarks>
    /// Seçili commit'in düğümü daima görünmeli — aksi halde kullanıcı seçtiği satırın
    /// grafikte nerede olduğunu göremez. Pencere yalnızca <b>gerektiğinde</b> kayar;
    /// her seçimde ortalamak grafiği sürekli zıplatırdı.
    /// </remarks>
    internal void EnsureLaneVisible(int? lane)
    {
        if (lane is not { } target || target < 0)
        {
            return;
        }

        int window = Math.Max(VisibleLanes, 1);

        if (target < FirstVisibleLane)
        {
            FirstVisibleLane = target;
        }
        else if (target > FirstVisibleLane + window - 1)
        {
            FirstVisibleLane = target - window + 1;
        }
    }

    /// <summary>
    /// Grafik penceresinde aynı anda gösterilen şerit sayısı (P03-T21).
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> gerçek depolarda şerit sayısı medyanda ~120 (git/git 118, Linux 120)
    /// ve düğümler bu şeritlere yayılıyor — 16 şeritlik bir sınır Linux'ta düğümlerin yalnızca
    /// %24'ünü gösterirdi. Yani sabit bir "kes at" sınırı işe yaramaz; sütun sabit genişlikte
    /// kalıp <see cref="FirstVisibleLane"/> ile <b>kayan bir pencere</b> gösteriyor.
    /// </remarks>
    public const int DefaultVisibleLanes = 12;

    /// <summary>
    /// Grafik penceresinin soldaki ilk şeridi.
    /// </summary>
    /// <remarks>
    /// Tüm satırlar aynı değeri kullanır; satır başına hesaplansaydı şeritler satırdan satıra
    /// kayar ve grafik okunamaz olurdu. Seçim değiştikçe pencere seçili commit'i içerecek
    /// şekilde kaydırılır.
    /// </remarks>
    [ObservableProperty]
    public partial int FirstVisibleLane { get; private set; }

    /// <summary>Grafik penceresinin üst sınırı.</summary>
    [ObservableProperty]
    public partial int MaxVisibleLanes { get; set; } = DefaultVisibleLanes;

    /// <summary>
    /// Sütunun gerçekten kullandığı şerit sayısı.
    /// </summary>
    /// <remarks>
    /// Üst sınırla deponun gerçek genişliğinin küçüğü. Dar bir depoda 12 şeritlik sabit
    /// sütun boşuna yer kaplardı; geniş depoda ise sınır devreye girer. Değer <b>tüm
    /// satırlarda ortak</b> olduğu için sütunlar hizasını korur.
    /// </remarks>
    [ObservableProperty]
    public partial int VisibleLanes { get; private set; } = 1;

    private int _widestRow = 1;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Şu ana kadar yüklenen satır sayısı — yükleme sürerken ilerlemeyi göstermek için (P03-T17).
    /// </summary>
    /// <remarks>
    /// <c>Rows.Count</c> yerine ayrı bir özellik: yüzbinlerce satırda her partide koleksiyon
    /// sayacına bağlanmak yerine tek bir bildirim yayınlamak yeterli.
    /// </remarks>
    [ObservableProperty]
    public partial int LoadedCount { get; private set; }

    /// <summary>
    /// Depo açık ama hiç commit yok mu? (Yeni <c>git init</c> — hata değil.)
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmptyRepository { get; private set; }

    /// <summary>
    /// Süren yüklemeyi iptal eder (P03-T17).
    /// </summary>
    /// <remarks>
    /// Çok büyük bir depoda kullanıcı yanlış klasörü açtığını fark edebilir; yüklemenin
    /// bitmesini beklemek zorunda kalmamalı. O ana kadar gelen satırlar ekranda kalır —
    /// yarım bir geçmiş, boş ekrandan yararlıdır.
    /// </remarks>
    public Task CancelLoadingAsync() => CancelLoadingCoreAsync();

    /// <summary>Arama kutusuna yazılan SHA öneki.</summary>
    [ObservableProperty]
    public partial string? SearchText { get; set; }

    /// <summary>Arama başarısızsa kullanıcıya gösterilecek kısa not; başarılıysa boş.</summary>
    [ObservableProperty]
    public partial string? SearchStatus { get; set; }

    partial void OnSearchTextChanged(string? value) => SearchStatus = null;

    /// <summary>
    /// Arama kutusundaki metni uygular (P03-T14).
    /// </summary>
    /// <remarks>
    /// Yazarken değil, kullanıcı <c>Enter</c>'a bastığında çalışır. Sebep: kısa önek araması
    /// listeyi tarıyor; her tuş vuruşunda 500 bin satırı taramak yazmayı takılır hale getirir.
    /// </remarks>
    public void ApplySearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchStatus = null;
            return;
        }

        SearchStatus = TryGoToCommit(SearchText) ? null : "not found";
    }

    /// <summary>Yükleme başarısız olduysa kullanıcıya gösterilecek mesaj.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Hatanın <b>tam</b> git çıktısı; yalnızca <see cref="GitException"/> için dolu (P05-T07).
    /// </summary>
    /// <remarks>
    /// <see cref="ErrorMessage"/> sınıflandırılmış <b>özet</b>tir ("Git komutu başarısız
    /// oldu.") ve tek başına teşhis ettirmez — git'in asıl söylediği burada.
    /// </remarks>
    [ObservableProperty]
    public partial GitOutputViewModel? ErrorDetails { get; set; }

    /// <summary>Açık deponun konumu; henüz açılmadıysa <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial RepositoryLocation? Repository { get; set; }

    /// <summary>
    /// Bir depoyu açar ve geçmişini yüklemeye başlar.
    /// </summary>
    /// <remarks>
    /// Önceki yükleme çalışıyorsa iptal edilir — kullanıcı hızlıca başka bir depo açtığında
    /// iki akışın aynı listeye yazmasını önler.
    /// </remarks>
    /// <summary>
    /// Açık depoyu kapatır ve listeyi boşaltır (P08-T26).
    /// </summary>
    /// <remarks>
    /// GitExtensions'ta karşılığı <i>Repository → Close (go to Dashboard)</i>. Yükleme
    /// sürüyorsa iptal ediliyor: kapatıldıktan sonra gelen satırların listeyi yeniden
    /// doldurması kullanıcıya deponun kapanmadığını düşündürürdü.
    /// </remarks>
    public void Close()
    {
        _loading?.Cancel();

        Rows.Clear();
        _rowIndex.Clear();
        SelectedIndex = -1;
        FirstVisibleLane = 0;
        _widestRow = 1;
        VisibleLanes = 1;
        LoadedCount = 0;
        IsEmptyRepository = false;
        ErrorMessage = null;
        ErrorDetails = null;
        IsLoading = false;
        Repository = null;

        Details.Show(null, null);
        Diff.Clear();
    }

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        await CancelLoadingCoreAsync().ConfigureAwait(true);

        _loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _loading.Token;

        Rows.Clear();
        _rowIndex.Clear();
        SelectedIndex = -1;
        FirstVisibleLane = 0;
        _widestRow = 1;
        VisibleLanes = 1;
        LoadedCount = 0;
        IsEmptyRepository = false;
        ErrorMessage = null;
        ErrorDetails = null;

        // Önceki depo unutulur: açılış başarısız olursa ekranda satırı kalmamış bir deponun
        // yolu durmamalı — kullanıcı hâlâ onun açık olduğunu sanır.
        Repository = null;
        IsLoading = true;

        try
        {
            RepositoryLocation location = await _locator
                .LocateAsync(path, token)
                .ConfigureAwait(true);

            Repository = location;

            // Ref'ler geçmişten ÖNCE okunur: rozet dizini olmadan satırlar rozetsiz kalır
            // ve sonradan eklemek tüm satırları yeniden üretmek demek olurdu.
            // Tek çağrı, büyük depoda bile birkaç milisaniye (ölçüldü).
            RefBadgeIndex badges = await LoadBadgesAsync(location.WorkingDirectory, token)
                .ConfigureAwait(true);

            await LoadHistoryAsync(location.WorkingDirectory, badges, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Kullanıcı başka bir depo açtı veya pencereyi kapattı; hata değil.
        }
        catch (Exception ex) when (ex is GitException or GitNotFoundException
                                       or GitVersionTooOldException or DirectoryNotFoundException)
        {
            // GitException'da mesaj Kind'a göre çevriliyor (P11-T06); diğer istisnalarda
            // (git bulunamadı, sürüm eski, dizin yok) sınıflandırma yok, kendi mesajı
            // zaten kullanıcıya yönelik.
            ErrorMessage = ex is GitException classified
                ? Loc.GitError(classified)
                : ex.Message;

            // Özet tek başına yetmez: git'in asıl çıktısı (ve varsa hook'un söyledikleri)
            // ancak burada görünür hale geliyor.
            ErrorDetails = ex is GitException gitException
                ? GitOutputViewModel.ForFailure(gitException)
                : null;
        }
        finally
        {
            IsLoading = false;

            // "Depo açık ama commit yok" bir hata değil, anlatılması gereken bir durum:
            // yeni `git init` edilmiş bir depoda kullanıcı boş ekrana bakmamalı (P03-T17).
            IsEmptyRepository = Repository is not null && ErrorMessage is null && Rows.Count == 0;
        }
    }

    /// <summary>
    /// Seçimi <paramref name="delta"/> satır kaydırır; liste sınırlarında durur (P03-T14).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Neden bu metot var?</b> <c>ListBox</c> <c>↑↓</c>, <c>Home</c> ve <c>End</c>'i kendi
    /// hallediyor ama <c>PgUp</c>/<c>PgDn</c>'i <b>halletmiyor</b> — ölçüldü: bu tuşlar
    /// <c>ScrollViewer</c>'ı kaydırıyor, seçim yerinde kalıyor. Sayfa gezinmesi bu yüzden
    /// elle uygulanıyor.
    /// </para>
    /// <para>
    /// Sınırda durmak sarmalamaktan (wrap) yeğdir: uzun bir listenin sonunda <c>PgDn</c>'e
    /// basıp başa dönmek, kullanıcının yerini kaybetmesi demektir.
    /// </para>
    /// </remarks>
    /// <returns>Seçim gerçekten değiştiyse <see langword="true"/>.</returns>
    public bool MoveSelection(int delta)
    {
        if (Rows.Count == 0)
        {
            return false;
        }

        // Seçim yokken ilk hareket listenin ucundan başlar; aşağı gidiliyorsa baştan,
        // yukarı gidiliyorsa sondan.
        int current = SelectedIndex >= 0 ? SelectedIndex : (delta >= 0 ? -1 : Rows.Count);

        int target = Math.Clamp(current + delta, 0, Rows.Count - 1);

        if (target == SelectedIndex)
        {
            return false;
        }

        SelectedIndex = target;
        return true;
    }

    /// <summary>
    /// Seçili commit'in <b>ilk ebeveynine</b> atlar (daha eski commit, listede aşağıda).
    /// </summary>
    /// <remarks>
    /// İlk ebeveyn seçildi çünkü birleşme commit'lerinde dalın "ana hattı" odur. Diğer
    /// ebeveynlere detay panelinden tıklanarak gidilecek (P03-T15).
    /// </remarks>
    /// <returns>Ebeveyn bulunup seçildiyse <see langword="true"/>.</returns>
    public bool GoToParent()
    {
        CommitInfo? commit = SelectedRow?.Commit;

        return commit is { Parents.Count: > 0 } && TryGoToCommit(commit.Parents[0]);
    }

    /// <summary>
    /// Seçili commit'i ebeveyn olarak gösteren en yakın çocuğa atlar (listede yukarıda).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ters indeks tutulmuyor, yukarı doğru taranıyor. Gerekçe: çocuk indeksi belleği ikiye
    /// katlar, oysa <c>--topo-order</c> her çocuğun ebeveyninden <b>önce</b> gelmesini garanti
    /// ettiği için çocuk daima yukarıdadır ve pratikte birkaç satır uzaktadır.
    /// </para>
    /// <para>
    /// En kötü durum bir dal ucudur (çocuğu yok): liste baştan sona taranır. Kullanıcının
    /// tek bir tuş vuruşu için bu kabul edilebilir.
    /// </para>
    /// </remarks>
    /// <returns>Çocuk bulunup seçildiyse <see langword="true"/>.</returns>
    public bool GoToChild()
    {
        CommitRowViewModel? selected = SelectedRow;

        if (selected is null)
        {
            return false;
        }

        CommitId id = selected.Commit.Id;

        for (int i = SelectedIndex - 1; i >= 0; i--)
        {
            if (Rows[i].Commit.Parents.Contains(id))
            {
                SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Kimliği verilen commit'e atlar.
    /// </summary>
    /// <returns>Commit yüklü satırlar arasında bulunduysa <see langword="true"/>.</returns>
    public bool TryGoToCommit(CommitId id)
    {
        if (!_rowIndex.TryGetValue(id, out int index))
        {
            return false;
        }

        SelectedIndex = index;
        return true;
    }

    /// <summary>
    /// SHA öneki ile commit arayıp bulunana atlar (P03-T14).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Yalnızca <b>SHA öneki</b> aranır. Mesaj/yazar araması ayrı bir görevdir (P07-T21);
    /// burada karıştırmak, kullanıcının 7 karakterlik bir SHA yazdığında konu satırında
    /// eşleşen alakasız commit'lere düşmesi demek olurdu.
    /// </para>
    /// <para>
    /// Tam uzunluktaki SHA sözlükten O(1) bulunur. Kısa önek listeyi taramayı gerektirir —
    /// bir sözlük öneklere göre indekslenemez. Tarama yalnızca kullanıcı açıkça arattığında
    /// çalışır, tuş başına değil.
    /// </para>
    /// </remarks>
    /// <returns>Eşleşen ilk commit'e atlandıysa <see langword="true"/>.</returns>
    public bool TryGoToCommit(string shaPrefix)
    {
        if (string.IsNullOrWhiteSpace(shaPrefix))
        {
            return false;
        }

        string prefix = shaPrefix.Trim().ToLowerInvariant();

        if (CommitId.TryParse(prefix, out CommitId exact) && exact.IsFull && TryGoToCommit(exact))
        {
            return true;
        }

        if (prefix.Length < CommitId.MinimumLength)
        {
            return false;
        }

        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Commit.Id.Value.StartsWith(prefix, StringComparison.Ordinal))
            {
                SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ref rozetlerini okur. Başarısız olursa rozetsiz devam edilir.
    /// </summary>
    /// <remarks>
    /// Rozetler yardımcı bilgidir; okunamaması geçmişi göstermemek için sebep değil.
    /// </remarks>
    private async Task<RefBadgeIndex> LoadBadgesAsync(string workingDirectory, CancellationToken token)
    {
        try
        {
            RepositoryRefs refs = await _refReader
                .ReadAsync(workingDirectory, token)
                .ConfigureAwait(true);

            // HEAD durumu ayrıca saklanıyor: ayrık HEAD şeridi (P06-T04) bunu okuyor ve
            // ikinci bir `symbolic-ref` çağrısı yapmanın anlamı yok.
            Refs = refs;

            return RefBadgeIndex.Build(refs);
        }
        catch (GitException)
        {
            Refs = null;

            return RefBadgeIndex.Empty;
        }
    }

    private async Task LoadHistoryAsync(
        string workingDirectory,
        RefBadgeIndex badges,
        CancellationToken token)
    {
        GraphLayoutEngine engine = new();
        List<CommitRowViewModel> batch = new(BatchSize);

        // TopologicalOrder varsayılan olarak açık — yerleşim buna bağımlı (ADR-0007).
        CommitLogQuery query = new() { IncludeAllRefs = true };

        try
        {
            await foreach (CommitInfo commit in _logReader
                               .StreamAsync(workingDirectory, query, token)
                               .ConfigureAwait(false))
            {
                GraphRow row = engine.Add(ToDagCommit(commit));
                batch.Add(new CommitRowViewModel(commit, row, badges.For(commit.Id)));

                if (batch.Count >= BatchSize)
                {
                    await FlushAsync(batch, token).ConfigureAwait(false);
                }
            }

            if (batch.Count > 0)
            {
                await FlushAsync(batch, token).ConfigureAwait(false);
            }
        }
        catch (GitException ex) when (ex.Kind is GitFailureKind.UnknownRevision or GitFailureKind.Unknown)
        {
            // Doğmamış depo: commit yok. Boş liste doğru sonuç, hata değil.
            if (Rows.Count > 0)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Biriken partiyi UI iş parçacığında listeye ekler.
    /// </summary>
    /// <remarks>
    /// <c>GitExt.Core</c> iş parçacığından habersizdir (ADR-0004); UI'a dönüş burada,
    /// açıkça yapılır.
    /// </remarks>
    private async Task FlushAsync(List<CommitRowViewModel> batch, CancellationToken token)
    {
        CommitRowViewModel[] items = [.. batch];
        batch.Clear();

        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                // İndeks satırlarla BİRLİKTE güncellenir. Ayrı bir geçişte doldurmak,
                // arada gelen bir gezinme isteğinin yarım indeks görmesi demek olurdu.
                int next = Rows.Count;

                foreach (CommitRowViewModel item in items)
                {
                    // Aynı commit iki satırda görünemez; yine de TryAdd kullanılıyor ki
                    // bozuk bir depoda çökmek yerine ilk satır kazansın.
                    _rowIndex.TryAdd(item.Commit.Id, next++);
                }

                Rows.AddRange(items);
                LoadedCount = Rows.Count;

                // Sütun genişliği yükleme sürerken büyüyebilir ama üst sınıra hızla doyar;
                // bu yüzden yalnızca değer gerçekten değişince bildirim yayınlanır.
                foreach (CommitRowViewModel item in items)
                {
                    if (item.GraphRow.LaneCount > _widestRow)
                    {
                        _widestRow = item.GraphRow.LaneCount;
                    }
                }

                VisibleLanes = Math.Clamp(_widestRow, 1, MaxVisibleLanes);

                // İlk parti gelince en yeni commit seçilir: boş bir detay paneliyle
                // karşılaşmak yerine kullanıcı doğrudan bir şey görür. Yalnızca SEÇİM
                // yapılır, odak çalınmaz — kullanıcı arama kutusuna yazıyor olabilir.
                if (SelectedIndex < 0)
                {
                    SelectedIndex = 0;
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// <see cref="CommitInfo"/>'yu yerleşim motorunun ihtiyaç duyduğu daraltılmış tipe çevirir.
    /// </summary>
    /// <remarks>
    /// Motor yalnızca kimlik ve ebeveynleri bilir; tarih, yazar ve mesaj onu ilgilendirmez
    /// (ADR-0003 — <c>GitExt.Graph</c> saf veri üzerinde çalışır).
    /// </remarks>
    private static DagCommit ToDagCommit(CommitInfo commit)
    {
        string[] parents = new string[commit.Parents.Count];

        for (int i = 0; i < parents.Length; i++)
        {
            parents[i] = commit.Parents[i].Value;
        }

        return new DagCommit(commit.Id.Value, parents);
    }

    private async Task CancelLoadingCoreAsync()
    {
        if (_loading is null)
        {
            return;
        }

        await _loading.CancelAsync().ConfigureAwait(true);
        _loading.Dispose();
        _loading = null;
    }
}
