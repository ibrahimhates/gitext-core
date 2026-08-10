using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// Deponun <c>commit-graph</c> dosyasının durumunu bildirir (P09-T07).
/// </summary>
/// <remarks>
/// <para>
/// <c>commit-graph</c>, commit üst bilgilerini ve nesil (generation) numaralarını önceden
/// hesaplanmış bir dosyada tutuyor; <c>--topo-order</c> gibi geçmişi gezen sorgular onu
/// kullanınca nesne veritabanını okumak zorunda kalmıyor.
/// </para>
/// <para>
/// <b>Ölçülen kazanç (500.000 commit'lik depo, P09-T04 temel çizgisiyle aynı makine):</b>
/// </para>
/// <list type="bullet">
///   <item>Grafiğin <b>ilk satırı</b>: 1.281 ms → <b>7.8 ms</b> (~164×)</item>
///   <item>Tamamının okunması: 3.411 ms → 2.867 ms</item>
///   <item>Dosyanın yazılması: ~1.7 sn (bir kez)</item>
/// </list>
/// <para>
/// İlk satırdaki fark, kullanıcının doğrudan gördüğü şey: <c>--topo-order</c> nesil
/// numaraları olmadan ilk satırı basmadan önce geçmişin tamamını gezmek zorunda.
/// </para>
/// <para>
/// 🔒 <b>Dosya kendiliğinden YAZILMIYOR.</b> Kullanıcının deposuna izinsiz dosya eklemek
/// doğru değil — hele bu depo paylaşılan bir çalışma kopyasıysa. Sınıf yalnızca durumu
/// bildiriyor; yazma kararı kullanıcının.
/// </para>
/// </remarks>
public interface ICommitGraphAdvisor
{
    /// <summary>Deponun commit-graph durumunu okur.</summary>
    Task<CommitGraphStatus> InspectAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kullanıcı onayladığında <c>commit-graph</c> dosyasını yazar.
    /// </summary>
    /// <remarks>
    /// Yalnızca açık bir kullanıcı eylemiyle çağrılmalı.
    /// </remarks>
    Task WriteAsync(string workingDirectory, CancellationToken cancellationToken = default);
}

/// <summary>
/// Bir deponun <c>commit-graph</c> durumu.
/// </summary>
/// <param name="Exists">Dosya var mı?</param>
/// <param name="CommitCount">Depodaki commit sayısı.</param>
/// <param name="IsWorthwhile">Öneri gösterilmeli mi?</param>
public readonly record struct CommitGraphStatus(bool Exists, int CommitCount, bool IsWorthwhile)
{
    /// <summary>
    /// Önerinin gösterilmeye değer olduğu alt sınır.
    /// </summary>
    /// <remarks>
    /// Küçük depolarda kazanç ölçülemez: 10.000 commit'lik depo zaten 99 ms'de tamamen
    /// okunuyor (P09-T04). Her depoda öneri göstermek, kullanıcıyı hiçbir şey
    /// kazandırmayan bir işlem için rahatsız etmek olurdu.
    /// </remarks>
    public const int RecommendedThreshold = 50_000;
}

/// <inheritdoc cref="ICommitGraphAdvisor"/>
public sealed class CommitGraphAdvisor : ICommitGraphAdvisor
{
    private readonly IGitProcessRunner _runner;

    public CommitGraphAdvisor(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<CommitGraphStatus> InspectAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        bool exists = await ExistsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        // Dosya varsa commit saymaya gerek yok: öneri zaten gösterilmeyecek ve sayma
        // işleminin kendisi büyük depoda saniyeler sürüyor.
        if (exists)
        {
            return new CommitGraphStatus(Exists: true, CommitCount: 0, IsWorthwhile: false);
        }

        int count = await CountCommitsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        return new CommitGraphStatus(
            Exists: false,
            CommitCount: count,
            IsWorthwhile: count >= CommitGraphStatus.RecommendedThreshold);
    }

    public async Task WriteAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "commit-graph", "write", "--reachable"),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// commit-graph dosyası var mı?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dosya sistemine <b>doğrudan</b> bakılıyor, git'e sorulmuyor: <c>commit-graph verify</c>
    /// dosyanın tamamını doğruluyor ve büyük depoda saniyeler sürüyor — depo açılışında
    /// ödenecek bir maliyet değil.
    /// </para>
    /// <para>
    /// İki yer denetleniyor: tek dosyalık <c>commit-graph</c> ve zincirlenmiş biçimin
    /// <c>commit-graphs/commit-graph-chain</c>'i. <c>--split</c> ile yazılan depolarda
    /// yalnızca ikincisi var; birinciye bakmak "dosya yok" der ve öneri, gereği yokken
    /// tekrar tekrar gösterilirdi.
    /// </para>
    /// </remarks>
    private async Task<bool> ExistsAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        string objectsDirectory = await ResolveObjectsDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return File.Exists(Path.Combine(objectsDirectory, "info", "commit-graph"))
            || File.Exists(Path.Combine(objectsDirectory, "info", "commit-graphs", "commit-graph-chain"));
    }

    private async Task<string> ResolveObjectsDirectoryAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string path = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "--git-path", "objects"),
            cancellationToken).ConfigureAwait(false);

        return Path.GetFullPath(path.Trim(), workingDirectory);
    }

    private async Task<int> CountCommitsAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        string output = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--count", "--all"),
            cancellationToken).ConfigureAwait(false);

        return int.TryParse(output.Trim(), out int count) ? count : 0;
    }
}
