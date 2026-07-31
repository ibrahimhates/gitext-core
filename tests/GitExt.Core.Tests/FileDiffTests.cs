using GitExt.Core.Model;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T01 — Diff domain modeli.
/// </summary>
/// <remarks>
/// Model saf veridir; buradaki testler türetilmiş özelliklerin <b>ölçülen git davranışını</b>
/// doğru yansıttığını sabitler. Ayrıştırma testleri ayrı (P04-T07).
/// </remarks>
public class FileDiffTests
{
    private static FileDiff Diff(
        string path = "a.txt",
        FileChangeKind change = FileChangeKind.Modified,
        string oldMode = "100644",
        string newMode = "100644",
        string? oldBlob = null,
        string? newBlob = null,
        IReadOnlyList<DiffHunk>? hunks = null) =>
        new()
        {
            Path = RepositoryPath.Parse(path),
            Change = change,
            OldMode = oldMode,
            NewMode = newMode,
            OldBlob = oldBlob is null ? default : CommitId.Parse(oldBlob),
            NewBlob = newBlob is null ? default : CommitId.Parse(newBlob),
            Hunks = hunks ?? [],
        };

    private static DiffHunk Hunk(params DiffLine[] lines) =>
        new()
        {
            Header = "@@ -1,1 +1,1 @@",
            OldStart = 1,
            OldLength = 1,
            NewStart = 1,
            NewLength = 1,
            Lines = lines,
        };

    [Fact]
    public void Hunksiz_diff_gecerlidir()
    {
        // ÖLÇÜLDÜ: %100 rename, yalnızca mod değişikliği, boş yeni dosya ve binary
        // dosyalarda git HİÇ hunk üretmiyor. Her dosyanın hunk'ı olduğunu varsayan kod
        // gerçek depolarda kırılır.
        FileDiff diff = Diff();

        diff.HasHunks.ShouldBeFalse();
        diff.AddedLines.ShouldBe(0);
        diff.RemovedLines.ShouldBe(0);
    }

    [Fact]
    public void Yalnizca_mod_degisimi_blob_esitliginden_anlasilir()
    {
        // ÖLÇÜLDÜ: `git diff --raw` bu durumda iki blob kimliğini de AYNI veriyor
        // (`:100644 100755 9405325 9405325 M`), durum harfi yine M.
        const string blob = "9405325aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        FileDiff modeOnly = Diff(oldMode: "100644", newMode: "100755", oldBlob: blob, newBlob: blob);

        modeOnly.IsModeOnlyChange.ShouldBeTrue();
        modeOnly.IsExecutableChanged.ShouldBeTrue();
    }

    [Fact]
    public void Icerik_degistiyse_mod_degisimi_sayilmaz()
    {
        FileDiff diff = Diff(
            oldMode: "100644",
            newMode: "100755",
            oldBlob: "9405325aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            newBlob: "24f0fe5bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        diff.IsModeOnlyChange.ShouldBeFalse();
        diff.IsExecutableChanged.ShouldBeTrue();
    }

    [Fact]
    public void Blob_bilgisi_yoksa_mod_degisimi_iddia_edilmez()
    {
        // Blob kimlikleri okunamadıysa (ör. yalnızca unified diff'ten üretilmiş bir model)
        // "yalnızca mod değişti" demek uydurma olurdu.
        FileDiff diff = Diff(oldMode: "100644", newMode: "100755");

        diff.IsModeOnlyChange.ShouldBeFalse();
    }

    [Theory]
    [InlineData("160000", true, false)]
    [InlineData("120000", false, true)]
    [InlineData("100644", false, false)]
    public void Ozel_modlar_taninir(string mode, bool submodule, bool symlink)
    {
        FileDiff diff = Diff(oldMode: mode, newMode: mode);

        diff.IsSubmodule.ShouldBe(submodule);
        diff.IsSymlink.ShouldBe(symlink);
    }

    [Fact]
    public void Yeniden_adlandirmada_iki_yol_da_tasinir()
    {
        FileDiff diff = Diff(change: FileChangeKind.Renamed) with
        {
            Path = RepositoryPath.Parse("yeni-ad.txt"),
            OldPath = RepositoryPath.Parse("eski-ad.txt"),
            SimilarityScore = 100,
        };

        diff.OldPath!.Value.Value.ShouldBe("eski-ad.txt");
        diff.Path.Value.ShouldBe("yeni-ad.txt");
        diff.ToString().ShouldContain("→");
    }

    [Fact]
    public void Satir_sayilari_hunklardan_toplanir()
    {
        FileDiff diff = Diff(hunks:
        [
            Hunk(
                new DiffLine(DiffLineKind.Context, "bir"),
                new DiffLine(DiffLineKind.Removed, "iki"),
                new DiffLine(DiffLineKind.Added, "iki DEGISTI")),
            Hunk(
                new DiffLine(DiffLineKind.Added, "uc"),
                new DiffLine(DiffLineKind.Added, "dort")),
        ]);

        diff.AddedLines.ShouldBe(3);
        diff.RemovedLines.ShouldBe(1);
    }

    [Fact]
    public void Satir_sonu_isareti_satira_bagli_bir_niteliktir()
    {
        // ÖLÇÜLDÜ: `\ No newline at end of file` kendi başına bir satır değil; kendinden
        // ÖNCEKİ satıra ait ve aynı hunk'ta hem `-` hem `+` satırından sonra çıkabiliyor.
        // Ayrı bir satır türü olsaydı yamayı birebir geri üretmek mümkün olmazdı.
        DiffHunk hunk = Hunk(
            new DiffLine(DiffLineKind.Removed, "eski") { EndsWithoutNewline = true },
            new DiffLine(DiffLineKind.Added, "yeni") { EndsWithoutNewline = true });

        hunk.Lines.ShouldAllBe(l => l.EndsWithoutNewline);
        hunk.AddedCount.ShouldBe(1);
        hunk.RemovedCount.ShouldBe(1);
    }

    [Fact]
    public void Satir_numaralari_ture_gore_bos_kalabilir()
    {
        DiffLine added = new(DiffLineKind.Added, "yeni") { NewLineNumber = 5 };
        DiffLine removed = new(DiffLineKind.Removed, "eski") { OldLineNumber = 5 };
        DiffLine context = new(DiffLineKind.Context, "aynı") { OldLineNumber = 4, NewLineNumber = 4 };

        added.OldLineNumber.ShouldBeNull();
        removed.NewLineNumber.ShouldBeNull();
        context.OldLineNumber.ShouldBe(4);
        context.NewLineNumber.ShouldBe(4);
    }

    [Fact]
    public void Satir_icerigi_isaret_karakteri_TASIMAZ()
    {
        // İçeriğe +/- gömmek, kopyalama ve kelime seviyesi diff'te (P04-T05) her yerde
        // ayıklama gerektirirdi.
        DiffLine line = new(DiffLineKind.Added, "kod satırı");

        line.Content.ShouldBe("kod satırı");
        line.ToString().ShouldBe("+kod satırı");
    }

    [Fact]
    public void Hunk_ham_basligini_saklar()
    {
        // Faz 05'te değiştirilmiş yama `git apply`'a geri verilecek; ham başlık olmadan
        // git'in biçimindeki ince ayrıntıları (tek satırlık hunk'ta uzunluğun yazılmaması
        // gibi) taklit etmek gerekirdi.
        DiffHunk hunk = new()
        {
            Header = "@@ -12,7 +12,9 @@ void Main()",
            OldStart = 12,
            OldLength = 7,
            NewStart = 12,
            NewLength = 9,
            Section = "void Main()",
            Lines = [],
        };

        hunk.Header.ShouldStartWith("@@");
        hunk.ToString().ShouldBe(hunk.Header);
    }
}
