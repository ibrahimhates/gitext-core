using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T06 — Large and binary file protection.
/// </summary>
/// <remarks>
/// <b>MEASURED:</b> a 12.7 MB text file that changed entirely produces a <b>23 MB</b> patch and
/// git does it in 0.12 seconds — so the problem is not git, it is taking that output into memory
/// and creating hundreds of thousands of line objects (the per-object overhead was measured in Phase 03).
/// </remarks>
public class DiffSizeGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<DiffReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new DiffReader(new GitProcessRunner(executable));
    }

    private static CommitId Head(TestRepository repository) =>
        CommitId.Parse(repository.Git("rev-parse", "HEAD").Trim());

    /// <summary>A repository containing a file of <paramref name="lines"/> lines that changed entirely.</summary>
    private static TestRepository CreateWithLargeChange(int lines)
    {
        TestRepository repository = TestRepository.CreateEmpty();

        repository.WriteFile("buyuk.txt", Build(lines, "ilk"));
        repository.WriteFile("kucuk.txt", "tek satır\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("buyuk.txt", Build(lines, "ikinci"));
        repository.WriteFile("kucuk.txt", "tek satır değişti\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        return repository;

        static string Build(int count, string tag) =>
            string.Join('\n', Enumerable.Range(1, count).Select(i => $"{tag} satır {i}")) + "\n";
    }

    [Fact]
    public async Task Sinirlari_asan_dosyanin_icerigi_okunmaz_ama_listede_kalir()
    {
        using TestRepository repository = CreateWithLargeChange(500);

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path, Head(repository), new DiffOptions { MaximumChangedLines = 100 }, Ct);

        FileDiff big = diffs.Single(d => d.Path.Value == "buyuk.txt");
        FileDiff small = diffs.Single(d => d.Path.Value == "kucuk.txt");

        big.IsTooLarge.ShouldBeTrue();
        big.HasHunks.ShouldBeFalse();

        // A small file must not be affected — the guard is applied PER file.
        small.IsTooLarge.ShouldBeFalse();
        small.HasHunks.ShouldBeTrue();
    }

    [Fact]
    public async Task Icerik_okunmasa_da_satir_sayilari_DOGRU_kalir()
    {
        // The numbers come from --numstat and are obtained without generating content; showing
        // "+500 −500" in the file list does not require reading the patch.
        using TestRepository repository = CreateWithLargeChange(500);

        DiffReader reader = await CreateReaderAsync();

        FileDiff big = (await reader.ReadCommitAsync(
                repository.Path, Head(repository), new DiffOptions { MaximumChangedLines = 100 }, Ct))
            .Single(d => d.Path.Value == "buyuk.txt");

        big.AddedLines.ShouldBe(500);
        big.RemovedLines.ShouldBe(500);
        big.ChangedLines.ShouldBe(1000);
    }

    [Fact]
    public async Task Sinir_kapatilinca_icerik_yine_de_okunur()
    {
        // The "show anyway" in the UI uses this.
        using TestRepository repository = CreateWithLargeChange(500);

        DiffReader reader = await CreateReaderAsync();

        FileDiff big = (await reader.ReadCommitAsync(
                repository.Path, Head(repository), new DiffOptions { MaximumChangedLines = 0 }, Ct))
            .Single(d => d.Path.Value == "buyuk.txt");

        big.IsTooLarge.ShouldBeFalse();
        big.HasHunks.ShouldBeTrue();
        big.AddedLines.ShouldBe(500);
    }

    [Fact]
    public async Task Sinir_altindaki_dosya_etkilenmez()
    {
        using TestRepository repository = CreateWithLargeChange(50);

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path, Head(repository), new DiffOptions { MaximumChangedLines = 20_000 }, Ct);

        diffs.ShouldAllBe(d => !d.IsTooLarge);
        diffs.ShouldAllBe(d => d.HasHunks);
    }

    [Fact]
    public async Task Numstat_binary_dosyada_sayi_vermez()
    {
        // MEASURED: numstat gives `-` for a binary file. Treating that as 0 would mean "nothing changed";
        // it is left null instead, falling back to the value computed from the hunks.
        using TestRepository repository = TestRepository.CreateEmpty();
        File.WriteAllBytes(Path.Combine(repository.Path, "veri.bin"), [0, 1, 2, 3]);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        File.WriteAllBytes(Path.Combine(repository.Path, "veri.bin"), [9, 9, 9, 9, 9]);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        FileDiff diff = (await reader.ReadCommitAsync(repository.Path, Head(repository), cancellationToken: Ct))
            .Single();

        diff.IsBinary.ShouldBeTrue();
        diff.StatAdded.ShouldBeNull();
        diff.StatRemoved.ShouldBeNull();
        diff.IsTooLarge.ShouldBeFalse();
    }

    [Fact]
    public async Task Yeniden_adlandirmada_numstat_dogru_eslesir()
    {
        // MEASURED: on a rename numstat leaves the path EMPTY and gives the two paths as separate NUL
        // tokens (`0⇥0⇥` + old + new). A parser that does not read this would shift the line counts of
        // all subsequent files.
        using TestRepository repository = TestRepository.CreateEmpty();

        string content = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"satır {i}")) + "\n";

        repository.WriteFile("eski.txt", content);
        repository.WriteFile("digeri.txt", "bir\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.Git("mv", "eski.txt", "yeni.txt");
        repository.WriteFile("digeri.txt", "bir\niki\nüç\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path, Head(repository), cancellationToken: Ct);

        FileDiff renamed = diffs.Single(d => d.Change == FileChangeKind.Renamed);
        FileDiff other = diffs.Single(d => d.Path.Value == "digeri.txt");

        renamed.AddedLines.ShouldBe(0);
        renamed.RemovedLines.ShouldBe(0);

        // If there were a shift, this file would get the rename's numbers.
        other.AddedLines.ShouldBe(2);
        other.RemovedLines.ShouldBe(0);
    }

    [Fact]
    public async Task Cikti_siniri_asilinca_yarim_veri_ayristirilmaz()
    {
        // The last line of defence: parsing truncated output would mean silently showing an INCOMPLETE diff.
        //
        // 🔴 REGRESSION (Windows CI): the line count has to be large enough that what is left over
        // after the limit STILL DOES NOT FIT in the pipe's buffer. Reaching the limit stops the
        // reading and git blocks writing; if it is not killed right there, the wait for stderr never
        // ends and the command only returns at the 120-second timeout. With 2000 lines the leftover
        // fitted in Linux's 64 KB buffer, git finished anyway and the deadlock only appeared on
        // Windows (4 KB buffer). At this size it reproduces on every platform.
        const int lines = 20_000;

        using TestRepository repository = CreateWithLargeChange(lines);

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path,
            Head(repository),
            new DiffOptions { MaximumChangedLines = 0, MaximumOutputBytes = 4096 },
            Ct);

        // The file list must still arrive — the user has to see what changed.
        diffs.Select(d => d.Path.Value).ShouldBe(["buyuk.txt", "kucuk.txt"], ignoreOrder: true);

        // But there is no content and that is explicitly flagged.
        diffs.ShouldAllBe(d => !d.HasHunks);
        diffs.ShouldAllBe(d => d.IsTooLarge);

        // The line counts are still correct.
        diffs.Single(d => d.Path.Value == "buyuk.txt").AddedLines.ShouldBe(lines);
    }

    [Fact]
    public async Task Cikti_siniri_normal_diffi_etkilemez()
    {
        using TestRepository repository = CreateWithLargeChange(10);

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path,
            Head(repository),
            new DiffOptions { MaximumOutputBytes = 64L * 1024 * 1024 },
            Ct);

        diffs.ShouldAllBe(d => d.HasHunks);
        diffs.ShouldAllBe(d => !d.IsTooLarge);
    }
}
