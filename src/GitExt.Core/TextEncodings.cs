using System.Text;

namespace GitExt.Core;

/// <summary>
/// Metin kodlamalarını çözer ve eski kod sayfalarını kullanılabilir kılar (P04-T07).
/// </summary>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ:</b> .NET varsayılan olarak yalnızca UTF-8/16/32, ASCII ve Latin-1 <i>kayıtlı</i>
/// tutuyor; <c>Encoding.GetEncoding("ISO-8859-9")</c> <b>istisna fırlatıyor</b>. Bir Git
/// arayüzü için bu kabul edilemez: kullanıcının deposundaki dosyalar Windows-1254, Shift-JIS
/// veya başka bir eski kod sayfasında olabilir ve diff'te okunamaz hâle gelir.
/// <c>System.Text.Encoding.CodePages</c> paketine <b>gerek yok</b> — sağlayıcı .NET 10'un
/// paylaşılan çatısında zaten var, yalnızca kaydedilmesi gerekiyor (NuGet paketi eklemeye
/// çalışınca NU1510 ile "gereksiz" uyarısı verdi).
/// </para>
/// <para>
/// <see cref="CodePagesEncodingProvider"/> bir kez kaydedilince <see cref="Encoding.GetEncoding(string)"/>
/// her yerde çalışır. Kayıt statik kurucuda yapılıyor, yani bu sınıfa ilk erişimde. Tüm
/// kodlama çözümlemeleri <see cref="TryGet"/> üzerinden geçtiği için bu yeterli;
/// <c>ModuleInitializer</c> kütüphane kodunda önerilmiyor (CA2255).
/// </para>
/// </remarks>
public static class TextEncodings
{
    /// <summary>Kodlama adı verilmediğinde kullanılan varsayılan.</summary>
    public static Encoding Default => Encoding.UTF8;

    static TextEncodings() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Eski kod sayfalarının kayıtlı olduğundan emin olur.
    /// </summary>
    /// <remarks>
    /// Statik kurucuyu tetiklemekten başka bir şey yapmaz. Uygulama açılışında bir kez
    /// çağrılır ki <see cref="Encoding.GetEncoding(string)"/> her yerde çalışsın.
    /// </remarks>
    public static void EnsureRegistered()
    {
        // Statik kurucu bu çağrıyla tetiklenir.
    }

    /// <summary>
    /// Ada göre kodlama çözer; tanınmıyorsa <see langword="null"/> döner.
    /// </summary>
    /// <remarks>
    /// Kullanıcı ayarından gelen bir ad geçersiz olabilir. İstisna fırlatmak yerine
    /// <see langword="null"/> dönmek, çağıranın varsayılana düşmesine izin verir —
    /// tek bir yanlış ayar yüzünden diff hiç gösterilmemeli.
    /// </remarks>
    public static Encoding? TryGet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
