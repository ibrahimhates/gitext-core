using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitExt.UI.Localization;

/// <summary>
/// Bir dil dosyasının çözümlenmiş hâli (P11-T01).
/// </summary>
/// <remarks>
/// JSON düz bir <c>anahtar → metin</c> sözlüğü; tek istisna <c>_meta</c> bloğu. Düz tutulması
/// bilinçli: iç içe bir yapı, çevirmenin anahtarı bulmak için ağaçta gezinmesini gerektirirdi
/// ve <c>settings.tab.appearance</c> gibi bir anahtar zaten hiyerarşiyi taşıyor.
/// </remarks>
internal sealed class LocaleFile
{
    /// <summary>Dilin kendi tanımı: kod ve görünen ad.</summary>
    [JsonPropertyName("_meta")]
    public LocaleMeta? Meta { get; set; }

    /// <summary>
    /// <c>_meta</c> dışındaki her şey: anahtar → çevrilmiş metin.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonExtensionDataAttribute"/> ile toplanıyor, böylece her yeni anahtar için
    /// C# tarafında bir özellik tanımlamak gerekmiyor — dil dosyası tek başına büyüyebiliyor.
    /// </remarks>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Entries { get; set; }
}

/// <summary>Dil dosyasının kendini tanıttığı blok.</summary>
internal sealed class LocaleMeta
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>
/// Dil dosyaları için kaynak üretici (source-generated) JSON bağlamı.
/// </summary>
/// <remarks>
/// 🔴 <b>Yansımalı <c>JsonSerializer</c> aşırı yüklemeleri KULLANILAMAZ.</b> Ölçüldü (P11-T00):
/// gömülü bir JSON'u <c>JsonSerializer.Deserialize&lt;Dictionary&lt;string,string&gt;&gt;</c> ile
/// okumak <c>PublishTrimmed=true</c> altında <b>çalışma zamanında</b> çöküyor — derleme
/// sırasında değil. Aynı tuzak bu projede daha önce ayarlar tarafında da yaşandı
/// (<c>SettingsSerializer</c>); orada da ancak gerçek bir trimmed publish denemesinde çıkmıştı.
/// </remarks>
[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(LocaleFile))]
internal sealed partial class LocaleJsonContext : JsonSerializerContext;
