using GitExt.UI.Storage;

namespace GitExt.UI.Tests.Storage;

/// <summary>
/// P03-T16 — Son açılan depolar listesi.
/// </summary>
public class RecentRepositoryStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "gitext-recent-" + Guid.NewGuid().ToString("N")[..8]);

    private string FilePath => Path.Combine(_directory, "recent.json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RecentRepositoryStore Create() => new(FilePath);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Dosya_yokken_bos_liste_doner()
    {
        (await Create().LoadAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Eklenen_depo_kalici_olur()
    {
        await Create().AddAsync("/depo/bir", Ct);

        // Yeni bir örnek: gerçekten diske yazıldığını doğrular.
        (await Create().LoadAsync(Ct)).ShouldBe(["/depo/bir"]);
    }

    [Fact]
    public async Task En_son_acilan_basa_gelir()
    {
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/bir", Ct);
        await store.AddAsync("/depo/iki", Ct);
        await store.AddAsync("/depo/uc", Ct);

        (await store.LoadAsync(Ct)).ShouldBe(["/depo/uc", "/depo/iki", "/depo/bir"]);
    }

    [Fact]
    public async Task Ayni_depo_yeniden_acilinca_kopyalanmaz_basa_tasinir()
    {
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/bir", Ct);
        await store.AddAsync("/depo/iki", Ct);
        await store.AddAsync("/depo/bir", Ct);

        (await store.LoadAsync(Ct)).ShouldBe(["/depo/bir", "/depo/iki"]);
    }

    [Fact]
    public async Task Sondaki_ayrac_ayni_depoyu_iki_kez_yazdirmaz()
    {
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/bir", Ct);
        await store.AddAsync("/depo/bir/", Ct);

        (await store.LoadAsync(Ct)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Liste_ust_sinirda_kirpilir()
    {
        RecentRepositoryStore store = Create();

        for (int i = 0; i <= RecentRepositoryStore.MaximumCount + 5; i++)
        {
            await store.AddAsync($"/depo/{i}", Ct);
        }

        IReadOnlyList<string> recent = await store.LoadAsync(Ct);

        recent.Count.ShouldBe(RecentRepositoryStore.MaximumCount);

        // En eskiler düşer, en yeni başta kalır.
        recent[0].ShouldBe($"/depo/{RecentRepositoryStore.MaximumCount + 5}");
        recent.ShouldNotContain("/depo/0");
    }

    [Fact]
    public async Task Depo_listeden_cikarilabilir()
    {
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/bir", Ct);
        await store.AddAsync("/depo/iki", Ct);
        await store.RemoveAsync("/depo/bir", Ct);

        (await store.LoadAsync(Ct)).ShouldBe(["/depo/iki"]);
    }

    [Fact]
    public async Task Bozuk_dosya_uygulamayi_durdurmaz()
    {
        // Son açılanlar bir kolaylıktır; bozuk bir dosya yüzünden açılışta çökmek
        // kabul edilemez.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{ bu gecerli json degil ]]]", Ct);

        RecentRepositoryStore store = Create();

        (await store.LoadAsync(Ct)).ShouldBeEmpty();

        // Üstüne yazmak da çalışmalı.
        await store.AddAsync("/depo/bir", Ct);
        (await store.LoadAsync(Ct)).ShouldBe(["/depo/bir"]);
    }

    [Fact]
    public async Task Yazilan_dosya_surum_alani_tasir()
    {
        // ADR-0006: ayar formatı v1.0.0'da donuyor; sürüm alanı sonradan eklenemez.
        await Create().AddAsync("/depo/bir", Ct);

        string json = await File.ReadAllTextAsync(FilePath, Ct);

        json.ShouldContain("\"version\"");
    }

    [Fact]
    public void Yapilandirma_dizini_daima_mutlak_yoldur()
    {
        // ÖLÇÜLDÜ: XDG_CONFIG_HOME var olmayan bir dizini gösteriyorsa .NET'in
        // ApplicationData değeri BOŞ DİZE döner. Korumasız bırakılsa Path.Combine
        // göreli bir yol üretir ve dosya kullanıcının açtığı deponun içine yazılırdı.
        string directory = RecentRepositoryStore.ConfigurationDirectory();

        directory.ShouldNotBeNullOrWhiteSpace();
        Path.IsPathRooted(directory).ShouldBeTrue();
        directory.ShouldEndWith("gitext-core");
    }
}
