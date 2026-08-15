using Avalonia.Headless.XUnit;
using GitExt.UI.Commands;
using GitExt.UI.Localization;
using GitExt.UI.Themes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// Ayarlar ekranındaki dil seçicisini doğrular (P11-T07).
/// </summary>
/// <remarks>
/// Seçicinin sessizce çalışmaması iki biçimde olabilir: listenin boş gelmesi (kullanıcı dil
/// değiştiremiyor) ya da seçimin çevirmene ulaşmaması (seçiyor, hiçbir şey olmuyor).
/// İkisi de istisna fırlatmıyor.
/// </remarks>
public class LanguageSelectionTests
{
    private static SettingsViewModel Build(out Translator translator, out InMemorySettingsStore settings)
    {
        settings = new InMemorySettingsStore();
        translator = new Translator(settings);

        return new SettingsViewModel(
            new AppearanceService(Avalonia.Application.Current!, settings),
            settings,
            new CommandRegistry(settings),
            translator: translator);
    }

    [AvaloniaFact]
    public void Dil_listesi_gomulu_dosyalardan_doluyor()
    {
        SettingsViewModel model = Build(out _, out _);

        model.Languages.Select(l => l.Code).ShouldContain("en");
        model.Languages.Select(l => l.Code).ShouldContain("tr");
    }

    [AvaloniaFact]
    public void Acilista_etkin_dil_secili_geliyor()
    {
        // Boş bir açılır liste, kullanıcıya hangi dilde olduğunu söylemez.
        SettingsViewModel model = Build(out Translator translator, out _);

        model.Language.ShouldNotBeNull();
        model.Language!.Code.ShouldBe(translator.Current);
    }

    [AvaloniaFact]
    public void Secim_cevirmene_ulasiyor()
    {
        SettingsViewModel model = Build(out Translator translator, out _);

        model.Language = model.Languages.First(l => l.Code == "tr");

        translator.Current.ShouldBe("tr");
    }

    [AvaloniaFact]
    public void Secim_ayarlara_yaziliyor()
    {
        SettingsViewModel model = Build(out _, out InMemorySettingsStore settings);

        model.Language = model.Languages.First(l => l.Code == "tr");

        settings.Current.General.Language.ShouldBe("tr");
    }

    [AvaloniaFact]
    public void Ekran_acilirken_ayar_yeniden_yazilmiyor()
    {
        // 🔴 Yükleme sırasında özellik atamaları da PropertyChanged tetikliyor. Süzülmezse
        // ayarlar ekranını AÇMAK, hiçbir şey değiştirilmemişken dili yeniden yazardı.
        // Tema tarafında aynı gerekçe zaten yazılı (SettingsViewModel.Apply).
        InMemorySettingsStore settings = new();
        settings.Current.General.Language = "tr";

        Translator translator = new(settings);
        translator.ApplyStored();

        settings.Current.General.Language = "SENTINEL";

        _ = new SettingsViewModel(
            new AppearanceService(Avalonia.Application.Current!, settings),
            settings,
            new CommandRegistry(settings),
            translator: translator);

        settings.Current.General.Language.ShouldBe("SENTINEL");
    }

    [AvaloniaFact]
    public void Cevirmen_verilmezse_cokmuyor()
    {
        // Bazı testler ve tasarımcı çevirmensiz kuruyor; ekran yine açılabilmeli.
        InMemorySettingsStore settings = new();

        SettingsViewModel model = new(
            new AppearanceService(Avalonia.Application.Current!, settings),
            settings,
            new CommandRegistry(settings));

        model.Languages.ShouldBeEmpty();
        model.Language.ShouldBeNull();
    }
}
