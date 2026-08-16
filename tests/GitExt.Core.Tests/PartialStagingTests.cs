using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T04 / P05-T05 — Patch-based partial staging. <b>The riskiest code of the phase.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>Test approach (as the plan requires):</b> not the text of the patch but its <b>effect</b> is
/// verified — the patch is applied, then the index content is read with <c>git show :&lt;path&gt;</c>
/// and compared against the expected result.
/// </para>
/// <para>
/// <b>MEASURED — risk distribution:</b> count errors in the hunk header (<c>corrupt patch</c>) and
/// context mismatches (<c>patch failed</c>) are <b>rejected</b> by git. But a
/// <b>selection-logic bug</b> is not caught: since the patch is valid git accepts it and the wrong
/// content is silently staged. In the measurement, when an unselected <c>-</c> line was not turned
/// into context, that line disappeared from the index. Most of the tests below target that class.
/// </para>
/// </remarks>
public class PartialStagingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        StagingWriter Writer,
        DiffReader Reader,
        GitWriteQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        /// <summary>Working tree ↔ index diff (the source of partial staging).</summary>
        public Task<IReadOnlyList<FileDiff>> UnstagedAsync() =>
            Reader.ReadUnstagedAsync(Repository.Path, cancellationToken: Ct);

        /// <summary>Index ↔ HEAD diff (the source of partial unstaging).</summary>
        public Task<IReadOnlyList<FileDiff>> StagedAsync() =>
            Reader.ReadStagedAsync(Repository.Path, cancellationToken: Ct);

        /// <summary>The file content in the index.</summary>
        public string Indexed(string path) => Repository.Git("show", $":{path}");

        /// <summary>The file content in the working tree.</summary>
        public string OnDisk(string path) =>
            File.ReadAllText(Path.Combine(Repository.Path, path));
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(
            repository,
            new StagingWriter(new GitWriter(runner, queue), runner),
            new DiffReader(runner),
            queue);
    }

    /// <summary>Sets up a five-line file and changes it in two separate places.</summary>
    private static void SetupTwoChanges(Harness harness)
    {
        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\ndort\nbes\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        harness.Repository.WriteFile("a.txt", "bir\nIKI\nuc\nDORT\nbes\n");
    }

    /// <summary>Returns the indexes of lines of a given kind inside a hunk.</summary>
    private static PatchSelection SelectLines(FileDiff diff, int hunkIndex, params int[] lineIndexes) =>
        PatchSelection.Lines(lineIndexes.Select(line => (hunkIndex, line)));

    [Fact]
    public async Task Tek_hunk_stage_lenir_calisma_agaci_bozulmaz()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "bir\nIKI\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.Hunks(diff, 0), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\nIKI\n");

        // `--cached` does NOT touch the working tree.
        harness.OnDisk("a.txt").ShouldBe("bir\nIKI\n");
    }

    [Fact]
    public async Task SECILMEYEN_degisiklik_index_te_ESKI_haliyle_kalir()
    {
        // 🔴 This is the real risk: if an unselected `-` line is not turned into context it
        // DISAPPEARS from the index, and git does not catch that (the patch is valid).
        using Harness harness = await CreateAsync();
        SetupTwoChanges(harness);

        IReadOnlyList<FileDiff> diffs = await harness.UnstagedAsync();
        FileDiff diff = diffs.Single();
        DiffHunk hunk = diff.Hunks.Single();

        // Select only the second change (dort → DORT).
        int[] second = [.. Enumerable
            .Range(0, hunk.Lines.Count)
            .Where(i => hunk.Lines[i].Content is "dort" or "DORT")];

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, second), cancellationToken: Ct);

        // The "iki" line must stay UNCHANGED; "dort" must be updated.
        harness.Indexed("a.txt").ShouldBe("bir\niki\nuc\nDORT\nbes\n");
    }

    [Fact]
    public async Task Tek_bir_satir_stage_lenebilir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        // All three lines are added; only the middle one will be staged.
        harness.Repository.WriteFile("a.txt", "bir\nA\niki\nB\nuc\nC\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        DiffHunk hunk = diff.Hunks.Single();

        int bIndex = Enumerable.Range(0, hunk.Lines.Count).Single(i => hunk.Lines[i].Content == "B");

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, bIndex), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\niki\nB\nuc\n");
    }

    [Fact]
    public async Task Dosyanin_ILK_satiri_stage_lenebilir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "BIR\niki\nUC\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        DiffHunk hunk = diff.Hunks.Single();

        int[] first = [.. Enumerable
            .Range(0, hunk.Lines.Count)
            .Where(i => hunk.Lines[i].Content is "bir" or "BIR")];

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, first), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("BIR\niki\nuc\n");
    }

    [Fact]
    public async Task Dosyanin_SON_satiri_stage_lenebilir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "BIR\niki\nUC\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        DiffHunk hunk = diff.Hunks.Single();

        int[] last = [.. Enumerable
            .Range(0, hunk.Lines.Count)
            .Where(i => hunk.Lines[i].Content is "uc" or "UC")];

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, last), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\niki\nUC\n");
    }

    [Fact]
    public async Task Bitisik_olmayan_iki_hunk_tan_YALNIZCA_biri_stage_lenir()
    {
        using Harness harness = await CreateAsync();

        // Plenty of context is left between the hunks so that they stay separate.
        string original = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"satir{i}")) + "\n";
        harness.Repository.WriteFile("a.txt", original);
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        string[] lines = original.Split('\n');
        lines[0] = "BAS";
        lines[35] = "SON";
        harness.Repository.WriteFile("a.txt", string.Join('\n', lines));

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        diff.Hunks.Count.ShouldBeGreaterThan(1);

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.Hunks(diff, 1), cancellationToken: Ct);

        string indexed = harness.Indexed("a.txt");

        // The second hunk was applied, the first was not.
        indexed.ShouldContain("SON");
        indexed.ShouldNotContain("BAS");
        indexed.ShouldContain("satir1\n");
    }

    [Fact]
    public async Task Iki_hunk_birlikte_stage_lenir()
    {
        using Harness harness = await CreateAsync();

        string original = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"satir{i}")) + "\n";
        harness.Repository.WriteFile("a.txt", original);
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        string[] lines = original.Split('\n');
        lines[0] = "BAS";
        lines[35] = "SON";
        harness.Repository.WriteFile("a.txt", string.Join('\n', lines));

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        // The NEW start in the second hunk's header shifts by the first hunk's line delta.
        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe(harness.OnDisk("a.txt"));
    }

    [Fact]
    public async Task Dosya_sonunda_NEWLINE_YOKKEN_calisir()
    {
        // The marker line (`\ No newline at end of file`) can appear on both the old and the new
        // side (measured in P04-T01); if it is put in the wrong place in the patch git rejects it.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "bir\nIKI");

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\nIKI");
    }

    [Fact]
    public async Task CRLF_satir_sonlari_korunur()
    {
        using Harness harness = await CreateAsync();

        // ⚠️ `WriteFile` pins line endings to LF (a fixture decision); the CRLF test is forced to
        // write raw bytes, otherwise the test would never actually exercise CRLF.
        string path = Path.Combine(harness.Repository.Path, "a.txt");

        File.WriteAllBytes(path, "bir\r\niki\r\n"u8.ToArray());
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        File.WriteAllBytes(path, "bir\r\nIKI\r\n"u8.ToArray());

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\r\nIKI\r\n");
    }

    [Fact]
    public async Task eol_crlf_NORMALLESTIRMESI_aktifken_kismi_stage_calisir()
    {
        // Open question number 4 of the plan (measured in P05-T17). The test above exercises CRLF
        // while it is stored VERBATIM in the repository; the genuinely risky case here is:
        // `.gitattributes` normalises, that is, the bytes in the working tree (CRLF) are NOT THE
        // SAME as the ones in the index (LF). The patch is produced from `git diff` and that output
        // arrives normalised — if it were compared against the worktree bytes every line would
        // mismatch.
        using Harness harness = await CreateAsync();
        string path = Path.Combine(harness.Repository.Path, "a.txt");

        harness.Repository.WriteFile(".gitattributes", "* text=auto eol=crlf\n");
        File.WriteAllBytes(path, "bir\r\niki\r\nuc\r\n"u8.ToArray());
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        File.WriteAllBytes(path, "bir\r\nIKI\r\nuc\r\n"u8.ToArray());

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct);

        // The index holds the normalised form (LF) — if CRLF got in, the file would keep looking
        // "modified" to git and the user could not clear their staged state.
        harness.Indexed("a.txt").ShouldBe("bir\nIKI\nuc\n");

        // The working tree must NOT be touched: `eol=crlf` promises CRLF there.
        File.ReadAllBytes(path).ShouldBe("bir\r\nIKI\r\nuc\r\n"u8.ToArray());
    }

    [Fact]
    public async Task ASCII_disi_yol_ve_icerik_calisir()
    {
        // The path is passed unquoted (measured: `git apply` accepts raw UTF-8).
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("türkçe dosya.txt", "ilk satır\nikinci\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("türkçe dosya.txt", "ilk satır\nİKİNCİ ÖĞE\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct);

        harness.Indexed("türkçe dosya.txt").ShouldBe("ilk satır\nİKİNCİ ÖĞE\n");
    }

    [Fact]
    public async Task Yalnizca_SILINEN_satir_stage_lenir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "bir\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        DiffHunk hunk = diff.Hunks.Single();

        int ikiIndex = Enumerable.Range(0, hunk.Lines.Count).Single(i => hunk.Lines[i].Content == "iki");

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, ikiIndex), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\nuc\n");
    }

    // ---- Partial unstage (reverse direction) ----

    [Fact]
    public async Task Kismi_UNSTAGE_secilen_satiri_geri_alir()
    {
        // In the reverse direction the rules CHANGE symmetrically: an unselected `+` is turned
        // into context, an unselected `-` is skipped. Mixing them up silently corrupts the index.
        using Harness harness = await CreateAsync();
        SetupTwoChanges(harness);

        harness.Repository.Git("add", "-A");
        harness.Indexed("a.txt").ShouldBe("bir\nIKI\nuc\nDORT\nbes\n");

        FileDiff staged = (await harness.StagedAsync()).Single();
        DiffHunk hunk = staged.Hunks.Single();

        int[] firstChange = [.. Enumerable
            .Range(0, hunk.Lines.Count)
            .Where(i => hunk.Lines[i].Content is "iki" or "IKI")];

        await harness.Writer.UnstagePartialAsync(
            harness.Repository.Path, staged, SelectLines(staged, 0, firstChange), cancellationToken: Ct);

        // The first change was reverted, the second stayed in the index.
        harness.Indexed("a.txt").ShouldBe("bir\niki\nuc\nDORT\nbes\n");

        // The working tree must stay untouched.
        harness.OnDisk("a.txt").ShouldBe("bir\nIKI\nuc\nDORT\nbes\n");
    }

    [Fact]
    public async Task Bos_secim_HICBIR_SEY_yapmaz()
    {
        using Harness harness = await CreateAsync();
        SetupTwoChanges(harness);

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.Lines([]), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\niki\nuc\ndort\nbes\n");
    }

    [Fact]
    public async Task Uretilen_yama_gitin_kendi_dogrulamasindan_gecer()
    {
        // The safety net git gives us: a corrupt patch is rejected. This test verifies that the
        // net is really in place (and has not been disabled with `--recount`).
        using Harness harness = await CreateAsync();
        SetupTwoChanges(harness);

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        string patch = PatchBuilder.Build(diff, PatchSelection.All(diff), PatchDirection.Stage)
            .ShouldNotBeNull();

        string patchFile = Path.Combine(harness.Repository.Path, "..", "uretilen.patch");
        File.WriteAllText(patchFile, patch);

        try
        {
            // `--check` verifies without applying.
            harness.Repository.Git("apply", "--cached", "--check", patchFile);
        }
        finally
        {
            File.Delete(patchFile);
        }
    }

    // ---- P05-T16: encoding end to end ----

    [Fact]
    public async Task Latin5_dosyada_kismi_stage_KODLAMA_GECILINCE_calisir()
    {
        // 🔴 Measured on a real repository in P05-T16. The chain: the diff is READ with the
        // encoding → the patch is produced → it is WRITTEN with the same encoding. When one link
        // breaks, `git apply` rejects the patch.
        using Harness harness = await CreateAsync();
        System.Text.Encoding latin5 = TextEncodings.TryGet("ISO-8859-9").ShouldNotBeNull();

        await File.WriteAllBytesAsync(
            Path.Combine(harness.Repository.Path, "tr.txt"),
            latin5.GetBytes("Türkçe birinci\nikinci satır\n"),
            Ct);

        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        await File.WriteAllBytesAsync(
            Path.Combine(harness.Repository.Path, "tr.txt"),
            latin5.GetBytes("Türkçe birinci\nikinci satır\nÜÇÜNCÜ satır\n"),
            Ct);

        FileDiff diff = (await harness.Reader.ReadUnstagedAsync(
                harness.Repository.Path,
                new DiffOptions { ContentEncoding = latin5 },
                Ct))
            .Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), latin5, Ct);

        // The bytes that land in the index must be Latin-5 — a string comparison would not show
        // this.
        byte[] staged = System.Text.Encoding.Latin1.GetBytes(
            harness.Repository.GitLossless("show", ":tr.txt"));

        staged.ShouldBe(latin5.GetBytes("Türkçe birinci\nikinci satır\nÜÇÜNCÜ satır\n"));
    }

    [Fact]
    public async Task Kodlama_gecilmezse_git_yamayi_REDDEDER_sessizce_bozmaz()
    {
        // 🔑 This is the real guarantee: with the wrong encoding the wrong content is not staged,
        // the operation FAILS. This is the payoff of the "do not use `--recount`" decision from
        // P05-T04 — because git's verification is left on, the error stays visible.
        using Harness harness = await CreateAsync();
        System.Text.Encoding latin5 = TextEncodings.TryGet("ISO-8859-9").ShouldNotBeNull();

        await File.WriteAllBytesAsync(
            Path.Combine(harness.Repository.Path, "tr.txt"),
            latin5.GetBytes("Türkçe birinci\nikinci\n"),
            Ct);

        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        await File.WriteAllBytesAsync(
            Path.Combine(harness.Repository.Path, "tr.txt"),
            latin5.GetBytes("Türkçe birinci\nikinci\nÜÇÜNCÜ\n"),
            Ct);

        // The encoding is NOT passed: the UTF-8 default cannot decode Latin-5 bytes.
        FileDiff diff = (await harness.Reader.ReadUnstagedAsync(
                harness.Repository.Path, cancellationToken: Ct))
            .Single();

        await Should.ThrowAsync<GitException>(() =>
            harness.Writer.StagePartialAsync(
                harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct));

        // The index must be undamaged.
        harness.Repository.Git("diff", "--cached", "--stat").Trim().ShouldBeEmpty();
    }
}
