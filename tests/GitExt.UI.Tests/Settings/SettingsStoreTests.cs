using System.Text.Json.Nodes;
using GitExt.UI.Settings;

namespace GitExt.UI.Tests.Settings;

/// <summary>
/// P08-T14 — ayarlar altyapısı.
/// </summary>
/// <remarks>
/// Testlerin çoğu <b>kayıp</b> senaryolarını kovalıyor: ayar dosyası kullanıcının elle
/// düzenleyebildiği, uzun ömürlü ve <c>v1.0.0</c>'da donacak bir şema (ADR-0006). Sessizce
/// sıfırlanan bir ayar, hiç kaydedilmemiş bir ayardan daha kötüdür — kullanıcı kaydettiğini
/// sanır.
/// </remarks>
public class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "gitext-settings-" + Guid.NewGuid().ToString("N")[..8]);

    private string FilePath => Path.Combine(_directory, "settings.json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SettingsStore Create() => new(FilePath, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task WriteFileAsync(string content)
    {
        Directory.CreateDirectory(_directory);

        await File.WriteAllTextAsync(FilePath, content, Ct);
    }

    private async Task<JsonNode> ReadFileAsync() =>
        JsonNode.Parse(await File.ReadAllTextAsync(FilePath, Ct))!;

    [Fact]
    public async Task Dosya_yokken_varsayilanlar_gelir_ve_dosya_yaratilmaz()
    {
        SettingsStore store = Create();

        await store.LoadAsync(Ct);

        store.Current.Version.ShouldBe(AppSettings.CurrentVersion);
        store.Current.Appearance.Theme.ShouldBe("Light");
        File.Exists(FilePath).ShouldBeFalse("okumak dosya yaratmamalı");
    }

    [Fact]
    public async Task Degisiklik_diske_yazilir_ve_geri_okunur()
    {
        SettingsStore store = Create();
        await store.LoadAsync(Ct);

        store.Update(s => s.Appearance.Theme = "Dark");
        await store.FlushAsync(Ct);

        SettingsStore reopened = Create();
        await reopened.LoadAsync(Ct);

        reopened.Current.Appearance.Theme.ShouldBe("Dark");
    }

    [Fact]
    public async Task Update_Changed_olayini_tetikler()
    {
        SettingsStore store = Create();
        await store.LoadAsync(Ct);

        int raised = 0;
        store.Changed += (_, _) => raised++;

        store.Update(s => s.Appearance.UiFontSize = 14);

        raised.ShouldBe(1);
    }

    /// <summary>
    /// 🔴 İleriye dönük uyumluluk: yeni sürümün yazdığı alanlar eski sürümde <b>kaybolmamalı</b>.
    /// </summary>
    /// <remarks>
    /// Bu olmadan senaryo şu olurdu: kullanıcı yeni sürümde bir ayar yapar, bir kez eski
    /// sürümü açar, tek bir ayarı değiştirir — ve yeni sürümün bütün ayarları sessizce
    /// silinmiş olur.
    /// </remarks>
    [Fact]
    public async Task Taninmayan_alanlar_kaydedince_KAYBOLMUYOR()
    {
        await WriteFileAsync("""
            {
              "version": 1,
              "appearance": { "theme": "Dark", "futureKnob": 42 },
              "futureSection": { "enabled": true }
            }
            """);

        SettingsStore store = Create();
        await store.LoadAsync(Ct);

        store.Update(s => s.Appearance.UiFontSize = 15);
        await store.FlushAsync(Ct);

        JsonNode written = await ReadFileAsync();

        written["futureSection"]!["enabled"]!.GetValue<bool>().ShouldBeTrue();
        written["appearance"]!["futureKnob"]!.GetValue<int>().ShouldBe(42);
        written["appearance"]!["uiFontSize"]!.GetValue<double>().ShouldBe(15);
        written["appearance"]!["theme"]!.GetValue<string>().ShouldBe("Dark");
    }

    /// <summary>Bozuk dosya silinmiyor, yanına taşınıyor.</summary>
    [Fact]
    public async Task Bozuk_dosya_silinmez_yanina_tasinir()
    {
        await WriteFileAsync("{ bu json değil ");

        SettingsStore store = Create();
        await store.LoadAsync(Ct);

        store.Current.Appearance.Theme.ShouldBe("Light");
        File.Exists(store.InvalidFilePath).ShouldBeTrue();
        (await File.ReadAllTextAsync(store.InvalidFilePath, Ct)).ShouldContain("bu json değil");
    }

    /// <summary>Geçerli JSON ama yanlış şekil (bir bölüm dizi olmuş) da bozuk sayılır.</summary>
    [Fact]
    public async Task Yanlis_sekilli_json_de_bozuk_sayilir()
    {
        await WriteFileAsync("""{ "version": 1, "appearance": [1,2,3] }""");

        SettingsStore store = Create();
        await store.LoadAsync(Ct);

        store.Current.Appearance.Theme.ShouldBe("Light");
        File.Exists(store.InvalidFilePath).ShouldBeTrue();
    }

    /// <summary>
    /// 🔴 Gelecekten gelen dosyaya <b>dokunulmuyor</b> — ne okunuyor ne yeniden adlandırılıyor.
    /// </summary>
    [Fact]
    public async Task Gelecekten_gelen_dosyaya_DOKUNULMUYOR()
    {
        const string content = """{ "version": 999, "appearance": { "theme": "Dark" } }""";
        await WriteFileAsync(content);

        SettingsStore store = Create();
        await store.LoadAsync(Ct);

        store.Current.Appearance.Theme.ShouldBe("Light", "bilmediğimiz şema tahminle okunmamalı");
        File.Exists(store.InvalidFilePath).ShouldBeFalse("gelecekten gelen dosya bozuk değil");
        (await File.ReadAllTextAsync(FilePath, Ct)).ShouldBe(content);
    }

    /// <summary>
    /// Tanınmayan enum değeri <b>yalnızca o alanı</b> varsayılana düşürür.
    /// </summary>
    /// <remarks>
    /// Enum'ları doğrudan seri hale getirseydik <c>System.Text.Json</c> istisna atar, dosya
    /// bozuk sayılır ve <b>bütün</b> ayarlar giderdi. Bu test o kararı koruyor: aşağıda tema
    /// hatalı ama yazı tipi boyutu <b>korunuyor</b>.
    /// </remarks>
    [Fact]
    public async Task Taninmayan_enum_degeri_yalnizca_o_alani_etkiler()
    {
        await WriteFileAsync("""
            { "version": 1, "appearance": { "theme": "Ultraviolet", "uiFontSize": 17 } }
            """);

        SettingsStore store = Create();
        await store.LoadAsync(Ct);

        SettingsEnum.Parse(store.Current.Appearance.Theme, ThemePreference.Light)
            .ShouldBe(ThemePreference.Light);
        store.Current.Appearance.UiFontSize.ShouldBe(17);
        File.Exists(store.InvalidFilePath).ShouldBeFalse();
    }

    /// <summary>Yazma atomik: geçici dosya ortada kalmıyor, dosya her zaman geçerli JSON.</summary>
    [Fact]
    public async Task Yazma_atomik_gecici_dosya_birakmaz()
    {
        SettingsStore store = Create();
        await store.LoadAsync(Ct);

        store.Update(s => s.General.Language = "tr");
        await store.FlushAsync(Ct);

        File.Exists(FilePath + ".tmp").ShouldBeFalse();
        (await ReadFileAsync()).ShouldNotBeNull();
    }

    /// <summary>
    /// Gecikmeli kayıt: art arda gelen değişiklikler tek yazmada birleşiyor,
    /// <see cref="SettingsStore.FlushAsync"/> beklemeyi kesip yazıyor.
    /// </summary>
    [Fact]
    public async Task Flush_bekleyen_degisikligi_hemen_yazar()
    {
        SettingsStore store = new(FilePath, TimeSpan.FromSeconds(30));
        await store.LoadAsync(Ct);

        store.Update(s => s.Layout.BranchPanelWidth = 111);
        store.Update(s => s.Layout.BranchPanelWidth = 222);

        File.Exists(FilePath).ShouldBeFalse("gecikme dolmadan yazılmamalı");

        await store.FlushAsync(Ct);

        JsonNode written = await ReadFileAsync();
        written["layout"]!["branchPanelWidth"]!.GetValue<double>().ShouldBe(222);
    }

    [Fact]
    public async Task Secili_commitler_oturumlar_arasi_korunur()
    {
        SettingsStore store = Create();
        await store.LoadAsync(Ct);

        store.Update(s => s.Session.SelectedCommits["/depo"] = "abc123");
        await store.FlushAsync(Ct);

        SettingsStore reopened = Create();
        await reopened.LoadAsync(Ct);

        reopened.Current.Session.SelectedCommits["/depo"].ShouldBe("abc123");
    }
}

/// <summary>
/// P08-T14 — göç mekanizması. Kayıtlı gerçek göç yok; mekanizma sahte adımlarla doğrulanıyor.
/// </summary>
public class SettingsMigratorTests
{
    private sealed class SkinToThemeMigration : ISettingsMigration
    {
        public int FromVersion => 1;

        public void Apply(JsonObject root)
        {
            if (root["appearance"] is JsonObject appearance
                && appearance.Remove("skin", out JsonNode? value))
            {
                appearance["theme"] = value;
            }
        }
    }

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Gocler_sirayla_uygulanir_ve_surum_damgalanir()
    {
        SettingsMigrator migrator = new([new SkinToThemeMigration()], targetVersion: 2);

        JsonObject? result = migrator.Migrate(Parse("""
            { "version": 1, "appearance": { "skin": "Dark" } }
            """));

        result.ShouldNotBeNull();
        result["version"]!.GetValue<int>().ShouldBe(2);
        result["appearance"]!["theme"]!.GetValue<string>().ShouldBe("Dark");
        result["appearance"]!.AsObject().ContainsKey("skin").ShouldBeFalse();
    }

    [Fact]
    public void Surum_alani_yoksa_bir_kabul_edilir()
    {
        SettingsMigrator migrator = new([new SkinToThemeMigration()], targetVersion: 2);

        JsonObject? result = migrator.Migrate(Parse("""{ "appearance": { "skin": "Dark" } }"""));

        result.ShouldNotBeNull();
        result["appearance"]!["theme"]!.GetValue<string>().ShouldBe("Dark");
    }

    [Fact]
    public void Gelecekten_gelen_surum_reddedilir()
    {
        SettingsMigrator migrator = new([], targetVersion: 1);

        migrator.Migrate(Parse("""{ "version": 2 }""")).ShouldBeNull();
    }

    /// <summary>
    /// Zincirde eksik adım varsa <b>hiç</b> göç yapılmaz.
    /// </summary>
    /// <remarks>
    /// Eksik adımı atlayıp devam etmek, yapılmamış bir dönüşümü yapılmış saymak olurdu;
    /// sonuç, sessizce yanlış okunan bir ayar dosyası.
    /// </remarks>
    [Fact]
    public void Eksik_adim_gocu_tumden_iptal_eder()
    {
        SettingsMigrator migrator = new([new SkinToThemeMigration()], targetVersion: 3);

        migrator.Migrate(Parse("""{ "version": 1 }""")).ShouldBeNull();
    }

    [Fact]
    public void Guncel_surum_goc_gerektirmez()
    {
        SettingsMigrator migrator = new();

        JsonObject? result = migrator.Migrate(Parse($$"""
            { "version": {{AppSettings.CurrentVersion}}, "general": { "language": "tr" } }
            """));

        result.ShouldNotBeNull();
        result["general"]!["language"]!.GetValue<string>().ShouldBe("tr");
    }
}
