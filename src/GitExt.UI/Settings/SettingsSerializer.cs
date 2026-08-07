using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GitExt.UI.Settings;

/// <summary>
/// Ayarların JSON'a çevrimi. <b>Yalnızca kaynak üretimli</b> (P08-T14).
/// </summary>
/// <remarks>
/// Yansımalı <see cref="JsonSerializer"/> aşırı yüklemeleri <b>kullanılamaz</b>: trimming'i
/// bozuyorlar (IL2026) ve <c>PublishTrimmed</c> ile yayın derlenmiyor. Bu, P03-T16'da
/// ölçülerek öğrenildi — derleme ve testler yeşil kaldığı için hata ancak
/// <c>dotnet publish</c> denenince görülmüştü.
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
