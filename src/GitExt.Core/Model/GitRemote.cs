namespace GitExt.Core.Model;

/// <summary>
/// Yapılandırılmış bir uzak depo (P06-T05).
/// </summary>
/// <remarks>
/// <para>
/// Değerler <b>ham config</b> değerleridir. 🔴 <b>ÖLÇÜLDÜ:</b> <c>git remote get-url</c> ve
/// <c>git remote -v</c>, <c>url.&lt;taban&gt;.insteadOf</c> tanımlıysa URL'yi <b>yeniden
/// yazılmış</b> hâlde veriyor: config'te <c>ornek:proje</c> dururken ikisi de
/// <c>/…/up.gitproje</c> diyor. Arayüz o değeri düzenleme kutusuna koyup kaydederse
/// kullanıcının kısayolu <b>kalıcı olarak yok olur</b>. Bu yüzden buradaki değerler
/// yalnızca <c>git config</c>'ten okunur.
/// </para>
/// <para>
/// URL listeleri <b>çoğul</b>: <c>git remote set-url --add</c> aynı remote'a birden çok URL
/// yazabiliyor (fetch ilkini kullanır, push hepsine gider).
/// </para>
/// </remarks>
public sealed record GitRemote
{
    /// <summary>Remote adı (<c>origin</c> gibi).</summary>
    public required string Name { get; init; }

    /// <summary><c>remote.&lt;ad&gt;.url</c> — ham, sırayla.</summary>
    public IReadOnlyList<string> FetchUrls { get; init; } = [];

    /// <summary>
    /// <c>remote.&lt;ad&gt;.pushurl</c> — ham. Boşsa push <see cref="FetchUrls"/> kullanır.
    /// </summary>
    public IReadOnlyList<string> PushUrls { get; init; } = [];

    /// <summary><c>remote.&lt;ad&gt;.fetch</c> refspec'leri.</summary>
    public IReadOnlyList<string> FetchRefspecs { get; init; } = [];

    /// <summary><c>remote.&lt;ad&gt;.tagopt</c> (<c>--tags</c> / <c>--no-tags</c>).</summary>
    public string? TagOption { get; init; }

    /// <summary>
    /// Gösterilecek birincil URL; <b>tanımlı değilse <see langword="null"/></b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ:</b> yalnızca <c>fetch</c> anahtarı tanımlı bir remote için
    /// <c>git remote get-url &lt;ad&gt;</c> çıkış kodu <b>0</b> ile <b>adın kendisini</b>
    /// basıyor (git adı URL sanıyor), <c>remote -v</c> ise boş bırakıyor. Aynı soruya iki
    /// farklı cevap; ikisi de kullanılmıyor.
    /// </remarks>
    public string? Url => FetchUrls.Count > 0 ? FetchUrls[0] : null;

    /// <summary>Push için ayrı URL tanımlı mı?</summary>
    /// <remarks>
    /// Bu sorunun cevabı <c>remote -v</c>'nin <c>(push)</c> satırında <b>yok</b>: pushurl
    /// tanımlı değilken git orada fetch URL'sini tekrarlıyor.
    /// </remarks>
    public bool HasSeparatePushUrl => PushUrls.Count > 0;

    /// <summary>Push'un gerçekten gideceği URL'ler.</summary>
    public IReadOnlyList<string> EffectivePushUrls =>
        PushUrls.Count > 0 ? PushUrls : FetchUrls;

    /// <summary>
    /// <c>fetch</c> refspec'i git'in kurduğu varsayılan mı?
    /// </summary>
    /// <remarks>
    /// Varsayılan olmayan refspec, yeniden adlandırmada git tarafından <b>güncellenmiyor</b>
    /// (ölçüldü; uyarı yalnızca stderr'de, çıkış kodu 0).
    /// </remarks>
    public bool HasDefaultFetchRefspec =>
        FetchRefspecs.Count == 1
        && string.Equals(FetchRefspecs[0], DefaultFetchRefspec(Name), StringComparison.Ordinal);

    /// <summary>Bir remote için git'in kurduğu varsayılan fetch refspec'i.</summary>
    public static string DefaultFetchRefspec(string name) =>
        $"+refs/heads/*:refs/remotes/{name}/*";

    /// <summary>
    /// URL'deki parolayı gizler: <c>https://ali:s3cr3t@host/x.git</c> →
    /// <c>https://ali:***@host/x.git</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Yalnızca <b>gösterim</b> içindir. Düzenleme kutusuna asla maskelenmiş değer
    /// konulmaz: kullanıcı <c>***</c>'ı kaydeder ve parolasını bozar.
    /// </remarks>
    public static string MaskCredentials(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        // Kullanıcı bilgisi yalnızca `şema://` biçiminde olabilir; `git@host:yol` (scp benzeri)
        // biçiminde `:` yoldan önce gelir ve parola taşımaz.
        int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return url;
        }

        int authorityStart = schemeEnd + 3;
        int at = url.IndexOf('@', authorityStart);
        if (at < 0)
        {
            return url;
        }

        int colon = url.IndexOf(':', authorityStart);
        if (colon < 0 || colon > at)
        {
            // Parola yok, yalnızca kullanıcı adı var.
            return url;
        }

        return string.Concat(url.AsSpan(0, colon + 1), "***", url.AsSpan(at));
    }
}
