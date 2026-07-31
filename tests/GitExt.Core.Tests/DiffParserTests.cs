using System.Text;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T02 — Unified diff ayrıştırıcısı, <b>gerçek <c>git</c> çıktısına</b> karşı (ADR-0003).
/// </summary>
/// <remarks>
/// Elle yazılmış diff metni kullanmak, git'in gerçekte ürettiği ince ayrıntıları
/// (tek satırlık hunk'ta uzunluğun yazılmaması, satır sonu işaretinin yeri, hunk'sız
/// diff türleri) kaçırırdı. Her senaryo gerçek bir depoda üretiliyor.
/// </remarks>
public class DiffParserTests
{
    /// <summary>
    /// Son iki commit arasındaki diff'i ayrıştırır.
    /// </summary>
    private static IReadOnlyList<FileDiff> DiffOfHead(TestRepository repository) =>
        DiffParser.Parse(repository.GitLossless("diff", "--raw", "-z", "--patch", "-M", "HEAD^", "HEAD"));

    private static FileDiff Single(IReadOnlyList<FileDiff> diffs, string path) =>
        diffs.Single(d => d.Path.Value == path);

    [Fact]
    public void Normal_degisiklik_hunk_ve_satir_numaralariyla_gelir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "bir\niki\nuc\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("a.txt", "bir\niki DEGISTI\nuc\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        FileDiff diff = DiffOfHead(repository).Single();

        diff.Change.ShouldBe(FileChangeKind.Modified);
        diff.Path.Value.ShouldBe("a.txt");
        diff.AddedLines.ShouldBe(1);
        diff.RemovedLines.ShouldBe(1);

        DiffHunk hunk = diff.Hunks.Single();
        hunk.Header.ShouldStartWith("@@");
        hunk.OldStart.ShouldBe(1);
        hunk.NewStart.ShouldBe(1);

        DiffLine removed = hunk.Lines.Single(l => l.Kind == DiffLineKind.Removed);
        removed.Content.ShouldBe("iki");
        removed.OldLineNumber.ShouldBe(2);
        removed.NewLineNumber.ShouldBeNull();

        DiffLine added = hunk.Lines.Single(l => l.Kind == DiffLineKind.Added);
        added.Content.ShouldBe("iki DEGISTI");
        added.NewLineNumber.ShouldBe(2);
        added.OldLineNumber.ShouldBeNull();

        hunk.Lines.Where(l => l.Kind == DiffLineKind.Context)
            .Select(l => l.Content).ShouldBe(["bir", "uc"]);
    }

    [Fact]
    public void Yeni_silinen_ve_yeniden_adlandirilan_dosyalar_ayirt_edilir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("kalan.txt", "x\n");
        repository.WriteFile("silinecek.txt", "y\n");
        repository.WriteFile("eski-ad.txt", "tasinacak icerik\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("yeni.txt", "yepyeni\n");
        File.Delete(Path.Combine(repository.Path, "silinecek.txt"));
        repository.Git("mv", "eski-ad.txt", "yeni-ad.txt");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        IReadOnlyList<FileDiff> diffs = DiffOfHead(repository);

        Single(diffs, "yeni.txt").Change.ShouldBe(FileChangeKind.Added);
        Single(diffs, "silinecek.txt").Change.ShouldBe(FileChangeKind.Deleted);

        FileDiff renamed = Single(diffs, "yeni-ad.txt");
        renamed.Change.ShouldBe(FileChangeKind.Renamed);
        renamed.OldPath!.Value.Value.ShouldBe("eski-ad.txt");
        renamed.SimilarityScore.ShouldBe(100);

        // ÖLÇÜLDÜ: %100 benzerlikli rename HİÇ hunk üretmiyor.
        renamed.HasHunks.ShouldBeFalse();
    }

    [Fact]
    public void Yalnizca_mod_degisikligi_hunksiz_ve_ayni_blobla_gelir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("betik.sh", "echo merhaba\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.Git("update-index", "--chmod=+x", "betik.sh");
        repository.Git("commit", "-m", "calistirilabilir");

        FileDiff diff = DiffOfHead(repository).Single();

        diff.Change.ShouldBe(FileChangeKind.Modified);
        diff.HasHunks.ShouldBeFalse();
        diff.IsModeOnlyChange.ShouldBeTrue();
        diff.IsExecutableChanged.ShouldBeTrue();
        diff.OldMode.ShouldBe("100644");
        diff.NewMode.ShouldBe("100755");
    }

    [Fact]
    public void Binary_dosya_isaretlenir_ve_hunk_uretmez()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        File.WriteAllBytes(Path.Combine(repository.Path, "veri.bin"), [0, 1, 2, 3, 0]);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        File.WriteAllBytes(Path.Combine(repository.Path, "veri.bin"), [0, 9, 9, 9, 0, 7]);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        FileDiff diff = DiffOfHead(repository).Single();

        diff.IsBinary.ShouldBeTrue();
        diff.HasHunks.ShouldBeFalse();
    }

    [Fact]
    public void Bos_yeni_dosya_hunksiz_gelir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "x\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("bos.txt", string.Empty);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "bos dosya");

        FileDiff diff = DiffOfHead(repository).Single();

        diff.Path.Value.ShouldBe("bos.txt");
        diff.Change.ShouldBe(FileChangeKind.Added);
        diff.HasHunks.ShouldBeFalse();
    }

    [Fact]
    public void Satir_sonu_olmayan_dosyada_isaret_dogru_satira_baglanir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "son satır newline yok");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("a.txt", "son satır newline yok DEGISTI");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffHunk hunk = DiffOfHead(repository).Single().Hunks.Single();

        // ÖLÇÜLDÜ: işaret hem `-` hem `+` satırından sonra ayrı ayrı çıkıyor.
        hunk.Lines.Single(l => l.Kind == DiffLineKind.Removed).EndsWithoutNewline.ShouldBeTrue();
        hunk.Lines.Single(l => l.Kind == DiffLineKind.Added).EndsWithoutNewline.ShouldBeTrue();
    }

    [Fact]
    public void Zor_yollar_dogru_okunur()
    {
        // Bu testin varlık sebebi: `diff --git a/… b/…` başlığı bu yollarda ayrıştırılamıyor.
        // Boşluklu yolda iki yolu ayırmanın güvenli yolu yok, ASCII dışı adlar sekizlik
        // kaçışla tırnaklanıyor. Yollar bu yüzden yalnızca `--raw -z`'den okunuyor.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("baslangic.txt", "x\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("bosluklu ad.txt", "a\n");
        repository.WriteFile("türkçe-şğüöçİ.txt", "b\n");

        Directory.CreateDirectory(Path.Combine(repository.Path, "alt dizin"));
        repository.WriteFile(Path.Combine("alt dizin", "b -> c.txt"), "c\n");

        repository.Git("add", "-A");
        repository.Git("commit", "-m", "zor yollar");

        IReadOnlyList<FileDiff> diffs = DiffOfHead(repository);

        diffs.Select(d => d.Path.Value).ShouldBe(
            ["alt dizin/b -> c.txt", "bosluklu ad.txt", "türkçe-şğüöçİ.txt"],
            ignoreOrder: true);
    }

    [Fact]
    public void Cok_dosyali_committe_hunklar_dogru_dosyaya_baglanir()
    {
        // Eşleme sıraya dayanıyor (700 gerçek commit'te doğrulandı). Yanlış eşleme,
        // kullanıcıya BAŞKA bir dosyanın değişikliklerini göstermek demek olurdu.
        using TestRepository repository = TestRepository.CreateEmpty();

        for (int i = 1; i <= 4; i++)
        {
            repository.WriteFile($"d{i}.txt", $"dosya {i} satır 1\n");
        }

        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        for (int i = 1; i <= 4; i++)
        {
            repository.WriteFile($"d{i}.txt", $"dosya {i} satır 1 DEGISTI-{i}\n");
        }

        repository.Git("add", "-A");
        repository.Git("commit", "-m", "hepsi");

        IReadOnlyList<FileDiff> diffs = DiffOfHead(repository);

        diffs.Count.ShouldBe(4);

        foreach (FileDiff diff in diffs)
        {
            string index = diff.Path.Value[1..2];

            diff.Hunks.Single().Lines
                .Single(l => l.Kind == DiffLineKind.Added).Content
                .ShouldBe($"dosya {index} satır 1 DEGISTI-{index}");
        }
    }

    [Fact]
    public void Tek_satirlik_hunkta_uzunluk_yazilmasa_da_dogru_okunur()
    {
        // ÖLÇÜLDÜ: git tek satırlık hunk'ta `@@ -1 +1 @@` yazıyor — uzunluk YOK.
        // Varsayılanı 0 almak sonraki satır numaralarını kaydırırdı.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "tek\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("a.txt", "tek DEGISTI\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffHunk hunk = DiffOfHead(repository).Single().Hunks.Single();

        hunk.OldLength.ShouldBe(1);
        hunk.NewLength.ShouldBe(1);
        hunk.Header.ShouldBe("@@ -1 +1 @@");
    }

    [Fact]
    public void Hunk_baglam_metni_okunur()
    {
        using TestRepository repository = TestRepository.CreateEmpty();

        StringBuilder original = new();
        original.AppendLine("void Main()");
        original.AppendLine("{");

        for (int i = 1; i <= 12; i++)
        {
            original.AppendLine($"    satir{i};");
        }

        original.AppendLine("}");

        repository.WriteFile("kod.c", original.ToString());
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("kod.c", original.ToString().Replace("satir9;", "satir9_DEGISTI;", StringComparison.Ordinal));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffHunk hunk = DiffOfHead(repository).Single().Hunks.Single();

        hunk.Section.ShouldContain("Main");
    }

    [Fact]
    public void Bos_cikti_bos_liste_uretir()
    {
        DiffParser.Parse(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void Bozuk_ham_kayit_sessizce_yutulmaz()
    {
        // Sessizce yanlış veri üretmektense durmak tercih edildi.
        Should.Throw<DiffParseException>(() => DiffParser.Parse(":bozuk\0a.txt\0\0"));
    }

    [Fact]
    public void Yama_blogu_eksikse_hata_verilir()
    {
        // Hunk'ları yanlış dosyaya bağlamak sessiz veri bozulmasıdır; sayı uyuşmazsa dur.
        const string output =
            ":100644 100644 1111111 2222222 M\0a.txt\0"
            + ":100644 100644 3333333 4444444 M\0b.txt\0"
            + "\0diff --git a/a.txt b/a.txt\n@@ -1 +1 @@\n-x\n+y\n";

        Should.Throw<DiffParseException>(() => DiffParser.Parse(output));
    }
}
