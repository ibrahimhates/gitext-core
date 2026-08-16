using System.Reflection;
using System.Text;
using GitExt.UI.Localization;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// Verifies that a new language arrives <b>by adding a file only</b> (P11-T01).
/// </summary>
/// <remarks>
/// <para>
/// The requirement was: dropping <c>fr.json</c> into the <c>Locales/</c> folder must be enough
/// for that language to appear in the list — without touching <c>.csproj</c> or any C# code.
/// </para>
/// <para>
/// The verification goes through a hook that feeds resource streams straight to
/// <see cref="Translator"/>, instead of an <see cref="Assembly"/> that fakes embedded resources:
/// the real discovery path (resource name → language code → catalog) runs unchanged, only where
/// the resources come from differs. Pulling in Roslyn to emit an assembly at run time would be a
/// dependency that fails ADR-0006's "could we write this ourselves" bar.
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
        // 🔴 This is the real requirement: adding a language without changing code.
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
        // A missing translation must not make the language unusable: the untranslated line
        // shows up in English and the UI keeps working.
        //
        // ⚠️ The English fallback now comes from code, not FROM A FILE (P11-T10), so made-up
        // keys ("a", "b") cannot be used — this is verified with real keys.
        Translator translator = Build(
            ("fr", """{"_meta":{"code":"fr","name":"Français"},"settings.theme":"Thème"}"""));

        translator.Use("fr");

        translator["settings.theme"].ShouldBe("Thème");

        // Not in fr.json → comes from the built-in English.
        translator["settings.language"].ShouldBe("Language");
    }

    [Fact]
    public void Meta_bloku_olmayan_dosya_kodunu_ad_olarak_kullaniyor()
    {
        // A missing name must NOT remove the language from the list — nameless, it stays selectable.
        Translator translator = Build(
            ("en", """{"_meta":{"code":"en","name":"English"},"a":"A"}"""),
            ("de", """{"a":"A"}"""));

        translator.Available.First(l => l.Code == "de").Name.ShouldBe("de");
    }

    [Fact]
    public void Bozuk_dil_dosyasi_digerlerini_etkilemiyor()
    {
        // A syntax error in one file must not make the application fail to start.
        Translator translator = Build(
            ("tr", """{"_meta":{"code":"tr","name":"Türkçe"},"settings.theme":"Tema"}"""),
            ("xx", "{ bu geçerli JSON değil"));

        translator.Available.Select(l => l.Code).ShouldContain("tr");
        translator.Available.Select(l => l.Code).ShouldNotContain("xx");

        // English is always available: the built-in copy does not depend on a file (P11-T10).
        translator["settings.theme"].ShouldBe("Theme");
    }

    [Fact]
    public void Gercek_derlemede_dil_dosyalari_gomulu()
    {
        // The tests above verify the discovery LOGIC; this test verifies that the wildcard
        // entry in .csproj actually works. Two separate things: if the logic were right but
        // the files were not embedded, the application would start with no languages.
        string[] embedded = typeof(Translator).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith("GitExt.UI.Locales.", StringComparison.Ordinal))
            .ToArray();

        embedded.ShouldContain("GitExt.UI.Locales.en.json");
        embedded.ShouldContain("GitExt.UI.Locales.tr.json");
    }
}
