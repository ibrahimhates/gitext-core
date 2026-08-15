namespace GitExt.Core.Git;

/// <summary>
/// Depoda duran bir kilit dosyası hakkında bilgi (P05-T02).
/// </summary>
/// <param name="Path">Kilit dosyasının tam yolu.</param>
/// <param name="Age">Dosyanın oluşturulmasından bu yana geçen süre.</param>
public sealed record GitLockInfo(string Path, TimeSpan Age)
{
    /// <summary>
    /// Kilit, meşru bir işlem için beklenemeyecek kadar uzun süredir mi duruyor?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Bu bir tahmindir, kanıt değildir</b> — ve bilinçli olarak öyle adlandırıldı.
    /// Ölçüldü: kilit dosyası <b>boş</b> (süreç kimliği yok), git eski bir kilide farklı
    /// davranmıyor, dolayısıyla "sahibi öldü mü" sorusunun güvenilir bir cevabı yok.
    /// </para>
    /// <para>
    /// Eşik ölçüme dayanıyor: meşru kilit süresi <b>milisaniyeler</b> mertebesinde
    /// (300 dosyalık <c>git add</c> = 12 ms) ve yavaş bir <c>pre-commit</c> hook'u bile
    /// kilidi uzatmıyor — hook, kilit alınmadan <b>önce</b> çalışıyor (ölçüldü: 5 saniye
    /// uyuyan hook boyunca <c>index.lock</c> hiç görülmedi).
    /// </para>
    /// </remarks>
    public bool LooksStale => Age > TimeSpan.FromMinutes(5);

    public override string ToString() => $"{Path} ({Age.TotalSeconds:F0} sn)";
}

/// <summary>
/// Kilit dosyalarını inceler ve — <b>yalnızca açık istekle</b> — siler (P05-T02).
/// </summary>
/// <remarks>
/// <para>
/// <b>Kilit asla kendiliğinden silinmez.</b> Başka bir git süreci gerçekten çalışıyor
/// olabilir; o süreç index'i yazarken kilidi silmek deponun index'ini bozar.
/// </para>
/// <para>
/// <b>GitExtensions'a bakıldı:</b> orada da bayat tespiti <b>yok</b> —
/// <c>IndexLockManager</c> yalnızca "dosya var mı" diye bakıyor ve silme işlemi
/// kullanıcının menüden seçtiği <i>Delete index.lock</i> komutuna bağlı. Yani kararı
/// kullanıcı veriyor. Biz de aynısını yapıyoruz, üstüne kullanıcıya karar vermesi için
/// <b>kilidin yaşını</b> gösteriyoruz.
/// </para>
/// </remarks>
public static class GitLock
{
    /// <summary>Index kilidinin dosya adı.</summary>
    public const string IndexLockName = "index.lock";

    /// <summary>
    /// Depodaki index kilidini inceler; yoksa <see langword="null"/>.
    /// </summary>
    /// <param name="gitDirectory">
    /// Deponun git dizini. Worktree'lerde her birinin kendi index'i olduğu için ortak dizin
    /// değil, o worktree'nin dizini verilmelidir.
    /// </param>
    public static GitLockInfo? Inspect(string gitDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);

        string path = Path.Combine(gitDirectory, IndexLockName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            DateTime createdUtc = File.GetLastWriteTimeUtc(path);
            TimeSpan age = DateTime.UtcNow - createdUtc;

            // Saat kayması negatif yaş üretebilir; kullanıcıya "-3 saniyedir kilitli"
            // demektense sıfır göstermek dürüst.
            return new GitLockInfo(path, age > TimeSpan.Zero ? age : TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Dosya inceleme ile silme arasında kaybolmuş olabilir; kilit yok sayılır.
            return null;
        }
    }

    /// <summary>
    /// Kilit dosyasını siler.
    /// </summary>
    /// <param name="lockInfo">Silinecek kilit — önce <see cref="Inspect"/> ile alınmalı.</param>
    /// <param name="userConfirmed">
    /// Kullanıcının silmeyi <b>açıkça</b> onayladığı. <see langword="false"/> ise
    /// <see cref="InvalidOperationException"/> fırlatılır.
    /// </param>
    /// <remarks>
    /// Onay bir parametre olarak <b>zorunlu tutuluyor</b>: "sessizce silme" kuralını yorumda
    /// bırakmak, birinin ileride bu metodu onaysız çağırmasına engel olmaz.
    /// </remarks>
    public static void Remove(GitLockInfo lockInfo, bool userConfirmed)
    {
        ArgumentNullException.ThrowIfNull(lockInfo);

        if (!userConfirmed)
        {
            throw new InvalidOperationException(
                "The lock file can only be deleted with the user's explicit consent. "
                + "Another git process may be running.");
        }

        File.Delete(lockInfo.Path);
    }
}

/// <summary>
/// Kilit çakışmasında yeniden deneme politikası (P05-T02).
/// </summary>
/// <remarks>
/// <b>ÖLÇÜLDÜ:</b> depo kuyruğu (P05-T01) yalnızca <i>bizim</i> yazmalarımızı sıraya sokuyor;
/// kullanıcının terminali veya IDE'si de aynı depoya yazabiliyor. Dışarıdan sürekli yazan bir
/// süreç varken 30 <c>git add</c>'in <b>9'u</b> yeniden deneme gerektirdi (en fazla 6 deneme)
/// ve artan bekleme ile başarısızlık <b>sıfıra</b> indi.
/// </remarks>
public sealed record GitLockRetryOptions
{
    public static GitLockRetryOptions Default { get; } = new();

    /// <summary>Toplam deneme sayısı (ilk deneme dahil).</summary>
    public int MaximumAttempts { get; init; } = 8;

    /// <summary>
    /// İlk bekleme süresi; sonraki denemelerde katları kadar beklenir.
    /// </summary>
    /// <remarks>
    /// Kilit ölçümde ~10 ms tutuluyordu, bu yüzden ilk bekleme kısa. Varsayılan
    /// değerlerle toplam bekleme ~0,5 saniye — kullanıcı bir gecikme fark etmez.
    /// </remarks>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(15);
}

/// <summary>
/// Kilit çakışmasında işlemi yeniden deneyen yardımcı (P05-T02).
/// </summary>
public static class GitLockRetry
{
    /// <summary>
    /// İşlemi çalıştırır; kilit çakışmasında bekleyip yeniden dener.
    /// </summary>
    /// <remarks>
    /// Yalnızca <see cref="GitFailureKind.IndexLocked"/> yeniden denenir. Diğer hatalar
    /// olduğu gibi yükselir — bir kimlik doğrulama hatasını sekiz kez tekrarlamak
    /// kullanıcıyı bekletmekten başka işe yaramaz.
    /// </remarks>
    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        GitLockRetryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        options ??= GitLockRetryOptions.Default;

        int attempts = Math.Max(1, options.MaximumAttempts);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (GitException exception)
                when (exception.Kind == GitFailureKind.IndexLocked && attempt < attempts)
            {
                await Task.Delay(options.InitialDelay * attempt, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc cref="RunAsync{T}"/>
    public static Task RunAsync(
        Func<CancellationToken, Task> operation,
        GitLockRetryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunAsync<object?>(
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return null;
            },
            options,
            cancellationToken);
    }
}
