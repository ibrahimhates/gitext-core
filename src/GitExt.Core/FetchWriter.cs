using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Fetch sırasında etiketlerin nasıl alınacağı (P06-T06).</summary>
public enum FetchTagMode
{
    /// <summary>git'in varsayılanı: alınan commit'lere <b>işaret eden</b> etiketler gelir.</summary>
    Default,

    /// <summary><c>--tags</c>: uzaktaki tüm etiketler.</summary>
    All,

    /// <summary><c>--no-tags</c>: hiç etiket alma.</summary>
    None,
}

/// <summary>Fetch seçenekleri (P06-T06).</summary>
public sealed record FetchOptions
{
    /// <summary>
    /// Hangi remote? <see langword="null"/> ise <b>tümü</b> (<c>--all</c>).
    /// </summary>
    public string? Remote { get; init; }

    /// <summary>
    /// <c>--prune</c>: uzakta silinmiş dalların izleme ref'lerini kaldır.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — bu yıkıcı bir seçenek.</b> Budanan ref'in reflog'u da siliniyor ve
    /// yalnızca o ref'te duran commit budayan bir <c>gc</c> sonrası **kayboldu**. Arayüz
    /// önce <see cref="IFetchWriter.PreviewPruneAsync"/> ile ne kaybedileceğini göstermeli.
    /// </remarks>
    public bool Prune { get; init; }

    /// <summary>
    /// <c>--prune-tags</c>: uzakta silinmiş etiketleri de kaldır.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: <c>--prune</c> <b>tek başına etiketlere dokunmuyor</b> — uzakta silinen bir
    /// etiket yerelde kalmaya devam ediyor. Ayrı bayrak şart.
    /// </remarks>
    public bool PruneTags { get; init; }

    /// <summary>Etiket davranışı.</summary>
    public FetchTagMode Tags { get; init; }

    /// <summary><c>--dry-run</c>: hiçbir şey yazma.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Kullanıcının verdiği HTTPS kimlik bilgisi (P06-T09).
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> ise git kendi kanallarını (credential helper, SSH agent)
    /// kullanır — ölçüldü, ikisi de bizim ortamımızda sorunsuz çalışıyor. Dolu olduğunda
    /// değer <c>GIT_ASKPASS</c> üzerinden geçiriliyor, komut satırına <b>yazılmıyor</b>.
    /// </remarks>
    public GitCredentials? Credentials { get; init; }

    /// <summary>Canlı ilerleme bildirimi (P06-T10).</summary>
    public IProgress<GitProgress>? Progress { get; init; }
}

/// <summary>Bir ref'in fetch sonrası nasıl değiştiği (P06-T06).</summary>
public enum RefChangeKind
{
    /// <summary>Yeni ref.</summary>
    Created,

    /// <summary>Var olan ref başka bir commit'e taşındı.</summary>
    Updated,

    /// <summary>Ref kaldırıldı (budama).</summary>
    Deleted,
}

/// <param name="RefName">Tam ref adı (<c>refs/remotes/origin/main</c>).</param>
/// <param name="OldId">Önceki commit; yeni ref'te <see langword="null"/>.</param>
/// <param name="NewId">Sonraki commit; silinen ref'te <see langword="null"/>.</param>
/// <param name="Kind">Değişimin türü.</param>
public sealed record RefChange(string RefName, string? OldId, string? NewId, RefChangeKind Kind)
{
    /// <summary>Kısa ad (<c>origin/main</c>, <c>v1.0</c>).</summary>
    public string ShortName =>
        RefName.StartsWith(RemoteName.RemotesPrefix, StringComparison.Ordinal)
            ? RefName[RemoteName.RemotesPrefix.Length..]
            : RefName.StartsWith(TagsPrefix, StringComparison.Ordinal)
                ? RefName[TagsPrefix.Length..]
                : RefName;

    /// <summary>Bu bir etiket mi?</summary>
    public bool IsTag => RefName.StartsWith(TagsPrefix, StringComparison.Ordinal);

    internal const string TagsPrefix = "refs/tags/";
}

/// <param name="Remote">Fetch edilemeyen remote.</param>
/// <param name="Message">git'in o remote için verdiği hata satırı.</param>
public sealed record FetchFailure(string Remote, string Message);

/// <summary>Fetch sonucu (P06-T06).</summary>
public sealed record FetchResult
{
    /// <summary>Değişen ref'ler. Boş liste <b>"her şey güncel"</b> demektir.</summary>
    public IReadOnlyList<RefChange> Changes { get; init; } = [];

    /// <summary>
    /// Fetch edilemeyen remote'lar.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ:</b> <c>--all</c> ile bir remote bozuksa çıkış kodu <b>1</b> oluyor
    /// <b>ama diğerleri başarıyla fetch ediliyor</b>. Çıkış koduna bakıp "fetch başarısız"
    /// demek, gerçekte gelmiş olan değişiklikleri kullanıcıdan gizlerdi.
    /// </remarks>
    public IReadOnlyList<FetchFailure> Failures { get; init; } = [];

    /// <summary>Yalnızca deneme miydi?</summary>
    public bool DryRun { get; init; }

    /// <summary>Hiçbir remote fetch edilemedi mi?</summary>
    public bool FailedCompletely => Failures.Count > 0 && Changes.Count == 0;
}

/// <summary>Budamanın ne kaybettireceği (P06-T06).</summary>
/// <remarks>
/// P06-T05'teki <c>RemoteRemovalPlan</c> ile aynı gerekçe: budama sonrası bilgi
/// <b>okunamıyor</b>, önce toplanmalı.
/// </remarks>
public sealed record PrunePreview
{
    /// <summary>Budanacak izleme ref'leri ve şu anki uçları.</summary>
    public IReadOnlyList<RefChange> WouldDelete { get; init; } = [];

    /// <summary>Çalıştırılabilir kurtarma komutları (ref'i geri yazan).</summary>
    public IReadOnlyList<string> RecoveryCommands { get; init; } = [];
}

/// <summary>Fetch işlemleri (P06-T06).</summary>
public interface IFetchWriter
{
    /// <summary>Fetch eder ve <b>ne değiştiğini</b> döndürür.</summary>
    Task<FetchResult> FetchAsync(
        string workingDirectory,
        FetchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Budamanın hangi ref'leri sileceğini <b>budamadan önce</b> hesaplar.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Ağa çıkar</b> (<c>git ls-remote</c>): uzakta hangi dalların kaldığını başka
    /// türlü bilmenin yolu yok. <c>--dry-run --prune</c> çıktısı bunu söylüyor ama
    /// <b>insan-okunur</b> biçimde ve ADR-0002 gereği ayrıştırılmıyor.
    /// </remarks>
    Task<PrunePreview> PreviewPruneAsync(
        string workingDirectory,
        string remote,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git fetch</c> sarmalayıcısı (P06-T06).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ — fetch'in TÜM çıktısı <c>stderr</c>'de.</b> Değişiklik olduğunda bile
/// <c>stdout</c> <b>tamamen boş</b>; özet satırları (<c>From …</c>,
/// <c>db56d2e..e0a8e5f main -&gt; origin/main</c>) stderr'e yazılıyor. stdout'u okuyan bir
/// arayüz kullanıcıya <b>hiçbir şey</b> göstermezdi.
/// </para>
/// <para>
/// <b>Ne değiştiğini yine de o metinden okumuyoruz.</b> git 2.41'de eklenen
/// <c>--porcelain</c> makine-okunur bir kanal veriyor (ölçüldü, stdout'a yazıyor) ama
/// projenin desteklediği <b>minimum sürüm 2.30</b> (ADR-0002) — orada bayrak yok. İki kod
/// yolu tutmak, birinin sessizce test edilmemesi demek olurdu.
/// → Değişiklikler <b>ref anlık görüntüsü farkı</b> ile hesaplanıyor: fetch öncesi ve
/// sonrası <c>for-each-ref</c>. Sürümden bağımsız, silmeleri ve etiketleri de kapsıyor,
/// maliyeti ~1 ms.
/// </para>
/// </remarks>
public sealed class FetchWriter : IFetchWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public FetchWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<FetchResult> FetchAsync(
        string workingDirectory,
        FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyDictionary<string, string> before =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken).ConfigureAwait(false);

        string standardError;

        try
        {
            using AskPassSession? askPass = options.Credentials is { } credentials
                ? AskPassSession.Create(credentials)
                : null;

            GitResult result = await _writer
                .RunWithEnvironmentAsync(
                    workingDirectory,
                    BuildArguments(options),
                    askPass?.Environment,
                    options.Progress,
                    cancellationToken)
                .ConfigureAwait(false);

            standardError = result.StandardError;
        }
        catch (GitException error) when (ParseFailures(error.StandardError).Count > 0)
        {
            // 🔴 ÖLÇÜLDÜ: `--all` ile bir remote bozuksa çıkış kodu 1 oluyor AMA diğerleri
            // fetch ediliyor. İstisnayı olduğu gibi bırakmak, gerçekten gelmiş değişiklikleri
            // kullanıcıdan gizlerdi — üstelik ekranda "başarısız" yazarken depo değişmiş
            // olurdu. Kısmi sonuç, sonucun kendisidir.
            standardError = error.StandardError;
        }

        IReadOnlyDictionary<string, string> after = options.DryRun
            ? before
            : await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken).ConfigureAwait(false);

        return new FetchResult
        {
            Changes = RefSnapshot.Diff(before, after),
            Failures = ParseFailures(standardError),
            DryRun = options.DryRun,
        };
    }

    public async Task<PrunePreview> PreviewPruneAsync(
        string workingDirectory,
        string remote,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);

        // Uzakta HÂLÂ duran dallar. `--heads` yeterli: budama yalnızca izleme dallarını
        // etkiliyor, etiketler ayrı bayrağın (`--prune-tags`) işi.
        GitResult remoteRefs = await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "ls-remote", "--heads", "--", remote),
            cancellationToken).ConfigureAwait(false);

        HashSet<string> alive = [];

        foreach (string line in remoteRefs.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Biçim: "<sha>\t<refname>" — ref adı sekme veya satır sonu içeremiyor (P02-T09).
            int tab = line.IndexOf('\t', StringComparison.Ordinal);

            if (tab > 0)
            {
                alive.Add(line[(tab + 1)..].TrimEnd('\r'));
            }
        }

        IReadOnlyDictionary<string, string> local =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken).ConfigureAwait(false);

        string prefix = RemoteName.RemotesPrefix + remote + "/";
        List<RefChange> doomed = [];

        foreach ((string refName, string id) in local)
        {
            if (!refName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string branch = refName[prefix.Length..];

            // Not: `origin/HEAD` buraya hiç gelmiyor — anlık görüntü sembolik ref'leri
            // eliyor. Elenmeseydi uzakta `refs/heads/HEAD` diye bir dal olmadığı için
            // HER budama önizlemesinde yanlış bir kayıp uyarısı üretirdi.
            if (!alive.Contains(BranchName.HeadsPrefix + branch))
            {
                doomed.Add(new RefChange(refName, id, null, RefChangeKind.Deleted));
            }
        }

        return new PrunePreview
        {
            WouldDelete = doomed,

            // Ref'i geri yazmak commit'i kurtarır — ama yalnızca nesne hâlâ duruyorsa.
            // Budama sonrası `gc` çalışırsa nesne gider (ölçüldü), bu yüzden komut
            // kullanıcıya HEMEN veriliyor.
            RecoveryCommands =
            [
                .. doomed.Select(change =>
                    $"git update-ref {change.RefName} {change.OldId}"),
            ],
        };
    }

    /// <summary>
    /// Argümanları kurar.
    /// </summary>
    /// <remarks>
    /// <c>--progress</c> her zaman veriliyor: ölçüldü, terminal olmayan çıktıda git ilerleme
    /// satırlarını <b>hiç</b> yazmıyor ve yalnızca özet kalıyor. Canlı gösterim P06-T10'da,
    /// ama bayrağı oraya bırakmak "ilerleme neden yok?" sorusunu o güne saklamak olurdu.
    /// </remarks>
    private static IReadOnlyList<string> BuildArguments(FetchOptions options)
    {
        List<string> arguments = ["fetch", "--progress"];

        if (options.Prune)
        {
            arguments.Add("--prune");
        }

        if (options.PruneTags)
        {
            // ÖLÇÜLDÜ: `--prune-tags` tek başına yeterli değil, git `--prune` de istiyor.
            arguments.Add("--prune-tags");

            if (!options.Prune)
            {
                arguments.Add("--prune");
            }
        }

        switch (options.Tags)
        {
            case FetchTagMode.All:
                arguments.Add("--tags");
                break;
            case FetchTagMode.None:
                arguments.Add("--no-tags");
                break;
            case FetchTagMode.Default:
            default:
                break;
        }

        if (options.DryRun)
        {
            arguments.Add("--dry-run");
        }

        if (options.Remote is { Length: > 0 } remote)
        {
            arguments.Add("--");
            arguments.Add(remote);
        }
        else
        {
            arguments.Add("--all");
        }

        return arguments;
    }

    /// <summary>
    /// <c>--all</c>'da kısmi başarısızlıkları toplar.
    /// </summary>
    /// <remarks>
    /// git her başarısız remote için <c>error: could not fetch &lt;ad&gt;</c> yazıyor
    /// (ölçüldü). Bu <b>tek</b> makine-dostu iz; öncesindeki <c>fatal:</c> satırları
    /// ayrıntı olarak taşınıyor.
    /// </remarks>
    private static IReadOnlyList<FetchFailure> ParseFailures(string standardError)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return [];
        }

        const string marker = "error: could not fetch ";
        List<FetchFailure> failures = [];
        string detail = string.Empty;

        foreach (string raw in standardError.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.StartsWith("fatal:", StringComparison.Ordinal))
            {
                detail = line["fatal:".Length..].Trim();
            }
            else if (line.StartsWith(marker, StringComparison.Ordinal))
            {
                string name = line[marker.Length..].Trim();

                failures.Add(new FetchFailure(
                    name,
                    detail.Length > 0 ? detail : "Uzak depoya ulaşılamadı."));

                detail = string.Empty;
            }
        }

        return failures;
    }
}
