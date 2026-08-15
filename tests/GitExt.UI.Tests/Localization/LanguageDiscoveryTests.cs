using System.Reflection;
using System.Text;
using GitExt.UI.Localization;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// Yeni bir dilin <b>yalnızca dosya eklenerek</b> geldiğini doğrular (P11-T01).
/// </summary>
/// <remarks>
/// <para>
/// Gereksinim şuydu: <c>Locales/</c> klasörüne <c>fr.json</c> bırakmak, o dilin listede
/// görünmesi için yeterli olmalı — ne <c>.csproj</c>'a ne de C# koduna dokunmadan.
/// </para>
/// <para>
/// Doğrulama, gömülü kaynakları taklit eden bir <see cref="Assembly"/> yerine
/// <see cref="Translator"/>'a doğrudan kaynak akışı sağlayan bir kanca üzerinden yapılıyor:
/// gerçek keşif yolu (kaynak adı → dil kodu → katalog) aynen çalışıyor, yalnızca kaynakların
/// nereden geldiği değişiyor. Çalışma anında derleme üretmek için Roslyn eklemek, ADR-0006'nın
/// "bunu kendimiz yazabilir miyiz" eşiğini karşılamayan bir bağımlılık olurdu.
/// </para>
/// </remarks>
public class LanguageDiscoveryTests
{
    private static Translator Build(params (string Code, string Json)[] locales)
    {
        Dictionary<string, Func<Stream>> resources = locales.ToDictionary(
            l => $"GitExt.UI.Locales.{l.Code}.json",
            l => (Func<Stream>)(() => new MemoryStream(Encoding.UTF8.GetBytes(l.Json))));

        return Translator.ForTesting(new InMemorySettingsStore(), resources);
    }

    [Fact]
    public void Klasore_eklenen_yeni_dil_listede_beliriyor()
    {
        // 🔴 Asıl gereksinim bu: kod değişmeden dil ekleme.
        Translator translator = Build(
            ("en", """{"_meta":{"code":"en","name":"English"},"a":"A"}"""),
            ("tr", """{"_meta":{"code":"tr","name":"Türkçe"},"a":"A"}"""),
            ("fr", """{"_meta":{"code":"fr","name":"Français"},"a":"A"}"""));

        translator.Available.Count.ShouldBe(3);
        translator.Available.Select(l => l.Code).ShouldContain("fr");
        translator.Available.First(l => l.Code == "fr").Name.ShouldBe("Français");
    }

    [Fact]
    public void Yeni_dile_gecilebiliyor()
    {
        Translator translator = Build(
            ("en", """{"_meta":{"code":"en","name":"English"},"greeting":"Hello"}"""),
            ("fr", """{"_meta":{"code":"fr","name":"Français"},"greeting":"Bonjour"}"""));

        translator.Use("fr");

        translator["greeting"].ShouldBe("Bonjour");
    }

    [Fact]
    public void Yeni_dilde_eksik_anahtar_ingilizceye_dusuyor()
    {
        // Eksik çeviri, o dili kullanılamaz hâle getirmemeli: çevrilmemiş satır
        // İngilizce görünüyor, arayüz çalışmaya devam ediyor.
        //
        // ⚠️ Yedek İngilizce artık DOSYADAN değil koddan geliyor (P11-T10), bu yüzden
        // uydurma anahtar ("a", "b") kullanılamıyor — gerçek anahtarlarla doğrulanıyor.
        Translator translator = Build(
            ("fr", """{"_meta":{"code":"fr","name":"Français"},"settings.theme":"Thème"}"""));

        translator.Use("fr");

        translator["settings.theme"].ShouldBe("Thème");

        // fr.json'da yok → gömülü İngilizceden geliyor.
        translator["settings.language"].ShouldBe("Language");
    }

    [Fact]
    public void Meta_bloku_olmayan_dosya_kodunu_ad_olarak_kullaniyor()
    {
        // Ad eksikse dil listeden KAYBOLMAMALI — adsız da olsa seçilebilir kalmalı.
        Translator translator = Build(
            ("en", """{"_meta":{"code":"en","name":"English"},"a":"A"}"""),
            ("de", """{"a":"A"}"""));

        translator.Available.First(l => l.Code == "de").Name.ShouldBe("de");
    }

    [Fact]
    public void Bozuk_dil_dosyasi_digerlerini_etkilemiyor()
    {
        // Bir dosyadaki söz dizimi hatası uygulamayı açılmaz hâle getirmemeli.
        Translator translator = Build(
            ("tr", """{"_meta":{"code":"tr","name":"Türkçe"},"settings.theme":"Tema"}"""),
            ("xx", "{ bu geçerli JSON değil"));

        translator.Available.Select(l => l.Code).ShouldContain("tr");
        translator.Available.Select(l => l.Code).ShouldNotContain("xx");

        // İngilizce her zaman kullanılabilir: gömülü kopya dosyaya bağlı değil (P11-T10).
        translator["settings.theme"].ShouldBe("Theme");
    }

    [Fact]
    public void Gercek_derlemede_dil_dosyalari_gomulu()
    {
        // Yukarıdaki testler keşif MANTIĞINI doğruluyor; bu test .csproj'daki joker
        // girdinin gerçekten çalıştığını doğruluyor. İkisi ayrı şeyler: mantık doğru
        // olup dosyalar gömülmezse uygulama dilsiz açılırdı.
        string[] embedded = typeof(Translator).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith("GitExt.UI.Locales.", StringComparison.Ordinal))
            .ToArray();

        embedded.ShouldContain("GitExt.UI.Locales.en.json");
        embedded.ShouldContain("GitExt.UI.Locales.tr.json");
    }
}
