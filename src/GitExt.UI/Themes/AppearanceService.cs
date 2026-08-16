using Avalonia;
using Avalonia.Media;
using GitExt.UI.Settings;

namespace GitExt.UI.Themes;

/// <summary>
/// Binds the appearance settings (theme, palette, typography) to the application (P08-T07…T10).
/// </summary>
public interface IAppearanceService
{
    /// <summary>Applies all the saved appearance settings. Called once at startup.</summary>
    void ApplyStored();

    /// <summary>Changes the theme preference and saves it.</summary>
    void SetTheme(ThemePreference preference);

    /// <summary>Changes the palette preference and saves it.</summary>
    void SetPalette(PalettePreference preference);

    /// <summary>Changes the UI and code font sizes and saves them.</summary>
    void SetFontSizes(double uiFontSize, double monospaceFontSize);

    /// <summary>Changes the monospace font family; an empty string means the platform default.</summary>
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

        // When the theme changes the graph palette has to be RECOMPUTED: the palette lives not in a
        // theme dictionary but in a single computed resource (a colour list cannot be expressed in
        // XAML). Without this subscription, a user switching to the dark theme would be left with lanes
        // picked against a light background — some of them all but invisible.
        // 🔴 The overlay has to be reapplied too, not just the graph palette: the colour-blind brushes
        // have moved into code (P09-T11) and make the light/dark distinction themselves. That used to
        // be handled by the overlaid theme dictionary; refreshing only the graph would leave a user
        // switching to the dark theme with diff colours picked against a light background.
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
    /// Clamps the size into the usable range.
    /// </summary>
    /// <remarks>
    /// Leaving it unbounded meant a <c>0</c> or <c>900</c> in a hand-edited settings file could make
    /// the application <b>unreadable</b> — and undoing the setting would require using that unreadable
    /// UI.
    /// </remarks>
    private static double Clamp(double size) =>
        double.IsFinite(size)
            ? Math.Clamp(size, TypographyDefaults.MinimumFontSize, TypographyDefaults.MaximumFontSize)
            : TypographyDefaults.UiFontSize;

    private PalettePreference Palette =>
        SettingsEnum.Parse(_settings.Current.Appearance.Palette, PalettePreference.Default);

    /// <summary>
    /// Adds or removes the colour-blindness layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the keys that rely on a red/green distinction are overridden; the rest come from the
    /// palette underneath. Keeping two full palettes meant one being forgotten and silently diverging
    /// from the other.
    /// </para>
    /// <para>
    /// The values live in <see cref="DiffPalettes"/>, not in XAML: because the overlay is switched on
    /// and off at runtime, loading it with a <c>ResourceInclude</c> was breaking trimming (P09-T11).
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
            // The key has to be REMOVED from the application dictionary rather than overwritten with
            // the default: the theme dictionaries underneath make the light/dark distinction
            // themselves, and writing a single colour here would be wrong as soon as the theme changed.
            // Removing it lets the lookup chain fall back to the dictionary underneath.
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

        // The derived sizes shift together with the base size; were they fixed, tooltips and badges
        // would stay disproportionately small in an enlarged UI.
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
