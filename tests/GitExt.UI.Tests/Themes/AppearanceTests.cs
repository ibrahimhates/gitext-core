using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GitExt.UI.Settings;
using GitExt.UI.Themes;

namespace GitExt.UI.Tests.Themes;

/// <summary>
/// P08-T09 / P08-T10 — graph palette, colour-blind alternative and typography.
/// </summary>
public class AppearanceTests
{
    private static (AppearanceService Service, InMemorySettingsStore Settings) Create()
    {
        InMemorySettingsStore settings = new();

        return (new AppearanceService(Application.Current!, settings), settings);
    }

    private static void Restore()
    {
        Application application = Application.Current!;

        application.RequestedThemeVariant = ThemeVariant.Light;
        application.Resources.MergedDictionaries.Clear();

        Dispatcher.UIThread.RunJobs();
    }

    // ------------------------------------------------------------------ grafik paleti

    /// <summary>
    /// 🔴 The light and dark palettes cannot be the same.
    /// </summary>
    /// <remarks>
    /// A dark lane colour like <c>#264653</c>, picked against a light background, is
    /// <b>almost invisible</b> on a dark one. Using the same list in both themes meant some
    /// branches disappeared in the dark theme.
    /// </remarks>
    [Fact]
    public void Acik_ve_koyu_paletler_ayri()
    {
        GraphPalettes.LightDefault.ShouldNotBe(GraphPalettes.DarkDefault);
        GraphPalettes.LightColorBlindSafe.ShouldNotBe(GraphPalettes.DarkColorBlindSafe);
    }

    [Fact]
    public void Butun_paletler_ayni_uzunlukta()
    {
        int expected = GraphPalettes.LightDefault.Count;

        expected.ShouldBeGreaterThanOrEqualTo(8, "şerit sayısı ölçümde 2–3 çıktı ama derin "
            + "geçmişlerde daha fazlası mümkün; palet erken tekrara düşmemeli");

        GraphPalettes.DarkDefault.Count.ShouldBe(expected);
        GraphPalettes.LightColorBlindSafe.Count.ShouldBe(expected);
        GraphPalettes.DarkColorBlindSafe.Count.ShouldBe(expected);
    }

    [Theory]
    [InlineData(false, PalettePreference.Default)]
    [InlineData(true, PalettePreference.Default)]
    [InlineData(false, PalettePreference.ColorBlindSafe)]
    [InlineData(true, PalettePreference.ColorBlindSafe)]
    public void Palet_zemin_ve_tercihe_gore_seciliyor(bool dark, PalettePreference preference)
    {
        IReadOnlyList<Color> resolved = GraphPalettes.Resolve(
            dark ? ThemeVariant.Dark : ThemeVariant.Light,
            preference);

        IReadOnlyList<Color> expected = (dark, preference) switch
        {
            (false, PalettePreference.Default) => GraphPalettes.LightDefault,
            (true, PalettePreference.Default) => GraphPalettes.DarkDefault,
            (false, _) => GraphPalettes.LightColorBlindSafe,
            (true, _) => GraphPalettes.DarkColorBlindSafe,
        };

        resolved.ShouldBe(expected);
    }

    /// <summary>
    /// The colour-blind-safe palette does not rely on the red/green pair.
    /// </summary>
    /// <remarks>
    /// <b>Measurable definition:</b> under a deuteranopia simulation the red-green axis is useless
    /// for telling two colours apart; the separation has to be on the <b>blue-yellow axis and in
    /// luminance</b>. Here we verify that every colour pair separates on at least one of those two
    /// dimensions. Eyeballing it and saying "looks distinct enough" is not a verification.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Renk_korlugu_paletinde_her_cift_ayirt_edilebilir(bool dark)
    {
        IReadOnlyList<Color> palette = dark
            ? GraphPalettes.DarkColorBlindSafe
            : GraphPalettes.LightColorBlindSafe;

        List<string> tooClose = [];

        for (int i = 0; i < palette.Count; i++)
        {
            for (int j = i + 1; j < palette.Count; j++)
            {
                (double blueYellowA, double luminanceA) = Axes(palette[i]);
                (double blueYellowB, double luminanceB) = Axes(palette[j]);

                double blueYellow = Math.Abs(blueYellowA - blueYellowB);
                double luminance = Math.Abs(luminanceA - luminanceB);

                if (blueYellow < 0.10 && luminance < 0.12)
                {
                    tooClose.Add($"{palette[i]} ↔ {palette[j]} (b/y {blueYellow:F3}, parlaklık {luminance:F3})");
                }
            }
        }

        tooClose.ShouldBeEmpty();

        // Two dimensions independent of the red-green axis: blue-yellow and perceptual luminance.
        static (double BlueYellow, double Luminance) Axes(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            return (b - ((r + g) / 2), (0.2126 * r) + (0.7152 * g) + (0.0722 * b));
        }
    }

    /// <summary>The palette is written into resources and refreshed when the theme changes.</summary>
    [AvaloniaFact]
    public void Palet_kaynaga_yaziliyor_ve_tema_degisince_yenileniyor()
    {
        try
        {
            (AppearanceService service, _) = Create();

            service.SetTheme(ThemePreference.Light);
            Dispatcher.UIThread.RunJobs();

            Application.Current!.Resources[GraphPalettes.ResourceKey]
                .ShouldBe(GraphPalettes.LightDefault);

            service.SetTheme(ThemePreference.Dark);
            Dispatcher.UIThread.RunJobs();

            Application.Current!.Resources[GraphPalettes.ResourceKey]
                .ShouldBe(GraphPalettes.DarkDefault);
        }
        finally
        {
            Restore();
        }
    }

    // --------------------------------------------------------- colour-blind layer

    /// <summary>
    /// Turning the layer on changes the diff colours; turning it off <b>brings them back</b>.
    /// </summary>
    /// <remarks>
    /// Coming back matters: if the layer were two full palettes, switching it off would raise the
    /// question "which of the two palettes is current" instead of returning to the default. Only
    /// the difference is kept.
    /// </remarks>
    [AvaloniaFact]
    public void Renk_korlugu_katmani_acilip_kapanabiliyor()
    {
        try
        {
            (AppearanceService service, _) = Create();
            service.ApplyStored();
            Dispatcher.UIThread.RunJobs();

            Color before = Brush("GitExtDiffAddedBackgroundBrush");

            service.SetPalette(PalettePreference.ColorBlindSafe);
            Dispatcher.UIThread.RunJobs();

            Color safe = Brush("GitExtDiffAddedBackgroundBrush");
            safe.ShouldNotBe(before);

            service.SetPalette(PalettePreference.Default);
            Dispatcher.UIThread.RunJobs();

            Brush("GitExtDiffAddedBackgroundBrush").ShouldBe(before);
        }
        finally
        {
            Restore();
        }

        static Color Brush(string key)
        {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value)
                .ShouldBeTrue($"{key} çözülemedi");

            return ((ISolidColorBrush)value!).Color;
        }
    }

    /// <summary>
    /// The colour-blind layer is recomputed when the theme changes (P09-T11).
    /// </summary>
    /// <remarks>
    /// 🔴 The layer moved from XAML to code in P09-T11 — constructing a <c>ResourceInclude</c> at
    /// run time broke <c>PublishTrimmed</c> outright with <c>IL2026</c>. The overlaid theme
    /// dictionary did the light/dark split <b>itself</b>; the brushes moved into code do not, so
    /// they have to be rewritten on a theme change. Without this subscription a user switching to
    /// the dark theme would be left with diff colours picked against a light background — nearly
    /// unreadable on a dark one.
    /// </remarks>
    [AvaloniaFact]
    public void Renk_korlugu_katmani_tema_degisince_yeniden_hesaplaniyor()
    {
        try
        {
            (AppearanceService service, _) = Create();
            service.ApplyStored();
            service.SetPalette(PalettePreference.ColorBlindSafe);
            Dispatcher.UIThread.RunJobs();

            Color light = Brush("GitExtDiffAddedBackgroundBrush");
            light.ShouldBe(DiffPalettes.ColorBlindSafe(ThemeVariant.Light)["GitExtDiffAddedBackgroundBrush"]);

            service.SetTheme(ThemePreference.Dark);
            Dispatcher.UIThread.RunJobs();

            Color dark = Brush("GitExtDiffAddedBackgroundBrush");
            dark.ShouldBe(DiffPalettes.ColorBlindSafe(ThemeVariant.Dark)["GitExtDiffAddedBackgroundBrush"]);
            dark.ShouldNotBe(light);
        }
        finally
        {
            Restore();
        }

        static Color Brush(string key)
        {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value)
                .ShouldBeTrue($"{key} çözülemedi");

            return ((ISolidColorBrush)value!).Color;
        }
    }

    /// <summary>
    /// When the layer is switched off, every overridden key is dropped (P09-T11).
    /// </summary>
    /// <remarks>
    /// The keys come from <see cref="DiffPalettes.OverlayKeys"/>. If that list is incomplete,
    /// switching off leaves some brushes in the application dictionary: the palette looks
    /// "default" while the diff colours stay colour-blind, and the two palettes silently mix.
    /// </remarks>
    [AvaloniaFact]
    public void Katman_kapaninca_ezilen_butun_anahtarlar_birakiliyor()
    {
        try
        {
            (AppearanceService service, _) = Create();
            service.ApplyStored();
            Dispatcher.UIThread.RunJobs();

            service.SetPalette(PalettePreference.ColorBlindSafe);
            Dispatcher.UIThread.RunJobs();

            foreach (string key in DiffPalettes.OverlayKeys)
            {
                Application.Current!.Resources.ContainsKey(key)
                    .ShouldBeTrue($"{key} katman açıkken yazılmamış");
            }

            service.SetPalette(PalettePreference.Default);
            Dispatcher.UIThread.RunJobs();

            foreach (string key in DiffPalettes.OverlayKeys)
            {
                Application.Current!.Resources.ContainsKey(key)
                    .ShouldBeFalse($"{key} katman kapandıktan sonra da duruyor");
            }
        }
        finally
        {
            Restore();
        }
    }

    /// <remarks>
    /// The light and dark overlays must cover the same key set; if one is missing a key, that
    /// theme takes the brush from the default palette and the red/green distinction comes back
    /// in one single place for a colour-blind user.
    /// </remarks>
    [Fact]
    public void Acik_ve_koyu_kaplamalar_ayni_anahtarlari_kapsiyor()
    {
        IReadOnlyDictionary<string, Color> light = DiffPalettes.ColorBlindSafe(ThemeVariant.Light);
        IReadOnlyDictionary<string, Color> dark = DiffPalettes.ColorBlindSafe(ThemeVariant.Dark);

        light.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(DiffPalettes.OverlayKeys.OrderBy(k => k, StringComparer.Ordinal));
        dark.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(DiffPalettes.OverlayKeys.OrderBy(k => k, StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------- tipografi

    [AvaloniaFact]
    public void Yazi_boyutu_kaynaga_yaziliyor_ve_kaydediliyor()
    {
        try
        {
            (AppearanceService service, InMemorySettingsStore settings) = Create();

            service.SetFontSizes(16, 15);

            settings.Current.Appearance.UiFontSize.ShouldBe(16);
            settings.Current.Appearance.MonospaceFontSize.ShouldBe(15);
            Application.Current!.Resources["GitExtUiFontSize"].ShouldBe(16d);
            Application.Current!.Resources["GitExtMonospaceFontSize"].ShouldBe(15d);
        }
        finally
        {
            Restore();
        }
    }

    /// <summary>
    /// 🔴 The size is clamped.
    /// </summary>
    /// <remarks>
    /// A <c>0</c> in a hand-edited settings file would make the UI <b>unreadable</b>, and undoing
    /// the setting would require using that same unreadable UI.
    /// </remarks>
    [AvaloniaFact]
    public void Asiri_yazi_boyutu_sinirlaniyor()
    {
        try
        {
            (AppearanceService service, InMemorySettingsStore settings) = Create();

            service.SetFontSizes(0, 900);

            settings.Current.Appearance.UiFontSize.ShouldBe(TypographyDefaults.MinimumFontSize);
            settings.Current.Appearance.MonospaceFontSize.ShouldBe(TypographyDefaults.MaximumFontSize);
        }
        finally
        {
            Restore();
        }
    }

    /// <summary>Derived sizes scale together with the base size.</summary>
    [AvaloniaFact]
    public void Turetilmis_boyutlar_ana_boyutu_takip_ediyor()
    {
        try
        {
            (AppearanceService service, _) = Create();

            service.SetFontSizes(20, 20);

            Application.Current!.Resources["GitExtSmallFontSize"].ShouldBe(19d);
            Application.Current!.Resources["GitExtLargeFontSize"].ShouldBe(22d);
        }
        finally
        {
            Restore();
        }
    }

    [AvaloniaFact]
    public void Sabit_genislikli_yazi_tipi_degistirilebiliyor()
    {
        try
        {
            (AppearanceService service, _) = Create();

            service.SetMonospaceFont("Fira Code");

            Application.Current!.Resources["GitExtMonospaceFont"]
                .ShouldBeOfType<FontFamily>()
                .Name.ShouldBe("Fira Code");
        }
        finally
        {
            Restore();
        }
    }
}
