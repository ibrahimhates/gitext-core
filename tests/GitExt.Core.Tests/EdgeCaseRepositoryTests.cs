using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T15 — edge case repositories.
/// </summary>
/// <remarks>
/// <para>
/// The plan's list: empty repo · single commit · many branches · many tags · deep directory tree ·
/// large files · binary-heavy · shallow clone · bare repo.
/// </para>
/// <para>
/// What they have in common is that they break assumptions that never show up in a "normal" repository:
/// that <c>HEAD</c> exists, that there is a working tree, that history starts at a root.
/// If an edge case crashes, the user hits it <b>on the very first open</b>.
/// </para>
/// </remarks>
public class EdgeCaseRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(CommitLogReader Reader, RefReader Refs, RepositoryLocator Locator)> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct)
            .ConfigureAwait(true);

        GitProcessRunner runner = new(executable);

        return (new CommitLogReader(runner), new RefReader(runner), new RepositoryLocator(runner));
    }

    /// <summary>
    /// In an empty repository reading with <c>--all</c> gives an empty list, not an error (P09-T15).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — the two call forms behave DIFFERENTLY on an empty repository:</b>
    /// <code>
    /// git log                → fatal: your current branch 'main' does not have any commits yet  (128)
    /// git log --all          → (empty output, 0)
    /// </code>
    /// Because the UI reads with <c>IncludeAllRefs</c>, a freshly created repository opens with an empty
    /// list rather than an error screen. But the difference is <b>not accidental, it is a choice that
    /// must be preserved</b>: building the query without <c>--all</c> would turn the first open of a new
    /// repository into an error.
    /// </remarks>
    [Fact]
    public async Task Bos_depo_hata_degil_bos_liste_veriyor()
    {
        using TestRepository repo = TestRepository.CreateEmpty();
        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { IncludeAllRefs = true, MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        commits.ShouldBeEmpty();
    }

    /// <remarks>
    /// Reading the same repository without <c>--all</c> is treated as an <b>error</b> by git.
    /// This test pins that difference down: if the behaviour changes — if git one day starts returning 0,
    /// or if someone changes the query — it shows up here.
    /// </remarks>
    [Fact]
    public async Task Bos_depoda_HEAD_uzerinden_okuma_hata_veriyor()
    {
        using TestRepository repo = TestRepository.CreateEmpty();
        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        await Should.ThrowAsync<GitException>(async () => await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task Tek_commitlik_depo_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        commits.Count.ShouldBe(1);
        commits[0].Parents.ShouldBeEmpty("kök commit'in ebeveyni olmamalı");
    }

    /// <remarks>
    /// In a bare repository there is <b>no</b> working tree. A read path that assumes a tree would die
    /// here with "must be run in a work tree".
    /// </remarks>
    [Fact]
    public async Task Bare_depo_calisma_agaci_olmadan_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateBare();
        (_, _, RepositoryLocator locator) = await CreateAsync().ConfigureAwait(true);

        RepositoryLocation location = await locator.LocateAsync(repo.Path, Ct).ConfigureAwait(true);

        location.IsBare.ShouldBeTrue();
        location.WorkTreeRoot.ShouldBeNull();
    }

    /// <remarks>
    /// A large number of refs makes the <c>for-each-ref</c> output big. 500 tags push the text the parser
    /// reads in one go up to ~kilobytes; a record-separator bug would become visible
    /// here.
    /// </remarks>
    [Fact]
    public async Task Cok_sayida_etiket_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        for (int i = 0; i < 500; i++)
        {
            repo.Git("tag", $"v{i}");
        }

        (_, RefReader refs, _) = await CreateAsync().ConfigureAwait(true);

        RepositoryRefs all = await refs.ReadAsync(repo.Path, Ct).ConfigureAwait(true);

        all.Tags.Count.ShouldBe(500);
    }

    /// <remarks>
    /// An empty subject line (<c>--allow-empty-message</c>) puts two NULs next to each other in the field
    /// separator — that was exactly the bug that split records in half in Phase 07.
    /// </remarks>
    [Fact]
    public async Task Bos_commit_mesaji_kaydi_bolmuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        repo.WriteFile("x.txt", "x");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "--allow-empty-message", "-m", "");

        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        commits.Count.ShouldBe(2, "boş mesajlı commit kaydı ikiye böldü");
        commits[0].Subject.ShouldBeEmpty();
    }

    /// <remarks>
    /// In a shallow clone the history is <b>truncated</b>: the oldest commit's parent is defined but not
    /// present in the repository. Code that tries to resolve the parent would blow up here; the graph must
    /// show this as "the boundary of history".
    /// </remarks>
    [Fact]
    public async Task Shallow_clone_kesik_gecmisle_okunuyor()
    {
        using TestRepository origin = TestRepository.CreateWithSingleCommit();

        for (int i = 0; i < 5; i++)
        {
            origin.WriteFile($"f{i}.txt", $"{i}");
            origin.Git("add", "-A");
            origin.Git("commit", "-q", "-m", $"commit {i}");
        }

        string shallowPath = Path.Combine(Path.GetTempPath(), $"gitext-shallow-{Guid.NewGuid():N}");

        try
        {
            origin.Git("clone", "--depth", "2", "--no-local", origin.Path, shallowPath);

            (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

            IReadOnlyList<CommitInfo> commits = await reader
                .ReadAsync(shallowPath, new CommitLogQuery { MaxCount = 10 }, Ct)
                .ConfigureAwait(true);

            commits.Count.ShouldBeLessThanOrEqualTo(3, "shallow clone tüm geçmişi getirdi");
            commits.ShouldNotBeEmpty();
        }
        finally
        {
            if (Directory.Exists(shallowPath))
            {
                DeleteRecursive(shallowPath);
            }
        }
    }

    /// <remarks>
    /// If binary content is decoded as text it either blows up or produces corrupt data.
    /// Commit metadata must not be affected by binary files.
    /// </remarks>
    [Fact]
    public async Task Binary_dosyali_depo_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        byte[] payload = new byte[64 * 1024];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(Path.Combine(repo.Path, "veri.bin"), payload);

        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "binary");

        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        commits[0].Subject.ShouldBe("binary");
    }

    /// <remarks>
    /// Paths with Turkish characters are C-quoted without <c>-z</c> (measured in Phase 06).
    /// They can occur in the repository name too.
    /// </remarks>
    [Fact]
    public async Task Turkce_karakterli_yol_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        repo.WriteFile("şğüıöç/çalışma dosyası.txt", "içerik");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "Türkçe yol");

        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 1 }, Ct)
            .ConfigureAwait(true);

        commits[0].Subject.ShouldBe("Türkçe yol");
    }

    private static void DeleteRecursive(string path)
    {
        // Object files in a cloned repository can be read-only; deleting them directly
        // gives an access error.
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
