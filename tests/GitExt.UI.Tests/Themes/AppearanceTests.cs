using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GitExt.UI.Settings;
using GitExt.UI.Themes;

namespace GitExt.UI.Tests.Themes;

/// <summary>
/// P08-T09 / P08-T10 — grafik paleti, renk körlüğü alternatifi ve tipografi.
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
    /// 🔴 Açık ve koyu paletler aynı olamaz.
    /// </summary>
    /// <remarks>
    /// Açık zemine göre seçilmiş <c>#264653</c> gibi koyu bir şerit, koyu zeminde
    /// <b>neredeyse görünmez</b>. Aynı listeyi iki temada kullanmak, koyu temada bazı
    /// dalların kaybolması demekti.
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
    /// Renk körlüğü uyumlu palet kırmızı/yeşil çiftine dayanmıyor.
    /// </summary>
    /// <remarks>
    /// <b>Ölçülebilir tanım:</b> deuteranopi benzetiminde iki rengin ayırt edilebilmesi için
    /// kırmızı-yeşil ekseni işe yaramaz; ayrım <b>mavi-sarı ekseninde ve parlaklıkta</b>
    /// olmalı. Burada her renk çiftinin bu iki boyutun en az birinde ayrıldığı doğrulanıyor.
    /// Gözle bakıp "ayrışıyor gibi" demek bir doğrulama değildir.
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

        // Kırmızı-yeşil ekseninden bağımsız iki boyut: mavi-sarı ve algısal parlaklık.
        static (double BlueYellow, double Luminance) Axes(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            return (b - ((r + g) / 2), (0.2126 * r) + (0.7152 * g) + (0.0722 * b));
        }
    }

    /// <summary>Palet kaynağa yazılıyor ve tema değişince yenileniyor.</summary>
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

    // ------------------------------------------------------- renk körlüğü katmanı

    /// <summary>
    /// Katman açılınca diff renkleri değişiyor, kapanınca <b>geri geliyor</b>.
    /// </summary>
    /// <remarks>
    /// Geri gelmesi önemli: katman iki tam palet olsaydı kapatmak, varsayılana dönmek yerine
    /// "iki paletten hangisi güncel" sorusunu doğururdu. Yalnızca fark tutuluyor.
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
    /// Renk körlüğü katmanı tema değişince yeniden hesaplanıyor (P09-T11).
    /// </summary>
    /// <remarks>
    /// 🔴 Katman P09-T11'de XAML'den koda taşındı — <c>ResourceInclude</c>'u çalışma
    /// zamanında kurmak <c>PublishTrimmed</c>'i <c>IL2026</c> ile tamamen kırıyordu.
    /// Bindirilen tema sözlüğü açık/koyu ayrımını <b>kendisi</b> yapıyordu; koda taşınan
    /// fırçalar yapmıyor, tema değişiminde yeniden yazılmaları gerekiyor. Bu abonelik
    /// olmasa koyu temaya geçen kullanıcı açık zemine göre seçilmiş diff renkleriyle
    /// kalırdı — koyu zeminde neredeyse okunmaz.
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
    /// Katman kapatılınca ezilen bütün anahtarlar bırakılıyor (P09-T11).
    /// </summary>
    /// <remarks>
    /// Anahtarlar <see cref="DiffPalettes.OverlayKeys"/>'ten geliyor. Liste eksik kalırsa
    /// kapatma bazı fırçaları uygulama sözlüğünde bırakır: palet "varsayılan" görünürken
    /// diff renkleri renk körü kalır ve iki palet sessizce karışır.
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
    /// Açık ve koyu kaplamalar aynı anahtar kümesini kapsamalı; biri eksikse o tema o
    /// fırçayı varsayılan paletten alır ve renk körü kullanıcı için kırmızı/yeşil ayrımı
    /// tek bir yerde geri gelir.
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
    /// 🔴 Boyut sınırlanıyor.
    /// </summary>
    /// <remarks>
    /// Elle düzenlenmiş bir ayar dosyasındaki <c>0</c> arayüzü <b>okunamaz</b> hâle getirirdi
    /// ve ayarı geri almak için de o okunamaz arayüzü kullanmak gerekirdi.
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

    /// <summary>Türetilmiş boyutlar ana boyutla birlikte kayıyor.</summary>
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
