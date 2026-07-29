using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P02-T11 — Ham nesne erişimi. <c>cat-file --batch</c> protokolü ölçülüp buna göre yazıldı.
/// </summary>
public class ObjectReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ObjectReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new ObjectReader(new GitProcessRunner(executable));
    }

    private static TestRepository CreateSampleRepository()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("metin.txt", "satır1\nsatır2\n");
        repository.WriteFile("alt/derin/dosya.txt", "derin içerik\n");
        repository.WriteFile("boş.txt", string.Empty);
        File.WriteAllBytes(
            Path.Combine(repository.Path, "ikili.bin"),
            [0x00, 0xFF, 0xFE, 0x42, 0x00, 0x80, 0x81]);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "örnek ağaç");
        return repository;
    }

    [Fact]
    public async Task Kok_agac_listelenir()
    {
        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<TreeEntry> entries = await reader.ReadTreeAsync(
            repository.Path, "HEAD", cancellationToken: Ct);

        entries.Select(e => e.Path.Value)
            .ShouldBe(["alt", "boş.txt", "ikili.bin", "metin.txt"], ignoreOrder: true);

        entries.Single(e => e.Path.Value == "alt").IsDirectory.ShouldBeTrue();
        entries.Single(e => e.Path.Value == "metin.txt").Type.ShouldBe(GitObjectType.Blob);
    }

    [Fact]
    public async Task Ozyinelemeli_listeleme_alt_dizinlere_iner()
    {
        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<TreeEntry> entries = await reader.ReadTreeAsync(
            repository.Path, "HEAD", recursive: true, cancellationToken: Ct);

        entries.Select(e => e.Path.Value).ShouldContain("alt/derin/dosya.txt");
        // Özyinelemeli modda ağaç girdileri gelmez, yalnızca blob'lar.
        entries.ShouldAllBe(e => !e.IsDirectory);
    }

    [Fact]
    public async Task Boyut_isteginde_blob_boyutlari_gelir()
    {
        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<TreeEntry> entries = await reader.ReadTreeAsync(
            repository.Path, "HEAD", recursive: true, includeSize: true, cancellationToken: Ct);

        TreeEntry text = entries.Single(e => e.Path.Value == "metin.txt");
        text.Size.ShouldNotBeNull();
        // "satır1\nsatır2\n" — Türkçe karakterler UTF-8'de 2 bayt.
        text.Size!.Value.ShouldBe(Encoding.UTF8.GetByteCount("satır1\nsatır2\n"));
    }

    [Fact]
    public async Task Alt_dizin_listelenebilir()
    {
        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<TreeEntry> entries = await reader.ReadTreeAsync(
            repository.Path,
            "HEAD",
            RepositoryPath.Parse("alt"),
            cancellationToken: Ct);

        entries.ShouldHaveSingleItem().Path.Value.ShouldBe("alt/derin");
    }

    [Fact]
    public async Task Sembolik_bag_ve_calistirilabilir_modlar_ayirt_edilir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("hedef.txt", "içerik\n");
        repository.WriteFile("betik.sh", "#!/bin/sh\necho merhaba\n");
        repository.Git("add", "-A");
        repository.Git("update-index", "--chmod=+x", "betik.sh");

        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(Path.Combine(repository.Path, "baglanti"), "hedef.txt");
            repository.Git("add", "baglanti");
        }

        repository.Git("commit", "-m", "modlar");

        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<TreeEntry> entries = await reader.ReadTreeAsync(
            repository.Path, "HEAD", cancellationToken: Ct);

        entries.Single(e => e.Path.Value == "betik.sh").IsExecutable.ShouldBeTrue();
        entries.Single(e => e.Path.Value == "hedef.txt").IsExecutable.ShouldBeFalse();

        if (!OperatingSystem.IsWindows())
        {
            entries.Single(e => e.Path.Value == "baglanti").IsSymlink.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Bosluklu_yol_TAB_ayracindan_sonra_dogru_okunur()
    {
        // Metadata ile yol arasındaki ayraç TAB'dır; boşlukla bölmek yolu keserdi.
        const string awkward = "klasör adı/dosya adı ÖĞÜŞİ.txt";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile(awkward, "içerik\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "boşluklu yol");

        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<TreeEntry> entries = await reader.ReadTreeAsync(
            repository.Path, "HEAD", recursive: true, cancellationToken: Ct);

        entries.ShouldHaveSingleItem().Path.Value.ShouldBe(awkward);
    }

    [Fact]
    public async Task Blob_icerigi_okunur()
    {
        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<BlobContent> blobs = await reader.ReadBlobsAsync(
            repository.Path, ["HEAD:metin.txt"], cancellationToken: Ct);

        BlobContent blob = blobs.ShouldHaveSingleItem();
        blob.IsBinary.ShouldBeFalse();
        blob.IsTruncated.ShouldBeFalse();
        blob.GetText().ShouldBe("satır1\nsatır2\n");
    }

    [Fact]
    public async Task Ikili_icerik_bozulmadan_okunur_ve_isaretlenir()
    {
        // İçerik metne çevrilip bölünseydi geçersiz UTF-8 baytları U+FFFD'ye dönüşürdü.
        byte[] expected = [0x00, 0xFF, 0xFE, 0x42, 0x00, 0x80, 0x81];

        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<BlobContent> blobs = await reader.ReadBlobsAsync(
            repository.Path, ["HEAD:ikili.bin"], cancellationToken: Ct);

        BlobContent blob = blobs.ShouldHaveSingleItem();
        blob.Content.ShouldBe(expected);
        blob.IsBinary.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => blob.GetText());
    }

    [Fact]
    public async Task Birden_fazla_blob_tek_cagrida_okunur()
    {
        // ADR-0002'nin bilinen zayıflığı N+1 süreç çağrısı; toplu okuma bunun cevabı.
        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<BlobContent> blobs = await reader.ReadBlobsAsync(
            repository.Path,
            ["HEAD:metin.txt", "HEAD:ikili.bin", "HEAD:alt/derin/dosya.txt"],
            cancellationToken: Ct);

        blobs.Count.ShouldBe(3);
        blobs[0].GetText().ShouldBe("satır1\nsatır2\n");
        blobs[1].IsBinary.ShouldBeTrue();
        blobs[2].GetText().ShouldBe("derin içerik\n");
    }

    [Fact]
    public async Task Toplu_okumada_ikili_icerik_sonraki_nesneyi_kaydirmaz()
    {
        // KRİTİK: içerik ikili olabildiği için ayrıştırma bayt düzeyinde yapılmalı.
        // İçerikte satır sonu veya NUL varsa ve metin olarak bölünseydi, sonraki
        // nesnenin başlığı kaybolur ve tüm akış kayardı.
        using TestRepository repository = TestRepository.CreateEmpty();
        File.WriteAllBytes(
            Path.Combine(repository.Path, "a.bin"),
            [0x00, 0x0A, 0xFF, 0x0A, 0x00, 0x0A]);
        repository.WriteFile("b.txt", "sonraki\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikili sonra metin");

        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<BlobContent> blobs = await reader.ReadBlobsAsync(
            repository.Path, ["HEAD:a.bin", "HEAD:b.txt"], cancellationToken: Ct);

        blobs.Count.ShouldBe(2);
        blobs[0].Content.ShouldBe([0x00, 0x0A, 0xFF, 0x0A, 0x00, 0x0A]);
        blobs[1].GetText().ShouldBe("sonraki\n");
    }

    [Fact]
    public async Task Boyut_siniri_asilinca_icerik_kirpilir()
    {
        // Sınır olmadan 200 MB'lık tek bir dosya arayüzü kilitler.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("buyuk.txt", new string('x', 5000));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "büyük dosya");

        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<BlobContent> blobs = await reader.ReadBlobsAsync(
            repository.Path, ["HEAD:buyuk.txt"], maxBytes: 100, cancellationToken: Ct);

        BlobContent blob = blobs.ShouldHaveSingleItem();
        blob.Content.Length.ShouldBe(100);
        blob.IsTruncated.ShouldBeTrue();
        // Gerçek boyut kırpılmadan bildirilmeli.
        blob.Size.ShouldBe(5000);
    }

    [Fact]
    public async Task Bos_dosya_okunabilir()
    {
        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<BlobContent> blobs = await reader.ReadBlobsAsync(
            repository.Path, ["HEAD:boş.txt"], cancellationToken: Ct);

        BlobContent blob = blobs.ShouldHaveSingleItem();
        blob.Size.ShouldBe(0);
        blob.Content.ShouldBeEmpty();
        blob.IsBinary.ShouldBeFalse();
    }

    [Fact]
    public async Task Nesne_bilgisi_icerik_okunmadan_alinir()
    {
        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        IReadOnlyList<GitObjectInfo> infos = await reader.GetInfoAsync(
            repository.Path,
            ["HEAD:metin.txt", "HEAD:alt", "HEAD:yok.txt"],
            cancellationToken: Ct);

        infos.Count.ShouldBe(3);
        infos[0].Type.ShouldBe(GitObjectType.Blob);
        infos[0].Size.ShouldBe(Encoding.UTF8.GetByteCount("satır1\nsatır2\n"));
        infos[1].Type.ShouldBe(GitObjectType.Tree);
        infos[2].Exists.ShouldBeFalse();
        infos[2].Type.ShouldBe(GitObjectType.Missing);
    }

    [Fact]
    public async Task Bos_istek_listesi_bos_sonuc_dondurur()
    {
        using TestRepository repository = CreateSampleRepository();
        ObjectReader reader = await CreateReaderAsync();

        (await reader.ReadBlobsAsync(repository.Path, [], cancellationToken: Ct)).ShouldBeEmpty();
        (await reader.GetInfoAsync(repository.Path, [], cancellationToken: Ct)).ShouldBeEmpty();
    }

    [Fact]
    public void Agac_girdisi_ayristirilir()
    {
        TreeEntry? entry = ObjectReader.ParseTreeEntry(
            "100644 blob 5e1be32d69590e1725df70312bf2db7eed02e4d9\tsrc/Program.cs");

        entry.ShouldNotBeNull();
        entry!.Mode.ShouldBe("100644");
        entry.Type.ShouldBe(GitObjectType.Blob);
        entry.Path.Value.ShouldBe("src/Program.cs");
        entry.Size.ShouldBeNull();
    }

    [Fact]
    public void Boyutlu_agac_girdisi_ayristirilir()
    {
        // --long çıktısında boyut sağa hizalı, boşlukla doldurulmuş gelir.
        TreeEntry? entry = ObjectReader.ParseTreeEntry(
            "100644 blob 5e1be32d69590e1725df70312bf2db7eed02e4d9      14\tmetin.txt");

        entry.ShouldNotBeNull();
        entry!.Size.ShouldBe(14);
    }
}
