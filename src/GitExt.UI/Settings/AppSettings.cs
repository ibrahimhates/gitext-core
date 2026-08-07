using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitExt.UI.Settings;

/// <summary>
/// Kullanıcı ayarlarının tamamı (P08-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu şema <c>v1.0.0</c>'da donuyor</b> (ADR-0006). <see cref="Version"/> alanı ilk günden
/// burada; sonradan eklenemeyeceği için değil, <b>eklenmiş bir sürüm alanı olmadan göç
/// yazılamayacağı</b> için.
/// </para>
/// <para>
/// <b>Neden düz DTO ve neden her alan <c>string</c>?</b> Ayar dosyası kullanıcının elle
/// düzenleyebileceği bir dosya. Sayılabilir (enum) alanlar doğrudan enum olarak
/// seri hale getirilseydi <b>tek bir yazım hatası bütün dosyayı bozuk yapardı</b>:
/// <c>System.Text.Json</c> tanımadığı enum değerinde <see cref="JsonException"/> atar, biz de
/// dosyayı bozuk sayıp <b>kullanıcının tüm ayarlarını</b> varsayılana döndürürdük. Bu yüzden
/// enum'lar metin olarak taşınıyor ve <see cref="SettingsEnum"/> ile <b>hoşgörülü</b> çözülüyor:
/// tanınmayan değer yalnızca o alanı varsayılana düşürür.
/// </para>
/// </remarks>
public sealed class AppSettings
{
    /// <summary>Yazılan dosyaların şema sürümü.</summary>
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("general")]
    public GeneralSettings General { get; set; } = new();

    [JsonPropertyName("appearance")]
    public AppearanceSettings Appearance { get; set; } = new();

    [JsonPropertyName("shortcuts")]
    public Dictionary<string, string> Shortcuts { get; set; } = [];

    [JsonPropertyName("layout")]
    public LayoutSettings Layout { get; set; } = new();

    [JsonPropertyName("session")]
    public SessionSettings Session { get; set; } = new();

    /// <summary>
    /// Bu sürümün tanımadığı üst düzey alanlar.
    /// </summary>
    /// <remarks>
    /// <b>İleriye dönük uyumluluk bunun üzerinde duruyor.</b> Yeni bir sürümün yazdığı ayar
    /// dosyası eski bir sürümle açılırsa, tanınmayan alanlar burada tutulur ve kaydederken
    /// geri yazılır. Olmasaydı: kullanıcı eski sürümü bir kez açıp bir ayar değiştirdiğinde
    /// yeni sürümün bütün ayarları <b>sessizce silinirdi</b>.
    /// </remarks>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public AppSettings Clone() => SettingsSerializer.Clone(this);
}

/// <summary>Dile ve genel davranışa ait ayarlar.</summary>
public sealed class GeneralSettings
{
    /// <summary>Arayüz dili (<c>en</c>, <c>tr</c>). Boşsa sistem dili denenir (P08-T23).</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    /// <summary>Uygulama daha önce hiç çalıştırıldı mı (P08-T21).</summary>
    [JsonPropertyName("hasCompletedFirstRun")]
    public bool HasCompletedFirstRun { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>Tema, palet ve tipografi (P08-T07…T10).</summary>
public sealed class AppearanceSettings
{
    /// <summary><c>Light</c> · <c>Dark</c> · <c>System</c>. Varsayılan <c>Light</c>.</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = nameof(ThemePreference.Light);

    /// <summary><c>Default</c> · <c>ColorBlindSafe</c>.</summary>
    [JsonPropertyName("palette")]
    public string Palette { get; set; } = nameof(PalettePreference.Default);

    /// <summary>Arayüz yazı tipi boyutu (punto).</summary>
    [JsonPropertyName("uiFontSize")]
    public double UiFontSize { get; set; } = TypographyDefaults.UiFontSize;

    /// <summary>Kod/SHA için sabit genişlikli yazı tipi ailesi. Boşsa platform varsayılanı.</summary>
    [JsonPropertyName("monospaceFontFamily")]
    public string MonospaceFontFamily { get; set; } = "";

    /// <summary>Kod/diff yazı tipi boyutu (punto).</summary>
    [JsonPropertyName("monospaceFontSize")]
    public double MonospaceFontSize { get; set; } = TypographyDefaults.MonospaceFontSize;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>Panel düzeni (P08-T13).</summary>
public sealed class LayoutSettings
{
    [JsonPropertyName("branchPanelWidth")]
    public double BranchPanelWidth { get; set; } = 220;

    [JsonPropertyName("branchPanelVisible")]
    public bool BranchPanelVisible { get; set; } = true;

    [JsonPropertyName("bottomPanelHeight")]
    public double BottomPanelHeight { get; set; } = 220;

    [JsonPropertyName("bottomPanelVisible")]
    public bool BottomPanelVisible { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>Oturumlar arası hatırlananlar (P08-T16).</summary>
public sealed class SessionSettings
{
    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; }

    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; }

    [JsonPropertyName("windowMaximized")]
    public bool WindowMaximized { get; set; }

    /// <summary>Kapanışta açık olan depo. Boşsa karşılama ekranı gelir.</summary>
    [JsonPropertyName("lastRepository")]
    public string LastRepository { get; set; } = "";

    /// <summary>Depo yolu → son seçili commit SHA'sı.</summary>
    [JsonPropertyName("selectedCommits")]
    public Dictionary<string, string> SelectedCommits { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>Tema tercihi.</summary>
public enum ThemePreference
{
    /// <summary>Her zaman açık tema. <b>Varsayılan</b> — GitExtensions'ın görsel kimliği.</summary>
    Light,

    /// <summary>Her zaman koyu tema.</summary>
    Dark,

    /// <summary>İşletim sisteminin tercihini izler.</summary>
    System,
}

/// <summary>Grafik/diff renk paleti tercihi.</summary>
public enum PalettePreference
{
    /// <summary>Varsayılan palet.</summary>
    Default,

    /// <summary>Kırmızı/yeşil ayrımına dayanmayan palet (döterananopi/protanopi).</summary>
    ColorBlindSafe,
}

/// <summary>Tipografi varsayılanları (P08-T10).</summary>
public static class TypographyDefaults
{
    public const double UiFontSize = 12;
    public const double MonospaceFontSize = 12;
    public const double MinimumFontSize = 8;
    public const double MaximumFontSize = 32;
}

/// <summary>
/// Metin olarak saklanan sayılabilir ayarların <b>hoşgörülü</b> çözümü.
/// </summary>
/// <remarks>
/// Tanınmayan değer istisna atmaz, varsayılana düşer: elle düzenlenen bir dosyadaki tek bir
/// yazım hatası yalnızca o ayarı etkilemeli, dosyanın tamamını değil.
/// </remarks>
public static class SettingsEnum
{
    public static T Parse<T>(string? value, T fallback)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;
}
