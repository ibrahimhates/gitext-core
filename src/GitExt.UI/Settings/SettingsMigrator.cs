using System.Text.Json.Nodes;

namespace GitExt.UI.Settings;

/// <summary>
/// Bir şema sürümünden bir sonrakine taşıyan tek adım.
/// </summary>
/// <remarks>
/// Göçler <b>tiplenmiş modelden önce</b>, ham <see cref="JsonObject"/> üzerinde çalışır.
/// Sebebi basit: bir alan yeniden adlandırıldıysa tiplenmiş model onu zaten okuyamaz —
/// göç, okunabildiği tek katmanda yapılmalı.
/// </remarks>
internal interface ISettingsMigration
{
    /// <summary>Bu göçün <b>girdi</b> sürümü; çıktısı <c>FromVersion + 1</c>'dir.</summary>
    int FromVersion { get; }

    void Apply(JsonObject root);
}

/// <summary>
/// Ayar dosyasını okunduğu sürümden <see cref="AppSettings.CurrentVersion"/>'a taşır (P08-T14).
/// </summary>
internal sealed class SettingsMigrator
{
    /// <summary>
    /// Kayıtlı göçler. <b>Şu an boş</b> — ilk şema sürümündeyiz.
    /// </summary>
    /// <remarks>
    /// Boş olması mekanizmanın çalışmadığı anlamına gelmez: mekanizma testte enjekte edilen
    /// sahte göçlerle doğrulanıyor. İlk gerçek şema değişikliğinde buraya bir adım eklenecek;
    /// o gün mekanizmayı da yazmak zorunda kalmak, hem göçü hem altyapıyı aynı anda
    /// doğrulamak demek olurdu.
    /// </remarks>
    private static readonly ISettingsMigration[] Registered = [];

    private readonly IReadOnlyList<ISettingsMigration> _migrations;
    private readonly int _targetVersion;

    public SettingsMigrator()
        : this(Registered, AppSettings.CurrentVersion)
    {
    }

    internal SettingsMigrator(IReadOnlyList<ISettingsMigration> migrations, int targetVersion)
    {
        _migrations = migrations;
        _targetVersion = targetVersion;
    }

    /// <summary>
    /// Göçleri sırayla uygular ve sonuçtaki sürüm alanını günceller.
    /// </summary>
    /// <returns>
    /// Dosya okunabiliyorsa taşınmış kök; <b>gelecekten gelen</b> (bizden yeni) bir dosyaysa
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Gelecekten gelen dosya okunmaz.</b> Bilmediğimiz bir şemayı tahminle okuyup üstüne
    /// yazmak, kullanıcının yeni sürümde yaptığı ayarları <b>sessizce bozmak</b> olurdu.
    /// Bu durumda varsayılanlarla çalışılır ve dosyaya <b>hiç dokunulmaz</b>.
    /// </para>
    /// </remarks>
    public JsonObject? Migrate(JsonObject root)
    {
        int version = ReadVersion(root);

        if (version > _targetVersion)
        {
            return null;
        }

        while (version < _targetVersion)
        {
            ISettingsMigration? step = _migrations.FirstOrDefault(m => m.FromVersion == version);

            if (step is null)
            {
                // Aradaki bir adım eksik: elimizdekiyle devam etmek, olmayan bir dönüşümü
                // yapmış gibi davranmaktır. Varsayılanlara düşülür.
                return null;
            }

            step.Apply(root);
            version++;
        }

        root["version"] = _targetVersion;

        return root;
    }

    /// <summary>
    /// Sürüm alanını okur.
    /// </summary>
    /// <remarks>
    /// Alan yoksa veya sayı değilse <b>1</b> kabul edilir: sürüm alanı olmayan tek dosya
    /// biçimi ilk sürümdür.
    /// </remarks>
    private static int ReadVersion(JsonObject root) =>
        root.TryGetPropertyValue("version", out JsonNode? node)
        && node is JsonValue value
        && value.TryGetValue(out int parsed)
            ? parsed
            : 1;
}
