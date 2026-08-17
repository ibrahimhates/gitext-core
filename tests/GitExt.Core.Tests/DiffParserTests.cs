using System.Text;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T02 — Unified diff parser, against <b>real <c>git</c> output</b> (ADR-0003).
/// </summary>
/// <remarks>
/// Using hand-written diff text would miss the fine details git actually produces
/// (the length not being written on a single-line hunk, the position of the newline marker, hunkless
/// diff kinds). Every scenario is produced in a real repository.
/// </remarks>
public class DiffParserTests
{
    /// <summary>
    /// Parses the diff between the last two commits.
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

        // MEASURED: a 100% similarity rename produces NO hunk at all.
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

        // MEASURED: the marker comes out separately after both the `-` and the `+` line.
        hunk.Lines.Single(l => l.Kind == DiffLineKind.Removed).EndsWithoutNewline.ShouldBeTrue();
        hunk.Lines.Single(l => l.Kind == DiffLineKind.Added).EndsWithoutNewline.ShouldBeTrue();
    }

    [Fact]
    public void Zor_yollar_dogru_okunur()
    {
        // The reason this test exists: the `diff --git a/… b/…` header cannot be parsed on these paths.
        // On a path with spaces there is no safe way to separate the two paths, and non-ASCII names are
        // quoted with octal escapes. That is why paths are read only from `--raw -z`.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("baslangic.txt", "x\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("bosluklu ad.txt", "a\n");
        repository.WriteFile("türkçe-şğüöçİ.txt", "b\n");

        List<string> expected = ["bosluklu ad.txt", "türkçe-şğüöçİ.txt"];

        // `>` is one of the characters Windows forbids in a file name, so the name that imitates the
        // rename arrow cannot be created there — the file system refuses it, not git. On the other
        // platforms it stays in: a parser reading paths from the `diff --git` header would take
        // " -> " for a rename marker, and that is exactly what this name catches.
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Path.Combine(repository.Path, "alt dizin"));
            repository.WriteFile(Path.Combine("alt dizin", "b -> c.txt"), "c\n");
            expected.Add("alt dizin/b -> c.txt");
        }

        repository.Git("add", "-A");
        repository.Git("commit", "-m", "zor yollar");

        IReadOnlyList<FileDiff> diffs = DiffOfHead(repository);

        diffs.Select(d => d.Path.Value).ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void Cok_dosyali_committe_hunklar_dogru_dosyaya_baglanir()
    {
        // The matching relies on order (verified on 700 real commits). A wrong match would mean showing
        // the user the changes of ANOTHER file.
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
        // MEASURED: git writes `@@ -1 +1 @@` on a single-line hunk — there is NO length.
        // Taking the default as 0 would shift the subsequent line numbers.
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
        // Stopping was preferred over silently producing wrong data.
        Should.Throw<DiffParseException>(() => DiffParser.Parse(":bozuk\0a.txt\0\0"));
    }

    [Fact]
    public void Yama_blogu_eksikse_hata_verilir()
    {
        // Attaching hunks to the wrong file is silent data corruption; if the counts do not match, stop.
        const string output =
            ":100644 100644 1111111 2222222 M\0a.txt\0"
            + ":100644 100644 3333333 4444444 M\0b.txt\0"
            + "\0diff --git a/a.txt b/a.txt\n@@ -1 +1 @@\n-x\n+y\n";

        Should.Throw<DiffParseException>(() => DiffParser.Parse(output));
    }
}
