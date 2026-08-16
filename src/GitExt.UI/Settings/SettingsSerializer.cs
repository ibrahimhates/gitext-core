using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GitExt.UI.Settings;

/// <summary>
/// Converting the settings to JSON. <b>Source-generated only</b> (P08-T14).
/// </summary>
/// <remarks>
/// The reflection-based <see cref="JsonSerializer"/> overloads <b>cannot be used</b>: they break
/// trimming (IL2026) and the release does not build with <c>PublishTrimmed</c>. This was learned by
/// measurement in P03-T16 — because the build and the tests stayed green, the bug only became visible
/// when <c>dotnet publish</c> was attempted.
/// </remarks>
internal static class SettingsSerializer
{
    public static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

    public static AppSettings? Deserialize(JsonNode node) =>
        node.Deserialize(SettingsJsonContext.Default.AppSettings);

    public static AppSettings Clone(AppSettings settings) =>
        Deserialize(JsonNode.Parse(Serialize(settings))!) ?? new AppSettings();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
