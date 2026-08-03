namespace GitExt.Core;

/// <summary>
/// Bir dosya sistemi olayının hangi tazelemeyi gerektirdiği (P05-T14).
/// </summary>
public enum RepositoryChangeKind
{
    /// <summary>
    /// Çalışma ağacı veya index değişti → <c>git status</c> yeniden okunmalı.
    /// </summary>
    WorkingTree,

    /// <summary>
    /// Ref'ler, <c>HEAD</c> veya depo durumu değişti → commit listesi de yeniden okunmalı.
    /// </summary>
    Repository,
}

/// <summary>
/// Bir dosya sistemi olayının anlamlı olup olmadığına karar verir (P05-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu sınıf saf tutuldu</b> çünkü izleyicinin tüm zekâsı burada; dosya sistemine ve
/// zamanlayıcıya bağlı olsaydı bu kuralların hiçbiri hızlı ve deterministik test edilemezdi.
/// </para>
/// <para>
/// <b>⚠️ ÖLÇÜLDÜ — plan bu noktada düzeltildi.</b> Plan "<c>.git</c> dizinindeki değişiklikleri
/// filtrele" diyordu. Harfiyen uygulanırsa <b>çalışmaz</b>: başka bir terminalde yapılan
/// <c>git commit</c> ölçümde <b>64 olay</b> üretti ve <b>hepsi <c>.git</c> altındaydı</b>,
/// çalışma ağacında sıfır olay. <c>.git</c> tamamen elenirse dışarıdan yapılan commit, dal
/// değişimi ve ref güncellemesi <b>hiç fark edilmez</b>.
/// </para>
/// <para>
/// Doğru ayrım <c>.git</c> değil <b>kilit dosyaları</b>: ölçümde salt-okunur <c>git status</c>
/// bile <c>.git/index.lock</c> oluşturup siliyor (2 olay). Sonsuz tazeleme döngüsünü kapatan
/// şey <c>*.lock</c> filtresidir. Ref güncellemesi <c>refs/heads/x.lock → refs/heads/x</c>
/// olarak <b>yeniden adlandırma</b> ile geldiği ve olayın yolu <b>yeni ad</b> olduğu için
/// bu filtre gerçek sinyali yemez.
/// </para>
/// </remarks>
public static class RepositoryChangeClassifier
{
    /// <summary>
    /// Git dizininin çalışma ağacı içindeki adı.
    /// </summary>
    public const string GitDirectoryName = ".git";

    /// <summary>
    /// Çalışma ağacı köküne göreli bir yolu sınıflandırır.
    /// </summary>
    /// <param name="relativePath">
    /// Kök dizine göreli yol. Ayraç olarak <c>/</c> veya platform ayracı kabul edilir.
    /// </param>
    /// <returns>Gerekli tazeleme, olay yok sayılacaksa <see langword="null"/>.</returns>
    public static RepositoryChangeKind? ClassifyWorkingTreePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        string[] segments = Split(relativePath);

        if (segments.Length == 0)
        {
            return null;
        }

        // İç içe depolar da dahil olmak üzere `.git` içindeki her şey git dizini kuralına gider.
        // Alt modüllerde `.git` bir DOSYA olabilir; o da depo durumu değişimidir.
        for (int i = 0; i < segments.Length; i++)
        {
            if (!segments[i].Equals(GitDirectoryName, StringComparison.Ordinal))
            {
                continue;
            }

            // Parçalar yeniden birleştirilip tekrar bölünmüyor: her `.git` olayı için
            // fazladan bir birleştirme + bir dizi tahsisi demekti ve bu yol sıcak —
            // tek bir dal değişimi 2102 olay üretiyor (ölçüldü).
            return i == segments.Length - 1
                ? RepositoryChangeKind.Repository
                : ClassifySegments(segments.AsSpan(i + 1));
        }

        return IsLockFile(segments[^1]) ? null : RepositoryChangeKind.WorkingTree;
    }

    /// <summary>
    /// Git dizinine (<c>.git</c> veya bağlı çalışma ağacının kendi dizini) göreli bir yolu
    /// sınıflandırır.
    /// </summary>
    /// <remarks>
    /// Yok sayılanlar bilinçli: <c>objects/</c> ve <c>logs/</c> her yazma işleminde onlarca
    /// olay üretir ama tek başlarına hiçbir şey anlatmaz — nesne yazılmış olması ref
    /// güncellenmedikçe kullanıcı için görünür bir değişiklik değildir.
    /// <c>GITEXT_COMMITMESSAGE</c> ise <b>bizim kendi taslağımız</b> (P05-T13): her tuş
    /// vuruşundan sonra yazılıyor, elenmezse yazarken sürekli tazeleme tetiklerdi.
    /// </remarks>
    public static RepositoryChangeKind? ClassifyGitDirectoryPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        return ClassifySegments(Split(relativePath));
    }

    private static RepositoryChangeKind? ClassifySegments(ReadOnlySpan<string> segments)
    {
        if (segments.Length == 0)
        {
            return null;
        }

        // Kilit dosyaları sonsuz döngünün kaynağı: bizim her `git` çağrımız üretiyor.
        if (IsLockFile(segments[^1]))
        {
            return null;
        }

        return segments[0] switch
        {
            // Ref'ler: dal/etiket oluşturma, commit, fetch, reset.
            "refs" or "packed-refs" => RepositoryChangeKind.Repository,

            // Süregelen işlemler: rebase/merge/cherry-pick durum dosyaları.
            "rebase-merge" or "rebase-apply" => RepositoryChangeKind.Repository,

            "HEAD" or "MERGE_HEAD" or "CHERRY_PICK_HEAD" or "REVERT_HEAD" or "BISECT_LOG"
                => RepositoryChangeKind.Repository,

            // Index yalnızca stage durumunu değiştirir; commit listesi aynı kalır.
            "index" => RepositoryChangeKind.WorkingTree,

            _ => null,
        };
    }

    private static bool IsLockFile(string name) =>
        name.EndsWith(".lock", StringComparison.Ordinal);

    private static string[] Split(string path) =>
        path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
}
