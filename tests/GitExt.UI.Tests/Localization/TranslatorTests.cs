using System.ComponentModel;
using GitExt.UI.Localization;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// Verifies the behaviour of the translation infrastructure (P11-T01).
/// </summary>
/// <remarks>
/// The gap these tests close is localisation going wrong <b>silently</b>: a missing key produces
/// an empty label, an undiscovered language never shows up in the list, and if a language change
/// does not refresh the UI the user thinks nothing happened. None of these throw.
/// </remarks>
public class TranslatorTests
{
    private static Translator Build() => new(new InMemorySettingsStore());

    [Fact]
    public void Gomulu_dil_dosyalari_kesfediliyor()
    {
        // Languages come from embedded resources, not from a list hard-coded in the source.
        // If this test breaks, either the wildcard entry (.csproj) or the resource prefix broke.
        Translator translator = Build();

        translator.Available.Select(l => l.Code).ShouldContain("en");
        translator.Available.Select(l => l.Code).ShouldContain("tr");
    }

    [Fact]
    public void Dilin_adi_kendi_dilinde_gosteriliyor()
    {
        // The native name, not "Turkish": whoever reads the language list is LOOKING FOR it.
        Translator translator = Build();

        translator.Available.First(l => l.Code == "tr").Name.ShouldBe("Türkçe");
        translator.Available.First(l => l.Code == "en").Name.ShouldBe("English");
    }

    [Fact]
    public void Varsayilan_dil_ingilizce()
    {
        Build().Current.ShouldBe("en");
    }

    [Fact]
    public void Dil_degistirilebiliyor()
    {
        Translator translator = Build();

        translator.Use("tr");

        translator.Current.ShouldBe("tr");

        // The Turkish equivalent: did the language actually change, or only the code?
        translator["settings.settings"].ShouldBe("Ayarlar");
    }

    [Fact]
    public void Dil_tercihi_ayarlara_yaziliyor()
    {
        InMemorySettingsStore settings = new();
        Translator translator = new(settings);

        translator.Use("tr");

        settings.Current.General.Language.ShouldBe("tr");
    }

    [Fact]
    public void Dil_degisince_tum_baglamalar_tazeleniyor()
    {
        // PropertyChanged(null) = "everything on this object changed". Avalonia reads that as
        // re-evaluating all indexer bindings. Without this notification a language change would
        // only become visible after reopening the window.
        Translator translator = Build();
        List<string?> changed = [];
        ((INotifyPropertyChanged)translator).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        translator.Use("tr");

        changed.ShouldContain((string?)null);
    }

    [Fact]
    public void Ayni_dile_gecmek_bildirim_uretmiyor()
    {
        // A needless refresh means re-evaluating the whole UI on every settings save.
        Translator translator = Build();
        translator.Use("tr");

        int count = 0;
        ((INotifyPropertyChanged)translator).PropertyChanged += (_, _) => count++;

        translator.Use("tr");

        count.ShouldBe(0);
    }

    [Fact]
    public void Taninmayan_dil_yok_sayiliyor()
    {
        // Switching to a nonexistent language must not leave the UI full of key names.
        Translator translator = Build();

        translator.Use("klingon");

        translator.Current.ShouldBe("en");
    }

    [Fact]
    public void Eksik_anahtar_anahtarin_kendisini_donduruyor()
    {
        // Returning an empty string is the worst option: an empty label appears in the UI and
        // there is no way to tell where the gap came from. Showing the key makes the gap visible
        // at a glance and also tells you which key needs to be added.
        Build()["boyle.bir.anahtar.yok"].ShouldBe("boyle.bir.anahtar.yok");
    }

    [Fact]
    public void Bos_anahtar_cokmuyor()
    {
        Build()[""].ShouldBe(string.Empty);
    }

    [Fact]
    public void Format_yer_tutuculari_dolduruyor()
    {
        // A real key with a placeholder is used: the English fallback now comes from code, not
        // from a file (P11-T10), so a made-up "en" catalog cannot be injected.
        Translator translator = new(new InMemorySettingsStore());

        translator.Format("commit_list.commits_loaded", 42).ShouldBe("42 commits loaded…");
    }

    [Fact]
    public void Bozuk_yer_tutucu_cokmuyor()
    {
        // A broken "{0" in a translation must not crash the application: the raw template is
        // shown, the error stays visible but the UI stays up. A broken template can come from a
        // TRANSLATION, so this is set up through the active language.
        Translator translator = Translator.ForTesting(
            new InMemorySettingsStore(),
            new Dictionary<string, Func<Stream>>
            {
                ["GitExt.UI.Locales.tr.json"] = () => new MemoryStream(
                    System.Text.Encoding.UTF8.GetBytes(
                        """{"_meta":{"code":"tr","name":"Türkçe"},"commit_list.commits_loaded":"{0 bozuk"}""")),
            });

        translator.Use("tr");

        Should.NotThrow(() => translator.Format("commit_list.commits_loaded", 1));
    }

    [Fact]
    public void Kayitli_tercih_baslangicta_uygulaniyor()
    {
        InMemorySettingsStore settings = new();
        settings.Current.General.Language = "tr";

        Translator translator = new(settings);
        translator.ApplyStored();

        translator.Current.ShouldBe("tr");
    }

    [Fact]
    public void Tercih_yoksa_ingilizce_kaliyor()
    {
        // Under InvariantGlobalization=true, CurrentUICulture comes back empty (measured,
        // P11-T00), so system language detection does not work and it falls back to English.
        InMemorySettingsStore settings = new();

        Translator translator = new(settings);
        translator.ApplyStored();

        translator.Current.ShouldBe("en");
    }
}
