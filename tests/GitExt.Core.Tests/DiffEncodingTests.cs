using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T07 — Encoding, line endings and the remaining fixture scenarios.
/// </summary>
/// <remarks>
/// <b>MEASURED:</b> <c>git diff</c> output is <b>not in a single encoding</b> — the headers and
/// markers are ASCII, while the line contents are <b>the file's own bytes</b>. It does not do a
/// conversion the way it does for commit messages (in Phase 02 there was <c>i18n.logOutputEncoding</c>;
/// there is no equivalent for diff). The solution was taken from GitExtensions' <c>PatchProcessor</c>:
/// the output is read losslessly and the content is decoded separately.
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
        // The encoding is a per-repository setting; if it is chosen wrong the text is corrupted. What is
        // critical is that the PARSING is not corrupted: line counts, kinds and the file name must stay correct.
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
        // Paths are always UTF-8; the content encoding must not affect the paths.
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
        // MEASURED: in a CRLF file the content line ends with `\r`. In git's eyes the line's content
        // is `iki\r` and the separator is `\n`. This `\r` is PRESERVED because in Phase 05 it is needed
        // byte-for-byte in order to hand the patch back to `git apply`.
        // ⚠️ The UI must trim it before display (a note for P04-T09).
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

        // The same goes for context lines.
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

        // There must be NO `\r` on LF lines.
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

        // Unlike a 100% rename, here there IS a hunk.
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

        // A new commit in the submodule → the pointer changes in the outer repository.
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
        // MEASURED: .NET does not keep these REGISTERED by default; calling
        // Encoding.GetEncoding("ISO-8859-9") directly throws. TextEncodings registers the provider —
        // so that the user's Turkish files in Windows-1254 encoding can be read.
        TextEncodings.TryGet("ISO-8859-9").ShouldNotBeNull();
        TextEncodings.TryGet("windows-1254").ShouldNotBeNull();
        TextEncodings.TryGet("shift_jis").ShouldNotBeNull();
        TextEncodings.TryGet("utf-8").ShouldNotBeNull();
    }

    [Fact]
    public void Gecersiz_kodlama_adi_istisna_firlatmaz()
    {
        // A diff must not go entirely unshown because of a bad name coming from the settings file.
        TextEncodings.TryGet("boyle-bir-kodlama-yok").ShouldBeNull();
        TextEncodings.TryGet(null).ShouldBeNull();
        TextEncodings.TryGet("   ").ShouldBeNull();
    }
}
