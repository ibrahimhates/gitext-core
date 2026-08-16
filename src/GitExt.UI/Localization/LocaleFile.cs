using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitExt.UI.Localization;

/// <summary>
/// A language file in its parsed form (P11-T01).
/// </summary>
/// <remarks>
/// The JSON is a flat <c>key → text</c> dictionary; the one exception is the <c>_meta</c> block.
/// Keeping it flat is deliberate: a nested structure would make a translator navigate a tree to find a
/// key, and a key such as <c>settings.tab.appearance</c> already carries the hierarchy.
/// </remarks>
internal sealed class LocaleFile
{
    /// <summary>The language's own description: its code and display name.</summary>
    [JsonPropertyName("_meta")]
    public LocaleMeta? Meta { get; set; }

    /// <summary>
    /// Everything other than <c>_meta</c>: key → translated text.
    /// </summary>
    /// <remarks>
    /// Collected with <see cref="JsonExtensionDataAttribute"/>, so no property has to be defined on the
    /// C# side for every new key — the language file can grow on its own.
    /// </remarks>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Entries { get; set; }
}

/// <summary>The block in which a language file describes itself.</summary>
internal sealed class LocaleMeta
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>
/// The source-generated JSON context for the language files.
/// </summary>
/// <remarks>
/// 🔴 <b>The reflection-based <c>JsonSerializer</c> overloads CANNOT BE USED.</b> Measured (P11-T00):
/// reading an embedded JSON with
/// <c>JsonSerializer.Deserialize&lt;Dictionary&lt;string,string&gt;&gt;</c> crashes <b>at runtime</b>
/// under <c>PublishTrimmed=true</c> — not at build time. The same trap hit this project earlier on the
/// settings side (<c>SettingsSerializer</c>); there too it only surfaced in a real trimmed publish.
/// </remarks>
[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(LocaleFile))]
internal sealed partial class LocaleJsonContext : JsonSerializerContext;
