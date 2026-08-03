namespace GitExt.Core;

/// <summary>
/// Depoda bir değişiklik algılandığında taşınan veri.
/// </summary>
public sealed class RepositoryChangedEventArgs : EventArgs
{
    public RepositoryChangedEventArgs(RepositoryChangeKind kind) => Kind = kind;

    public RepositoryChangeKind Kind { get; }
}

/// <summary>
/// Çalışma ağacını ve git dizinini izleyip değişiklikleri bildirir (P05-T14).
/// </summary>
public interface IRepositoryWatcher : IDisposable
{
    /// <summary>
    /// Birleştirilmiş bir değişiklik algılandığında tetiklenir.
    /// <b>UI iş parçacığında değil</b>, zamanlayıcı iş parçacığında çağrılır.
    /// </summary>
    event EventHandler<RepositoryChangedEventArgs>? Changed;

    /// <summary>İzleme etkin mi? Hata durumunda kendiliğinden kapanabilir.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Verilen depoyu izlemeye başlar. Önceki izleme varsa durdurulur.
    /// </summary>
    /// <returns>
    /// İzleme kurulabildiyse <see langword="true"/>. <b>Hata fırlatmaz</b> — otomatik
    /// tazeleme bir kolaylıktır, kurulamadıysa uygulama elle tazelemeyle çalışmaya devam eder.
    /// </returns>
    bool Start(string workingTreeRoot, string gitDirectory, string commonDirectory);

    /// <summary>İzlemeyi durdurur ve bekleyen değişikliği atar.</summary>
    void Stop();

    /// <summary>
    /// İzlemeyi geçici olarak askıya alır; dönen nesne bırakıldığında devam eder.
    /// </summary>
    /// <remarks>
    /// Kendi yazma işlemlerimiz sırasında kullanılır: stage/commit zaten kendi tazelemesini
    /// yapıyor, izleyicinin aynı işi bir kez daha tetiklemesi boşuna <c>git status</c> demek.
    /// İç içe çağrılabilir.
    /// </remarks>
    IDisposable Suspend();
}

/// <inheritdoc cref="IRepositoryWatcher"/>
/// <remarks>
/// <para>
/// <b>⚠️ ÖLÇÜLDÜ — iki ayrı izleyici gerekiyor.</b> Normal bir depoda <c>.git</c> çalışma
/// ağacının altındadır ve tek izleyici yeter. Bağlı çalışma ağacında (<c>git worktree</c>)
/// ve alt modülde git dizini <b>başka yerdedir</b>; ikinci izleyici yalnızca o durumda
/// kuruluyor, aksi halde her olay iki kez gelirdi.
/// </para>
/// <para>
/// <b>⚠️ ÖLÇÜLDÜ — <c>EnableRaisingEvents</c> istisna fırlatabilir.</b> Linux'ta her izleyici
/// bir <c>inotify</c> örneği tüketiyor ve kullanıcı başına sınır bu makinede 1024; ölçümde
/// <b>949. izleyicide <c>IOException</c></b> alındı. Sarmalanmazsa uygulama depo açarken
/// çöker. İzlenen dizin sayısı da doğrudan maliyet: 11.512 dizinlik bir ağaçta 11.512
/// <c>inotify</c> izlemesi, 104 ms kurulum ve ~30 MB bellek ölçüldü.
/// </para>
/// </remarks>
public sealed class RepositoryWatcher : IRepositoryWatcher
{
    /// <summary>Son olaydan sonra beklenen sessizlik.</summary>
    public static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>İlk bekleyen olaydan sonraki üst sınır.</summary>
    public static readonly TimeSpan DefaultMaximumDelay = TimeSpan.FromSeconds(2);

    /// <summary>İki tazeleme arasındaki en kısa süre.</summary>
    public static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Kaçan olaylara karşı güvenlik ağı olarak yapılan periyodik tazeleme.
    /// </summary>
    /// <remarks>
    /// <b>⚠️ ÖLÇÜLDÜ:</b> hızla oluşan derin bir dizin ağacında ara olaylar kaybolabiliyor —
    /// <c>yeni/derin</c> iki seviyesi tek çağrıda oluşturulduğunda <c>derin</c> için
    /// <c>Created</c> hiç gelmedi (izleme eklenene kadar dizin çoktan oluşmuştu). Ağ
    /// dosya sistemleri ve WSL'de kayıp çok daha yaygın. Periyodik tazeleme bu boşluğu
    /// kapatıyor; sıklığı düşük çünkü asıl yol izleyicidir.
    /// </remarks>
    public static readonly TimeSpan DefaultPeriodicInterval = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly ChangeCoalescer _coalescer;
    private readonly TimeProvider _time;
    private readonly TimeSpan _periodicInterval;
    private readonly ITimer _timer;
    private readonly ITimer _periodicTimer;

    private FileSystemWatcher? _workTreeWatcher;
    private FileSystemWatcher? _gitDirectoryWatcher;
    private FileSystemWatcher? _commonDirectoryWatcher;
    private int _suspendCount;
    private bool _disposed;

    public RepositoryWatcher(
        TimeSpan? debounceDelay = null,
        TimeSpan? maximumDelay = null,
        TimeSpan? minimumInterval = null,
        TimeSpan? periodicInterval = null,
        TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _coalescer = new ChangeCoalescer(
            debounceDelay ?? DefaultDebounceDelay,
            maximumDelay ?? DefaultMaximumDelay,
            minimumInterval ?? DefaultMinimumInterval);

        _periodicInterval = periodicInterval ?? DefaultPeriodicInterval;

        _timer = _time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _periodicTimer = _time.CreateTimer(
            _ => OnRawChange(RepositoryChangeKind.Repository), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<RepositoryChangedEventArgs>? Changed;

    public bool IsRunning
    {
        get { lock (_gate) { return _workTreeWatcher is not null; } }
    }

    public bool Start(string workingTreeRoot, string gitDirectory, string commonDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingTreeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(commonDirectory);

        Stop();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            string root = Path.GetFullPath(workingTreeRoot);
            string gitDir = Path.GetFullPath(gitDirectory);
            string commonDir = Path.GetFullPath(commonDirectory);

            try
            {
                _workTreeWatcher = CreateWatcher(
                    root, RepositoryChangeClassifier.ClassifyWorkingTreePath);

                // Git dizini çalışma ağacının ALTINDAYSA zaten izleniyor; ikinci izleyici
                // yalnızca bağlı çalışma ağacı / alt modül durumunda gerekli.
                if (!IsInside(gitDir, root))
                {
                    _gitDirectoryWatcher = CreateWatcher(
                        gitDir, RepositoryChangeClassifier.ClassifyGitDirectoryPath);
                }

                // ⚠️ Bağlı çalışma ağacında ref'ler burada; git dizininde DEĞİL. Yalnızca
                // git dizinine bakılsaydı o ağaçtaki commit hiç görülmezdi (ölçüldü).
                if (!PathsEqual(commonDir, gitDir) && !IsInside(commonDir, root) && !IsInside(commonDir, gitDir))
                {
                    _commonDirectoryWatcher = CreateWatcher(
                        commonDir, RepositoryChangeClassifier.ClassifyGitDirectoryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // inotify sınırı, izin hatası veya silinmiş dizin. Otomatik tazeleme
                // kolaylıktır; uygulamayı çökertmemeli.
                DisposeWatchers();
                return false;
            }

            _periodicTimer.Change(_periodicInterval, _periodicInterval);
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            DisposeWatchers();
            _coalescer.Reset();
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _periodicTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public IDisposable Suspend()
    {
        lock (_gate)
        {
            _suspendCount++;
        }

        return new Suspension(this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeWatchers();
        }

        _timer.Dispose();
        _periodicTimer.Dispose();
    }

    private static bool IsInside(string candidate, string root)
    {
        string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.Ordinal);

    /// <summary>
    /// Bir dizini izleyen <see cref="FileSystemWatcher"/> kurar.
    /// </summary>
    /// <param name="root">
    /// İzlenecek dizin. Olay yolları buna göre <b>göreli</b> hâle getirilip
    /// <paramref name="classifier"/>'a veriliyor.
    /// </param>
    /// <param name="classifier">Göreli yolu sınıflandıran kural.</param>
    /// <remarks>
    /// Kök, alan yerine <b>kapanışta</b> tutuluyor: izleyici ile kökü aynı anda doğuyor ve
    /// birlikte ölüyorlar, yani ayrı bir alan tutmak olay başına gereksiz bir kilit
    /// alışı demekti — tek bir dal değişimi 2102 olay üretiyor (ölçüldü). Kapanış
    /// yalnızca bir <see cref="string"/> yakalıyor.
    /// </remarks>
    private FileSystemWatcher CreateWatcher(
        string root,
        Func<string, RepositoryChangeKind?> classifier)
    {
        void Handle(string fullPath)
        {
            if (classifier(Path.GetRelativePath(root, fullPath)) is { } kind)
            {
                OnRawChange(kind);
            }
        }

        FileSystemWatcher watcher = new(root)
        {
            IncludeSubdirectories = true,

            // LastWrite + Size: içerik değişimi. FileName + DirectoryName: oluşturma,
            // silme, yeniden adlandırma. Ref güncellemesi `x.lock → x` yeniden adlandırması
            // olarak geldiği için DirectoryName/FileName olmadan commit'ler kaçardı.
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
        };

        watcher.Changed += (_, e) => Handle(e.FullPath);
        watcher.Created += (_, e) => Handle(e.FullPath);
        watcher.Deleted += (_, e) => Handle(e.FullPath);

        // Yeniden adlandırmada YENİ ad kullanılıyor: ref güncellemesinin kaynağı
        // `refs/heads/x.lock`, hedefi `refs/heads/x`. Eski ad kullanılsaydı kilit
        // filtresi gerçek sinyali yerdi.
        watcher.Renamed += (_, e) => Handle(e.FullPath);

        // Olay kuyruğu taşarsa hangi dosyaların kaçtığı bilinmiyor; tek doğru cevap
        // her şeyi yeniden okumak.
        watcher.Error += (_, _) => OnRawChange(RepositoryChangeKind.Repository);

        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnRawChange(RepositoryChangeKind kind)
    {
        TimeSpan wait;

        lock (_gate)
        {
            if (_disposed || _workTreeWatcher is null)
            {
                return;
            }

            wait = _coalescer.Add(kind, _time.GetUtcNow());

            if (_suspendCount > 0)
            {
                // Askıdayken zamanlayıcı kurulmuyor; devam edildiğinde kurulacak.
                return;
            }

            _timer.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        RepositoryChangeKind? kind;

        lock (_gate)
        {
            if (_disposed || _workTreeWatcher is null || _suspendCount > 0)
            {
                return;
            }

            kind = _coalescer.TryTake(_time.GetUtcNow(), out TimeSpan? wait);

            if (kind is null)
            {
                if (wait is not null)
                {
                    _timer.Change(wait.Value, Timeout.InfiniteTimeSpan);
                }

                return;
            }
        }

        // Olay kilidin DIŞINDA tetikleniyor: abone tazeleme yapacak ve o tazeleme yeni
        // olaylar üretecek; kilit tutulsaydı olay işleyicileri birbirini bekletirdi.
        Changed?.Invoke(this, new RepositoryChangedEventArgs(kind.Value));
    }

    private void Resume()
    {
        lock (_gate)
        {
            if (_suspendCount > 0)
            {
                _suspendCount--;
            }

            if (_suspendCount > 0 || _disposed || _workTreeWatcher is null)
            {
                return;
            }

            if (_coalescer.HasPending)
            {
                _timer.Change(_coalescer.DebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void DisposeWatchers()
    {
        _workTreeWatcher?.Dispose();
        _gitDirectoryWatcher?.Dispose();
        _commonDirectoryWatcher?.Dispose();
        _workTreeWatcher = null;
        _gitDirectoryWatcher = null;
        _commonDirectoryWatcher = null;
    }

    private sealed class Suspension : IDisposable
    {
        private RepositoryWatcher? _owner;

        public Suspension(RepositoryWatcher owner) => _owner = owner;

        public void Dispose()
        {
            RepositoryWatcher? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Resume();
        }
    }
}
