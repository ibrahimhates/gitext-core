namespace GitExt.Core;

/// <summary>
/// Bir dal adının neden kabul edilemediği (P06-T01).
/// </summary>
public enum BranchNameProblem
{
    /// <summary>Ad boş veya yalnızca boşluk.</summary>
    Empty,

    /// <summary>
    /// Ad <c>refs/heads/</c> ile başlıyor.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ:</b> git bunu hata saymıyor — <c>git branch refs/heads/x</c>
    /// <c>refs/heads/refs/heads/x</c> oluşturuyor. Kullanıcı tam ref adını yapıştırdığında
    /// sessizce iç içe bir dal elde ediyor.
    /// </remarks>
    NestedRefsPrefix,

    /// <summary>
    /// Ad revizyon sözdizimi içeriyor (<c>@{-1}</c>, <c>@{u}</c> gibi).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ:</b> <c>git branch</c> bunları <b>çeviriyor</b>; <c>@{-1}</c>
    /// "bir önceki dal" demek ve yazılan adla oluşan ad farklı oluyor.
    /// </remarks>
    RevisionSyntax,

    /// <summary>Ad <c>-</c> ile başlıyor (git bunu seçenek sanar).</summary>
    LeadingDash,

    /// <summary>Ad <c>HEAD</c> (git özel olarak reddediyor).</summary>
    ReservedHead,

    /// <summary>Yasak karakter: boşluk, kontrol karakteri, <c>~ ^ : ? * [ \</c>.</summary>
    ForbiddenCharacter,

    /// <summary>Bölüm <c>.</c> ile başlıyor veya <c>.lock</c> ile bitiyor.</summary>
    InvalidSegment,

    /// <summary>Boş bölüm: baştaki/sondaki <c>/</c> veya art arda <c>//</c>.</summary>
    EmptySegment,

    /// <summary>Art arda iki nokta (<c>..</c>) veya sonda nokta.</summary>
    InvalidDot,
}

/// <summary>
/// Dal adı doğrulaması (P06-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden saf bir uygulama?</b> Doğrulama kullanıcı yazarken çalışıyor; her tuş vuruşunda
/// <c>git check-ref-format</c> süreci başlatmak hem yavaş hem gereksiz. Buradaki kuralların
/// git'inkilerle aynı kaldığı <b>ayrık bir testle</b> (aynı adlar hem buraya hem gerçek
/// <c>git check-ref-format --branch</c>'a verilir) sabitlendi — sapma testte kırmızı olur.
/// </para>
/// <para>
/// <b>⚠️ ÖLÇÜLDÜ — tek bir <c>check-ref-format</c> çağrısı bu işi DOĞRU yapmıyor.</b>
/// İki form birbirine zıt cevap veriyor:
/// </para>
/// <list type="table">
///   <listheader><term>Ad</term><description><c>--branch</c> · <c>--allow-onelevel refs/heads/…</c></description></listheader>
///   <item><term><c>@{-1}</c></term><description>geçiyor (ve <b>başka bir ada çeviriyor</b>) · reddediyor</description></item>
///   <item><term><c>HEAD</c></term><description>reddediyor · <b>geçiyor</b></description></item>
///   <item><term><c>-x</c></term><description>reddediyor · <b>geçiyor</b></description></item>
/// </list>
/// <para>
/// Doğru referans <c>--branch</c>: <c>git branch</c>'ın kendisi de aynı kuralları uyguluyor
/// (<c>--</c> ayracından sonra bile <c>HEAD</c> ve <c>-x</c> reddediliyor). Ama <c>--branch</c>
/// doğrulamıyor, <b>çeviriyor</b>: <c>@{-1}</c> için çıktısı yazılan ad değil "bir önceki
/// dal"ın adı. Bu yüzden revizyon sözdizimi burada <b>ayrıca</b> eleniyor.
/// </para>
/// </remarks>
public static class BranchName
{
    /// <summary>Git'in tam dal ref'i öneki.</summary>
    public const string HeadsPrefix = "refs/heads/";

    /// <summary>
    /// Adı doğrular.
    /// </summary>
    /// <param name="name">Kullanıcının yazdığı ad.</param>
    /// <returns>Sorun yoksa <see langword="null"/>.</returns>
    public static BranchNameProblem? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BranchNameProblem.Empty;
        }

        // 🔴 git bunu hata saymıyor, sessizce iç içe dal oluşturuyor (ölçüldü).
        if (name.StartsWith(HeadsPrefix, StringComparison.Ordinal))
        {
            return BranchNameProblem.NestedRefsPrefix;
        }

        // `@{` git için revizyon sözdizimi. `--branch` bunu ÇEVİRİYOR; yazılan ad ile
        // oluşan ad farklı olurdu.
        if (name.Contains("@{", StringComparison.Ordinal))
        {
            return BranchNameProblem.RevisionSyntax;
        }

        if (name[0] == '-')
        {
            return BranchNameProblem.LeadingDash;
        }

        if (name.Equals("HEAD", StringComparison.Ordinal))
        {
            return BranchNameProblem.ReservedHead;
        }

        if (name.Contains("..", StringComparison.Ordinal) || name[^1] == '.')
        {
            return BranchNameProblem.InvalidDot;
        }

        foreach (char c in name)
        {
            if (IsForbidden(c))
            {
                return BranchNameProblem.ForbiddenCharacter;
            }
        }

        // ⚠️ `Split` boş bölümleri koruyor: baştaki/sondaki `/` ve `//` böyle yakalanıyor.
        foreach (string segment in name.Split('/'))
        {
            if (segment.Length == 0)
            {
                return BranchNameProblem.EmptySegment;
            }

            if (segment[0] == '.' || segment.EndsWith(".lock", StringComparison.Ordinal))
            {
                return BranchNameProblem.InvalidSegment;
            }
        }

        return null;
    }

    /// <summary>Ad geçerli mi?</summary>
    public static bool IsValid(string? name) => Validate(name) is null;

    private static bool IsForbidden(char c) =>
        c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\' || char.IsControl(c);
}
