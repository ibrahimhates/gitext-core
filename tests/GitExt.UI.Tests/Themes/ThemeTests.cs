using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GitExt.UI.Settings;
using GitExt.UI.Themes;

namespace GitExt.UI.Tests.Themes;

/// <summary>
/// P08-T07 / P08-T08 — theme infrastructure and the light/dark switch.
/// </summary>
public class ThemeTests
{
    /// <summary>
    /// Every key defined in the palette file.
    /// </summary>
    /// <remarks>
    /// Read from the source file, not listed by hand: keeping a hand-written list would mean a
    /// test that <b>does not catch</b> a new key going missing in one theme.
    /// </remarks>
    private static (IReadOnlyList<string> Light, IReadOnlyList<string> Dark) PaletteKeys()
    {
        string xaml = File.ReadAllText(PaletteFilePath());

        int darkStart = xaml.IndexOf(@"x:Key=""Dark""", StringComparison.Ordinal);

        darkStart.ShouldBeGreaterThan(0, "koyu tema sözlüğü bulunmalı");

        return (Keys(xaml[..darkStart]), Keys(xaml[darkStart..]));

        static IReadOnlyList<string> Keys(string section) =>
        [
            .. Regex.Matches(section, """<SolidColorBrush x:Key="(?<key>[^"]+)" """)
                .Select(m => m.Groups["key"].Value)
        ];
    }

    private static string PaletteFilePath()
    {
        // While the test runs the working directory is bin/…; we walk up to the repository root.
        DirectoryInfo? directory = new(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("depo kökü bulunmalı");

        return Path.Combine(directory.FullName, "src", "GitExt.UI", "Themes", "Palette.axaml");
    }

    /// <summary>
    /// 🔴 A key <b>must</b> be defined in both themes.
    /// </summary>
    /// <remarks>
    /// A brush defined in only one theme silently <b>does not render</b> in the other:
    /// <c>DynamicResource</c> does not throw when it cannot resolve, it leaves the value unset.
    /// The symptom is "the text is not showing" and the cause is written down nowhere. A
    /// half-finished theme is worse than no theme at all (P08-T08).
    /// </remarks>
    [Fact]
    public void Her_anahtar_iki_temada_da_tanimli()
    {
        (IReadOnlyList<string> light, IReadOnlyList<string> dark) = PaletteKeys();

        light.ShouldNotBeEmpty();
        dark.Except(light).ShouldBeEmpty("koyu temada olup açıkta olmayan anahtar");
        light.Except(dark).ShouldBeEmpty("açık temada olup koyuda olmayan anahtar");
    }

    /// <summary>
    /// No hard-coded colours may be left in the views.
    /// </summary>
    /// <remarks>
    /// A hard-coded colour leaves the theme switch <b>half done</b>: most of the screen goes dark,
    /// that one element stays light. The worst case is light-coloured text unreadable on a dark
    /// background.
    /// </remarks>
    [Fact]
    public void Gorunumlerde_gomulu_renk_yok()
    {
        DirectoryInfo root = new(Path.GetDirectoryName(PaletteFilePath())!);
        DirectoryInfo ui = root.Parent!;

        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(ui.FullName, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.Combine("Themes", ""), StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(File.ReadAllText(file), "#[0-9A-Fa-f]{3,8}"))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// The resource updates <b>live</b> when the theme changes.
    /// </summary>
    [AvaloniaFact]
    public void Tema_degisince_firca_degisiyor()
    {
        Application application = Application.Current!;
        ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

        try
        {
            Border border = new();
            Window window = new() { Content = border };
            window.Show();

            border.Bind(Border.BackgroundProperty, border.GetResourceObservable("GitExtDiffAddedBackgroundBrush"));

            application.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            Color light = ((ISolidColorBrush)border.Background!).Color;

            application.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Color dark = ((ISolidColorBrush)border.Background!).Color;

            light.ShouldNotBe(dark, "aynı anahtar iki temada aynı rengi vermemeli");

            window.Close();
        }
        finally
        {
            application.RequestedThemeVariant = original;
        }
    }

    // ------------------------------------------------------------------ ThemeService

    [AvaloniaTheory]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    [InlineData(ThemePreference.System)]
    public void Tercih_ayara_yaziliyor(ThemePreference preference)
    {
        InMemorySettingsStore settings = new();
        ThemeService service = new(Application.Current!, settings);

        ThemeVariant original = Application.Current!.RequestedThemeVariant ?? ThemeVariant.Default;

        try
        {
            service.Apply(preference);

            settings.Current.Appearance.Theme.ShouldBe(preference.ToString());
            service.Preference.ShouldBe(preference);
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = original;
        }
    }

    /// <summary>
    /// "Follow the system" maps to Avalonia's <c>Default</c>.
    /// </summary>
    /// <remarks>
    /// Measured in P08-T00/M07b: <c>Default</c> resolves to a concrete variant from the platform,
    /// so there is no need to write a separate listener to follow the system — writing one would
    /// do the same job a second time, worse.
    /// </remarks>
    [Theory]
    [InlineData(ThemePreference.Light, "Light")]
    [InlineData(ThemePreference.Dark, "Dark")]
    [InlineData(ThemePreference.System, "Default")]
    public void Tercih_dogru_varyanta_cevriliyor(ThemePreference preference, string expected)
    {
        ThemeService.ToVariant(preference).Key.ToString().ShouldBe(expected);
    }

    /// <summary>The default is light — even if the system is dark (user decision, 2026-07-29).</summary>
    [Fact]
    public void Varsayilan_tercih_ACIK()
    {
        InMemorySettingsStore settings = new();

        SettingsEnum.Parse(settings.Current.Appearance.Theme, ThemePreference.Dark)
            .ShouldBe(ThemePreference.Light);
    }

    [AvaloniaFact]
    public void Kaydedilmis_tercih_acilista_uygulaniyor()
    {
        InMemorySettingsStore settings = new();
        settings.Update(s => s.Appearance.Theme = "Dark");

        Application application = Application.Current!;
        ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

        try
        {
            new ThemeService(application, settings).ApplyStored();

            application.RequestedThemeVariant.ShouldBe(ThemeVariant.Dark);
        }
        finally
        {
            application.RequestedThemeVariant = original;
        }
    }
}
