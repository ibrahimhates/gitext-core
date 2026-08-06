using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Pull'un birleştirme stratejisi (P06-T07).</summary>
public enum PullStrategy
{
    /// <summary>
    /// Kullanıcının ayarı ne diyorsa o.
    /// </summary>
    /// <remarks>
    /// Bu değer <b>komuta geçirilmez</b>: <see cref="IPullWriter.ResolveStrategyAsync"/> ile
    /// gerçek stratejiye çevrilir ve komuta <b>açık</b> bayrak yazılır. Gerekçesi
    /// <see cref="PullWriter"/> notlarında.
    /// </remarks>
    Default,

    /// <summary><c>--no-rebase</c>: uzak dalı mevcut dala birleştir.</summary>
    Merge,

    /// <summary><c>--rebase</c>: yerel commit'leri uzak dalın üstüne taşı.</summary>
    Rebase,

    /// <summary><c>--ff-only</c>: yalnızca ileri sarılabiliyorsa.</summary>
    FastForwardOnly,
}

/// <summary>Stratejinin <b>nereden</b> geldiği — kullanıcıya gösterilir (P06-T07).</summary>
public enum PullStrategySource
{
    /// <summary>Kullanıcı bu ekranda seçti.</summary>
    UserChoice,

    /// <summary><c>branch.&lt;dal&gt;.rebase</c> ayarı.</summary>
    BranchSetting,

    /// <summary><c>pull.rebase</c> ayarı.</summary>
    PullRebaseSetting,

    /// <summary><c>pull.ff</c> ayarı.</summary>
    PullFfSetting,

    /// <summary>Hiçbir ayar yok; uygulamanın varsayılanı (birleştir).</summary>
    ApplicationDefault,
}

/// <param name="Strategy">Uygulanacak strateji.</param>
/// <param name="Source">Kararın kaynağı.</param>
/// <param name="ConfigValue">Varsa ayarın ham değeri.</param>
public sealed record ResolvedPullStrategy(
    PullStrategy Strategy,
    PullStrategySource Source,
    string? ConfigValue);

/// <summary>Pull seçenekleri (P06-T07).</summary>
public sealed record PullOptions
{
    /// <summary>Hangi remote? <see langword="null"/> ise dalın upstream'i.</summary>
    public string? Remote { get; init; }

    /// <summary>Hangi uzak dal? <see langword="null"/> ise upstream'in dalı.</summary>
    public string? Branch { get; init; }

    /// <summary>Strateji; <see cref="PullStrategy.Default"/> ise ayarlardan çözülür.</summary>
    public PullStrategy Strategy { get; init; }

    /// <summary>
    /// <c>--autostash</c>: kirli ağaçta çalışmayı stash'leyip sonra geri koy.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — geri koyma ÇAKIŞABİLİYOR ve çıkış kodu yine 0 oluyor.</b> Böyle bir
    /// durumda çalışma ağacında <c>UU</c> dosyalar ve dosyanın <b>içinde çakışma işaretleri</b>
    /// kalıyor, stash de listede duruyor. Sonuç
    /// <see cref="PullResult.AutoStashConflict"/> ile ayrıca bildiriliyor.
    /// </remarks>
    public bool AutoStash { get; init; }

    /// <summary><c>--prune</c>: fetch aşamasında budama yap.</summary>
    public bool Prune { get; init; }

    /// <summary>Etiket davranışı (fetch aşaması).</summary>
    public FetchTagMode Tags { get; init; }
}

/// <summary>Pull sonucu (P06-T07).</summary>
public sealed record PullResult
{
    /// <summary>Gerçekten uygulanan strateji ve kaynağı.</summary>
    public required ResolvedPullStrategy Strategy { get; init; }

    /// <summary>Pull öncesi <c>HEAD</c>.</summary>
    public required string HeadBefore { get; init; }

    /// <summary>Pull sonrası <c>HEAD</c>.</summary>
    public required string HeadAfter { get; init; }

    /// <summary>Fetch aşamasında değişen uzak izleme ref'leri.</summary>
    public IReadOnlyList<RefChange> Changes { get; init; } = [];

    /// <summary>
    /// Çözülmemiş dosya kaldı mı?
    /// </summary>
    /// <remarks>
    /// 🔴 Çıkış koduna bakılarak <b>belirlenemez</b>: çakışmada rc=1 geliyor ama
    /// <c>--autostash</c> geri koyma çakışmasında rc <b>0</b>. Durum ayrıca okunuyor.
    /// </remarks>
    public bool HasConflicts { get; init; }

    /// <summary>Çakışma, stash'in geri konmasından mı kaynaklandı?</summary>
    /// <remarks>
    /// Ayırt etmek şart: kullanıcının yapması gereken iş farklı. Pull'un kendisi
    /// <b>başarılı</b> olmuştur, çözülecek olan kendi kaydedilmemiş değişikliğidir ve
    /// stash hâlâ durur.
    /// </remarks>
    public bool AutoStashConflict { get; init; }

    /// <summary><c>HEAD</c> hiç ilerlemedi mi ("zaten güncel")?</summary>
    public bool AlreadyUpToDate => string.Equals(HeadBefore, HeadAfter, StringComparison.Ordinal);

    /// <summary>
    /// Pull'u geri almanın <b>çalıştırılabilir</b> komutu.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: git <c>ORIG_HEAD</c>'i üç yolda da (ileri sarma, birleştirme, rebase)
    /// pull öncesi commit'e ayarlıyor ve <c>reset --hard</c> ile eski hâl birebir geri
    /// geliyor. Yine de <b>hash yazılıyor</b>, <c>ORIG_HEAD</c> değil: bir sonraki
    /// merge/rebase onu ezer ve kullanıcı komutu yarım saat sonra çalıştırabilir.
    /// </remarks>
    public string RecoveryCommand => $"git reset --hard {HeadBefore}";
}

/// <summary>Pull işlemleri (P06-T07).</summary>
public interface IPullWriter
{
    /// <summary>
    /// Kullanıcının ayarlarına göre <b>hangi stratejinin</b> uygulanacağını söyler.
    /// </summary>
    /// <remarks>
    /// Arayüz bunu <b>pull'dan önce</b> gösteriyor: README'nin "komutu göster" ilkesi ve
    /// planın "pull düğmesinin ne yaptığı belirsiz kalmamalı" maddesi.
    /// </remarks>
    Task<ResolvedPullStrategy> ResolveStrategyAsync(
        string workingDirectory,
        PullStrategy requested = PullStrategy.Default,
        CancellationToken cancellationToken = default);

    /// <summary>Pull yapar.</summary>
    Task<PullResult> PullAsync(
        string workingDirectory,
        PullOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git pull</c> sarmalayıcısı (P06-T07).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Strateji ASLA git'e bırakılmıyor.</b> ÖLÇÜLDÜ: hiçbir ayar yokken ve dallar
/// iraksamışken <c>git pull</c> <b>çalışmayı reddediyor</b> (çıkış kodu 128) ve ekrana
/// dokuz satırlık bir <c>hint:</c> bloğu basıyor. Daha kötüsü: reddetmeden <b>önce fetch
/// aşamasını tamamlıyor</b>, yani depo değişmiş oluyor ama kullanıcı "başarısız" görüyor.
/// Bu yüzden strateji önce <see cref="ResolveStrategyAsync"/> ile çözülüyor ve komuta
/// <b>her zaman açık bir bayrak</b> (<c>--rebase</c>/<c>--no-rebase</c>/<c>--ff-only</c>)
/// yazılıyor.
/// </para>
/// <para>
/// <b>Ayar önceliği ölçüldü:</b> <c>branch.&lt;dal&gt;.rebase</c>, <c>pull.rebase</c>'i
/// <b>eziyor</b> (<c>pull.rebase=true</c> + <c>branch.main.rebase=false</c> → merge yapıldı).
/// </para>
/// </remarks>
public sealed class PullWriter : IPullWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly IGitConfigReader _config;

    public PullWriter(IGitWriter writer, IGitProcessRunner runner, IGitConfigReader config)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(config);

        _writer = writer;
        _runner = runner;
        _config = config;
    }

    public async Task<ResolvedPullStrategy> ResolveStrategyAsync(
        string workingDirectory,
        PullStrategy requested = PullStrategy.Default,
        CancellationToken cancellationToken = default)
    {
        if (requested != PullStrategy.Default)
        {
            return new ResolvedPullStrategy(requested, PullStrategySource.UserChoice, null);
        }

        string? branch = await CurrentBranchAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        // Sıra ölçümle sabit: dal ayarı en güçlüsü.
        if (branch is { Length: > 0 })
        {
            string? branchSetting = await _config
                .GetAsync(workingDirectory, $"branch.{branch}.rebase", cancellationToken)
                .ConfigureAwait(false);

            if (ParseRebase(branchSetting) is { } fromBranch)
            {
                return new ResolvedPullStrategy(
                    fromBranch, PullStrategySource.BranchSetting, branchSetting);
            }
        }

        string? pullRebase = await _config
            .GetAsync(workingDirectory, "pull.rebase", cancellationToken)
            .ConfigureAwait(false);

        if (ParseRebase(pullRebase) is { } fromPull)
        {
            return new ResolvedPullStrategy(
                fromPull, PullStrategySource.PullRebaseSetting, pullRebase);
        }

        string? pullFf = await _config
            .GetAsync(workingDirectory, "pull.ff", cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(pullFf, "only", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedPullStrategy(
                PullStrategy.FastForwardOnly, PullStrategySource.PullFfSetting, pullFf);
        }

        // git'in bu noktadaki davranışı "reddet"; bizimki git'in belgelediği tarihsel
        // varsayılan olan birleştirme. Kullanıcı ekranda ne olacağını görüyor ve
        // değiştirebiliyor, yani sessiz bir tercih değil.
        return new ResolvedPullStrategy(
            PullStrategy.Merge, PullStrategySource.ApplicationDefault, null);
    }

    public async Task<PullResult> PullAsync(
        string workingDirectory,
        PullOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        ResolvedPullStrategy strategy = await ResolveStrategyAsync(
                workingDirectory, options.Strategy, cancellationToken)
            .ConfigureAwait(false);

        string before = await RevisionAsync(workingDirectory, "HEAD", cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> refsBefore =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken)
                .ConfigureAwait(false);

        string standardError;

        try
        {
            GitResult result = await _writer
                .RunAsync(workingDirectory, BuildArguments(options, strategy.Strategy), cancellationToken)
                .ConfigureAwait(false);

            standardError = result.StandardError;
        }
        catch (GitException error)
        {
            // Çakışma bir "hata" değil, bir sonuç: kullanıcı dosyaları çözecek. İstisna
            // olarak yükselirse arayüz ne olduğunu anlatamaz, yalnızca kırmızı bir kutu
            // gösterir — oysa depo şu an çakışma durumunda ve yapılacak iş belli.
            //
            // 🔴 Ayrım `Kind`'a göre YAPILAMIYOR: ÖLÇÜLDÜ, pull'un çakışma metni
            // (`Auto-merging…`, `CONFLICT (content):`, `Automatic merge failed`) **stdout'a**
            // yazılıyor; sınıflandırıcı yalnızca stderr'i gördüğü için `Unknown` diyor.
            // Bu yüzden karar metne değil **duruma** bakılarak veriliyor: birleşmemiş dosya
            // var mı? Kanal değişse bile bu doğru kalır.
            if (!await HasUnmergedAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            standardError = error.StandardError;
        }

        string after = await RevisionAsync(workingDirectory, "HEAD", cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> refsAfter =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken)
                .ConfigureAwait(false);

        bool conflicts = await HasUnmergedAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return new PullResult
        {
            Strategy = strategy,
            HeadBefore = before,
            HeadAfter = after,
            Changes = RefSnapshot.Diff(refsBefore, refsAfter),
            HasConflicts = conflicts,

            // 🔴 Ayrım git'in kendi metninden: geri koyma çakışmasında çıkış kodu 0 ve
            // özel bir açıklama yazılıyor. Bunu ayırmazsak kullanıcıya "birleştirme
            // çakıştı" derdik — oysa birleştirme başarılı, çakışan onun kendi
            // kaydedilmemiş değişikliği.
            AutoStashConflict = conflicts
                && standardError.Contains("applying them", StringComparison.Ordinal)
                && standardError.Contains("stash", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static IReadOnlyList<string> BuildArguments(PullOptions options, PullStrategy strategy)
    {
        List<string> arguments = ["pull", "--progress"];

        arguments.Add(strategy switch
        {
            PullStrategy.Rebase => "--rebase",
            PullStrategy.FastForwardOnly => "--ff-only",

            // ⚠️ `--no-rebase` AÇIKÇA yazılıyor. Bayraksız çağrı, ayarsız ve iraksayan
            // depoda git'i "reddet" moduna sokuyor (ölçüldü, rc=128).
            _ => "--no-rebase",
        });

        if (options.AutoStash)
        {
            arguments.Add("--autostash");
        }

        if (options.Prune)
        {
            arguments.Add("--prune");
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

        if (options.Remote is { Length: > 0 } remote)
        {
            arguments.Add("--");
            arguments.Add(remote);

            if (options.Branch is { Length: > 0 } branch)
            {
                arguments.Add(branch);
            }
        }

        return arguments;
    }

    /// <summary>
    /// <c>branch.&lt;dal&gt;.rebase</c> / <c>pull.rebase</c> değerini stratejiye çevirir.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: git <c>true</c>, <c>false</c>, <c>only</c>, <c>interactive</c>,
    /// <c>merges</c> değerlerini kabul ediyor. <c>interactive</c> ve <c>merges</c> bizde
    /// düz rebase'e düşüyor — etkileşimli rebase P07-T10'un konusu ve <b>burada</b>
    /// açılacak bir editör kullanıcıyı şaşırtırdı.
    /// </remarks>
    private static PullStrategy? ParseRebase(string? value) => value?.ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" or "interactive" or "merges" => PullStrategy.Rebase,
        "false" or "no" or "off" or "0" => PullStrategy.Merge,
        _ => null,
    };

    private async Task<string?> CurrentBranchAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["symbolic-ref", "-q", "--short", "HEAD"],

                // Ayrık HEAD'de çıkış kodu 1; bu bir hata değil, "dal yok" cevabı (P02-T09).
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0
            ? result.GetStandardOutputText().Trim()
            : null;
    }

    private async Task<string> RevisionAsync(
        string workingDirectory,
        string revision,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", revision],

                // Doğmamış HEAD: çıkış kodu 1, çıktı boş.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.GetStandardOutputText().Trim();
    }

    private async Task<bool> HasUnmergedAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        return result.StandardOutput.Length > 0;
    }

}
