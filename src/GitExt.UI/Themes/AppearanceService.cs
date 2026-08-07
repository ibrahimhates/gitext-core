using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using GitExt.UI.Settings;

namespace GitExt.UI.Themes;

/// <summary>
/// Görünüm ayarlarını (tema, palet, tipografi) uygulamaya bağlar (P08-T07…T10).
/// </summary>
public interface IAppearanceService
{
    /// <summary>Kaydedilmiş bütün görünüm ayarlarını uygular. Açılışta bir kez çağrılır.</summary>
    void ApplyStored();

    /// <summary>Tema tercihini değiştirir ve kaydeder.</summary>
    void SetTheme(ThemePreference preference);

    /// <summary>Palet tercihini değiştirir ve kaydeder.</summary>
    void SetPalette(PalettePreference preference);

    /// <summary>Arayüz ve kod yazı tipi boyutlarını değiştirir ve kaydeder.</summary>
    void SetFontSizes(double uiFontSize, double monospaceFontSize);

    /// <summary>Sabit genişlikli yazı tipi ailesini değiştirir; boş dize platform varsayılanı.</summary>
    void SetMonospaceFont(string fontFamily);
}

/// <inheritdoc cref="IAppearanceService"/>
public sealed class AppearanceService : IAppearanceService
{
    private static readonly Uri ColorBlindSafeSource =
        new("avares://GitExt.UI/Themes/ColorBlindSafe.axaml");

    private readonly Application _application;
    private readonly ISettingsStore _settings;
    private readonly ThemeService _theme;

    private ResourceInclude? _colorBlindOverlay;

    public AppearanceService(Application application, ISettingsStore settings)
    {
        _application = application;
        _settings = settings;
        _theme = new ThemeService(application, settings);

        // Tema değişince grafik paleti YENİDEN hesaplanmalı: palet bir tema sözlüğünde
        // değil, hesaplanmış tek bir kaynakta duruyor (renk listesi XAML'de ifade
        // edilemiyor). Bu abonelik olmadan koyu temaya geçen kullanıcı açık zemine göre
        // seçilmiş şeritlerle kalırdı — bazıları neredeyse görünmez.
        _application.ActualThemeVariantChanged += (_, _) => ApplyGraphPalette();
    }

    public void ApplyStored()
    {
        _theme.ApplyStored();

        ApplyColorBlindOverlay();
        ApplyGraphPalette();
        ApplyTypography();
    }

    public void SetTheme(ThemePreference preference)
    {
        _theme.Apply(preference);

        ApplyGraphPalette();
    }

    public void SetPalette(PalettePreference preference)
    {
        _settings.Update(s => s.Appearance.Palette = preference.ToString());

        ApplyColorBlindOverlay();
        ApplyGraphPalette();
    }

    public void SetFontSizes(double uiFontSize, double monospaceFontSize)
    {
        _settings.Update(s =>
        {
            s.Appearance.UiFontSize = Clamp(uiFontSize);
            s.Appearance.MonospaceFontSize = Clamp(monospaceFontSize);
        });

        ApplyTypography();
    }

    public void SetMonospaceFont(string fontFamily)
    {
        _settings.Update(s => s.Appearance.MonospaceFontFamily = fontFamily ?? "");

        ApplyTypography();
    }

    /// <summary>
    /// Boyutu kullanılabilir aralığa sıkıştırır.
    /// </summary>
    /// <remarks>
    /// Sınırsız bırakmak, elle düzenlenmiş bir ayar dosyasındaki <c>0</c> ya da <c>900</c>
    /// değerinin uygulamayı <b>okunamaz</b> hâle getirmesi demekti — ve ayarı geri almak
    /// için de o okunamaz arayüzü kullanmak gerekirdi.
    /// </remarks>
    private static double Clamp(double size) =>
        double.IsFinite(size)
            ? Math.Clamp(size, TypographyDefaults.MinimumFontSize, TypographyDefaults.MaximumFontSize)
            : TypographyDefaults.UiFontSize;

    private PalettePreference Palette =>
        SettingsEnum.Parse(_settings.Current.Appearance.Palette, PalettePreference.Default);

    /// <summary>
    /// Renk körlüğü katmanını ekler veya kaldırır.
    /// </summary>
    /// <remarks>
    /// Katman sonradan birleştirildiği için varsayılan paletin üstünde kalıyor; kaldırılınca
    /// altındaki değerler kendiliğinden geri geliyor. İki tam palet tutmak yerine yalnızca
    /// <b>farkı</b> tutmanın sebebi bu: ortak anahtarlar tek yerde kalıyor.
    /// </remarks>
    private void ApplyColorBlindOverlay()
    {
        bool wanted = Palette == PalettePreference.ColorBlindSafe;

        if (wanted && _colorBlindOverlay is null)
        {
            _colorBlindOverlay = new ResourceInclude((Uri?)null) { Source = ColorBlindSafeSource };

            _application.Resources.MergedDictionaries.Add(_colorBlindOverlay);
        }
        else if (!wanted && _colorBlindOverlay is not null)
        {
            _application.Resources.MergedDictionaries.Remove(_colorBlindOverlay);
            _colorBlindOverlay = null;
        }
    }

    private void ApplyGraphPalette()
    {
        _application.Resources[GraphPalettes.ResourceKey] =
            GraphPalettes.Resolve(_application.ActualThemeVariant, Palette);
    }

    private void ApplyTypography()
    {
        AppearanceSettings appearance = _settings.Current.Appearance;

        double ui = Clamp(appearance.UiFontSize);
        double mono = Clamp(appearance.MonospaceFontSize);

        _application.Resources["GitExtUiFontSize"] = ui;
        _application.Resources["GitExtMonospaceFontSize"] = mono;

        // Türetilmiş boyutlar ana boyutla birlikte kayıyor; sabit kalsalardı büyütülen
        // arayüzde ipuçları ve rozetler orantısız biçimde küçük kalırdı.
        _application.Resources["GitExtSmallFontSize"] = Math.Max(
            TypographyDefaults.MinimumFontSize,
            ui - 1);
        _application.Resources["GitExtLargeFontSize"] = ui + 2;

        if (appearance.MonospaceFontFamily.Length > 0)
        {
            _application.Resources["GitExtMonospaceFont"] = new FontFamily(appearance.MonospaceFontFamily);
        }
    }
}
