using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T05 — Intra-line (word/character level) diff.
/// </summary>
/// <remarks>
/// <para>
/// <b>git's <c>--word-diff</c> is not used.</b> The plan suggested starting with it, but measurement
/// showed it is not correct: (1) with the default word separator a phantom space is added to the end
/// of the old line, (2) even with a character separator, <b>an added/removed blank line yields only a
/// bare <c>~</c></b> and which side the line belongs to is nowhere in the output — in a real
/// repository that put 5,701 lines on the wrong side across 150 commits.
/// </para>
/// <para>
/// The segments are therefore computed locally with <see cref="InlineDiff"/>, over the <b>exact</b>
/// line texts the parser produces.
/// </para>
/// </remarks>
public class WordDiffTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<DiffReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new DiffReader(new GitProcessRunner(executable));
    }

    private static CommitId Head(TestRepository repository) =>
        CommitId.Parse(repository.Git("rev-parse", "HEAD").Trim());

    private static TestRepository CreateWith(string before, string after)
    {
        TestRepository repository = TestRepository.CreateEmpty();

        repository.WriteFile("kod.cs", before);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("kod.cs", after);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        return repository;
    }

    private static readonly DiffOptions _word = new() { WordLevel = true };

    [Fact]
    public async Task Satir_ici_degisiklik_parcalara_ayrilir()
    {
        using TestRepository repository = CreateWith(
            "public void Merhaba(int sayi)\n",
            "public void MerhabaDunya(int sayi)\n");

        DiffReader reader = await CreateReaderAsync();

        DiffHunk hunk = (await reader.ReadCommitAsync(repository.Path, Head(repository), _word, Ct))
            .Single().Hunks.Single();

        DiffLine added = hunk.Lines.Single(l => l.Kind == DiffLineKind.Added);

        added.HasSegments.ShouldBeTrue();

        // Only the added part should be marked; the rest of the line is context.
        added.Segments.Where(s => s.Kind == DiffLineKind.Added)
            .Select(s => s.Text)
            .ShouldBe(["Dunya"]);

        added.Segments.ShouldNotContain(s => s.Kind == DiffLineKind.Removed);
    }

    [Fact]
    public async Task Silinen_satirda_yalnizca_silinen_parca_isaretlenir()
    {
        using TestRepository repository = CreateWith(
            "public void MerhabaDunya(int sayi)\n",
            "public void Merhaba(int sayi)\n");

        DiffReader reader = await CreateReaderAsync();

        DiffHunk hunk = (await reader.ReadCommitAsync(repository.Path, Head(repository), _word, Ct))
            .Single().Hunks.Single();

        DiffLine removed = hunk.Lines.Single(l => l.Kind == DiffLineKind.Removed);

        removed.Segments.Where(s => s.Kind == DiffLineKind.Removed)
            .Select(s => s.Text)
            .ShouldBe(["Dunya"]);

        removed.Segments.ShouldNotContain(s => s.Kind == DiffLineKind.Added);
    }

    [Fact]
    public async Task Satir_icerigi_normal_diffle_BIREBIR_ayni()
    {
        // The segments MUST NOT CHANGE the line text. git's --word-diff failed at exactly this point
        // (blank lines landing on the wrong side, a phantom space added to the old line); because the
        // local computation works over the exact lines, that risk does not exist.
        using TestRepository repository = CreateWith(
            "bir  iki\tuc\nsonda bosluk   \n\nson satir\n",
            "bir  IKI\tuc\nsonda bosluk   \n\nson satir DEGISTI\n");

        DiffReader reader = await CreateReaderAsync();
        CommitId head = Head(repository);

        IReadOnlyList<DiffLine> normal =
            [.. (await reader.ReadCommitAsync(repository.Path, head, cancellationToken: Ct))
                .Single().Hunks.SelectMany(h => h.Lines)];

        IReadOnlyList<DiffLine> word =
            [.. (await reader.ReadCommitAsync(repository.Path, head, _word, Ct))
                .Single().Hunks.SelectMany(h => h.Lines)];

        word.Select(l => (l.Kind, l.Content, l.OldLineNumber, l.NewLineNumber))
            .ShouldBe(normal.Select(l => (l.Kind, l.Content, l.OldLineNumber, l.NewLineNumber)));
    }

    [Fact]
    public async Task Parcalar_birlestirilince_satir_icerigi_elde_edilir()
    {
        using TestRepository repository = CreateWith(
            "alpha beta gamma\n",
            "alpha DELTA gamma\n");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<DiffLine> lines =
            [.. (await reader.ReadCommitAsync(repository.Path, Head(repository), _word, Ct))
                .Single().Hunks.SelectMany(h => h.Lines)];

        foreach (DiffLine line in lines)
        {
            string joined = string.Concat(line.Segments.Select(s => s.Text));

            joined.ShouldBe(line.Content);
        }
    }

    [Fact]
    public async Task Degismemis_satirlarda_da_parcalar_bulunur()
    {
        using TestRepository repository = CreateWith(
            "bir\niki\nuc\n",
            "bir\nIKI\nuc\n");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<DiffLine> lines =
            [.. (await reader.ReadCommitAsync(repository.Path, Head(repository), _word, Ct))
                .Single().Hunks.SelectMany(h => h.Lines)];

        DiffLine context = lines.First(l => l.Kind == DiffLineKind.Context);

        context.Segments.ShouldAllBe(s => s.Kind == DiffLineKind.Context);
        context.Content.ShouldBe("bir");
    }

    [Fact]
    public async Task Kelime_diffi_istenmezse_parcalar_bos_kalir()
    {
        // The default path must not change: computing segments means an extra git run and extra work.
        using TestRepository repository = CreateWith("bir\n", "iki\n");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<DiffLine> lines =
            [.. (await reader.ReadCommitAsync(repository.Path, Head(repository), cancellationToken: Ct))
                .Single().Hunks.SelectMany(h => h.Lines)];

        lines.ShouldAllBe(l => !l.HasSegments);
    }

    [Fact]
    public async Task Kelime_diffinde_dosya_bilgisi_korunur()
    {
        // Word diff replaces the patch section; the raw section (path, kind, mode) must be unaffected.
        using TestRepository repository = TestRepository.CreateEmpty();
        // The file has to be long enough: in a short file a one-word change drops the similarity below
        // 50% and hides the rename.
        string original = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"satır {i}")) + "\n";

        repository.WriteFile("eski.txt", original);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.Git("mv", "eski.txt", "yeni.txt");
        repository.WriteFile("yeni.txt", original.Replace("satır 5", "satır BEŞ", StringComparison.Ordinal));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "taşındı ve değişti");

        DiffReader reader = await CreateReaderAsync();

        FileDiff diff = (await reader.ReadCommitAsync(repository.Path, Head(repository), _word, Ct))
            .Single();

        diff.Change.ShouldBe(FileChangeKind.Renamed);
        diff.OldPath!.Value.Value.ShouldBe("eski.txt");
        diff.Path.Value.ShouldBe("yeni.txt");
        diff.Hunks.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Cok_satirli_degisiklikte_satir_numaralari_dogru_ilerler()
    {
        using TestRepository repository = CreateWith(
            "bir\niki\nuc\ndort\nbes\n",
            "bir\nIKI\nuc\nDORT\nbes\n");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<DiffLine> lines =
            [.. (await reader.ReadCommitAsync(repository.Path, Head(repository), _word, Ct))
                .Single().Hunks.SelectMany(h => h.Lines)];

        lines.Where(l => l.Kind == DiffLineKind.Removed)
            .Select(l => l.OldLineNumber)
            .ShouldBe([2, 4]);

        lines.Where(l => l.Kind == DiffLineKind.Added)
            .Select(l => l.NewLineNumber)
            .ShouldBe([2, 4]);
    }

    [Fact]
    public async Task Farkli_sayida_satirda_DOGRU_satirlar_eslesir()
    {
        // Positional matching (i-th ↔ i-th) would be WRONG here: with two lines removed and three
        // added, the pairs that should match are shifted. The approach anchoring on the pair that
        // shares the most words was adapted from GitExtensions' LinesMatcher.
        using TestRepository repository = CreateWith(
            "alpha bir\nbeta iki\n",
            "YEPYENI SATIR\nalpha BIR\nbeta IKI\n");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<DiffLine> lines =
            [.. (await reader.ReadCommitAsync(repository.Path, Head(repository), _word, Ct))
                .Single().Hunks.SelectMany(h => h.Lines)];

        DiffLine alphaAdded = lines.Single(l => l.Kind == DiffLineKind.Added && l.Content.StartsWith("alpha", StringComparison.Ordinal));

        // Because "alpha" is shared it must be a context segment; only "bir"→"BIR" should change.
        alphaAdded.Segments.ShouldContain(s => s.Kind == DiffLineKind.Context && s.Text.Contains("alpha", StringComparison.Ordinal));
        alphaAdded.Segments.Where(s => s.Kind == DiffLineKind.Added).Select(s => s.Text).ShouldBe(["BIR"]);

        // An unmatched new line must be left without segments — an invented match would highlight the
        // wrong place.
        lines.Single(l => l.Content == "YEPYENI SATIR").HasSegments.ShouldBeFalse();
    }

    [Fact]
    public void Cok_uzun_satirda_satir_ici_hesaplanmaz()
    {
        // In single-line giants such as minified JS, highlighting is unreadable and computing it is wasted.
        string longOld = new('a', InlineDiff.MaximumLineLength + 1);
        string longNew = new('b', InlineDiff.MaximumLineLength + 1);

        (IReadOnlyList<DiffSegment> old, IReadOnlyList<DiffSegment> updated) =
            InlineDiff.Compute(longOld, longNew);

        old.Single().Kind.ShouldBe(DiffLineKind.Removed);
        updated.Single().Kind.ShouldBe(DiffLineKind.Added);
    }

    [Fact]
    public void Parcalar_ayni_turde_birlestirilir()
    {
        // Were every token its own segment, there would be hundreds of needless drawing elements in the UI.
        (IReadOnlyList<DiffSegment> old, IReadOnlyList<DiffSegment> updated) =
            InlineDiff.Compute("bir iki uc", "bir DORT BES uc");

        old.ShouldNotContain(s => s.Kind == DiffLineKind.Added);
        updated.ShouldNotContain(s => s.Kind == DiffLineKind.Removed);

        // No two consecutive segments of the same kind should remain.
        for (int i = 1; i < updated.Count; i++)
        {
            updated[i].Kind.ShouldNotBe(updated[i - 1].Kind);
        }

        string.Concat(old.Select(s => s.Text)).ShouldBe("bir iki uc");
        string.Concat(updated.Select(s => s.Text)).ShouldBe("bir DORT BES uc");
    }
}
