using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T08 — dosya işlemleri (geri alma, silme, <c>.gitignore</c>, <c>clean</c>).
/// </summary>
/// <remarks>
/// Fazın en tehlikeli görevi: buradaki her işlem kullanıcının <b>henüz kaydedilmemiş</b>
/// emeğini silebilir. Testlerin ağırlığı, ölçümde bulunan <b>sessiz</b> davranışlarda —
/// git'in hata vermeden hiçbir şey yapmadığı durumlar.
/// </remarks>
public class WorkingTreeWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        WorkingTreeWriter Writer,
        StagingWriter Staging,
        GitWriteQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        public string Path => Repository.Path;

        public string Read(string name) =>
            File.ReadAllText(System.IO.Path.Combine(Repository.Path, name));

        public bool Exists(string name) =>
            File.Exists(System.IO.Path.Combine(Repository.Path, name));

        public string Status(string name) =>
            Repository.Git("status", "--porcelain", "--", name).Trim();
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();
        GitWriter writer = new(runner, queue);

        repository.WriteFile("a.txt", "satir1\nsatir2\n");
        repository.WriteFile("b.txt", "b\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "init");

        return new Harness(
            repository,
            new WorkingTreeWriter(writer, runner),
            new StagingWriter(writer, runner),
            queue);
    }

    private static IReadOnlyList<RepositoryPath> Paths(params string[] values) =>
        [.. values.Select(value => RepositoryPath.Parse(value))];

    // ---- Geri alma (git restore) ----

    [Fact]
    public async Task Onaysiz_geri_alma_REDDEDILIR()
    {
        // Onay parametre olarak zorunlu (P05-T02'deki `GitLock.Remove` deseni): kuralı
        // yorumda bırakmak, birinin ileride onaysız çağırmasına engel olmaz.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "degisti\n");

        await Should.ThrowAsync<InvalidOperationException>(
            harness.Writer.DiscardChangesAsync(
                harness.Path, Paths("a.txt"), DiscardScope.UnstagedOnly, userConfirmed: false, Ct));

        harness.Read("a.txt").ShouldBe("degisti\n");
    }

    [Fact]
    public async Task Bos_yol_listesi_HICBIR_SEYI_geri_almaz()
    {
        // ⚠️ Yolsuz `git restore --` deponun TAMAMINI geri alırdı (P05-T03'teki
        // `git add -A --` korumasının aynısı).
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "degisti\n");
        harness.Repository.WriteFile("b.txt", "b degisti\n");

        await harness.Writer.DiscardChangesAsync(
            harness.Path, [], DiscardScope.All, userConfirmed: true, Ct);

        harness.Read("a.txt").ShouldBe("degisti\n");
        harness.Read("b.txt").ShouldBe("b degisti\n");
    }

    [Fact]
    public async Task Stage_lenmemis_degisiklik_atilir_STAGE_LENMIS_KORUNUR()
    {
        // ÖLÇÜLDÜ: düz `git restore` çalışma ağacını HEAD'den değil INDEX'ten geri yüklüyor.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "stage edilmis\n");
        await harness.Staging.StageAsync(harness.Path, Paths("a.txt"), Ct);
        harness.Repository.WriteFile("a.txt", "stage edilmis + fazlasi\n");

        await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("a.txt"), DiscardScope.UnstagedOnly, userConfirmed: true, Ct);

        harness.Read("a.txt").ShouldBe("stage edilmis\n");
        harness.Repository.Git("show", ":a.txt").ShouldBe("stage edilmis\n");
    }

    [Fact]
    public async Task Tum_kapsamda_stage_lenmis_degisiklik_de_atilir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "stage edilmis\n");
        await harness.Staging.StageAsync(harness.Path, Paths("a.txt"), Ct);
        harness.Repository.WriteFile("a.txt", "stage edilmis + fazlasi\n");

        await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("a.txt"), DiscardScope.All, userConfirmed: true, Ct);

        harness.Read("a.txt").ShouldBe("satir1\nsatir2\n");
        harness.Status("a.txt").ShouldBeEmpty();
    }

    [Fact]
    public async Task Silinmis_dosya_geri_getirilir()
    {
        using Harness harness = await CreateAsync();
        File.Delete(Path.Combine(harness.Path, "b.txt"));

        await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("b.txt"), DiscardScope.UnstagedOnly, userConfirmed: true, Ct);

        harness.Read("b.txt").ShouldBe("b\n");
    }

    [Fact]
    public async Task Atilan_icerik_YEDEKTEN_geri_okunabilir()
    {
        // CLAUDE.md § 8: geri alınamaz işlem için bir dönüş yolu. Reflog burada yok
        // (içerik hiç commit'lenmedi), ama nesne veritabanına yazılabiliyor.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "KAYBOLMAMASI GEREKEN\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("a.txt"), DiscardScope.UnstagedOnly, userConfirmed: true, Ct);

        harness.Read("a.txt").ShouldBe("satir1\nsatir2\n");

        DiscardBackup backup = backups.ShouldHaveSingleItem();
        backup.Path.Value.ShouldBe("a.txt");
        harness.Repository.Git("cat-file", "-p", backup.BlobId).ShouldBe("KAYBOLMAMASI GEREKEN\n");
    }

    [Fact]
    public async Task Diskte_olmayan_yol_yedeklenmeye_CALISILMAZ()
    {
        // `hash-object` olmayan dosyada düşer; silinmiş dosyanın geri alınacak içeriği de yok.
        using Harness harness = await CreateAsync();
        File.Delete(Path.Combine(harness.Path, "b.txt"));

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("b.txt"), DiscardScope.UnstagedOnly, userConfirmed: true, Ct);

        backups.ShouldBeEmpty();
        harness.Read("b.txt").ShouldBe("b\n");
    }

    // ---- Takip edilmeyen dosyayı silme ----

    [Fact]
    public async Task Takip_edilmeyen_dosya_silinir()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni.txt", "x\n");

        await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("yeni.txt"), userConfirmed: true, Ct);

        harness.Exists("yeni.txt").ShouldBeFalse();
    }

    [Fact]
    public async Task YOK_SAYILAN_dosya_da_silinir()
    {
        // 🔴 ÖLÇÜLDÜ: `-x` olmadan `git clean -f -- hata.log` çıkış 0 veriyor ve dosya
        // DURUYOR. Kullanıcı adıyla seçtiği dosyanın silinmesini bekler; sessizce hiçbir
        // şey yapmamak bu görevdeki en kötü sonuçtur.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitignore", "*.log\n");
        harness.Repository.WriteFile("hata.log", "x\n");

        await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("hata.log"), userConfirmed: true, Ct);

        harness.Exists("hata.log").ShouldBeFalse();
    }

    [Fact]
    public async Task Takip_edilmeyen_DIZIN_silinir()
    {
        // ÖLÇÜLDÜ: `-d` olmadan dizin hiç silinmiyor ve hata da verilmiyor.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("dizin/ic.txt", "x\n");

        await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("dizin"), userConfirmed: true, Ct);

        Directory.Exists(Path.Combine(harness.Path, "dizin")).ShouldBeFalse();
    }

    [Fact]
    public async Task Onaysiz_silme_REDDEDILIR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni.txt", "x\n");

        await Should.ThrowAsync<InvalidOperationException>(
            harness.Writer.DeleteUntrackedAsync(
                harness.Path, Paths("yeni.txt"), userConfirmed: false, Ct));

        harness.Exists("yeni.txt").ShouldBeTrue();
    }

    [Fact]
    public async Task Bos_yol_listesi_HICBIR_SEYI_silmez()
    {
        // ⚠️ Yolsuz `git clean -f` çalışma ağacının TAMAMINI siler.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni.txt", "x\n");

        await harness.Writer.DeleteUntrackedAsync(harness.Path, [], userConfirmed: true, Ct);

        harness.Exists("yeni.txt").ShouldBeTrue();
    }

    [Fact]
    public async Task Silme_IZLENEN_dosyaya_dokunmaz()
    {
        // Karşı kanıt: `-x` eklemek "her şeyi sil"e dönüşmemeli.
        using Harness harness = await CreateAsync();

        await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("a.txt"), userConfirmed: true, Ct);

        harness.Exists("a.txt").ShouldBeTrue();
    }

    // ---- git clean (tüm ağaç) ----

    [Fact]
    public async Task Clean_takip_edilmeyenleri_siler_yok_sayilanlari_BIRAKIR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitignore", "*.log\n");
        harness.Repository.Git("add", ".gitignore");
        harness.Repository.Git("commit", "-m", "ignore");

        harness.Repository.WriteFile("yeni.txt", "x\n");
        harness.Repository.WriteFile("hata.log", "x\n");
        harness.Repository.WriteFile("dizin/ic.txt", "x\n");

        await harness.Writer.CleanAsync(harness.Path, CleanOptions.Default, userConfirmed: true, Ct);

        harness.Exists("yeni.txt").ShouldBeFalse();
        Directory.Exists(Path.Combine(harness.Path, "dizin")).ShouldBeFalse();

        // Yok sayılanlar ayrı bir karar: `.env` gibi yeniden üretilemeyen dosyalar da
        // genellikle yok sayılıyor.
        harness.Exists("hata.log").ShouldBeTrue();
    }

    [Fact]
    public async Task Clean_ISTENIRSE_yok_sayilanlari_da_siler()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitignore", "*.log\n");
        harness.Repository.Git("add", ".gitignore");
        harness.Repository.Git("commit", "-m", "ignore");
        harness.Repository.WriteFile("hata.log", "x\n");

        await harness.Writer.CleanAsync(
            harness.Path, new CleanOptions { IncludeIgnored = true }, userConfirmed: true, Ct);

        harness.Exists("hata.log").ShouldBeFalse();
    }

    [Fact]
    public async Task Clean_ic_ice_depoyu_ancak_ISTENIRSE_siler()
    {
        // 🔴 ÖLÇÜLDÜ: tek `-f` ile iç içe depo çıktıda HİÇ görünmüyor, sessizce atlanıyor.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("init", "icdepo");
        harness.Repository.WriteFile("icdepo/dosya.txt", "x\n");

        await harness.Writer.CleanAsync(harness.Path, CleanOptions.Default, userConfirmed: true, Ct);
        Directory.Exists(Path.Combine(harness.Path, "icdepo")).ShouldBeTrue();

        await harness.Writer.CleanAsync(
            harness.Path,
            new CleanOptions { IncludeNestedRepositories = true },
            userConfirmed: true,
            Ct);

        Directory.Exists(Path.Combine(harness.Path, "icdepo")).ShouldBeFalse();
    }

    [Fact]
    public async Task Onaysiz_clean_REDDEDILIR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni.txt", "x\n");

        await Should.ThrowAsync<InvalidOperationException>(
            harness.Writer.CleanAsync(harness.Path, CleanOptions.Default, userConfirmed: false, Ct));

        harness.Exists("yeni.txt").ShouldBeTrue();
    }

    // ---- .gitignore ----

    [Theory]
    [InlineData("duz.log")]
    [InlineData("bosluklu ad.txt")]
    [InlineData("#diyez.txt")]
    [InlineData("!unlem.txt")]
    [InlineData("kose[bracket].txt")]
    [InlineData("yildiz*.txt")]
    [InlineData("ters\\slash.txt")]
    public async Task Ozel_karakterli_ad_GERCEKTEN_yok_sayilir(string name)
    {
        // 🔴 ÖLÇÜLDÜ: adı HAM yazmak `#`, `!`, `[` ve `\` için sessizce çalışmıyor —
        // git hata vermiyor, dosya da yok sayılmıyor. Doğrulama `check-ignore` ile
        // gerçek git'e sorularak yapılıyor; desenin "doğru göründüğü" yetmez.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(name, "x\n");

        RepositoryPath path = RepositoryPath.Parse(name);

        GitIgnoreOutcome outcome = await harness.Writer.AddToGitIgnoreAsync(
            harness.Path, path, GitIgnorePattern.ForPath(path), Ct);

        outcome.ShouldBe(GitIgnoreOutcome.Added);

        // ⚠️ Doğrulama `-z` ile yapılıyor: `git status` özel karakterli adları TIRNAKLIYOR
        // (`"ters\\slash.txt"`), dolayısıyla ham adı düz çıktıda aramak hatalı sürümde de
        // "bulunamadı" derdi — yani test sessizce boşa geçerdi (P04-T09'un dersi).
        Untracked(harness).ShouldNotContain(name);

        // Ve doğrudan git'e sorularak. Çıkış koduna bakılıyor, çıktıya DEĞİL: `check-ignore`
        // özel karakterli adı tırnaklıyor ve `-z` yalnızca `--stdin` ile çalışıyor (ölçüldü).
        // `TestRepository.Git` sıfır olmayan çıkışta fırlatıyor, yani eşleşme yoksa test kırılır.
        Should.NotThrow(() => harness.Repository.Git("check-ignore", "--quiet", "--", name));
    }

    /// <summary>Takip edilmeyen yollar — <c>-z</c> ile, yani tırnaklanmamış ham adlar.</summary>
    private static IReadOnlyList<string> Untracked(Harness harness) =>
        [.. harness.Repository
            .Git("status", "--porcelain=v2", "-z", "--untracked-files=all")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(record => record.StartsWith("? ", StringComparison.Ordinal))
            .Select(record => record[2..])];

    [Fact]
    public async Task Satir_sonu_olmayan_gitignore_BOZULMAZ()
    {
        // 🔴 ÖLÇÜLDÜ: satır sonu eklenmezse yeni desen bir öncekine yapışıyor
        // (`derleme/` + `/kok.txt` → `derleme//kok.txt`). Sonuç yalnızca yeni desenin
        // çalışmaması değil — kullanıcının VAR OLAN deseni de bozuluyor.
        using Harness harness = await CreateAsync();

        File.WriteAllText(Path.Combine(harness.Path, ".gitignore"), "derleme/");
        harness.Repository.WriteFile("derleme/cikti.o", "x\n");
        harness.Repository.WriteFile("kok.txt", "x\n");

        RepositoryPath path = RepositoryPath.Parse("kok.txt");
        await harness.Writer.AddToGitIgnoreAsync(
            harness.Path, path, GitIgnorePattern.ForPath(path), Ct);

        IReadOnlyList<string> untracked = Untracked(harness);

        untracked.ShouldNotContain("kok.txt");
        untracked.ShouldNotContain("derleme/cikti.o");
    }

    [Fact]
    public async Task IZLENEN_dosya_icin_gitignore_YAZILMAZ()
    {
        // 🔴 ÖLÇÜLDÜ: izlenen bir dosyayı `.gitignore`'a eklemek HİÇBİR ŞEY yapmıyor —
        // `git status` dosyayı göstermeye devam ediyor. Yazıp "eklendi" demek, kullanıcıya
        // olmayan bir sonuç vaat etmek olurdu.
        using Harness harness = await CreateAsync();
        RepositoryPath path = RepositoryPath.Parse("a.txt");

        GitIgnoreOutcome outcome = await harness.Writer.AddToGitIgnoreAsync(
            harness.Path, path, GitIgnorePattern.ForPath(path), Ct);

        outcome.ShouldBe(GitIgnoreOutcome.PathIsTracked);
        harness.Exists(".gitignore").ShouldBeFalse();
    }

    [Fact]
    public async Task Zaten_yok_sayilan_dosya_icin_TEKRAR_yazilmaz()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitignore", "*.log\n");
        harness.Repository.WriteFile("hata.log", "x\n");

        RepositoryPath path = RepositoryPath.Parse("hata.log");

        GitIgnoreOutcome outcome = await harness.Writer.AddToGitIgnoreAsync(
            harness.Path, path, GitIgnorePattern.ForPath(path), Ct);

        outcome.ShouldBe(GitIgnoreOutcome.AlreadyIgnored);
        harness.Read(".gitignore").ShouldBe("*.log\n");
    }

    [Fact]
    public async Task Uzanti_deseni_ayni_uzantili_TUM_dosyalari_yok_sayar()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("bir.log", "x\n");
        harness.Repository.WriteFile("alt/iki.log", "x\n");

        RepositoryPath path = RepositoryPath.Parse("bir.log");
        string? pattern = GitIgnorePattern.ForExtensionOf(path);

        pattern.ShouldNotBeNull().ShouldBe("*.log");
        await harness.Writer.AddToGitIgnoreAsync(harness.Path, path, pattern!, Ct);

        IReadOnlyList<string> untracked = Untracked(harness);

        untracked.ShouldNotContain("bir.log");
        untracked.ShouldNotContain("alt/iki.log");
    }

    [Fact]
    public void Uzantisiz_ve_gizli_dosyalarda_uzanti_deseni_URETILMEZ()
    {
        // `.env` bir uzantı değil, gizli dosya adıdır; `*.env` yazmak farklı bir şey demek.
        GitIgnorePattern.ForExtensionOf(RepositoryPath.Parse("Makefile")).ShouldBeNull();
        GitIgnorePattern.ForExtensionOf(RepositoryPath.Parse(".env")).ShouldBeNull();
        GitIgnorePattern.ForExtensionOf(RepositoryPath.Parse("alt/.env")).ShouldBeNull();
    }

    [Fact]
    public void Dizin_deseni_yalnizca_alt_dizinler_icin_uretilir()
    {
        GitIgnorePattern.ForDirectoryOf(RepositoryPath.Parse("alt/derin/x.txt"))
            .ShouldBe("/alt/derin/");

        GitIgnorePattern.ForDirectoryOf(RepositoryPath.Parse("kok.txt")).ShouldBeNull();
    }
}
