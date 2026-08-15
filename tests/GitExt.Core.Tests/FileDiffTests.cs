using GitExt.Core.Model;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T01 — Diff domain model.
/// </summary>
/// <remarks>
/// The model is pure data; the tests here pin down that the derived properties correctly reflect the
/// <b>measured git behaviour</b>. The parsing tests are separate (P04-T07).
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
        // MEASURED: on a 100% rename, a mode-only change, an empty new file and binary files git produces
        // NO hunks at all. Code that assumes every file has a hunk breaks on real
        // repositories.
        FileDiff diff = Diff();

        diff.HasHunks.ShouldBeFalse();
        diff.AddedLines.ShouldBe(0);
        diff.RemovedLines.ShouldBe(0);
    }

    [Fact]
    public void Yalnizca_mod_degisimi_blob_esitliginden_anlasilir()
    {
        // MEASURED: `git diff --raw` gives BOTH blob ids as the SAME in this case
        // (`:100644 100755 9405325 9405325 M`), and the status letter is still M.
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
        // If the blob ids could not be read (e.g. a model produced only from a unified diff),
        // saying "only the mode changed" would be making things up.
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
        // MEASURED: `\ No newline at end of file` is not a line of its own; it belongs to the line
        // BEFORE it and can appear after both the `-` and the `+` line in the same hunk.
        // Had it been a separate line kind, reproducing the patch byte-for-byte would be impossible.
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
        // Embedding +/- into the content would require stripping it everywhere in copying and in
        // word-level diff (P04-T05).
        DiffLine line = new(DiffLineKind.Added, "kod satırı");

        line.Content.ShouldBe("kod satırı");
        line.ToString().ShouldBe("+kod satırı");
    }

    [Fact]
    public void Hunk_ham_basligini_saklar()
    {
        // In Phase 05 the modified patch will be handed back to `git apply`; without the raw header we
        // would have to imitate the fine details of git's format (such as the length not being written
        // on a single-line hunk).
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
