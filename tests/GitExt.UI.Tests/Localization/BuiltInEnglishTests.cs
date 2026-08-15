using System.Text;
using GitExt.UI.Localization;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// İngilizce metinlerin dosyaya bağlı OLMADIĞINI doğrular (P11-T10).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Ölçülmüş kusur:</b> yedek dil önce <c>en.json</c>'dan okunuyordu. Dosya silinir
/// veya bozulursa yedek <b>boş sözlük</b> oluyor ve etkin dilde bulunmayan her anahtar
/// arayüzde ham anahtar olarak görünüyordu — <c>settings.language</c> gibi. Hiçbir istisna
/// fırlatmıyor, hiçbir test kırılmıyordu.
/// </para>
/// <para>
/// Bu testler <c>en.json</c>'un <b>hiç olmadığı</b> durumu kuruyor: gömülü kopya devredeyse
/// arayüz yine tam İngilizce.
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
        // 🔴 Asıl senaryo: dosya silinmiş. Eskiden burada anahtar adı dönerdi.
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

        // tr.json'da yok → gömülü İngilizceden geliyor, anahtar adı DEĞİL.
        translator["settings.language"].ShouldBe("Language");
    }

    [Fact]
    public void En_json_yokken_ingilizce_yine_secilebiliyor()
    {
        // Dil listesinde İngilizce görünmezse kullanıcı ona geri dönemezdi.
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
        // Silinmek zorunda değil; bozulması da yeter.
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
        // Gömülü kopya en.json'dan ÜRETİLİYOR; ikisi ayrışırsa çeviri kaynağı belirsizleşir.
        // CI ayrıca generate-fallback.py --check ile bunu doğruluyor; bu test aynı
        // güvenceyi çalışma anında veriyor.
        Translator translator = new(new InMemorySettingsStore());

        foreach ((string key, string english) in BuiltInEnglish.Entries)
        {
            translator[key].ShouldBe(english, $"{key} dosya ile gömülü kopya arasında farklı");
        }
    }
}
