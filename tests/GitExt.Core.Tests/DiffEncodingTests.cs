using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T07 — Kodlama, satır sonu ve kalan fixture senaryoları.
/// </summary>
/// <remarks>
/// <b>ÖLÇÜLDÜ:</b> <c>git diff</c> çıktısı <b>tek bir kodlamada değil</b> — başlıklar ve
/// işaretler ASCII, satır içerikleri ise <b>dosyanın kendi baytları</b>. git commit
/// mesajlarında yaptığı gibi bir çeviri yapmıyor (Faz 02'de <c>i18n.logOutputEncoding</c>
/// vardı; diff'te karşılığı yok). Çözüm GitExtensions'ın <c>PatchProcessor</c>'ından alındı:
/// çıktı kayıpsız okunup içerik ayrıca çözülüyor.
/// </remarks>
public class DiffEncodingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<DiffReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new DiffReader(new GitProcessRunner(executable));
    }

    private static CommitId Head(TestRepository repository) =>
        CommitId.Parse(repository.Git("rev-parse", "HEAD").Trim());

    private static void WriteBytes(TestRepository repository, string name, byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(repository.Path, name), bytes);

    [Fact]
    public async Task Latin5_dosya_dogru_kodlamayla_okunur()
    {
        Encoding latin5 = TextEncodings.TryGet("ISO-8859-9").ShouldNotBeNull();

        using TestRepository repository = TestRepository.CreateEmpty();
        WriteBytes(repository, "latin5.txt", latin5.GetBytes("Türkçe şğüöç\n"));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        WriteBytes(repository, "latin5.txt", latin5.GetBytes("Türkçe DEĞİŞTİ\n"));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        DiffHunk hunk = (await reader.ReadCommitAsync(
                repository.Path, Head(repository), new DiffOptions { ContentEncoding = latin5 }, Ct))
            .Single().Hunks.Single();

        hunk.Lines.Single(l => l.Kind == DiffLineKind.Removed).Content.ShouldBe("Türkçe şğüöç");
        hunk.Lines.Single(l => l.Kind == DiffLineKind.Added).Content.ShouldBe("Türkçe DEĞİŞTİ");
    }

    [Fact]
    public async Task Yanlis_kodlama_secilirse_icerik_bozulur_ama_YAPI_bozulmaz()
    {
        // Kodlama depo başına bir ayar; yanlış seçilirse metin bozulur. Kritik olan
        // AYRIŞTIRMANIN bozulmaması: satır sayıları, türleri ve dosya adı doğru kalmalı.
        Encoding latin5 = TextEncodings.TryGet("ISO-8859-9").ShouldNotBeNull();

        using TestRepository repository = TestRepository.CreateEmpty();
        WriteBytes(repository, "latin5.txt", latin5.GetBytes("şğüöç\n"));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        WriteBytes(repository, "latin5.txt", latin5.GetBytes("ĞÜÖÇİ\n"));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        FileDiff diff = (await reader.ReadCommitAsync(repository.Path, Head(repository), cancellationToken: Ct))
            .Single();

        diff.Path.Value.ShouldBe("latin5.txt");
        diff.Hunks.Single().Lines.Count(l => l.Kind == DiffLineKind.Removed).ShouldBe(1);
        diff.Hunks.Single().Lines.Count(l => l.Kind == DiffLineKind.Added).ShouldBe(1);
    }

    [Fact]
    public async Task UTF8_varsayilan_kodlamadir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("utf8.txt", "Türkçe şğüöçİ\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("utf8.txt", "Türkçe DEĞİŞTİ şğüöç\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        DiffHunk hunk = (await reader.ReadCommitAsync(repository.Path, Head(repository), cancellationToken: Ct))
            .Single().Hunks.Single();

        hunk.Lines.Single(l => l.Kind == DiffLineKind.Added).Content.ShouldBe("Türkçe DEĞİŞTİ şğüöç");
    }

    [Fact]
    public async Task ASCII_disi_dosya_adi_yol_olarak_dogru_okunur()
    {
        // Yollar her zaman UTF-8; içerik kodlaması yolları etkilememeli.
        Encoding latin5 = TextEncodings.TryGet("ISO-8859-9").ShouldNotBeNull();

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("baslangic.txt", "x\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("türkçe-şğüöçİ.txt", "içerik\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        FileDiff diff = (await reader.ReadCommitAsync(
                repository.Path, Head(repository), new DiffOptions { ContentEncoding = latin5 }, Ct))
            .Single();

        diff.Path.Value.ShouldBe("türkçe-şğüöçİ.txt");
    }

    [Fact]
    public async Task CRLF_satir_sonu_icerikte_KORUNUR()
    {
        // ÖLÇÜLDÜ: CRLF dosyada içerik satırı `\r` ile bitiyor. git'in gözünde satırın
        // içeriği `iki\r`, ayraç ise `\n`. Bu `\r` KORUNUYOR çünkü Faz 05'te yamayı
        // `git apply`'a geri vermek için birebir gerekiyor.
        // ⚠️ Arayüz göstermeden önce kırpmalı (P04-T09'a not).
        using TestRepository repository = TestRepository.CreateEmpty();
        WriteBytes(repository, "crlf.txt", "bir\r\niki\r\nuc\r\n"u8.ToArray());
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        WriteBytes(repository, "crlf.txt", "bir\r\nIKI\r\nuc\r\n"u8.ToArray());
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        DiffHunk hunk = (await reader.ReadCommitAsync(repository.Path, Head(repository), cancellationToken: Ct))
            .Single().Hunks.Single();

        hunk.Lines.Single(l => l.Kind == DiffLineKind.Removed).Content.ShouldBe("iki\r");
        hunk.Lines.Single(l => l.Kind == DiffLineKind.Added).Content.ShouldBe("IKI\r");

        // Bağlam satırları da aynı şekilde.
        hunk.Lines.Where(l => l.Kind == DiffLineKind.Context)
            .Select(l => l.Content).ShouldBe(["bir\r", "uc\r"]);
    }

    [Fact]
    public async Task LF_ve_CRLF_karisik_dosyada_satirlar_ayrilir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        WriteBytes(repository, "karisik.txt", "lf\ncrlf\r\nlf2\n"u8.ToArray());
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        WriteBytes(repository, "karisik.txt", "lf\ncrlf DEGISTI\r\nlf2\n"u8.ToArray());
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        DiffHunk hunk = (await reader.ReadCommitAsync(repository.Path, Head(repository), cancellationToken: Ct))
            .Single().Hunks.Single();

        hunk.Lines.Single(l => l.Kind == DiffLineKind.Added).Content.ShouldBe("crlf DEGISTI\r");

        // LF satırlarında `\r` OLMAMALI.
        hunk.Lines.Where(l => l.Kind == DiffLineKind.Context)
            .Select(l => l.Content).ShouldBe(["lf", "lf2"]);
    }

    [Fact]
    public async Task Yeniden_adlandirma_ve_degisiklik_birlikte_okunur()
    {
        using TestRepository repository = TestRepository.CreateEmpty();

        string original = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"satır {i}")) + "\n";

        repository.WriteFile("eski.txt", original);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.Git("mv", "eski.txt", "yeni.txt");
        repository.WriteFile("yeni.txt", original.Replace("satır 3", "satır ÜÇ", StringComparison.Ordinal));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "taşındı ve değişti");

        DiffReader reader = await CreateReaderAsync();

        FileDiff diff = (await reader.ReadCommitAsync(repository.Path, Head(repository), cancellationToken: Ct))
            .Single();

        diff.Change.ShouldBe(FileChangeKind.Renamed);
        diff.OldPath!.Value.Value.ShouldBe("eski.txt");
        diff.SimilarityScore.ShouldNotBeNull();
        diff.SimilarityScore!.Value.ShouldBeLessThan(100);

        // %100 rename'den farklı olarak burada hunk VAR.
        diff.HasHunks.ShouldBeTrue();
        diff.AddedLines.ShouldBe(1);
        diff.RemovedLines.ShouldBe(1);
    }

    [Fact]
    public async Task Submodule_degisikligi_ayirt_edilir()
    {
        using TestRepository inner = TestRepository.CreateWithSingleCommit();
        using TestRepository outer = TestRepository.CreateWithSingleCommit();

        outer.AddSubmodule(inner, "alt");
        outer.Git("commit", "-m", "submodule eklendi");

        // Alt depoda yeni bir commit → dış depoda gösterici değişir.
        inner.WriteFile("README.md", "# test\ndeğişti\n");
        inner.Git("add", "-A");
        inner.Git("commit", "-m", "alt değişiklik");

        outer.Git("-C", "alt", "fetch", "-q", "origin");
        outer.Git("-C", "alt", "checkout", "-q", inner.Git("rev-parse", "HEAD").Trim());
        outer.Git("add", "-A");
        outer.Git("commit", "-m", "submodule güncellendi");

        DiffReader reader = await CreateReaderAsync();

        FileDiff diff = (await reader.ReadCommitAsync(outer.Path, Head(outer), cancellationToken: Ct))
            .Single();

        diff.Path.Value.ShouldBe("alt");
        diff.IsSubmodule.ShouldBeTrue();
        diff.OldMode.ShouldBe("160000");
        diff.NewMode.ShouldBe("160000");
    }

    [Fact]
    public void Eski_kod_sayfalari_cozulebilir()
    {
        // ÖLÇÜLDÜ: .NET varsayılan olarak bunları KAYITLI tutmuyor; doğrudan
        // Encoding.GetEncoding("ISO-8859-9") istisna fırlatıyor. TextEncodings sağlayıcıyı
        // kaydediyor — kullanıcının Windows-1254 kodlamalı Türkçe dosyaları okunabilsin.
        TextEncodings.TryGet("ISO-8859-9").ShouldNotBeNull();
        TextEncodings.TryGet("windows-1254").ShouldNotBeNull();
        TextEncodings.TryGet("shift_jis").ShouldNotBeNull();
        TextEncodings.TryGet("utf-8").ShouldNotBeNull();
    }

    [Fact]
    public void Gecersiz_kodlama_adi_istisna_firlatmaz()
    {
        // Ayar dosyasından gelen bozuk bir ad yüzünden diff hiç gösterilmemeli.
        TextEncodings.TryGet("boyle-bir-kodlama-yok").ShouldBeNull();
        TextEncodings.TryGet(null).ShouldBeNull();
        TextEncodings.TryGet("   ").ShouldBeNull();
    }
}
