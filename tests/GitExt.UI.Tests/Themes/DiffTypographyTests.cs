using Avalonia;
using Avalonia.Headless.XUnit;
using GitExt.UI.Settings;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.Themes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.Themes;

/// <summary>
/// P14-T05 — the diff has its own, larger font, as it does in GitExtensions.
/// </summary>
/// <remarks>
/// GitExtensions keeps two monospace settings, not one: <c>MonospaceFont</c> is Consolas 9pt and
/// <c>DiffFont</c> Consolas 10pt. Levelling them would shrink the surface the user reads longest.
/// </remarks>
public class DiffTypographyTests
{
    [Fact]
    public void Diff_varsayilani_monospaceten_bir_punto_buyuk()
    {
        TypographyDefaults.DiffFontSizeOffset.ShouldBe(1);

        new DiffViewModel(new FakeDiffReader()).FontSize
            .ShouldBe(TypographyDefaults.MonospaceFontSize + TypographyDefaults.DiffFontSizeOffset);
    }

    [AvaloniaFact]
    public void Yazi_boyutu_ayari_diff_kaynagini_da_gunceller()
    {
        InMemorySettingsStore settings = new();
        AppearanceService appearance = new(Application.Current!, settings);

        appearance.SetFontSizes(uiFontSize: 14, monospaceFontSize: 16);

        Application.Current!.Resources["GitExtMonospaceFontSize"].ShouldBe(16d);
        Application.Current!.Resources["GitExtDiffFontSize"]
            .ShouldBe(16d + TypographyDefaults.DiffFontSizeOffset);
    }

    [AvaloniaFact]
    public void Diff_kaynagi_monospace_ile_birlikte_hareket_eder()
    {
        InMemorySettingsStore settings = new();
        AppearanceService appearance = new(Application.Current!, settings);

        appearance.SetFontSizes(uiFontSize: 12, monospaceFontSize: 10);

        double small = (double)Application.Current!.Resources["GitExtDiffFontSize"]!;

        appearance.SetFontSizes(uiFontSize: 12, monospaceFontSize: 20);

        double large = (double)Application.Current!.Resources["GitExtDiffFontSize"]!;

        large.ShouldBeGreaterThan(small);
        (large - small).ShouldBe(10);
    }
}
