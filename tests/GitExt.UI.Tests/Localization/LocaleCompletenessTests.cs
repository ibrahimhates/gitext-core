using System.Reflection;
using System.Text.Json;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// Verifies that the language files are complete and consistent (P11-T08).
/// </summary>
/// <remarks>
/// <para>
/// This is the <b>real</b> guard for localisation. A missing translation throws nothing, breaks
/// no test, stops no build — only that one line shows up in the wrong language. The user notices,
/// we do not.
/// </para>
/// <para>
/// When a key is added and written only into <c>en.json</c>, these tests break; that is,
/// forgetting the translation is <b>not possible.</b>
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
        // 🔴 This is the real guard. Adding a key only to en.json means that line shows up in
        // English on the Turkish UI — and nothing tells you about it.
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
        // An empty value means an empty label in the UI — worse than a missing key, because
        // not even Translator's fallback path kicks in.
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
        // 🔴 If "{0}" is dropped from a translation the text silently shows up with missing
        // information; if an extra "{1}" is added, the closest thing to a FormatException is
        // the raw template being displayed. Both only surface for users of that one language.
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
        // This test breaks when a new value is added to the enum: forgetting to add it means
        // showing the user raw git output for that error kind.
        Dictionary<string, string> english = LoadAll()[Translator.FallbackLanguage];

        foreach (GitFailureKind kind in Enum.GetValues<GitFailureKind>())
        {
            if (kind == GitFailureKind.Unknown)
            {
                // Deliberate: an unclassifiable error falls back to the raw message (Loc.GitError).
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
