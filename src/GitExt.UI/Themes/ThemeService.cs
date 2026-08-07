using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using GitExt.UI.Settings;

namespace GitExt.UI.Themes;

/// <summary>
/// Tema tercihini uygulamaya bağlar (P08-T08).
/// </summary>
public interface IThemeService
{
    /// <summary>Yürürlükteki tercih.</summary>
    ThemePreference Preference { get; }

    /// <summary>Tercihi değiştirir, uygular ve kaydeder.</summary>
    void Apply(ThemePreference preference);

    /// <summary>Kaydedilmiş tercihi okuyup uygular. Açılışta bir kez çağrılır.</summary>
    void ApplyStored();
}

/// <summary>
/// <see cref="Application.RequestedThemeVariant"/> üzerinden tema tercihini uygular.
/// </summary>
/// <remarks>
/// <para>
/// <b>Varsayılan AÇIK</b> — GitExtensions'ın görsel kimliği bu ve hedef kitle oradan geliyor.
/// Sistem koyu temada olsa bile varsayılan açıktır; "sistemi takip et" bilinçli bir tercihtir
/// (kullanıcı kararı, 2026-07-29).
/// </para>
/// <para>
/// <b>ÖLÇÜLDÜ (P08-T00):</b> <c>RequestedThemeVariant</c> çalışma sırasında değiştirilebiliyor
/// ve <c>ActualThemeVariantChanged</c> tetikleniyor (M07); <c>Default</c> verildiğinde
/// <c>ActualThemeVariant</c> platformdan somut bir değere <b>çözülüyor</b> (M07b), yani
/// "sistemi takip et" ayrıca bir dinleyici gerektirmiyor — Avalonia'nın kendi
/// <c>Default</c>'u zaten sistemi izliyor.
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

    /// <summary>Tercihin Avalonia karşılığı.</summary>
    public static ThemeVariant ToVariant(ThemePreference preference) => preference switch
    {
        ThemePreference.Dark => ThemeVariant.Dark,

        // `Default` = "sistemi takip et". Avalonia bunu platformdan çözüyor; kendi
        // dinleyicimizi yazmak aynı işi ikinci kez, daha kötü yapmak olurdu.
        ThemePreference.System => ThemeVariant.Default,

        _ => ThemeVariant.Light,
    };

    /// <summary>
    /// İşletim sisteminin bildirdiği tema.
    /// </summary>
    /// <remarks>
    /// Yalnızca ayarlar ekranında "sistemi takip et" seçiliyken <i>hangi</i> temanın etkin
    /// olduğunu göstermek için. Tema uygulaması buna bakmıyor.
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
