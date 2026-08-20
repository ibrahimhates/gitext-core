using GitExt.UI.Storage;

namespace GitExt.UI.Tests.Storage;

/// <summary>
/// P03-T16 — the recently opened repositories list.
/// </summary>
public class RecentRepositoryStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "gitext-recent-" + Guid.NewGuid().ToString("N")[..8]);

    private string FilePath => Path.Combine(_directory, "recent.json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RecentRepositoryStore Create() => new(FilePath);

    /// <summary>Only the paths — that is what most of these tests are about.</summary>
    private static async Task<IReadOnlyList<string>> PathsOf(IRecentRepositoryStore store) =>
        [.. (await store.LoadAsync(Ct)).Select(repository => repository.Path)];

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

        // A fresh instance: verifies it really was written to disk.
        (await PathsOf(Create())).ShouldBe(["/depo/bir"]);
    }

    [Fact]
    public async Task En_son_acilan_basa_gelir()
    {
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/bir", Ct);
        await store.AddAsync("/depo/iki", Ct);
        await store.AddAsync("/depo/uc", Ct);

        (await PathsOf(store)).ShouldBe(["/depo/uc", "/depo/iki", "/depo/bir"]);
    }

    [Fact]
    public async Task Ayni_depo_yeniden_acilinca_kopyalanmaz_basa_tasinir()
    {
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/bir", Ct);
        await store.AddAsync("/depo/iki", Ct);
        await store.AddAsync("/depo/bir", Ct);

        (await PathsOf(store)).ShouldBe(["/depo/bir", "/depo/iki"]);
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

        IReadOnlyList<string> recent = await PathsOf(store);

        recent.Count.ShouldBe(RecentRepositoryStore.MaximumCount);

        // The oldest fall off, the newest stays at the top.
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

        (await PathsOf(store)).ShouldBe(["/depo/iki"]);
    }

    [Fact]
    public async Task Bozuk_dosya_uygulamayi_durdurmaz()
    {
        // The recent list is a convenience; crashing on startup because of a corrupted file
        // is unacceptable.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{ bu gecerli json degil ]]]", Ct);

        RecentRepositoryStore store = Create();

        (await store.LoadAsync(Ct)).ShouldBeEmpty();

        // Overwriting must work too.
        await store.AddAsync("/depo/bir", Ct);
        (await PathsOf(store)).ShouldBe(["/depo/bir"]);
    }

    [Fact]
    public async Task Yazilan_dosya_surum_alani_tasir()
    {
        // ADR-0006: the settings format freezes at v1.0.0; a version field cannot be added later.
        await Create().AddAsync("/depo/bir", Ct);

        string json = await File.ReadAllTextAsync(FilePath, Ct);

        json.ShouldContain("\"version\"");
    }

    // ------------------------------------------------------------------ P12-T02: categories

    [Fact]
    public async Task Kategori_kaliciyor()
    {
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/bir", Ct);
        await store.SetCategoryAsync("/depo/bir", "İş", Ct);

        // A fresh instance: it really went to disk, along with the path.
        IReadOnlyList<RecentRepository> loaded = await Create().LoadAsync(Ct);

        loaded.Single().Category.ShouldBe("İş");
        loaded.Single().IsFavourite.ShouldBeTrue();
    }

    [Fact]
    public async Task Kategori_geri_alinabiliyor()
    {
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/bir", Ct);
        await store.SetCategoryAsync("/depo/bir", "İş", Ct);
        await store.SetCategoryAsync("/depo/bir", null, Ct);

        (await store.LoadAsync(Ct)).Single().IsFavourite.ShouldBeFalse();
    }

    [Fact]
    public async Task Depo_yeniden_acilinca_kategorisini_KAYBETMIYOR()
    {
        // 🔴 Reopening writes the entry afresh. Were the category not carried over, opening a
        // favourite would quietly demote it back into "Recent repositories" — the user's filing
        // undone by the very act of using it.
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/bir", Ct);
        await store.SetCategoryAsync("/depo/bir", "İş", Ct);
        await store.AddAsync("/depo/bir", Ct);

        (await store.LoadAsync(Ct)).Single().Category.ShouldBe("İş");
    }

    [Fact]
    public async Task Kategorili_depolar_ust_sinirdan_ETKILENMIYOR()
    {
        // The cap exists so the list stays scannable; a favourite is there because the user put
        // it there, and dropping it after twelve other repositories were opened would throw away
        // a decision rather than tidy up a history.
        RecentRepositoryStore store = Create();

        await store.AddAsync("/depo/sabit", Ct);
        await store.SetCategoryAsync("/depo/sabit", "İş", Ct);

        for (int i = 0; i <= RecentRepositoryStore.MaximumCount + 5; i++)
        {
            await store.AddAsync($"/depo/{i}", Ct);
        }

        IReadOnlyList<RecentRepository> loaded = await store.LoadAsync(Ct);

        loaded.Count(r => !r.IsFavourite).ShouldBe(RecentRepositoryStore.MaximumCount);
        loaded.ShouldContain(r => r.Path == "/depo/sabit");
    }

    [Fact]
    public async Task Eski_surum_1_dosyasi_okunuyor()
    {
        // 🔴 v1 wrote plain strings. A typed read of the v2 shape throws on such a file, and this
        // class treats an unreadable file as an empty list — so the upgrade would have SILENTLY
        // WIPED the user's repository list. Nothing would have failed; the list would just be
        // gone.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            FilePath,
            """{ "version": 1, "repositories": ["/depo/bir", "/depo/iki"] }""",
            Ct);

        (await PathsOf(Create())).ShouldBe(["/depo/bir", "/depo/iki"]);
    }

    [Fact]
    public async Task Anlasilmayan_kayit_dosyanin_kalanini_dusurmuyor()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            FilePath,
            """{ "version": 2, "repositories": [42, { "path": "/depo/iki" }] }""",
            Ct);

        (await PathsOf(Create())).ShouldBe(["/depo/iki"]);
    }

    [Fact]
    public void Yapilandirma_dizini_daima_mutlak_yoldur()
    {
        // MEASURED: if XDG_CONFIG_HOME points at a directory that does not exist, .NET's
        // ApplicationData value returns an EMPTY STRING. Left unguarded, Path.Combine would
        // produce a relative path and the file would land inside the repository the user opened.
        string directory = RecentRepositoryStore.ConfigurationDirectory();

        directory.ShouldNotBeNullOrWhiteSpace();
        Path.IsPathRooted(directory).ShouldBeTrue();
        directory.ShouldEndWith("gitext-core");
    }
}
