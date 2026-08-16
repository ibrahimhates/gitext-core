using System.Text;
using GitExt.UI.Localization;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// Verifies that the English strings are NOT tied to a file (P11-T10).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Measured defect:</b> the fallback language used to be read from <c>en.json</c>. If the
/// file is deleted or corrupted the fallback becomes an <b>empty dictionary</b>, and every key
/// missing from the active language showed up in the UI as the raw key — like
/// <c>settings.language</c>. Nothing threw, no test broke.
/// </para>
/// <para>
/// These tests set up the case where <c>en.json</c> <b>does not exist at all</b>: if the built-in
/// copy is in play, the UI is still fully English.
/// </para>
/// </remarks>
public class BuiltInEnglishTests
{
    private static Dictionary<string, Func<Stream>> Locale(string code, string json) =>
        new()
        {
            [$"GitExt.UI.Locales.{code}.json"] =
                () => new MemoryStream(Encoding.UTF8.GetBytes(json)),
        };

    [Fact]
    public void Gomulu_ingilizce_bos_degil()
    {
        BuiltInEnglish.Entries.ShouldNotBeEmpty();
        BuiltInEnglish.Entries.Count.ShouldBeGreaterThan(400);
    }

    [Fact]
    public void En_json_hic_yokken_metinler_yine_geliyor()
    {
        // 🔴 The real scenario: the file is deleted. This used to return the key name.
        Translator translator = Translator.ForTesting(
            new InMemorySettingsStore(),
            Locale("tr", """{"_meta":{"code":"tr","name":"Türkçe"},"settings.theme":"Tema"}"""));

        translator["settings.theme"].ShouldBe("Theme");
        translator["settings.language"].ShouldBe("Language");
    }

    [Fact]
    public void En_json_yokken_turkcede_eksik_anahtar_ingilizceye_dusuyor()
    {
        Translator translator = Translator.ForTesting(
            new InMemorySettingsStore(),
            Locale("tr", """{"_meta":{"code":"tr","name":"Türkçe"},"settings.theme":"Tema"}"""));

        translator.Use("tr");

        translator["settings.theme"].ShouldBe("Tema");

        // Not in tr.json → comes from the built-in English, NOT the key name.
        translator["settings.language"].ShouldBe("Language");
    }

    [Fact]
    public void En_json_yokken_ingilizce_yine_secilebiliyor()
    {
        // If English is missing from the language list, the user could not switch back to it.
        Translator translator = Translator.ForTesting(
            new InMemorySettingsStore(),
            Locale("tr", """{"_meta":{"code":"tr","name":"Türkçe"},"settings.theme":"Tema"}"""));

        translator.Available.Select(l => l.Code).ShouldContain("en");

        translator.Use("tr");
        translator.Use("en");

        translator.Current.ShouldBe("en");
        translator["settings.theme"].ShouldBe("Theme");
    }

    [Fact]
    public void Bozuk_en_json_metinleri_kaybettirmiyor()
    {
        // It does not have to be deleted; being corrupted is enough.
        Translator translator = Translator.ForTesting(
            new InMemorySettingsStore(),
            new Dictionary<string, Func<Stream>>
            {
                ["GitExt.UI.Locales.en.json"] =
                    () => new MemoryStream(Encoding.UTF8.GetBytes("{ bu geçerli JSON değil")),
            });

        translator["settings.theme"].ShouldBe("Theme");
    }

    [Fact]
    public void Dosyadaki_en_json_gomuluyu_ezmiyor_ama_ayni_olmali()
    {
        // The built-in copy is GENERATED from en.json; if the two diverge the translation
        // source becomes ambiguous. CI also verifies this with generate-fallback.py --check;
        // this test gives the same guarantee at run time.
        Translator translator = new(new InMemorySettingsStore());

        foreach ((string key, string english) in BuiltInEnglish.Entries)
        {
            translator[key].ShouldBe(english, $"{key} dosya ile gömülü kopya arasında farklı");
        }
    }
}
