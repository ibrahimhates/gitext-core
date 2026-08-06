using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Yapılandırılmış uzak depoları okur (P06-T05).
/// </summary>
public interface IRemoteReader
{
    /// <summary>
    /// Depodaki tüm uzak depoları ad sırasına göre okur.
    /// </summary>
    Task<IReadOnlyList<GitRemote>> ReadAllAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tek bir uzak depoyu okur; yoksa <see langword="null"/>.
    /// </summary>
    Task<GitRemote?> FindAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRemoteReader"/>
/// <remarks>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ — <c>git remote -v</c> genel olarak AYRIŞTIRILAMAZ</b> ve bu yüzden hiç
/// kullanılmıyor (planın önerdiği kanaldı). Üç ayrı yerden kırılıyor:
/// </para>
/// <list type="number">
///   <item><description>
///     Ayraç <b>sekme</b>, ama <b>URL de sekme içerebiliyor</b>: config'e sekmeli bir URL
///     yazılınca satır <c>sekmeli⇥https://a⇥b/c.git (fetch)</c> oluyor.
///   </description></item>
///   <item><description>
///     Satır sayısı ada göre sabit değil: <c>set-url --add</c> sonrası tek bir remote
///     <b>üç satır</b> veriyor (1 fetch + 2 push).
///   </description></item>
///   <item><description>
///     <c>(fetch)</c>/<c>(push)</c> soneki URL'nin parçası olabilir; URL tırnaklanmıyor.
///   </description></item>
/// </list>
/// <para>
/// Kullanılan kanal <b>iki çağrı</b>:
/// </para>
/// <list type="number">
///   <item><description>
///     <c>git remote</c> → <b>yetkili ad listesi</b>. Satır satır bölmek güvenli: remote adı
///     satır sonu <b>içeremiyor</b> (ölçüldü — <c>git config</c> böyle bir anahtarı
///     <c>invalid key (newline)</c> ile reddediyor). ⚠️ <c>git remote -z</c> <b>yok</b>.
///   </description></item>
///   <item><description>
///     <c>git config -z --get-regexp</c> → tek çağrıda url/pushurl/fetch/tagopt, <b>ham</b>
///     değerler. 🔴 <c>-z</c> şart: <c>-z</c>'siz biçim <b>satır tabanlı</b> ve ölçümde
///     satır sonu içeren bir URL çıktıda <b>iki satıra bölündü</b> — ayrıştırıcı ikinci
///     parçayı ayrı bir kayıt sanardı.
///   </description></item>
/// </list>
/// <para>
/// 🔴 <b>Neden <c>git remote get-url</c> kullanılmıyor?</b> İki sebep, ikisi de ölçüldü:
/// <c>url.&lt;taban&gt;.insteadOf</c> tanımlıysa <b>yeniden yazılmış</b> URL veriyor (ham
/// config farklı), ve URL'siz bir remote için <b>adın kendisini</b> URL diye basıyor.
/// </para>
/// </remarks>
public sealed class RemoteReader : IRemoteReader
{
    private readonly IGitProcessRunner _runner;

    public RemoteReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<GitRemote>> ReadAllAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult names = await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "remote"),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> remoteNames =
        [
            .. names.GetStandardOutputText()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r')),
        ];

        GitResult config = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", "-z", "--get-regexp", RemoteConfigParser.KeyPattern],

                // Hiç remote yoksa çıkış kodu 1 ve çıktı boş; bu bir hata değil, "yok" cevabı.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return RemoteConfigParser.Parse(
            config.ExitCode == 0 ? config.SplitStandardOutputAtNul() : [],
            remoteNames);
    }

    public async Task<GitRemote?> FindAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        IReadOnlyList<GitRemote> remotes =
            await ReadAllAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        // Ordinal: adlar büyük/küçük harf DUYARLI — ölçüldü, `Buyuk` ve `buyuk` aynı depoda
        // aynı anda var olabiliyor.
        return remotes.FirstOrDefault(remote =>
            string.Equals(remote.Name, name, StringComparison.Ordinal));
    }
}

/// <summary>
/// <c>git config -z --get-regexp '^remote\.'</c> çıktısını <see cref="GitRemote"/>'lara çevirir.
/// </summary>
/// <remarks>
/// <para>
/// Ayrı bir sınıf çünkü <b>iki çağıran</b> var: <see cref="RemoteReader"/> ve
/// <see cref="RefReader"/>. P06-T04'ün dersi: aynı soruya iki ayrı yoldan cevap vermek,
/// birinin sessizce yanlış olmasına izin veriyor — bu yüzden ayrıştırma tek yerde.
/// </para>
/// <para>
/// <b>Kayıt biçimi (ölçüldü):</b> her kayıt <c>anahtar\ndeğer</c>, kayıtlar arasında
/// <c>NUL</c>. Değer satır sonu içerebilir; anahtar içeremez.
/// </para>
/// </remarks>
internal static class RemoteConfigParser
{
    /// <summary>Okunacak config anahtarlarının deseni.</summary>
    internal const string KeyPattern = "^remote\\.";

    private const string Prefix = "remote.";

    /// <param name="records"><c>-z</c> ile bölünmüş <c>anahtar\ndeğer</c> kayıtları.</param>
    /// <param name="knownNames">
    /// <c>git remote</c>'tan gelen yetkili ad listesi. Verilirse ad ayrımı buna göre yapılır
    /// (bir remote'un <b>hiç</b> URL'si olmasa bile listede kalması bunu gerektiriyor);
    /// <see langword="null"/> ise adlar yalnızca anahtarlardan çıkarılır.
    /// </param>
    internal static IReadOnlyList<GitRemote> Parse(
        IReadOnlyList<string> records,
        IReadOnlyList<string>? knownNames)
    {
        Dictionary<string, Builder> builders = [];
        List<string> order = [];

        if (knownNames is not null)
        {
            foreach (string name in knownNames)
            {
                if (!builders.ContainsKey(name))
                {
                    builders[name] = new Builder();
                    order.Add(name);
                }
            }
        }

        foreach (string record in records)
        {
            int newline = record.IndexOf('\n', StringComparison.Ordinal);
            if (newline < 0)
            {
                // Değeri olmayan anahtar (`git config --add remote.x.y` boş değerle) —
                // ilgilendiğimiz anahtarların hiçbiri böyle olamaz, atlanıyor.
                continue;
            }

            string key = record[..newline];
            string value = record[(newline + 1)..];

            if (!key.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (SplitKey(key, knownNames) is not { } parts)
            {
                continue;
            }

            (string name, string subKey) = parts;

            if (!builders.TryGetValue(name, out Builder? builder))
            {
                builder = new Builder();
                builders[name] = builder;
                order.Add(name);
            }

            switch (subKey)
            {
                case "url":
                    builder.FetchUrls.Add(value);
                    break;
                case "pushurl":
                    builder.PushUrls.Add(value);
                    break;
                case "fetch":
                    builder.FetchRefspecs.Add(value);
                    break;
                case "tagopt":
                    builder.TagOption = value;
                    break;
                default:
                    // `prune`, `proxy`, kullanıcının yazdığı bilinmeyen alt anahtarlar…
                    break;
            }
        }

        return
        [
            .. order
                .Select(name => builders[name].Build(name))
                .OrderBy(remote => remote.Name, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// <c>remote.&lt;ad&gt;.&lt;altanahtar&gt;</c> anahtarını ikiye ayırır.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — ad NOKTA içerebiliyor:</b> <c>git remote add a.b …</c> geçerli ve
    /// anahtar <c>remote.a.b.url</c> oluyor. <c>Split('.')[1]</c> ile okumak adı <c>a</c>
    /// sanardı. Doğru kural: <b>son</b> nokta alt anahtarı ayırır. Yetkili ad listesi varsa
    /// önce ona bakılıyor (en uzun eşleşme), çünkü bilinmeyen alt anahtarlar da nokta
    /// içerebilir.
    /// </remarks>
    private static (string Name, string SubKey)? SplitKey(
        string key,
        IReadOnlyList<string>? knownNames)
    {
        string remainder = key[Prefix.Length..];

        if (knownNames is not null)
        {
            string? best = null;

            foreach (string name in knownNames)
            {
                if (remainder.Length > name.Length
                    && remainder[name.Length] == '.'
                    && remainder.StartsWith(name, StringComparison.Ordinal)
                    && (best is null || name.Length > best.Length))
                {
                    best = name;
                }
            }

            if (best is not null)
            {
                return (best, remainder[(best.Length + 1)..]);
            }
        }

        int lastDot = remainder.LastIndexOf('.');

        return lastDot <= 0
            ? null
            : (remainder[..lastDot], remainder[(lastDot + 1)..]);
    }

    private sealed class Builder
    {
        public List<string> FetchUrls { get; } = [];

        public List<string> PushUrls { get; } = [];

        public List<string> FetchRefspecs { get; } = [];

        public string? TagOption { get; set; }

        public GitRemote Build(string name) => new()
        {
            Name = name,
            FetchUrls = FetchUrls,
            PushUrls = PushUrls,
            FetchRefspecs = FetchRefspecs,
            TagOption = TagOption,
        };
    }
}
