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
        DiffReader Diff,
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
            new DiffReader(runner),
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
    public async Task Silinen_takip_edilmeyen_dosyanin_icerigi_YEDEKLENIR()
    {
        // 🔴 P05-T15'in gerekçesi, ölçümle: `git clean` ile silinen bir dosyanın nesne
        // veritabanında hiçbir izi kalmıyor — `git fsck --lost-found` bile bulmuyor.
        // Bu, deponun tek gerçekten geri döndürülemez işlemiydi. Oysa takip edilmeyen
        // dosyalar tipik olarak henüz commit edilmemiş YENİ KAYNAK DOSYALAR: bu deponun
        // kendisinde `git clean -dn` çıktısı, o sırada yazılmakta olan dosyaları
        // listeliyordu.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni-kaynak.cs", "çok değerli emek\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("yeni-kaynak.cs"), userConfirmed: true, Ct);

        harness.Exists("yeni-kaynak.cs").ShouldBeFalse();

        backups.Count.ShouldBe(1);
        backups[0].Path.Value.ShouldBe("yeni-kaynak.cs");

        // Yedek gerçekten okunabilmeli; kimlik döndürüp içeriği kaybetmek işe yaramaz.
        harness.Repository.Git("cat-file", "-p", backups[0].BlobId)
            .ShouldContain("çok değerli emek");
    }

    [Fact]
    public async Task Yedek_normal_gc_ile_KAYBOLMUYOR()
    {
        // ⚠️ ÖLÇÜLDÜ — T08'in "garanti değil" notu doğru ama fazla karamsardı: yedeğe
        // hiçbir ref işaret etmiyor, ama `git gc` onu SİLMİYOR (dangling nesneler
        // varsayılan `gc.pruneExpire=2.weeks` boyunca korunuyor). Silen şey yalnızca
        // `gc --prune=now`. Kullanıcıya söylenen kurtarma yolu bu yüzden gerçekçi.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("gidecek.txt", "kurtarılacak içerik\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("gidecek.txt"), userConfirmed: true, Ct);

        harness.Repository.Git("gc", "--quiet");

        harness.Repository.Git("cat-file", "-p", backups[0].BlobId)
            .ShouldContain("kurtarılacak içerik");
    }

    [Fact]
    public async Task Secili_satirlar_geri_alinir_INDEX_korunur()
    {
        // 🔴 ÖLÇÜLDÜ: `git apply --reverse` (--cached OLMADAN) yamayı yalnızca çalışma
        // ağacına uyguluyor; dosyanın stage'lenmiş sürümü olduğu gibi kalıyor. `--cached`
        // eklenseydi kullanıcı "şu satırı geri al" derken index'ini de değiştirmiş olurdu.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "a.txt");
        harness.Repository.Commit("temel");

        harness.Repository.WriteFile("a.txt", "BIR\niki\nUC\n");
        harness.Repository.Git("add", "a.txt");
        harness.Repository.WriteFile("a.txt", "BIR\niki\nUCUC\n");

        FileDiff diff = (await harness.Diff.ReadUnstagedAsync(harness.Path, cancellationToken: Ct))
            .Single();

        // Tüm hunk'ı seç: dosyada tek bir değişiklik var, ölçülmek istenen şey index'in
        // korunması.
        PatchSelection selection = PatchSelection.All(diff);

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DiscardPartialAsync(
            harness.Path, diff, selection, userConfirmed: true, cancellationToken: Ct);

        backups.Count.ShouldBe(1);

        // Çalışma ağacı yamadan önceki hâle dönmeli…
        harness.Read("a.txt").ShouldBe("BIR\niki\nUC\n");

        // …index'e ise DOKUNULMAMALI. ⚠️ Tam eşitlik şart: "UC içeriyor mu" diye bakmak
        // "UCUC" ile de geçerdi (P04-T09'un dersi: baktığın yer, doğruladığın şeyin
        // yeri olmalı).
        harness.Repository.Git("show", ":a.txt").ShouldBe("BIR\niki\nUC\n");
    }

    [Fact]
    public async Task Kismi_geri_alma_eol_crlf_altinda_SATIR_SONLARINI_bozmuyor()
    {
        // P05-T17 ölçümü: yama `git diff`ten geldiği için LF, çalışma ağacındaki dosya ise
        // CRLF. `git apply` (worktree yolu) aynı filtreleri kendisi uyguladığı için yama
        // tutuyor ve CRLF korunuyor. Tutmasaydı belirti sessiz değil `patch does not apply`
        // olurdu — ama geri alma kullanıcının emeğini silen bir işlem, bunun testsiz
        // varsayıma bırakılacak yeri yok.
        using Harness harness = await CreateAsync();
        string path = System.IO.Path.Combine(harness.Path, "c.txt");

        harness.Repository.WriteFile(".gitattributes", "* text=auto eol=crlf\n");
        File.WriteAllBytes(path, "bir\r\niki\r\nuc\r\n"u8.ToArray());
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("temel");

        File.WriteAllBytes(path, "bir\r\nIKI\r\nuc\r\n"u8.ToArray());

        FileDiff diff = (await harness.Diff.ReadUnstagedAsync(harness.Path, cancellationToken: Ct))
            .Single();

        await harness.Writer.DiscardPartialAsync(
            harness.Path, diff, PatchSelection.All(diff), userConfirmed: true, cancellationToken: Ct);

        File.ReadAllBytes(path).ShouldBe("bir\r\niki\r\nuc\r\n"u8.ToArray());
    }

    [Fact]
    public async Task Kismi_geri_almada_onay_ZORUNLU()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "bir\n");
        harness.Repository.Git("add", "a.txt");
        harness.Repository.Commit("temel");
        harness.Repository.WriteFile("a.txt", "BIR\n");

        FileDiff diff = (await harness.Diff.ReadUnstagedAsync(harness.Path, cancellationToken: Ct))
            .Single();

        PatchSelection selection = PatchSelection.All(diff);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            harness.Writer.DiscardPartialAsync(
                harness.Path, diff, selection, userConfirmed: false, cancellationToken: Ct));
    }

    [Fact]
    public async Task Yedek_geri_yazilabiliyor()
    {
        // Yedek almak tek başına güvenlik ağı değil: kullanıcıya blob kimliği verip
        // `git cat-file` yazmasını beklemek panik anında işe yaramaz.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("alt/dizin/yeni.cs", "geri gelmeli\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("alt/dizin/yeni.cs"), userConfirmed: true, Ct);

        harness.Exists("alt/dizin/yeni.cs").ShouldBeFalse();

        IReadOnlyList<DiscardBackup> restored =
            await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        restored.Count.ShouldBe(1);

        // Dizin de silinmişti (`clean -d`); geri yazma onu yeniden oluşturmalı.
        (await File.ReadAllTextAsync(
            Path.Combine(harness.Path, "alt/dizin/yeni.cs"), Ct))
            .ShouldBe("geri gelmeli\n");
    }

    [Fact]
    public async Task Yedek_IKILI_dosyada_da_BIREBIR()
    {
        using Harness harness = await CreateAsync();

        byte[] content = new byte[8192];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(Path.Combine(harness.Path, "resim.bin"), content, Ct);

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("resim.bin"), userConfirmed: true, Ct);

        await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        (await File.ReadAllBytesAsync(Path.Combine(harness.Path, "resim.bin"), Ct))
            .ShouldBe(content);
    }

    [Fact]
    public async Task Yedek_CRLF_donusumune_ugramaz()
    {
        // 🔴 ÖLÇÜLDÜ: `--no-filters` olmadan `.gitattributes`'ta `text=auto` varken git
        // yedeği yazarken CRLF'i LF'e çeviriyor — geri yazımda kullanıcının satır sonları
        // sessizce değişirdi. Kurtarma vaadi veren bir yedeğin içeriği değiştirmesi,
        // hiç yedek almamaktan daha kötü: kullanıcı kurtardığını sanır.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitattributes", "* text=auto\n");
        harness.Repository.Git("add", ".gitattributes");
        harness.Repository.Commit("attributes");

        byte[] crlf = "birinci\r\nikinci\r\n"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(harness.Path, "crlf.txt"), crlf, Ct);

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("crlf.txt"), userConfirmed: true, Ct);

        await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        (await File.ReadAllBytesAsync(Path.Combine(harness.Path, "crlf.txt"), Ct))
            .ShouldBe(crlf);
    }

    [Fact]
    public async Task Yedek_CLEAN_FILTRESINDEN_etkilenmez()
    {
        // 🔴 ÖLÇÜLDÜ ve en tehlikelisi: özel bir clean filtresi (Git LFS'in çalışma biçimi)
        // varken filtresiz yazılmayan yedeğe dosyanın kendisi değil FİLTRENİN ÇIKTISI
        // giriyor — ölçümde `GIZLI parola` içeriği yedekte `*** parola` oldu.
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "filter.maskele.clean", "sed s/GIZLI/***/");
        harness.Repository.WriteFile(".gitattributes", "*.gizli filter=maskele\n");
        harness.Repository.Git("add", ".gitattributes");
        harness.Repository.Commit("filtre");

        harness.Repository.WriteFile("kasa.gizli", "GIZLI parola\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("kasa.gizli"), userConfirmed: true, Ct);

        await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        (await File.ReadAllTextAsync(Path.Combine(harness.Path, "kasa.gizli"), Ct))
            .ShouldBe("GIZLI parola\n");
    }

    [Fact]
    public async Task COK_dosyali_ikili_kurtarma_her_dosyayi_DOGRU_esliyor()
    {
        // `cat-file --batch` akışı tek bir bayt dizisi: içerikler arka arkaya geliyor ve
        // sınırları yalnızca başlıktaki BOYUT belirliyor. Ayrıştırıcı bir bayt kayarsa
        // dosyalar birbirinin içeriğiyle "kurtarılır" ve bu sessiz bir veri kaybıdır —
        // tam da P05-T15'in engellemek için var olduğu şey. Farklı boyutlarda ve ayraç
        // baytları (\n) içeren ikili içerikle sınanıyor.
        using Harness harness = await CreateAsync();

        Dictionary<string, byte[]> contents = [];

        for (int i = 1; i <= 5; i++)
        {
            byte[] data = new byte[i * 1000];
            Random.Shared.NextBytes(data);

            // Satır sonu baytları bilinçli: ayraç arayan bir ayrıştırıcı burada kırılır.
            data[i * 10] = (byte)'\n';
            data[^1] = (byte)'\n';

            contents[$"ikili{i}.bin"] = data;
            await File.WriteAllBytesAsync(Path.Combine(harness.Path, $"ikili{i}.bin"), data, Ct);
        }

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths([.. contents.Keys]), userConfirmed: true, Ct);

        backups.Count.ShouldBe(5);

        IReadOnlyList<DiscardBackup> restored =
            await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        restored.Count.ShouldBe(5);

        foreach ((string name, byte[] expected) in contents)
        {
            (await File.ReadAllBytesAsync(Path.Combine(harness.Path, name), Ct))
                .ShouldBe(expected, $"{name} yanlış içerikle kurtarıldı");
        }
    }

    [Fact]
    public async Task Budanmis_yedek_digerlerini_ENGELLEMEZ()
    {
        // `gc --prune=now` yedeği anında siliyor (ölçüldü). Kısmi kurtarma, hiç
        // kurtarmamaktan iyidir; tek bir kayıp nesne diğerlerini düşürmemeli.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("duran.txt", "bu kurtarılmalı\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("duran.txt"), userConfirmed: true, Ct);

        List<DiscardBackup> withMissing =
        [
            new DiscardBackup
            {
                Path = RepositoryPath.Parse("kayip.txt"),
                BlobId = "0000000000000000000000000000000000000000",
            },
            .. backups,
        ];

        IReadOnlyList<DiscardBackup> restored =
            await harness.Writer.RestoreBackupsAsync(harness.Path, withMissing, Ct);

        restored.Count.ShouldBe(1);
        harness.Exists("duran.txt").ShouldBeTrue();
        harness.Exists("kayip.txt").ShouldBeFalse();
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
