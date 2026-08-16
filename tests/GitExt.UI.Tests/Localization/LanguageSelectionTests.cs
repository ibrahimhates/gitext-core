using Avalonia.Headless.XUnit;
using GitExt.UI.Commands;
using GitExt.UI.Localization;
using GitExt.UI.Themes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// Verifies the language picker on the settings screen (P11-T07).
/// </summary>
/// <remarks>
/// The picker can fail silently in two ways: the list comes back empty (the user cannot change
/// language), or the selection never reaches the translator (they pick one, nothing happens).
/// Neither throws.
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
        // An empty drop-down does not tell the user which language they are in.
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
        // 🔴 Property assignments during load also raise PropertyChanged. Unfiltered, merely
        // OPENING the settings screen would rewrite the language with nothing changed.
        // The same rationale is already written down on the theme side (SettingsViewModel.Apply).
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
        // Some tests and the designer construct it without a translator; the screen must still open.
        InMemorySettingsStore settings = new();

        SettingsViewModel model = new(
            new AppearanceService(Avalonia.Application.Current!, settings),
            settings,
            new CommandRegistry(settings));

        model.Languages.ShouldBeEmpty();
        model.Language.ShouldBeNull();
    }
}
