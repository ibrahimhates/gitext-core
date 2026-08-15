namespace GitExt.Core;

/// <summary>
/// Bir uzak depo adının neden kabul edilemediği (P06-T05).
/// </summary>
public enum RemoteNameProblem
{
    /// <summary>Ad boş veya yalnızca boşluk.</summary>
    Empty,

    /// <summary>
    /// Ad <c>refs/</c> ile başlıyor.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ:</b> git bunu hata saymıyor — <c>git remote add refs/remotes/x …</c>
    /// çıkış kodu <b>0</b> veriyor ve <c>refs/remotes/refs/remotes/x/*</c> altına yazan bir
    /// remote oluşuyor. Kullanıcı <c>branch -a</c> çıktısından ad kopyaladığında sessizce
    /// iç içe bir ad elde ediyor. Bu reddi git değil <b>biz</b> koyuyoruz (P06-T01'deki
    /// <c>refs/heads/</c> kararının aynısı).
    /// </remarks>
    NestedRefsPrefix,

    /// <summary>
    /// Ad <c>-</c> ile başlıyor.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: git böyle bir adı <b>kabul ediyor</b> (<c>--</c> ayracıyla, çıkış kodu 0),
    /// ama <c>--</c> unutulduğu her yerde bayrak sanılıyor (<c>unknown switch</c>, rc=129).
    /// Kendi komutlarımız her zaman <c>--</c> kullanıyor; yine de <b>kullanıcının başka
    /// araçlarda</b> başına iş açacak bir ad üretmesine izin vermiyoruz.
    /// </remarks>
    LeadingDash,

    /// <summary>Yasak karakter: boşluk, kontrol karakteri, <c>~ ^ : ? * [ \</c>.</summary>
    ForbiddenCharacter,

    /// <summary>Bölüm <c>.</c> ile başlıyor veya <c>.lock</c> ile bitiyor.</summary>
    InvalidSegment,

    /// <summary>Boş bölüm: baştaki/sondaki <c>/</c> veya art arda <c>//</c>.</summary>
    EmptySegment,

    /// <summary>
    /// Art arda iki nokta (<c>..</c>).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Sonda nokta burada YOK</b> — dal adlarından ayrıldığı bir nokta daha.
    /// ÖLÇÜLDÜ: <c>git remote add -- "a." …</c> çalışıyor ve <c>a.</c> remote'u
    /// <b>tamamen işlevsel</b>: <c>fetch</c> geçiyor, <c>refs/remotes/a./main</c> oluşuyor,
    /// <c>rename</c> çalışıyor. Sebebi <c>check-ref-format</c>'ın kuralı: <b>ref'in tamamı</b>
    /// nokta ile bitemez, ama remote adı her zaman <c>/…</c> ile devam ettiği için
    /// <c>refs/remotes/a./HEAD</c> geçerli. <c>BranchName</c>'in kuralı kopyalansaydı
    /// git'in kabul ettiği bir ad sebepsiz reddedilirdi (ayrık test yakaladı).
    /// </remarks>
    InvalidDot,
}

/// <summary>
/// Uzak depo adı doğrulaması (P06-T05).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Neden <see cref="BranchName"/> yeniden KULLANILMIYOR?</b> Kurallar aynı değil.
/// ÖLÇÜLDÜ:
/// </para>
/// <list type="table">
///   <listheader><term>Ad</term><description><c>git remote add</c> · <c>git branch</c></description></listheader>
///   <item><term><c>HEAD</c></term><description><b>kabul</b> · ret</description></item>
///   <item><term><c>@{-1}</c></term><description>ret · "kabul" (ama başka bir ada çevirerek)</description></item>
/// </list>
/// <para>
/// <c>BranchName</c> burada kullanılsaydı <c>HEAD</c> adlı bir remote — git'in izin verdiği
/// ve GitHub akışlarında görülebilen bir ad — sebepsiz reddedilirdi.
/// </para>
/// <para>
/// Doğrulama <b>saf</b>: kullanıcı yazarken her tuş vuruşunda süreç başlatmıyoruz. Sapma
/// sessiz olurdu, bu yüzden ayrık bir test aynı adları hem buraya hem <b>gerçek</b>
/// <c>git remote add</c>'e veriyor (bilinçli sapmalar testte adıyla listeli).
/// </para>
/// </remarks>
public static class RemoteName
{
    /// <summary>Uzak izleme dallarının ref öneki.</summary>
    public const string RemotesPrefix = "refs/remotes/";

    /// <summary>
    /// Adı doğrular.
    /// </summary>
    /// <param name="name">Kullanıcının yazdığı ad.</param>
    /// <returns>Sorun yoksa <see langword="null"/>.</returns>
    public static RemoteNameProblem? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RemoteNameProblem.Empty;
        }

        // 🔴 git bunu hata saymıyor, sessizce iç içe ad oluşturuyor (ölçüldü).
        if (name.StartsWith("refs/", StringComparison.Ordinal))
        {
            return RemoteNameProblem.NestedRefsPrefix;
        }

        if (name[0] == '-')
        {
            return RemoteNameProblem.LeadingDash;
        }

        // Yalnızca `..`; sonda nokta git'te GEÇERLİ (yukarıdaki nota bak).
        if (name.Contains("..", StringComparison.Ordinal))
        {
            return RemoteNameProblem.InvalidDot;
        }

        foreach (char c in name)
        {
            if (IsForbidden(c))
            {
                return RemoteNameProblem.ForbiddenCharacter;
            }
        }

        // ⚠️ `Split` boş bölümleri koruyor: baştaki/sondaki `/` ve `//` böyle yakalanıyor.
        foreach (string segment in name.Split('/'))
        {
            if (segment.Length == 0)
            {
                return RemoteNameProblem.EmptySegment;
            }

            if (segment[0] == '.' || segment.EndsWith(".lock", StringComparison.Ordinal))
            {
                return RemoteNameProblem.InvalidSegment;
            }
        }

        return null;
    }

    /// <summary>Ad geçerli mi?</summary>
    public static bool IsValid(string? name) => Validate(name) is null;

    /// <summary>
    /// Sorunun kullanıcıya gösterilecek açıklaması.
    /// </summary>
    public static string Describe(RemoteNameProblem problem) => problem switch
    {
        RemoteNameProblem.Empty => "A name cannot be empty.",
        RemoteNameProblem.NestedRefsPrefix =>
            "A name cannot start with \"refs/\". Git does not reject it but it creates a nested name "
            + "(\"refs/remotes/refs/remotes/…\"); that is not what you want.",
        RemoteNameProblem.LeadingDash =>
            "A name cannot start with \"-\"; git commands would read it as an option.",
        RemoteNameProblem.ForbiddenCharacter =>
            "A name cannot contain spaces or these characters: ~ ^ : ? * [ \\",
        RemoteNameProblem.InvalidSegment =>
            "Components cannot start with \".\" or end with \".lock\".",
        RemoteNameProblem.EmptySegment => "A name cannot start or end with \"/\", or contain \"//\".",
        RemoteNameProblem.InvalidDot => "A name cannot contain \"..\".",
        _ => "The name is invalid.",
    };

    private static bool IsForbidden(char c) =>
        c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\' || char.IsControl(c);
}
