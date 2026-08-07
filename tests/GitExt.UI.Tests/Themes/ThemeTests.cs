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
/// P08-T07 / P08-T08 — tema altyapısı ve açık/koyu geçişi.
/// </summary>
public class ThemeTests
{
    /// <summary>
    /// Palet dosyasında tanımlı bütün anahtarlar.
    /// </summary>
    /// <remarks>
    /// Kaynak dosyadan okunuyor, elle listelenmiyor: elle liste tutmak, yeni bir anahtarın
    /// bir temada eksik kalmasını <b>yakalamayan</b> bir test demek olurdu.
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
        // Test çalışırken çalışma dizini bin/…; depo köküne çıkılıyor.
        DirectoryInfo? directory = new(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("depo kökü bulunmalı");

        return Path.Combine(directory.FullName, "src", "GitExt.UI", "Themes", "Palette.axaml");
    }

    /// <summary>
    /// 🔴 Bir anahtar iki temada da tanımlı olmak <b>zorunda</b>.
    /// </summary>
    /// <remarks>
    /// Yalnızca bir temada tanımlı bir fırça, diğer temada sessizce <b>çizilmez</b>:
    /// <c>DynamicResource</c> bulamadığında istisna atmaz, değeri atanmamış bırakır.
    /// Belirti "yazı görünmüyor" olur ve sebebi hiçbir yerde yazmaz. Yarım kalmış bir tema,
    /// temasızlıktan kötüdür (P08-T08).
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
    /// Görünümlerde gömülü renk kalmamalı.
    /// </summary>
    /// <remarks>
    /// Gömülü bir renk tema geçişini <b>kısmen</b> bırakır: ekranın çoğu koyulaşır, o bir
    /// öğe açık kalır. En kötü hâli, koyu zeminde okunmayan açık renkli bir yazıdır.
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
    /// Tema değişince kaynak <b>canlı</b> güncelleniyor.
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
    /// "Sistemi takip et" Avalonia'nın <c>Default</c>'una çevriliyor.
    /// </summary>
    /// <remarks>
    /// P08-T00/M07b'de ölçüldü: <c>Default</c> platformdan somut bir varyanta çözülüyor,
    /// yani sistemi izlemek için ayrı bir dinleyici yazmaya gerek yok — yazsaydık aynı işi
    /// ikinci kez, daha kötü yapardık.
    /// </remarks>
    [Theory]
    [InlineData(ThemePreference.Light, "Light")]
    [InlineData(ThemePreference.Dark, "Dark")]
    [InlineData(ThemePreference.System, "Default")]
    public void Tercih_dogru_varyanta_cevriliyor(ThemePreference preference, string expected)
    {
        ThemeService.ToVariant(preference).Key.ToString().ShouldBe(expected);
    }

    /// <summary>Varsayılan açık — sistem koyu olsa bile (kullanıcı kararı, 2026-07-29).</summary>
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
