using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using GitExt.UI.Settings;

namespace GitExt.UI.Themes;

/// <summary>
/// Binds the theme preference to the application (P08-T08).
/// </summary>
public interface IThemeService
{
    /// <summary>The preference in force.</summary>
    ThemePreference Preference { get; }

    /// <summary>Changes the preference, applies it and saves it.</summary>
    void Apply(ThemePreference preference);

    /// <summary>Reads the saved preference and applies it. Called once at startup.</summary>
    void ApplyStored();
}

/// <summary>
/// Applies the theme preference through <see cref="Application.RequestedThemeVariant"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The default is LIGHT</b> — that is GitExtensions' visual identity and the target audience comes
/// from there. Even when the system is in dark mode the default is light; "follow the system" is a
/// deliberate choice (the user's decision, 2026-07-29).
/// </para>
/// <para>
/// <b>MEASURED (P08-T00):</b> <c>RequestedThemeVariant</c> can be changed at runtime and
/// <c>ActualThemeVariantChanged</c> fires (M07); given <c>Default</c>, <c>ActualThemeVariant</c>
/// <b>resolves</b> to a concrete value from the platform (M07b), meaning "follow the system" needs no
/// separate listener — Avalonia's own <c>Default</c> already follows the system.
/// </para>
/// </remarks>
public sealed class ThemeService : IThemeService
{
    private readonly Application _application;
    private readonly ISettingsStore _settings;

    public ThemeService(Application application, ISettingsStore settings)
    {
        _application = application;
        _settings = settings;
    }

    public ThemePreference Preference =>
        SettingsEnum.Parse(_settings.Current.Appearance.Theme, ThemePreference.Light);

    public void Apply(ThemePreference preference)
    {
        _settings.Update(s => s.Appearance.Theme = preference.ToString());

        ApplyToApplication(preference);
    }

    public void ApplyStored() => ApplyToApplication(Preference);

    /// <summary>The Avalonia counterpart of the preference.</summary>
    public static ThemeVariant ToVariant(ThemePreference preference) => preference switch
    {
        ThemePreference.Dark => ThemeVariant.Dark,

        // `Default` = "follow the system". Avalonia resolves this from the platform; writing our own
        // listener would be doing the same job a second time, worse.
        ThemePreference.System => ThemeVariant.Default,

        _ => ThemeVariant.Light,
    };

    /// <summary>
    /// The theme the operating system reports.
    /// </summary>
    /// <remarks>
    /// Only for showing <i>which</i> theme is in force while "follow the system" is selected on the
    /// settings screen. Applying the theme does not consult this.
    /// </remarks>
    public ThemePreference DetectSystemTheme()
    {
        PlatformColorValues? colors = _application.PlatformSettings?.GetColorValues();

        return colors?.ThemeVariant == PlatformThemeVariant.Dark
            ? ThemePreference.Dark
            : ThemePreference.Light;
    }

    private void ApplyToApplication(ThemePreference preference) =>
        _application.RequestedThemeVariant = ToVariant(preference);
}
