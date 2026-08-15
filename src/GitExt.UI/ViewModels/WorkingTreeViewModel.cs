using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Çalışma dizini listelerindeki tek satır (P05-T09).
/// </summary>
public sealed class WorkingTreeFileRow
{
    public WorkingTreeFileRow(FileStatus status, bool staged)
    {
        ArgumentNullException.ThrowIfNull(status);

        Status = status;
        IsStagedSide = staged;
    }

    public FileStatus Status { get; }

    /// <summary>Satır <b>stage'lenmiş</b> listeye mi ait?</summary>
    /// <remarks>
    /// Aynı dosya iki listede birden olabilir: bir kısmı stage'lenmiş, kalanı değil.
    /// Diff okunurken hangi tarafın gösterileceğini bu belirliyor.
    /// </remarks>
    public bool IsStagedSide { get; }

    public RepositoryPath Path => Status.Path;

    public string Name => Status.Path.Name;

    public string Directory => Status.Path.Parent.Value;

    /// <summary>
    /// git'in kendi durum harfi.
    /// </summary>
    /// <remarks>
    /// Harfler <c>git status</c>'takiyle aynı: kullanıcı zaten onları biliyor, yeni bir
    /// alfabe öğretmek gereksiz (P04-T08'de değişen dosyalar listesi için de aynı karar).
    /// </remarks>
    public string StatusLetter => Status switch
    {
        { IsConflicted: true } => "U",
        { IsUntracked: true } => "?",
        _ => Kind switch
        {
            FileChangeKind.Added => "A",
            FileChangeKind.Deleted => "D",
            FileChangeKind.Renamed => "R",
            FileChangeKind.Copied => "C",
            FileChangeKind.TypeChanged => "T",
            _ => "M",
        },
    };

    private FileChangeKind Kind =>
        IsStagedSide ? Status.StagedChange : Status.UnstagedChange;

    public bool IsUntracked => Status.IsUntracked;

    public bool IsConflicted => Status.IsConflicted;

    public bool IsDeleted => Kind == FileChangeKind.Deleted;

    public bool IsAdded => Kind == FileChangeKind.Added || Status.IsUntracked;

    public override string ToString() => $"{StatusLetter} {Path}";
}

/// <summary>
/// Çalışma dizini görünümü: stage'lenmemiş ve stage'lenmiş dosyalar (P05-T09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Yerleşim GitExtensions'ın <c>FormCommit</c>'inden alındı</b> (CLAUDE.md § 9): solda üstte
/// <i>Unstaged</i>, altta <i>Staged</i>, aralarındaki araç çubuğunda stage/unstage düğmeleri;
/// sağda seçili dosyanın diff'i.
/// </para>
/// <para>
/// 🔴 <b>Plandan sapma:</b> plan "staged / unstaged / <b>untracked</b> bölümleri" diyordu.
/// GitExtensions'ta <b>ayrı bir untracked bölümü YOK</b> — takip edilmeyen dosyalar
/// <i>Unstaged</i> listesinde duruyor (gizleme seçeneği <c>tsmiShowUntrackedFiles</c>).
/// Üçüncü bir liste, "değişikliklerim" ile "yeni dosyalarım" arasında kullanıcının
/// yapmadığı bir ayrım kurar ve stage etmek için iki ayrı yere bakmayı gerektirirdi.
/// § 9 kuralı gereği yerleşim kazandı.
/// </para>
/// </remarks>
public sealed partial class WorkingTreeViewModel : ViewModelBase, IPartialStagingHost, IDisposable
{
    private readonly IStatusReader _statusReader;
    private readonly IStagingWriter _staging;
    private readonly ICommitWriter _commitWriter;
    private readonly IRepositoryWatcher? _watcher;
    private readonly IWorkingTreeWriter? _workingTreeWriter;

    /// <summary>
    /// Kullanıcı "bir daha sorma" dedi mi?
    /// </summary>
    /// <remarks>
    /// 🔑 <b>Bilinçli olarak OTURUM İÇİ</b> — diske yazılmıyor. Kalıcı olsaydı kullanıcı
    /// aylar sonra, o kutuyu işaretlediğini çoktan unutmuşken hiç uyarılmadan veri
    /// kaybederdi. Asıl şikâyet olan "art arda on dosya sıfırlıyorum, her seferinde
    /// soruyor" durumunu oturum içi bastırma zaten çözüyor.
    /// </remarks>
    private bool _suppressResetPrompt;

    /// <summary>
    /// Son sıfırlamanın yedekleri; "geri al" bunları kullanıyor.
    /// </summary>
    private readonly List<DiscardBackup> _lastResetBackups = [];

    private CancellationTokenSource? _refreshing;

    /// <summary>
    /// Yenileme sırasında seçim <b>programatik</b> değişiyor; etkin liste değişmemeli.
    /// </summary>
    /// <remarks>
    /// 🔴 Bir test yakaladı: stage'lenen dosya listeden çıkınca seçim kaydırılıyor, bu da
    /// karşı listenin indeksini de değiştiriyordu ve etkin liste <b>sessizce</b> karşı tarafa
    /// atlıyordu — kullanıcı unstaged listesinde çalışırken diff birden staged tarafı
    /// göstermeye başlıyordu. Etkin listeyi yalnızca <b>kullanıcının</b> seçimi değiştirir.
    /// </remarks>
    private bool _refreshingSelection;

    public WorkingTreeViewModel(
        IStatusReader statusReader,
        IStagingWriter staging,
        ICommitWriter commitWriter,
        DiffViewModel diff,
        ICommitMessageReader? messageReader = null,
        ICommitMessageStore? messageStore = null,
        IRepositoryWatcher? watcher = null,
        IWorkingTreeWriter? workingTreeWriter = null)
    {
        ArgumentNullException.ThrowIfNull(statusReader);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(commitWriter);
        ArgumentNullException.ThrowIfNull(diff);

        _statusReader = statusReader;
        _staging = staging;
        _commitWriter = commitWriter;
        _watcher = watcher;
        _workingTreeWriter = workingTreeWriter;

        if (_watcher is not null)
        {
            _watcher.Changed += OnRepositoryChanged;
        }

        Message = new CommitMessageViewModel(messageReader, messageStore);

        Diff = diff;

        // Dosya listesi bu görünümde SOLDA; diff bileşeninin kendi listesi gizleniyor.
        // Aynı listeyi iki kez göstermek, kullanıcıya hangisinin seçim kaynağı olduğunu
        // sordururdu.
        Diff.ShowFileList = false;

        // Kısmi staging yalnızca burada anlamlı; diff bileşeni bunu dışarıdan alıyor.
        Diff.StagingHost = this;

        // Mesaj boşalıp dolunca commit düğmesinin durumu değişiyor.
        Message.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanCommit));
    }

    /// <inheritdoc />
    /// <remarks>
    /// İki komut birbirini dışlıyor — GitExtensions'ta da öyle: <c>stageSelectedLines</c>
    /// yalnızca çalışma ağacı tarafında, <c>unstageSelectedLines</c> yalnızca index tarafında
    /// görünüyor.
    /// </remarks>
    bool IPartialStagingHost.CanStage => SelectedRow is { IsStagedSide: false };

    /// <inheritdoc />
    bool IPartialStagingHost.CanUnstage => SelectedRow is { IsStagedSide: true };

    /// <inheritdoc />
    async Task IPartialStagingHost.ApplyAsync(FileDiff diff, PatchSelection selection, bool stage)
    {
        if (WorkingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        // Kodlama diff'in okunduğu kodlama olmalı: yama git'in çalışma ağacındaki baytlarla
        // karşılaştırdığı metin (P05-T04'te iki tur hata yapılmıştı).
        if (stage)
        {
            await _staging
                .StagePartialAsync(directory, diff, selection, Diff.ContentEncoding)
                .ConfigureAwait(true);
        }
        else
        {
            await _staging
                .UnstagePartialAsync(directory, diff, selection, Diff.ContentEncoding)
                .ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Commit'te hook doğrulaması atlansın mı (P05-T15)?
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ÖLÇÜLDÜ (P05-T07):</b> bu bayrak "hook'ları atla" DEĞİL — yalnızca
    /// <c>pre-commit</c> ve <c>commit-msg</c> atlanıyor; <c>prepare-commit-msg</c> ve
    /// <c>post-commit</c> çalışmaya devam ediyor, yani mesaj hâlâ değişebilir.
    /// <para>
    /// Onay diyaloğu <b>yok</b>: atlanan hook veri kaybettirmiyor, oluşan commit reflog'da
    /// duruyor ve geri alınabiliyor (P05-T15'te ölçüldü). Diyalog yalnızca geri
    /// getirilemeyen işlemler için var.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    public partial bool SkipHooks { get; set; }

    /// <summary>
    /// Yıkıcı işlemler için onay soran taraf (P05-T15).
    /// </summary>
    /// <remarks>
    /// Ctor yerine <b>özellik</b>: onay diyaloğu sahip bir pencere istiyor, ViewModel ise
    /// pencereden önce kuruluyor. Aynı desen <see cref="DiffViewModel.StagingHost"/>'ta da
    /// kullanıldı (P05-T10).
    /// <para>
    /// Atanmamışsa sıfırlama komutları <b>hiçbir şey yapmaz</b>: onaysız yıkıcı işlem
    /// çalıştırmaktansa hiç çalıştırmamak yeğdir.
    /// </para>
    /// </remarks>
    public IDestructiveActionConfirmer? Confirmer { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Diff panelinden gelen <b>yıkıcı</b> istek: dosya listesindeki sıfırlamayla aynı
    /// güvenlik ağından geçiyor — onay, yedek, geri alma şeridi. Farkı yalnızca kapsamı.
    /// </remarks>
    async Task IPartialStagingHost.DiscardAsync(FileDiff diff, PatchSelection selection)
    {
        if (WorkingDirectory is not { Length: > 0 } directory
            || _workingTreeWriter is null
            || Confirmer is null)
        {
            return;
        }

        ResetChangesDecision? decision = await RequestResetAsync(new ResetChangesRequest
        {
            ModifiedPaths = [diff.Path],
            UntrackedPaths = [],

            // Yama yalnızca çalışma ağacına uygulanıyor; index'e dokunulmuyor (ölçüldü).
            IncludesStaged = false,
            CanSuppress = true,
        }).ConfigureAwait(true);

        if (decision is null)
        {
            return;
        }

        IReadOnlyList<DiscardBackup> backups = await _workingTreeWriter
            .DiscardPartialAsync(
                directory,
                diff,
                selection,
                userConfirmed: true,
                Diff.ContentEncoding)
            .ConfigureAwait(true);

        RecordReset(
            backups,
            backups.Count == 0
                ? "The selected lines were reverted."
                : $"The selected lines were reverted. {diff.Path.Name} was backed up.");

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Seçili dosyanın diff'i — sağ paneldeki bileşen.</summary>
    public DiffViewModel Diff { get; }

    /// <summary>Stage'lenmemiş değişiklikler; <b>takip edilmeyenler dahil</b>.</summary>
    public AvaloniaList<WorkingTreeFileRow> Unstaged { get; } = [];

    /// <summary>Stage'lenmiş değişiklikler.</summary>
    public AvaloniaList<WorkingTreeFileRow> Staged { get; } = [];

    /// <summary>Açık deponun çalışma dizini; depo yoksa <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? WorkingDirectory { get; private set; }

    [ObservableProperty]
    public partial int SelectedUnstagedIndex { get; set; } = -1;

    [ObservableProperty]
    public partial int SelectedStagedIndex { get; set; } = -1;

    /// <summary>
    /// Odaklı liste <b>stage'lenmiş</b> olan mı?
    /// </summary>
    /// <remarks>
    /// İki listede aynı anda seçim olabilir; diff hangisini göstereceğini bilmek zorunda.
    /// Kullanıcının en son dokunduğu liste kazanır.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsStagedListActive { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanCommit));

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Hatanın tam git çıktısı (P05-T07).</summary>
    [ObservableProperty]
    public partial GitOutputViewModel? ErrorDetails { get; private set; }

    /// <summary>Commit edilecek bir şey var mı?</summary>
    public bool HasStagedChanges => Staged.Count > 0;

    /// <summary>Hiç değişiklik yok mu?</summary>
    [ObservableProperty]
    public partial bool IsClean { get; private set; }

    /// <summary>Commit mesajı kutusu (P05-T12) ve yardımcıları (P05-T13).</summary>
    public CommitMessageViewModel Message { get; }

    /// <summary>Son commit'in üzerine yaz (<c>--amend</c>).</summary>
    /// <remarks>
    /// ⚠️ Yayınlanmış bir commit'te geçmişi yeniden yazar; uyarı P05-T15'te gelecek.
    /// </remarks>
    [ObservableProperty]
    public partial bool Amend { get; set; }

    /// <summary>
    /// Commit oluşturulabilir mi?
    /// </summary>
    /// <remarks>
    /// Bileşik koşul XAML'de değil <b>burada</b> (P04-T10 kararı). Mesaj boşken commit
    /// düğmesini açık bırakmak, git'in reddedeceği bir işlemi sunmak olurdu — boş mesaj
    /// çıkış 1 veriyor (P05-T06'da ölçüldü).
    /// </remarks>
    public bool CanCommit => !IsBusy && !Message.IsEmpty && (HasStagedChanges || Amend);

    /// <summary>Commit sonrası gösterilecek çıktı; yoksa <see langword="null"/> (P05-T07).</summary>
    [ObservableProperty]
    public partial GitOutputViewModel? CommitOutput { get; set; }

    partial void OnAmendChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCommit));

        // Amend işaretlenince düzeltilecek mesajı görmek gerekiyor: kutu boşken HEAD'in
        // mesajı yükleniyor (GitExtensions'ta da koşul bu). Dolu kutuya dokunulmuyor —
        // kullanıcı yeni bir mesaj yazmaya başladıysa amend'i işaretlemek onu silmemeli.
        if (value)
        {
            _ = Message.LoadHeadMessageAsync();
        }
    }

    /// <summary>
    /// Stage'lenmiş değişikliklerden commit oluşturur.
    /// </summary>
    /// <remarks>
    /// Başarıda mesaj <b>temizleniyor</b>: aynı metinle ikinci bir commit atmak neredeyse her
    /// zaman kazadır. Hook çıktısı veya mesaj değişikliği varsa
    /// <see cref="CommitOutput"/> doluyor (P05-T07).
    /// </remarks>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (WorkingDirectory is not { Length: > 0 } directory || !CanCommit)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ErrorDetails = null;
            CommitOutput = null;

            CommitResult result = await _commitWriter
                .CommitAsync(directory, Message.Text, new CommitOptions { Amend = Amend, SkipHooks = SkipHooks }, cancellationToken)
                .ConfigureAwait(true);

            // Kutu ve TASLAK birlikte temizleniyor: commit'lenen metin diskte kalsaydı ekran
            // bir daha açıldığında geri gelir ve ikinci bir commit'e davet ederdi (P05-T13).
            await Message.OnCommittedAsync(cancellationToken).ConfigureAwait(true);
            Amend = false;

            // Gösterilecek bir şey varsa: hook konuştu ya da mesaj değişti.
            CommitOutput = result.NeedsReporting ? GitOutputViewModel.ForCommit(result) : null;
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
            ErrorDetails = GitOutputViewModel.ForFailure(ex);
            IsBusy = false;
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Etkin listedeki seçili satır.</summary>
    public WorkingTreeFileRow? SelectedRow =>
        IsStagedListActive
            ? Get(Staged, SelectedStagedIndex)
            : Get(Unstaged, SelectedUnstagedIndex);

    private static WorkingTreeFileRow? Get(AvaloniaList<WorkingTreeFileRow> rows, int index) =>
        index >= 0 && index < rows.Count ? rows[index] : null;

    partial void OnSelectedUnstagedIndexChanged(int value)
    {
        if (value >= 0 && !_refreshingSelection)
        {
            IsStagedListActive = false;
        }

        OnSelectionChanged();
    }

    partial void OnSelectedStagedIndexChanged(int value)
    {
        if (value >= 0 && !_refreshingSelection)
        {
            IsStagedListActive = true;
        }

        OnSelectionChanged();
    }

    partial void OnIsStagedListActiveChanged(bool value) => OnSelectionChanged();

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedRow));

        // Seçim taraf değiştirdiyse hangi kısmi staging komutunun açık olduğu da değişti.
        Diff.NotifyStagingAvailabilityChanged();

        _ = ShowSelectedDiffAsync();
    }

    /// <summary>
    /// Seçili dosyanın diff'ini sağ panele yükler.
    /// </summary>
    /// <remarks>
    /// Diff <b>tüm</b> taraf için tek çağrıda okunuyor ve seçim yalnızca hangi dosyanın
    /// gösterileceğini belirliyor. Dosya başına ayrı <c>git</c> çalıştırmak, kullanıcı ok
    /// tuşuyla listede gezerken satır başına bir süreç demekti (P04-T08'de aynı hata
    /// ölçülmüştü).
    /// </remarks>
    private async Task ShowSelectedDiffAsync()
    {
        WorkingTreeFileRow? row = SelectedRow;

        if (WorkingDirectory is not { Length: > 0 } directory || row is null)
        {
            Diff.Clear();
            return;
        }

        await Diff.ShowWorkingTreeAsync(directory, row.IsStagedSide, row.Path.Value)
            .ConfigureAwait(true);

        Diff.SelectPath(row.Path);
    }

    /// <summary>
    /// Depoyu bağlar ve durumu okur.
    /// </summary>
    public async Task OpenAsync(string? workingDirectory, CancellationToken cancellationToken = default)
    {
        WorkingDirectory = workingDirectory;

        // Taslak, git'in hazırladığı mesaj (merge/cherry-pick) ve şablon durumu burada
        // yükleniyor; kutu doluysa hiçbiri üzerine yazmıyor (P05-T13).
        await Message.OpenAsync(workingDirectory, cancellationToken).ConfigureAwait(true);

        if (string.IsNullOrEmpty(workingDirectory))
        {
            Unstaged.Clear();
            Staged.Clear();
            Diff.Clear();
            IsClean = true;
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// İzleyici bir değişiklik bildirdiğinde çalışır (P05-T14).
    /// </summary>
    /// <remarks>
    /// Olay <b>zamanlayıcı iş parçacığında</b> geliyor; koleksiyonlara ancak UI iş
    /// parçacığında dokunulabilir.
    /// </remarks>
    private void OnRepositoryChanged(object? sender, RepositoryChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => _ = AutoRefreshAsync());

    /// <summary>
    /// Dışarıdan gelen değişiklik sonrası kendiliğinden tazeler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Yazılan commit mesajına <b>dokunmaz</b>: tazeleme yalnızca dosya listelerini ve diff'i
    /// yeniler. Kullanıcının yazdığı metni bir arka plan olayının silmesi kabul edilemez
    /// (P05-T13'ün değişmezi).
    /// </para>
    /// <para>
    /// <b>Askıya alma şart:</b> ölçümde <c>git status</c> bir depoda ilk çalıştığında
    /// index'i yeniden yazıyor — yani tazelemenin kendisi yeni bir tazeleme olayı
    /// doğurabiliyor. Askı bu zinciri kesiyor.
    /// </para>
    /// </remarks>
    private async Task AutoRefreshAsync()
    {
        if (IsBusy || WorkingDirectory is not { Length: > 0 })
        {
            return;
        }

        using IDisposable? suspension = _watcher?.Suspend();

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Çalışma dizini durumunu yeniden okur.
    /// </summary>
    /// <remarks>
    /// Seçim <b>korunmaya çalışılır</b>: kullanıcı bir dosyayı stage'ledikten sonra listenin
    /// başına fırlamak, sıradaki dosyaya her seferinde elle gitmek demek olurdu. Dosya
    /// listeden çıktıysa seçim <b>aynı konumda</b> kalır (yani sıradaki dosyaya kayar).
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (WorkingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        _refreshing?.Cancel();
        _refreshing?.Dispose();
        _refreshing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        CancellationToken token = _refreshing.Token;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ErrorDetails = null;

            WorkingTreeStatus status = await _statusReader
                .ReadAsync(directory, includeIgnored: false, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            Replace(Unstaged, [.. status.Unstaged.Concat(status.Untracked)
                .OrderBy(entry => entry.Path)
                .Select(entry => new WorkingTreeFileRow(entry, staged: false))]);

            Replace(Staged, [.. status.Staged
                .OrderBy(entry => entry.Path)
                .Select(entry => new WorkingTreeFileRow(entry, staged: true))]);

            _refreshingSelection = true;

            try
            {
                SelectedUnstagedIndex = Clamp(SelectedUnstagedIndex, Unstaged.Count);
                SelectedStagedIndex = Clamp(SelectedStagedIndex, Staged.Count);
            }
            finally
            {
                _refreshingSelection = false;
            }

            // ⚠️ Etkin liste boşalsa bile KARŞI listeye atlanmıyor. Atlamak cazip görünüyor
            // ("gösterilecek bir şey kalsın") ama tehlikeli: kullanıcı son dosyasını
            // stage'ledikten sonra etkin liste staged olurdu ve `Space` tuşu bu kez
            // az önce stage'lediği dosyayı GERİ ALIRDI. Boş liste boş kalıyor.

            IsClean = Unstaged.Count == 0 && Staged.Count == 0;

            OnPropertyChanged(nameof(HasStagedChanges));
            OnPropertyChanged(nameof(CanCommit));

            // Seçim aynı indekste kalsa bile ARDINDAKİ dosya değişmiş olabilir; diff
            // yenilenmezse kullanıcı başka bir dosyanın içeriğine bakar.
            await ShowSelectedDiffAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
            ErrorDetails = GitOutputViewModel.ForFailure(ex);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    private static void Replace(
        AvaloniaList<WorkingTreeFileRow> target,
        IReadOnlyList<WorkingTreeFileRow> rows)
    {
        target.Clear();
        target.AddRange(rows);
    }

    /// <summary>
    /// Seçimi liste sınırları içinde tutar.
    /// </summary>
    /// <remarks>
    /// Dosya stage'lendiğinde listeden çıkıyor; indeksi <b>korumak</b> seçimi kendiliğinden
    /// sıradaki dosyaya taşıyor. Son satırdaysa bir yukarı çekiliyor.
    /// </remarks>
    private static int Clamp(int index, int count)
    {
        if (count == 0)
        {
            return -1;
        }

        return index < 0 ? 0 : Math.Min(index, count - 1);
    }

    /// <summary>
    /// Değişiklikleri sıfırlar — <b>yıkıcı</b> (P05-T15).
    /// </summary>
    /// <param name="scope">
    /// <see cref="DiscardScope.UnstagedOnly"/> yalnızca çalışma ağacını,
    /// <see cref="DiscardScope.All"/> stage'lenmiş içeriği de atar.
    /// </param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    /// <remarks>
    /// <para>
    /// GitExtensions'ta karşılığı <c>FormCommit</c>'teki *Reset all changes* /
    /// *Reset unstaged changes* düğmeleri; onlar da <c>FormResetChanges</c> ile
    /// soruyor ve "yeni dosyaları da sil" seçeneğini aynı diyalogda sunuyor (§ 9).
    /// </para>
    /// <para>
    /// 🔑 <b>Onay atlanabilir, güvenlik ağı atlanamaz.</b> Kullanıcı "bir daha sorma"
    /// dese bile içerik her zaman yedekleniyor ve işlem sonrası geri alma sunuluyor.
    /// </para>
    /// </remarks>
    public async Task ResetChangesAsync(
        DiscardScope scope,
        CancellationToken cancellationToken = default)
    {
        if (WorkingDirectory is not { Length: > 0 } directory
            || _workingTreeWriter is null
            || Confirmer is null
            || IsBusy)
        {
            return;
        }

        List<RepositoryPath> modified = [];
        List<RepositoryPath> untracked = [];

        foreach (WorkingTreeFileRow row in Unstaged)
        {
            (row.IsUntracked ? untracked : modified).Add(row.Path);
        }

        if (scope == DiscardScope.All)
        {
            // Stage'lenmiş dosyalar da HEAD'e dönecek; kullanıcıya sayısı söylenmeli.
            foreach (WorkingTreeFileRow row in Staged)
            {
                if (!modified.Contains(row.Path))
                {
                    modified.Add(row.Path);
                }
            }
        }

        if (modified.Count == 0 && untracked.Count == 0)
        {
            return;
        }

        ResetChangesDecision? decision = await RequestResetAsync(new ResetChangesRequest
        {
            ModifiedPaths = modified,
            UntrackedPaths = untracked,
            IncludesStaged = scope == DiscardScope.All,

            // Her iki yol da yedekleniyor (P05-T15), yani bastırma güvenli.
            CanSuppress = true,
        }).ConfigureAwait(true);

        if (decision is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ErrorDetails = null;

            List<DiscardBackup> backups = [];

            if (modified.Count > 0)
            {
                backups.AddRange(await _workingTreeWriter
                    .DiscardChangesAsync(directory, modified, scope, userConfirmed: true, cancellationToken)
                    .ConfigureAwait(true));
            }

            if (decision.DeleteUntracked && untracked.Count > 0)
            {
                backups.AddRange(await _workingTreeWriter
                    .DeleteUntrackedAsync(directory, untracked, userConfirmed: true, cancellationToken)
                    .ConfigureAwait(true));
            }

            IsBusy = false;
            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            RecordReset(
                backups,
                backups.Count == 0
                    ? "The changes were reset."
                    : $"The changes were reset. The content of {backups.Count} files was backed up.");
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
            ErrorDetails = GitOutputViewModel.ForFailure(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Yıkıcı bir işlem için onay ister ve "bir daha sorma" seçimini uygular (P05-T15).
    /// </summary>
    /// <returns>
    /// Kullanıcının kararı; iptal edildiyse <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Dosya listesinden ve diff panelinden gelen iki yıkıcı yol da buradan geçiyor:
    /// bastırma kuralı tek yerde durmazsa, ikinci yolun onu uygulamayı unutması sessiz
    /// bir davranış farkı olurdu.
    /// </remarks>
    private async Task<ResetChangesDecision?> RequestResetAsync(ResetChangesRequest request)
    {
        if (_suppressResetPrompt)
        {
            return new ResetChangesDecision { Confirmed = true };
        }

        ResetChangesDecision decision =
            await Confirmer!.ConfirmResetAsync(request).ConfigureAwait(true);

        if (!decision.Confirmed)
        {
            return null;
        }

        if (decision.DoNotAskAgain)
        {
            _suppressResetPrompt = true;
        }

        return decision;
    }

    /// <summary>
    /// Yıkıcı bir işlemin yedeklerini ve özetini kaydeder; şerit bunlardan besleniyor.
    /// </summary>
    private void RecordReset(IReadOnlyList<DiscardBackup> backups, string notice)
    {
        _lastResetBackups.Clear();
        _lastResetBackups.AddRange(backups);

        ResetNotice = notice;

        OnPropertyChanged(nameof(CanUndoReset));
    }

    /// <summary>
    /// Son sıfırlamanın özeti; şerit olarak gösteriliyor. Yoksa <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? ResetNotice { get; private set; }

    /// <summary>Son sıfırlama geri alınabilir mi?</summary>
    public bool CanUndoReset => _lastResetBackups.Count > 0;

    /// <summary>
    /// Son sıfırlamayı geri alır (P05-T15).
    /// </summary>
    /// <remarks>
    /// 🔑 Güvenlik ağının asıl değeri burada: kullanıcıya blob kimliği verip
    /// <c>git cat-file</c> yazmasını beklemek panik anında işe yaramaz. Yedeği olmayan bir
    /// işlem için bu komut hiç görünmüyor.
    /// </remarks>
    public async Task UndoResetAsync(CancellationToken cancellationToken = default)
    {
        if (WorkingDirectory is not { Length: > 0 } directory
            || _workingTreeWriter is null
            || _lastResetBackups.Count == 0)
        {
            return;
        }

        IReadOnlyList<DiscardBackup> backups = [.. _lastResetBackups];

        IReadOnlyList<DiscardBackup> restored = await _workingTreeWriter
            .RestoreBackupsAsync(directory, backups, cancellationToken)
            .ConfigureAwait(true);

        // Yedekler tüketildi: aynı geri almayı ikinci kez sunmak anlamsız. Kısmi kurtarma
        // ise sessizce "başarılı" gösterilmemeli — yedeği budanmış dosyalar
        // (`gc --prune=now`) geri gelmez ve kullanıcı bunu bilmeli.
        RecordReset(
            [],
            restored.Count == backups.Count
                ? $"{restored.Count} files were restored."
                : $"{restored.Count}/{backups.Count} files were restored; "
                  + "the rest have no backup in the object database.");

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Şeridi kapatır.</summary>
    public void ClearResetNotice()
    {
        _lastResetBackups.Clear();
        ResetNotice = null;
        OnPropertyChanged(nameof(CanUndoReset));
    }

    /// <summary>Seçili stage'lenmemiş dosyayı stage'ler.</summary>
    public Task StageSelectedAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            paths => _staging.StageAsync(WorkingDirectory!, paths, cancellationToken),
            Get(Unstaged, SelectedUnstagedIndex),
            cancellationToken);

    /// <summary>Tüm stage'lenmemiş dosyaları stage'ler.</summary>
    public Task StageAllAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            paths => _staging.StageAsync(WorkingDirectory!, paths, cancellationToken),
            [.. Unstaged],
            cancellationToken);

    /// <summary>Seçili stage'lenmiş dosyayı geri alır.</summary>
    public Task UnstageSelectedAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            paths => _staging.UnstageAsync(WorkingDirectory!, paths, cancellationToken),
            Get(Staged, SelectedStagedIndex),
            cancellationToken);

    /// <summary>Tüm stage'lenmiş dosyaları geri alır.</summary>
    public Task UnstageAllAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            paths => _staging.UnstageAsync(WorkingDirectory!, paths, cancellationToken),
            [.. Staged],
            cancellationToken);

    private Task RunAsync(
        Func<IReadOnlyList<RepositoryPath>, Task> operation,
        WorkingTreeFileRow? row,
        CancellationToken cancellationToken) =>
        RunAsync(operation, row is null ? [] : [row], cancellationToken);

    private async Task RunAsync(
        Func<IReadOnlyList<RepositoryPath>, Task> operation,
        IReadOnlyList<WorkingTreeFileRow> rows,
        CancellationToken cancellationToken)
    {
        if (WorkingDirectory is not { Length: > 0 } || rows.Count == 0)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ErrorDetails = null;

            await operation([.. rows.Select(row => row.Path)]).ConfigureAwait(true);
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
            ErrorDetails = GitOutputViewModel.ForFailure(ex);
            IsBusy = false;
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// İzleyici aboneliğini bırakır (P05-T14).
    /// </summary>
    /// <remarks>
    /// İzleyici <b>uygulama ömrü boyunca yaşayan</b> tek bir nesne; commit penceresi ise
    /// açılıp kapanıyor. Abonelik bırakılmazsa kapatılmış her pencere olayları almaya devam
    /// eder ve kapalı bir ekran için <c>git status</c> çalıştırılır.
    /// </remarks>
    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.Changed -= OnRepositoryChanged;
        }

        _refreshing?.Cancel();
        _refreshing?.Dispose();
        _refreshing = null;
    }
}
