using Avalonia;
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
    private readonly Application _application;
    private readonly ISettingsStore _settings;
    private readonly ThemeService _theme;

    private bool _colorBlindOverlayApplied;

    public AppearanceService(Application application, ISettingsStore settings)
    {
        _application = application;
        _settings = settings;
        _theme = new ThemeService(application, settings);

        // Tema değişince grafik paleti YENİDEN hesaplanmalı: palet bir tema sözlüğünde
        // değil, hesaplanmış tek bir kaynakta duruyor (renk listesi XAML'de ifade
        // edilemiyor). Bu abonelik olmadan koyu temaya geçen kullanıcı açık zemine göre
        // seçilmiş şeritlerle kalırdı — bazıları neredeyse görünmez.
        // 🔴 Kaplama da yeniden uygulanmalı, yalnızca grafik paleti değil: renk körlüğü
        // fırçaları artık koda taşındı (P09-T11) ve açık/koyu ayrımını kendileri yapıyor.
        // Eskiden bunu bindirilen tema sözlüğü hallediyordu; sadece grafiği tazelemek,
        // koyu temaya geçen kullanıcıyı açık zemine göre seçilmiş diff renkleriyle
        // bırakırdı.
        _application.ActualThemeVariantChanged += (_, _) =>
        {
            ApplyColorBlindOverlay();
            ApplyGraphPalette();
        };
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

        ApplyColorBlindOverlay();
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
    /// <para>
    /// Yalnızca kırmızı/yeşil ayrımına dayanan anahtarlar eziliyor; ötekiler alttaki
    /// paletten geliyor. İki tam palet tutmak, birinin unutulup ikisi arasında sessizce
    /// ayrışması demekti.
    /// </para>
    /// <para>
    /// Değerler <see cref="DiffPalettes"/>'te, XAML'de değil: kaplama çalışma zamanında
    /// açılıp kapandığı için <c>ResourceInclude</c> ile yüklenmesi trimming'i kırıyordu
    /// (P09-T11).
    /// </para>
    /// </remarks>
    private void ApplyColorBlindOverlay()
    {
        bool wanted = Palette == PalettePreference.ColorBlindSafe;

        if (wanted)
        {
            IReadOnlyDictionary<string, Color> overlay =
                DiffPalettes.ColorBlindSafe(_application.ActualThemeVariant);

            foreach ((string key, Color color) in overlay)
            {
                _application.Resources[key] = new SolidColorBrush(color);
            }

            _colorBlindOverlayApplied = true;
        }
        else if (_colorBlindOverlayApplied)
        {
            // Anahtarı uygulama sözlüğünden SİLMEK gerekiyor, üzerine varsayılanı yazmak
            // değil: alttaki tema sözlükleri açık/koyu ayrımını kendisi yapıyor ve buraya
            // tek bir renk yazmak, tema değişince yanlış kalırdı. Silince arama zinciri
            // yeniden alttaki sözlüğe düşüyor.
            foreach (string key in DiffPalettes.OverlayKeys)
            {
                _application.Resources.Remove(key);
            }

            _colorBlindOverlayApplied = false;
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
