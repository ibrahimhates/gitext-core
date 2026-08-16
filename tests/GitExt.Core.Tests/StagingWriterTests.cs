using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T03 — File-level stage / unstage.
/// </summary>
/// <remarks>
/// <para>
/// The application's <b>first write operations</b>. The tests verify the command's <b>effect</b>, not
/// its text: every scenario is run against a real repository and the result read back with
/// <c>git status</c>.
/// </para>
/// <para>
/// <b>MEASURED:</b> unstaging cannot be done with a single command — with no HEAD,
/// <c>restore --staged</c> fails with <c>fatal: could not resolve 'HEAD'</c>, while with a HEAD
/// present <c>rm --cached</c> stages the file as <i>deleted</i>.
/// </para>
/// </remarks>
public class StagingWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(StagingWriter Writer, GitWriteQueue Queue)> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return (new StagingWriter(new GitWriter(runner, queue), runner), queue);
    }

    private static RepositoryPath[] Paths(params string[] values) =>
        [.. values.Select(RepositoryPath.Parse)];

    /// <summary>A path's XY status code in <c>git status --porcelain=v2</c>.</summary>
    private static string Status(TestRepository repository, string path)
    {
        foreach (string line in repository.Git("status", "--porcelain=v2").Split('\n'))
        {
            if (line.Length > 4 && line.EndsWith(path, StringComparison.Ordinal))
            {
                return line.StartsWith("? ", StringComparison.Ordinal)
                    ? "??"
                    : line.Split(' ')[1];
            }
        }

        return string.Empty;
    }

    [Fact]
    public async Task Dosya_stage_lenir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("yeni.txt", "icerik\n");

        Status(repository, "yeni.txt").ShouldBe("??");

        await writer.StageAsync(repository.Path, Paths("yeni.txt"), Ct);

        // "A." = added to the index, no change in the working tree.
        Status(repository, "yeni.txt").ShouldBe("A.");
    }

    [Fact]
    public async Task Stage_lenen_dosya_unstage_edilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("yeni.txt", "icerik\n");
        await writer.StageAsync(repository.Path, Paths("yeni.txt"), Ct);

        await writer.UnstageAsync(repository.Path, Paths("yeni.txt"), Ct);

        // The file becomes untracked but STAYS ON DISK.
        Status(repository, "yeni.txt").ShouldBe("??");
        File.Exists(Path.Combine(repository.Path, "yeni.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Takip_edilen_dosyanin_degisikligi_unstage_edilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("dosya.txt", "ilk\n");
        await writer.StageAsync(repository.Path, Paths("dosya.txt"), Ct);
        repository.Git("commit", "-m", "dosya eklendi");

        repository.WriteFile("dosya.txt", "ilk\ndegisiklik\n");
        await writer.StageAsync(repository.Path, Paths("dosya.txt"), Ct);
        Status(repository, "dosya.txt").ShouldBe("M.");

        await writer.UnstageAsync(repository.Path, Paths("dosya.txt"), Ct);

        // ⚠️ Had `rm --cached` been used here the result would be "D.": the user asking to unstage would
        // see the file staged as DELETED.
        Status(repository, "dosya.txt").ShouldBe(".M");
    }

    [Fact]
    public async Task HEAD_YOKKEN_unstage_calisir()
    {
        // MEASURED: `git restore --staged` fails in this case
        // (fatal: could not resolve 'HEAD'). Taking back a file staged before the first commit is a
        // common operation; it must not crash.
        using TestRepository repository = TestRepository.CreateEmpty();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("ilk.txt", "icerik\n");
        await writer.StageAsync(repository.Path, Paths("ilk.txt"), Ct);
        Status(repository, "ilk.txt").ShouldBe("A.");

        await writer.UnstageAsync(repository.Path, Paths("ilk.txt"), Ct);

        Status(repository, "ilk.txt").ShouldBe("??");
        File.Exists(Path.Combine(repository.Path, "ilk.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Silinen_dosya_da_stage_lenir()
    {
        // `git add` on its own does not pick up deletions; `-A` is needed (measured).
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("silinecek.txt", "icerik\n");
        await writer.StageAsync(repository.Path, Paths("silinecek.txt"), Ct);
        repository.Git("commit", "-m", "eklendi");

        File.Delete(Path.Combine(repository.Path, "silinecek.txt"));

        await writer.StageAsync(repository.Path, Paths("silinecek.txt"), Ct);

        Status(repository, "silinecek.txt").ShouldBe("D.");
    }

    [Fact]
    public async Task Tire_ile_baslayan_ve_bosluklu_yollar_calisir()
    {
        // The paths are given AFTER the `--` separator; otherwise git would take them for options.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("-tireli.txt", "a\n");
        repository.WriteFile("bosluklu ad.txt", "b\n");

        await writer.StageAsync(repository.Path, Paths("-tireli.txt", "bosluklu ad.txt"), Ct);

        Status(repository, "-tireli.txt").ShouldBe("A.");
        // `--porcelain=v2` does not quote a name containing spaces (measured); the path passes through as is.
        Status(repository, "bosluklu ad.txt").ShouldBe("A.");
    }

    [Fact]
    public async Task Bos_liste_HICBIR_SEY_yapmaz()
    {
        // ⚠️ Running `git add -A --` with no path would stage the WHOLE repository.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("dokunulmayacak.txt", "icerik\n");

        await writer.StageAsync(repository.Path, [], Ct);

        Status(repository, "dokunulmayacak.txt").ShouldBe("??");
    }

    [Fact]
    public async Task Untrack_dosyayi_diskte_birakir()
    {
        // This operation is DELIBERATELY separate from unstage: on a tracked file the result is "staged
        // as deleted", and the user does that on purpose.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("ayar.json", "{}\n");
        await writer.StageAsync(repository.Path, Paths("ayar.json"), Ct);
        repository.Git("commit", "-m", "ayar eklendi");

        await writer.UntrackAsync(repository.Path, Paths("ayar.json"), Ct);

        Status(repository, "ayar.json").ShouldBe("D.");
        File.Exists(Path.Combine(repository.Path, "ayar.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task Coklu_yol_tek_cagrida_stage_lenir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("bir.txt", "1\n");
        repository.WriteFile("iki.txt", "2\n");
        repository.WriteFile("uc.txt", "3\n");

        await writer.StageAsync(repository.Path, Paths("bir.txt", "iki.txt"), Ct);

        Status(repository, "bir.txt").ShouldBe("A.");
        Status(repository, "iki.txt").ShouldBe("A.");

        // Verilmeyen yol etkilenmemeli.
        Status(repository, "uc.txt").ShouldBe("??");
    }

    [Fact]
    public async Task Eszamanli_stage_cagrilari_cakismaz()
    {
        // The write path goes through the queue (P05-T01): had `git add` been run directly, this
        // scenario would have collided.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        for (int i = 0; i < 12; i++)
        {
            repository.WriteFile($"eszamanli{i}.txt", $"{i}\n");
        }

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            writer.StageAsync(repository.Path, Paths($"eszamanli{i}.txt"), Ct)));

        for (int i = 0; i < 12; i++)
        {
            Status(repository, $"eszamanli{i}.txt").ShouldBe("A.");
        }
    }
}
