using System.Reflection;
using System.Text.Json;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// Dil dosyalarının eksiksiz ve tutarlı olduğunu doğrular (P11-T08).
/// </summary>
/// <remarks>
/// <para>
/// Bu, yerelleştirmenin <b>asıl</b> korumasıdır. Eksik bir çeviri istisna fırlatmaz, testleri
/// kırmaz, derlemeyi durdurmaz — yalnızca o satır yanlış dilde görünür. Kullanıcı fark eder,
/// biz etmeyiz.
/// </para>
/// <para>
/// Bir anahtar eklenip yalnızca <c>en.json</c>'a yazıldığında bu testler kırılıyor; yani
/// çeviriyi unutmak <b>mümkün değil.</b>
/// </para>
/// </remarks>
public class LocaleCompletenessTests
{
    private static Dictionary<string, Dictionary<string, string>> LoadAll()
    {
        Assembly assembly = typeof(Translator).Assembly;
        Dictionary<string, Dictionary<string, string>> locales = [];

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith("GitExt.UI.Locales.", StringComparison.Ordinal))
            {
                continue;
            }

            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using JsonDocument document = JsonDocument.Parse(stream);

            Dictionary<string, string> entries = [];

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    entries[property.Name] = property.Value.GetString()!;
                }
            }

            locales[resource["GitExt.UI.Locales.".Length..^".json".Length]] = entries;
        }

        return locales;
    }

    [Fact]
    public void Butun_diller_ayni_anahtar_kumesine_sahip()
    {
        // 🔴 Asıl koruma bu. Bir anahtarı yalnızca en.json'a eklemek, o satırın Türkçe
        // arayüzde İngilizce görünmesi demek — ve hiçbir şey bunu haber vermez.
        Dictionary<string, Dictionary<string, string>> locales = LoadAll();

        locales.ShouldContainKey(Translator.FallbackLanguage);

        HashSet<string> reference = [.. locales[Translator.FallbackLanguage].Keys];

        foreach ((string code, Dictionary<string, string> entries) in locales)
        {
            HashSet<string> keys = [.. entries.Keys];

            keys.Except(reference).ShouldBeEmpty(
                $"{code}.json'da en.json'da olmayan anahtar var");

            reference.Except(keys).ShouldBeEmpty(
                $"{code}.json'da eksik anahtar var");
        }
    }

    [Fact]
    public void Hicbir_ceviri_bos_degil()
    {
        // Boş bir değer, arayüzde boş bir etiket demek — eksik anahtardan daha kötü,
        // çünkü Translator'ın geri düşme yolu bile devreye girmiyor.
        foreach ((string code, Dictionary<string, string> entries) in LoadAll())
        {
            foreach ((string key, string value) in entries)
            {
                value.ShouldNotBeNullOrWhiteSpace($"{code}.json → {key} boş");
            }
        }
    }

    [Fact]
    public void Her_dil_kendini_tanitiyor()
    {
        Translator translator = new(new InMemorySettingsStore());

        foreach ((string code, Dictionary<string, string> _) in LoadAll())
        {
            List<LanguageInfo> matches = [.. translator.Available.Where(l => l.Code == code)];

            matches.Count.ShouldBe(1, $"{code} için tam olarak bir dil kaydı olmalı");
            matches[0].Name.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Yer_tutucular_diller_arasinda_ayni()
    {
        // 🔴 Çeviride "{0}" düşerse metin sessizce eksik bilgiyle görünür; fazladan bir
        // "{1}" eklenirse FormatException'a en yakın hâli ham şablonun gösterilmesi olur.
        // İkisi de yalnızca o dili kullanan kullanıcıda ortaya çıkar.
        Dictionary<string, Dictionary<string, string>> locales = LoadAll();
        Dictionary<string, string> reference = locales[Translator.FallbackLanguage];

        foreach ((string code, Dictionary<string, string> entries) in locales)
        {
            if (code == Translator.FallbackLanguage)
            {
                continue;
            }

            foreach ((string key, string english) in reference)
            {
                if (!entries.TryGetValue(key, out string? translated))
                {
                    continue;
                }

                Placeholders(translated).ShouldBe(
                    Placeholders(english),
                    $"{code}.json → {key} yer tutucuları uyuşmuyor");
            }
        }

        static IEnumerable<string> Placeholders(string text) =>
            System.Text.RegularExpressions.Regex
                .Matches(text, @"\{\d+\}")
                .Select(m => m.Value)
                .Order();
    }

    [Fact]
    public void Her_GitFailureKind_degeri_icin_ceviri_var()
    {
        // Enum'a yeni bir değer eklendiğinde bu test kırılıyor: eklemeyi unutmak,
        // o hata türünde kullanıcıya ham git çıktısı göstermek demek.
        Dictionary<string, string> english = LoadAll()[Translator.FallbackLanguage];

        foreach (GitFailureKind kind in Enum.GetValues<GitFailureKind>())
        {
            if (kind == GitFailureKind.Unknown)
            {
                // Bilinçli: sınıflandırılamayan hata ham mesaja düşüyor (Loc.GitError).
                continue;
            }

            string key = $"git.error.{ToSnakeCase(kind.ToString())}";

            english.ShouldContainKey(key, $"{kind} için çeviri anahtarı yok");
        }

        static string ToSnakeCase(string name) =>
            string.Concat(name.Select((c, i) =>
                char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
