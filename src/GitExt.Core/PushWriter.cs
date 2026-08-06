using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>Push sırasında etiketlerin nasıl gönderileceği (P06-T08).</summary>
public enum PushTagMode
{
    /// <summary>Etiket gönderme (git'in varsayılanı).</summary>
    None,

    /// <summary>
    /// <c>--follow-tags</c>: gönderilen commit'lere ulaşan <b>annotated</b> etiketler.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ÖLÇÜLDÜ — hafif (lightweight) etiketleri ATLIYOR.</b> Yerelde <c>v3</c> (hafif) ve
    /// <c>v4</c> (annotated) varken <c>--follow-tags</c> yalnızca <c>v4</c>'ü gönderdi.
    /// Kullanıcı "etiketleri de gönder" deyip v3'ün gitmediğini fark etmezse, etiketin
    /// uzakta olduğunu sanır. Arayüz bunu yazmak zorunda.
    /// </remarks>
    FollowAnnotated,

    /// <summary><c>--tags</c>: yereldeki <b>tüm</b> etiketler.</summary>
    All,
}

/// <summary>
/// Tek bir ref gönderimi (P06-T08).
/// </summary>
/// <param name="Source">
/// Yerel kaynak (<c>main</c>, <c>refs/tags/v1</c>). Silmede <b>boş</b>.
/// </param>
/// <param name="Destination">Uzaktaki hedef dal/etiket kısa adı.</param>
/// <param name="Delete">Uzaktaki ref silinecek mi (<c>--delete</c>)?</param>
public sealed record PushSpec(string Source, string Destination, bool Delete = false)
{
    /// <summary>
    /// <c>--force-with-lease</c> için <b>beklenen</b> uzak uç.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Bu alan olmadan force-with-lease KORUMUYOR — ölçüldü.</b> Ayrıntı:
    /// <see cref="PushOptions.ForceWithLease"/>.
    /// </remarks>
    public string? ExpectedRemoteObjectId { get; init; }

    /// <summary>git'e verilecek refspec.</summary>
    internal string ToRefspec() => Delete ? Destination : $"{Source}:{Destination}";
}

/// <summary>Push seçenekleri (P06-T08).</summary>
public sealed record PushOptions
{
    /// <summary>Hedef uzak depo adı.</summary>
    public required string Remote { get; init; }

    /// <summary>Gönderilecek ref'ler. Boşsa git'in varsayılanı çalışır.</summary>
    public IReadOnlyList<PushSpec> Refs { get; init; } = [];

    /// <summary>
    /// <c>--set-upstream</c>: gönderim sonrası yerel dalın upstream'ini kur.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: <c>-u</c> sonrası <c>branch.&lt;dal&gt;.remote</c> ve
    /// <c>branch.&lt;dal&gt;.merge</c> gerçekten yazılıyor. Upstream'i olmayan bir dalda
    /// çıplak <c>git push</c> ise <b>çalışmıyor</b> (çıkış kodu 128).
    /// </remarks>
    public bool SetUpstream { get; init; }

    /// <summary>
    /// <c>--force-with-lease</c>: uzak uç beklenenden farklıysa reddet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>ÖLÇÜLDÜ — çıplak <c>--force-with-lease</c> güvenli DEĞİL.</b> git'in örtük
    /// kirası, dalın <b>uzak izleme ref'inin</b> o anki değeridir. Yani araya giren <b>herhangi
    /// bir fetch</b> kirayı tazeler ve gönderim, başkasının commit'lerini görmediğimiz hâlde
    /// geçer. Ölçümde: <c>b</c> deposu <c>a</c>'nın commit'ini hiç görmeden bir
    /// <c>git fetch</c> yaptı ve ardından <c>--force-with-lease</c> <b>başarıyla</b> o
    /// commit'i sildi. Bu projede fetch'i kullanıcı istemeden de yapabiliriz (P05'in
    /// otomatik tazelemesi, Pull/Fetch ekranı) — yani koruma tam da bizim yüzümüzden
    /// çökerdi.
    /// </para>
    /// <para>
    /// → Bu yüzden kira <b>her zaman açık biçimde</b> yazılıyor:
    /// <c>--force-with-lease=&lt;hedef&gt;:&lt;kullanıcının GÖRDÜĞÜ sha&gt;</c>. Çıpa
    /// <see cref="IPushWriter.PlanAsync"/> ile ekran açılırken okunur ve kullanıcıya
    /// gösterilir. Ölçümde aynı senaryo bu biçimle <c>[rejected] (stale info)</c> verdi.
    /// </para>
    /// <para>
    /// <b>Çıplak <c>--force</c> hiç sunulmuyor</b> (plan kararı): başkasının commit'lerini
    /// sessizce siler. Gerçekten isteyen terminale gidebilir.
    /// </para>
    /// </remarks>
    public bool ForceWithLease { get; init; }

    /// <summary>Etiket davranışı.</summary>
    public PushTagMode Tags { get; init; }

    /// <summary><c>--dry-run</c>: hiçbir şey gönderme, ne olacağını söyle.</summary>
    public bool DryRun { get; init; }
}

/// <summary>Bir ref'in push sonrası durumu (P06-T08).</summary>
public enum PushRefStatus
{
    /// <summary>Uzakta yoktu, oluşturuldu (<c>*</c>).</summary>
    Created,

    /// <summary>İleri sarıldı (<c>' '</c> — bayrak alanı <b>boşluk</b>).</summary>
    FastForward,

    /// <summary>Zorla değiştirildi (<c>+</c>).</summary>
    Forced,

    /// <summary>Uzaktan silindi (<c>-</c>).</summary>
    Deleted,

    /// <summary>Zaten aynıydı (<c>=</c>).</summary>
    UpToDate,

    /// <summary>Reddedildi (<c>!</c>).</summary>
    Rejected,
}

/// <summary>
/// Reddin sebebi (P06-T08).
/// </summary>
/// <remarks>
/// Sebep, porcelain özet alanının parantez içindeki kısmından geliyor — <b>stderr'deki
/// <c>hint:</c> satırlarından değil.</b> GitExtensions bu tespiti insan-okunur çıktıya
/// düzenli ifade uygulayarak yapıyor (<c>FormPush.cs</c>); ADR-0002 bunu yasaklıyor.
/// </remarks>
public enum PushRejectionKind
{
    /// <summary>Tanınmayan sebep; ham metin gösterilmeli.</summary>
    Unknown,

    /// <summary>Uzakta bizde olmayan commit'ler var (<c>fetch first</c> / <c>non-fast-forward</c>).</summary>
    Behind,

    /// <summary>Kira tutmadı: uzak uç, kullanıcının gördüğünden farklı (<c>stale info</c>).</summary>
    StaleLease,

    /// <summary>Uzak taraf reddetti — kanca, korumalı dal, yetki (<c>remote rejected</c>).</summary>
    RemoteRejected,
}

/// <summary>Tek bir ref'in gönderim sonucu (P06-T08).</summary>
/// <param name="Flag">Porcelain bayrağı: <c>* + - = !</c> ya da boşluk.</param>
/// <param name="Source">Kaynak ref; silmede <c>(delete)</c> ya da boş.</param>
/// <param name="Destination">Uzaktaki tam ref adı.</param>
/// <param name="Summary">Özet alanı (<c>abc..def</c>, <c>[new branch]</c>, <c>[rejected]</c>).</param>
/// <param name="Reason">Özetin parantez içindeki sebebi; yoksa <see langword="null"/>.</param>
public sealed record PushRefResult(
    char Flag,
    string Source,
    string Destination,
    string Summary,
    string? Reason)
{
    /// <summary>Bayraktan türetilen durum.</summary>
    public PushRefStatus Status => Flag switch
    {
        '*' => PushRefStatus.Created,
        '+' => PushRefStatus.Forced,
        '-' => PushRefStatus.Deleted,
        '=' => PushRefStatus.UpToDate,
        '!' => PushRefStatus.Rejected,
        _ => PushRefStatus.FastForward,
    };

    /// <summary>Hedefin kısa adı (<c>main</c>, <c>v1.0</c>).</summary>
    public string ShortDestination =>
        Destination.StartsWith(BranchName.HeadsPrefix, StringComparison.Ordinal)
            ? Destination[BranchName.HeadsPrefix.Length..]
            : Destination.StartsWith(RefChange.TagsPrefix, StringComparison.Ordinal)
                ? Destination[RefChange.TagsPrefix.Length..]
                : Destination;

    /// <summary>Bu bir etiket mi?</summary>
    public bool IsTag => Destination.StartsWith(RefChange.TagsPrefix, StringComparison.Ordinal);

    /// <summary>Uzak depo gerçekten değişti mi?</summary>
    public bool Changed => Status is PushRefStatus.Created or PushRefStatus.FastForward
        or PushRefStatus.Forced or PushRefStatus.Deleted;

    /// <summary>Red sebebi; reddedilmediyse <see langword="null"/>.</summary>
    public PushRejectionKind? Rejection => Status != PushRefStatus.Rejected
        ? null
        : Reason switch
        {
            null => PushRejectionKind.Unknown,
            _ when Reason.Contains("stale info", StringComparison.OrdinalIgnoreCase)
                => PushRejectionKind.StaleLease,
            _ when Reason.Contains("fetch first", StringComparison.OrdinalIgnoreCase)
                || Reason.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
                => PushRejectionKind.Behind,
            _ when Summary.Contains("remote rejected", StringComparison.OrdinalIgnoreCase)
                => PushRejectionKind.RemoteRejected,
            _ => PushRejectionKind.Unknown,
        };
}

/// <summary>Push sonucu (P06-T08).</summary>
public sealed record PushResult
{
    /// <summary>Gönderilen her ref için bir satır.</summary>
    public IReadOnlyList<PushRefResult> Refs { get; init; } = [];

    /// <summary>
    /// Uzak tarafın <c>remote:</c> ön ekiyle yazdığı satırlar.
    /// </summary>
    /// <remarks>
    /// Korumalı dal kancasının gerekçesi (<i>"korumalı dal, push yasak"</i>) yalnızca burada.
    /// Porcelain satırı sadece <c>(pre-receive hook declined)</c> diyor — <b>neden</b>
    /// olduğunu söylemiyor.
    /// </remarks>
    public IReadOnlyList<string> RemoteMessages { get; init; } = [];

    /// <summary>Yalnızca deneme miydi?</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// git bir şey yazmadan çuvalladı mı (çıkış kodu 128)?
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: remote yoksa ya da adrese ulaşılamıyorsa <c>--porcelain</c> stdout'a
    /// <b>hiçbir şey</b> yazmıyor. Yani "satır yok" ≠ "değişiklik yok".
    /// </remarks>
    public bool Aborted { get; init; }

    /// <summary>Reddedilen ref'ler.</summary>
    public IReadOnlyList<PushRefResult> Rejected =>
        [.. Refs.Where(item => item.Status == PushRefStatus.Rejected)];

    /// <summary>Uzak depoyu gerçekten değiştiren ref'ler.</summary>
    public IReadOnlyList<PushRefResult> Applied => [.. Refs.Where(item => item.Changed)];

    /// <summary>
    /// Bir kısmı gitti, bir kısmı reddedildi mi?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ:</b> iki dal gönderilip biri reddedildiğinde çıkış kodu <b>1</b>, ama
    /// diğer dal <b>gerçekten gitti</b>. Çıkış koduna bakıp "push başarısız" demek, kullanıcıya
    /// hiçbir şeyin gitmediğini düşündürürdü.
    /// </remarks>
    public bool IsPartial => Applied.Count > 0 && Rejected.Count > 0;
}

/// <summary>
/// Gönderim öncesi durum — ekranı doldurmak ve <b>kirayı çıpalamak</b> için (P06-T08).
/// </summary>
public sealed record PushPlan
{
    public required string Remote { get; init; }

    public required string LocalBranch { get; init; }

    /// <summary>Varsayılan hedef dal adı.</summary>
    public required string RemoteBranch { get; init; }

    /// <summary>
    /// Uzak izleme ref'inin <b>şu anki</b> ucu — <c>--force-with-lease</c> çıpası.
    /// </summary>
    /// <remarks>
    /// Ekran açılırken okunur ve kullanıcıya gösterilir. Arada bir fetch olsa bile kira bu
    /// değerde kalır; <see cref="PushOptions.ForceWithLease"/>'in gerekçesi.
    /// </remarks>
    public string? RemoteTipObjectId { get; init; }

    /// <summary>Uzakta bu dal var mı (izleme ref'ine göre)?</summary>
    public bool RemoteBranchExists => RemoteTipObjectId is not null;

    /// <summary>Yerel dalın upstream'i kurulu mu?</summary>
    public bool HasUpstream { get; init; }

    /// <summary>Upstream'e göre konum.</summary>
    public UpstreamTracking Tracking { get; init; } = UpstreamTracking.None;

    /// <summary>Uzakta yeni bir dal oluşacak mı?</summary>
    public bool WouldCreateBranch => !RemoteBranchExists;

    /// <summary>Yerelde gönderilebilecek etiketler.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Uzaktaki dallar (silme sekmesi için).</summary>
    public IReadOnlyList<string> RemoteBranches { get; init; } = [];
}

/// <summary>Push işlemleri (P06-T08).</summary>
public interface IPushWriter
{
    /// <summary>
    /// Gönderim öncesi durumu okur — ekranı doldurur ve kirayı çıpalar.
    /// </summary>
    /// <remarks>Ağa çıkmaz; yalnızca yereldeki izleme ref'lerine bakar.</remarks>
    Task<PushPlan> PlanAsync(
        string workingDirectory,
        string remote,
        string localBranch,
        CancellationToken cancellationToken = default);

    /// <summary>Gönderir ve <b>her ref için ne olduğunu</b> döndürür.</summary>
    Task<PushResult> PushAsync(
        string workingDirectory,
        PushOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Çalıştırılacak komutu üretir ("komutu göster" ilkesi).</summary>
    string DescribeCommand(PushOptions options);
}

/// <summary>
/// <c>git push</c> sarmalayıcısı (P06-T08).
/// </summary>
/// <remarks>
/// <para>
/// <b>Sonuç <c>--porcelain</c> ile stdout'tan okunuyor.</b> Fetch'te bu mümkün değildi
/// (bayrak git 2.41'de eklendi, projenin tabanı 2.30) ama <c>push --porcelain</c> git'in
/// en eski bayraklarından. Ölçülen biçim, sekmeyle ayrılmış üç alan:
/// <c>&lt;bayrak&gt;\t&lt;kaynak&gt;:&lt;hedef&gt;\t&lt;özet&gt;</c>.
/// </para>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ — porcelain stdout'u SAF DEĞİL.</b> Arasına insan-okunur satırlar
/// karışıyor: <c>To ../remote.git</c>, <c>branch 'x' set up to track 'origin/x'.</c>
/// (<c>-u</c> ile), <c>Would set upstream of …</c> (<c>push.autoSetupRemote</c> ile) ve
/// kapanışta <c>Done</c>. Satırları sırayla ayrıştıran bir kod bunlarda sessizce
/// saçmalardı. Ayraç: <b>ref satırının tam iki sekmesi vardır</b>, diğerlerinin hiç yok.
/// </para>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ — bayrak alanı BOŞLUK olabilir.</b> Normal ileri sarmada bayrak
/// <c>' '</c>; satır <c>Trim()</c>'lenirse alanlar kayar ve her ileri sarma yanlış
/// sınıflandırılırdı.
/// </para>
/// </remarks>
public sealed class PushWriter : IPushWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public PushWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<PushPlan> PlanAsync(
        string workingDirectory,
        string remote,
        string localBranch,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(localBranch);

        GitResult refs = await _runner.RunCheckedAsync(
            GitCommand.Create(
                workingDirectory,
                "for-each-ref",
                "--format=%(refname)%00%(objectname)%00%(symref)%00%(upstream:short)%00%(upstream:track)",
                "refs/heads",
                "refs/remotes",
                "refs/tags"),
            cancellationToken).ConfigureAwait(false);

        string prefix = RemoteName.RemotesPrefix + remote + "/";
        string? remoteTip = null;
        string? upstream = null;
        UpstreamTracking tracking = UpstreamTracking.None;
        List<string> tags = [];
        List<string> remoteBranches = [];

        foreach (string line in refs.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split('\0');

            if (fields.Length != 5)
            {
                continue;
            }

            string name = fields[0];

            if (name == BranchName.HeadsPrefix + localBranch)
            {
                upstream = fields[3].Length > 0 ? fields[3] : null;
                tracking = RefReader.ParseTracking(fields[4]);
            }
            else if (name.StartsWith(RefChange.TagsPrefix, StringComparison.Ordinal))
            {
                tags.Add(name[RefChange.TagsPrefix.Length..]);
            }
            else if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                // Sembolik `origin/HEAD` atlanıyor: uzakta `refs/heads/HEAD` diye bir dal
                // yok, silme listesine girseydi kullanıcıya olmayan bir dal sunulurdu.
                // Aynı ref bu projede dördüncü kez tuzak kuruyor (P03-T12, P06-T05, P06-T06).
                if (fields[2].Length > 0)
                {
                    continue;
                }

                string branch = name[prefix.Length..];
                remoteBranches.Add(branch);

                if (branch == localBranch)
                {
                    remoteTip = fields[1];
                }
            }
        }

        return new PushPlan
        {
            Remote = remote,
            LocalBranch = localBranch,
            RemoteBranch = localBranch,
            RemoteTipObjectId = remoteTip,
            HasUpstream = upstream is not null,
            Tracking = tracking,
            Tags = tags,
            RemoteBranches = remoteBranches,
        };
    }

    public async Task<PushResult> PushAsync(
        string workingDirectory,
        PushOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        string standardOutput;
        string standardError;
        bool aborted = false;

        try
        {
            GitResult result = await _writer
                .RunAsync(workingDirectory, BuildArguments(options), cancellationToken)
                .ConfigureAwait(false);

            standardOutput = result.GetStandardOutputText();
            standardError = result.StandardError;
        }
        catch (GitException error)
        {
            // 🔴 Çıkış kodu 1 "hiçbir şey gitmedi" demek DEĞİL: ölçümde iki dal gönderildi,
            // biri reddedildi, diğeri gerçekten gitti — kod yine 1'di. Gerçek sonuç
            // porcelain satırlarında; onları atarsak kullanıcı gitmiş bir push'u tekrar
            // dener. Satır hiç yoksa (kod 128, remote yok / ulaşılamıyor) hata gerçekten
            // ölümcül demektir ve olduğu gibi yukarı gider.
            standardOutput = error.StandardOutput;
            standardError = error.StandardError;

            if (PushPorcelainParser.Parse(standardOutput).Count == 0)
            {
                throw;
            }

            aborted = false;
        }

        return new PushResult
        {
            Refs = PushPorcelainParser.Parse(standardOutput),
            RemoteMessages = ParseRemoteMessages(standardError),
            DryRun = options.DryRun,
            Aborted = aborted,
        };
    }

    public string DescribeCommand(PushOptions options) => Describe(options);

    /// <summary>Çalıştırılacak komutu üretir ("komutu göster" ilkesi).</summary>
    public static string Describe(PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return "git " + string.Join(' ', BuildArguments(options).Where(part => part != "--progress"));
    }

    /// <remarks>
    /// <c>--porcelain</c> her zaman veriliyor: sonucun tek makine-okunur kanalı o.
    /// <c>--</c> ayracı da her zaman — <c>-</c> ile başlayan bir dal adı aksi hâlde bayrak
    /// sanılırdı (P06-T01'in dersi).
    /// </remarks>
    private static IReadOnlyList<string> BuildArguments(PushOptions options)
    {
        List<string> arguments = ["push", "--porcelain", "--progress"];

        if (options.SetUpstream)
        {
            arguments.Add("--set-upstream");
        }

        if (options.Refs.Any(spec => spec.Delete))
        {
            arguments.Add("--delete");
        }

        if (options.ForceWithLease)
        {
            foreach (PushSpec spec in options.Refs)
            {
                // Çıpa yoksa kirayı git'in örtük hâline bırakmıyoruz: ölçümde o hâl bir
                // fetch'ten sonra korumayı tamamen bırakıyor. Çıpasız ref zorlanmaz.
                if (spec.ExpectedRemoteObjectId is { Length: > 0 } expected)
                {
                    arguments.Add($"--force-with-lease={spec.Destination}:{expected}");
                }
            }
        }

        switch (options.Tags)
        {
            case PushTagMode.All:
                arguments.Add("--tags");
                break;
            case PushTagMode.FollowAnnotated:
                arguments.Add("--follow-tags");
                break;
            case PushTagMode.None:
            default:
                break;
        }

        if (options.DryRun)
        {
            arguments.Add("--dry-run");
        }

        arguments.Add("--");
        arguments.Add(options.Remote);

        foreach (PushSpec spec in options.Refs)
        {
            arguments.Add(spec.ToRefspec());
        }

        return arguments;
    }

    /// <summary>Uzak tarafın <c>remote:</c> satırlarını toplar.</summary>
    private static IReadOnlyList<string> ParseRemoteMessages(string standardError)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return [];
        }

        const string marker = "remote:";
        List<string> messages = [];

        foreach (string raw in standardError.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (!line.StartsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }

            // Ölçüldü: git satırı sabit genişliğe kadar boşlukla dolduruyor.
            string text = line[marker.Length..].Trim();

            if (text.Length > 0)
            {
                messages.Add(text);
            }
        }

        return messages;
    }
}

/// <summary>
/// <c>git push --porcelain</c> stdout ayrıştırıcısı (P06-T08).
/// </summary>
internal static class PushPorcelainParser
{
    public static IReadOnlyList<PushRefResult> Parse(string standardOutput)
    {
        if (string.IsNullOrEmpty(standardOutput))
        {
            return [];
        }

        List<PushRefResult> results = [];

        foreach (string raw in standardOutput.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            // ⚠️ Trim YOK: normal ileri sarmada bayrak alanı tek bir BOŞLUK.
            string[] fields = line.Split('\t');

            // `To …`, `Done`, `branch 'x' set up to track …`, `Would set upstream of …`
            // satırlarının hiç sekmesi yok — ayraç bu.
            if (fields.Length != 3 || fields[0].Length != 1)
            {
                continue;
            }

            int colon = fields[1].LastIndexOf(':');

            if (colon < 0)
            {
                continue;
            }

            (string summary, string? reason) = SplitReason(fields[2]);

            results.Add(new PushRefResult(
                fields[0][0],
                fields[1][..colon],
                fields[1][(colon + 1)..],
                summary,
                reason));
        }

        return results;
    }

    /// <summary>
    /// Özetin sonundaki <c>(sebep)</c> kısmını ayırır.
    /// </summary>
    /// <remarks>
    /// Ölçülen biçimler: <c>[rejected] (fetch first)</c>, <c>[rejected] (stale info)</c>,
    /// <c>[remote rejected] (pre-receive hook declined)</c>, <c>abc..def</c> (sebepsiz),
    /// <c>abc...def (forced update)</c>.
    /// </remarks>
    private static (string Summary, string? Reason) SplitReason(string field)
    {
        if (!field.EndsWith(')'))
        {
            return (field, null);
        }

        int open = field.LastIndexOf('(');

        return open < 0
            ? (field, null)
            : (field[..open].TrimEnd(), field[(open + 1)..^1]);
    }

    /// <summary>Sayısal özet — testlerin okunurluğu için.</summary>
    public static string Describe(PushRefResult result) => string.Create(
        CultureInfo.InvariantCulture,
        $"{result.Flag} {result.Source}:{result.Destination} {result.Summary}");
}
