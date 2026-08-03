namespace GitExt.Core;

/// <summary>
/// Dosya sistemi olaylarını tek bir tazelemede birleştirir (P05-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zamanlayıcıdan bilinçli olarak ayrıldı.</b> Burada yalnızca "şimdi ne kadar beklenmeli"
/// hesabı var; gerçek zamanlayıcı <see cref="RepositoryWatcher"/> tarafında. Böylece
/// birleştirme kuralları gerçek zaman geçirmeden, deterministik test edilebiliyor —
/// aksi halde her test yüzlerce milisaniye uyumak zorunda kalır ve yavaş makinede kırılırdı.
/// </para>
/// <para>
/// <b>⚠️ ÖLÇÜLDÜ — üç sayı bu tasarımı belirledi:</b>
/// </para>
/// <list type="bullet">
///   <item>800 dosyalık dal değişimi <b>2102 olay</b> üretti, hepsi ~50 ms içinde.
///     Gecikme olmadan 2102 <c>git status</c> çalışırdı.</item>
///   <item>Tek bir dosyayı kaydetmek bile <b>2 olay</b>, editörlerin yaptığı atomik kaydetme
///     (geçici dosya + yeniden adlandırma) <b>4 olay</b>.</item>
///   <item>Tek projelik bir <c>dotnet build</c> <b>92 olay</b> üretti ve <b>1,5 saniye</b>
///     sürdü — hepsi git'in yok saydığı <c>obj/</c> altında. Sürekli akan bu tür gürültüde
///     saf "her olayda sayacı sıfırla" debounce <b>hiç tetiklenmez</b>; bu yüzden
///     <see cref="MaximumDelay"/> üst sınırı var.</item>
/// </list>
/// </remarks>
public sealed class ChangeCoalescer
{
    private readonly Lock _gate = new();

    private RepositoryChangeKind? _pending;
    private DateTimeOffset _firstPendingAt;
    private DateTimeOffset _lastEventAt;
    private DateTimeOffset _lastTakenAt = DateTimeOffset.MinValue;

    /// <param name="debounceDelay">Son olaydan sonra beklenecek sessizlik süresi.</param>
    /// <param name="maximumDelay">
    /// İlk bekleyen olaydan sonra en fazla beklenecek süre. Sürekli olay akarken
    /// tazelemenin sonsuza kadar ertelenmesini engeller.
    /// </param>
    /// <param name="minimumInterval">
    /// İki tazeleme arasındaki en kısa süre. <see cref="MaximumDelay"/>'i de ezer:
    /// gürültülü bir depoda üst sınır bu olmalı.
    /// </param>
    public ChangeCoalescer(
        TimeSpan debounceDelay,
        TimeSpan maximumDelay,
        TimeSpan minimumInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(debounceDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDelay, debounceDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumInterval, TimeSpan.Zero);

        DebounceDelay = debounceDelay;
        MaximumDelay = maximumDelay;
        MinimumInterval = minimumInterval;
    }

    public TimeSpan DebounceDelay { get; }

    public TimeSpan MaximumDelay { get; }

    public TimeSpan MinimumInterval { get; }

    /// <summary>Bekleyen bir değişiklik var mı?</summary>
    public bool HasPending
    {
        get { lock (_gate) { return _pending is not null; } }
    }

    /// <summary>
    /// Bir olayı kaydeder ve tetikleme için beklenecek süreyi döndürür.
    /// </summary>
    /// <remarks>
    /// Farklı türden olaylar birleşirken <b>daha kapsamlısı kazanır</b>: çalışma ağacı ve
    /// ref değişimi aynı pencereye düşerse ikisini de kapsayan tam tazeleme yapılır.
    /// </remarks>
    public TimeSpan Add(RepositoryChangeKind kind, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_pending is null)
            {
                _pending = kind;
                _firstPendingAt = now;
            }
            else if (kind == RepositoryChangeKind.Repository)
            {
                _pending = RepositoryChangeKind.Repository;
            }

            _lastEventAt = now;
            return WaitTime(now);
        }
    }

    /// <summary>
    /// Zamanı geldiyse bekleyen değişikliği alır ve durumu sıfırlar.
    /// </summary>
    /// <param name="now">Şu anki zaman.</param>
    /// <param name="wait">
    /// Zamanı gelmediyse tekrar denemeden önce beklenecek süre;
    /// bekleyen değişiklik yoksa <see langword="null"/>.
    /// </param>
    public RepositoryChangeKind? TryTake(DateTimeOffset now, out TimeSpan? wait)
    {
        lock (_gate)
        {
            if (_pending is null)
            {
                wait = null;
                return null;
            }

            TimeSpan remaining = WaitTime(now);

            if (remaining > TimeSpan.Zero)
            {
                wait = remaining;
                return null;
            }

            RepositoryChangeKind taken = _pending.Value;
            _pending = null;
            _lastTakenAt = now;
            wait = null;
            return taken;
        }
    }

    /// <summary>
    /// Bekleyen değişikliği atar. Depo kapanırken veya izleme durdurulurken kullanılır.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _pending = null;
        }
    }

    private TimeSpan WaitTime(DateTimeOffset now)
    {
        // Debounce SON OLAYDAN itibaren sayılır: amaç "olaylar durdu mu" sorusuna cevap
        // vermek. İlk olaydan sayılsaydı bu bir gecikme olurdu, sessizlik beklemek değil.
        TimeSpan wait = DebounceDelay - (now - _lastEventAt);

        // Üst sınır: ilk bekleyen olaydan beri geçen süre.
        TimeSpan cap = MaximumDelay - (now - _firstPendingAt);

        if (wait > cap)
        {
            wait = cap;
        }

        // Alt sınır: son tazelemeden beri geçen süre. Üst sınırı EZER — sürekli yazan bir
        // derleme sırasında tazeleme sıklığını sınırlayan tek şey bu.
        if (_lastTakenAt != DateTimeOffset.MinValue)
        {
            TimeSpan floor = MinimumInterval - (now - _lastTakenAt);

            if (wait < floor)
            {
                wait = floor;
            }
        }

        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }
}
