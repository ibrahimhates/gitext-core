using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitExt.UI.Settings;

/// <summary>
/// The complete user settings (P08-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>This schema freezes at <c>v1.0.0</c></b> (ADR-0006). The <see cref="Version"/> field has been
/// here from day one; not because it could not be added later, but because <b>no migration can be
/// written without a version field having been there</b>.
/// </para>
/// <para>
/// <b>Why a flat DTO, and why is every field a <c>string</c>?</b> The settings file is one the user
/// can edit by hand. Had the enumerable fields been serialised directly as enums, <b>a single typo
/// would make the whole file corrupt</b>: <c>System.Text.Json</c> throws a
/// <see cref="JsonException"/> on an unrecognised enum value, and we would count the file as corrupt
/// and reset <b>all of the user's settings</b> to the defaults. So the enums are carried as text and
/// resolved <b>leniently</b> by <see cref="SettingsEnum"/>: an unrecognised value drops only that
/// one field to its default.
/// </para>
/// </remarks>
public sealed class AppSettings
{
    /// <summary>The schema version of the files written.</summary>
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
    /// Top-level fields this version does not recognise.
    /// </summary>
    /// <remarks>
    /// <b>Forward compatibility rests on this.</b> When a settings file written by a newer version is
    /// opened by an older one, the unrecognised fields are kept here and written back on save.
    /// Without it: the moment the user opened the older version once and changed a setting, all of
    /// the newer version's settings would be <b>silently deleted</b>.
    /// </remarks>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public AppSettings Clone() => SettingsSerializer.Clone(this);
}

/// <summary>Settings for the language and general behaviour.</summary>
public sealed class GeneralSettings
{
    /// <summary>The UI language (<c>en</c>, <c>tr</c>). When empty the system language is tried (P08-T23).</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    /// <summary>Has the application ever been run before (P08-T21)?</summary>
    [JsonPropertyName("hasCompletedFirstRun")]
    public bool HasCompletedFirstRun { get; set; }

    /// <summary>
    /// Should the repository that was open at shutdown be reopened at startup (P12-T04)?
    /// </summary>
    /// <remarks>
    /// <b>Off by default</b>, and that is GitExtensions' default too
    /// (<c>AppSettings.StartWithRecentWorkingDir</c>, <c>GetBool(…, false)</c>): starting the
    /// application lands on the dashboard, and the repository is chosen from there. Turning it on
    /// restores the P08-T16 behaviour.
    /// </remarks>
    [JsonPropertyName("startWithRecentWorkingDir")]
    public bool StartWithRecentWorkingDir { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>Which sections the left panel shows (P12-T13).</summary>
public sealed class LeftPanelSections
{
    [JsonPropertyName("branches")]
    public bool Branches { get; set; } = true;

    [JsonPropertyName("remotes")]
    public bool Remotes { get; set; } = true;

    [JsonPropertyName("worktrees")]
    public bool WorkTrees { get; set; } = true;

    [JsonPropertyName("tags")]
    public bool Tags { get; set; } = true;

    [JsonPropertyName("submodules")]
    public bool Submodules { get; set; } = true;

    [JsonPropertyName("stashes")]
    public bool Stashes { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>Tema, palet ve tipografi (P08-T07…T10).</summary>
public sealed class AppearanceSettings
{
    /// <summary><c>Light</c> · <c>Dark</c> · <c>System</c>. <c>Light</c> by default.</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = nameof(ThemePreference.Light);

    /// <summary><c>Default</c> · <c>ColorBlindSafe</c>.</summary>
    [JsonPropertyName("palette")]
    public string Palette { get; set; } = nameof(PalettePreference.Default);

    /// <summary>The UI font size (points).</summary>
    [JsonPropertyName("uiFontSize")]
    public double UiFontSize { get; set; } = TypographyDefaults.UiFontSize;

    /// <summary>The monospace font family for code/SHAs. When empty, the platform default.</summary>
    [JsonPropertyName("monospaceFontFamily")]
    public string MonospaceFontFamily { get; set; } = "";

    /// <summary>The code/diff font size (points).</summary>
    [JsonPropertyName("monospaceFontSize")]
    public double MonospaceFontSize { get; set; } = TypographyDefaults.MonospaceFontSize;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>The panel layout (P08-T13).</summary>
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

    /// <summary>
    /// Which sections the left panel shows (P12-T13).
    /// </summary>
    /// <remarks>
    /// GitExtensions keeps the same six toggles
    /// (<c>RepoObjectsTreeShowBranches</c> … <c>RepoObjectsTreeShowStashes</c>). All on by
    /// default: a repository with no submodules does not show the heading anyway, because an
    /// empty section is not drawn.
    /// </remarks>
    [JsonPropertyName("leftPanelSections")]
    public LeftPanelSections LeftPanel { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>What is remembered between sessions (P08-T16).</summary>
public sealed class SessionSettings
{
    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; }

    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; }

    [JsonPropertyName("windowMaximized")]
    public bool WindowMaximized { get; set; }

    /// <summary>The repository open at shutdown. When empty, the welcome screen appears.</summary>
    [JsonPropertyName("lastRepository")]
    public string LastRepository { get; set; } = "";

    /// <summary>Repository path → the SHA of the last selected commit.</summary>
    [JsonPropertyName("selectedCommits")]
    public Dictionary<string, string> SelectedCommits { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>Tema tercihi.</summary>
public enum ThemePreference
{
    /// <summary>Always the light theme. <b>The default</b> — GitExtensions' visual identity.</summary>
    Light,

    /// <summary>Her zaman koyu tema.</summary>
    Dark,

    /// <summary>Follows the operating system's preference.</summary>
    System,
}

/// <summary>Grafik/diff renk paleti tercihi.</summary>
public enum PalettePreference
{
    /// <summary>The default palette.</summary>
    Default,

    /// <summary>A palette that does not rely on a red/green distinction (deuteranopia/protanopia).</summary>
    ColorBlindSafe,
}

/// <summary>Typography defaults (P08-T10).</summary>
public static class TypographyDefaults
{
    public const double UiFontSize = 12;
    public const double MonospaceFontSize = 12;
    public const double MinimumFontSize = 8;
    public const double MaximumFontSize = 32;
}

/// <summary>
/// The <b>lenient</b> resolution of enumerable settings stored as text.
/// </summary>
/// <remarks>
/// An unrecognised value does not throw, it falls back to the default: a single typo in a
/// hand-edited file must affect only that setting, not the whole file.
/// </remarks>
public static class SettingsEnum
{
    public static T Parse<T>(string? value, T fallback)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;
}
