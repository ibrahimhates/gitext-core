using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P12-T06 — the history filters (message · author · committer · diff content).
/// </summary>
/// <remarks>
/// These feed the filter toolbar. Every test runs against <b>real git</b>, because what is being
/// pinned down is not our string building but git's own matching rules — and those rules held two
/// surprises, both of which are asserted here.
/// </remarks>
public class CommitLogFilterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(TestRepository Repository, CommitLogReader Reader) : IDisposable
    {
        public void Dispose() => Repository.Dispose();

        public async Task<string[]> SubjectsAsync(CommitLogQuery query) =>
            [.. (await Reader.ReadAsync(Repository.Path, query, Ct)).Select(c => c.Subject)];
    }

    /// <summary>
    /// A repository where author and committer differ, and the file content is known.
    /// </summary>
    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);

        Commit("add login page", author: "ada", committer: "grace", content: "alpha\n");
        Commit("fix(auth): token bug", author: "grace", committer: "grace", content: "beta token\n");
        Commit("chore: bump deps", author: "ada", committer: "ada", content: "gamma\n");
        Commit("revert: a.b.c thing", author: "turing", committer: "ada", content: "delta\n");

        return new Harness(repository, new CommitLogReader(new GitProcessRunner(executable)));

        void Commit(string message, string author, string committer, string content)
        {
            repository.WriteFile("f.txt", content);
            repository.Git("add", "f.txt");
            repository.GitWithEnvironment(
                new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_NAME"] = author,
                    ["GIT_AUTHOR_EMAIL"] = $"{author}@example.invalid",
                    ["GIT_COMMITTER_NAME"] = committer,
                    ["GIT_COMMITTER_EMAIL"] = $"{committer}@example.invalid",
                },
                "commit", "-m", message);
        }
    }

    [Fact]
    public async Task Mesaj_filtresi_calisiyor()
    {
        using Harness harness = await CreateAsync();

        (await harness.SubjectsAsync(new CommitLogQuery { MessageContains = "token" }))
            .ShouldBe(["fix(auth): token bug"]);
    }

    [Fact]
    public async Task Yazar_ve_islemci_AYRI_filtreler()
    {
        // After a rebase or a cherry-pick these are different people; a filter that could not tell
        // them apart would answer the wrong question.
        using Harness harness = await CreateAsync();

        (await harness.SubjectsAsync(new CommitLogQuery { Author = "ada" }))
            .ShouldBe(["chore: bump deps", "add login page"]);

        (await harness.SubjectsAsync(new CommitLogQuery { Committer = "ada" }))
            .ShouldBe(["revert: a.b.c thing", "chore: bump deps"]);
    }

    [Fact]
    public async Task Filtreler_varsayilan_olarak_BUYUK_KUCUK_HARF_duyarsiz()
    {
        // 🔴 MEASURED: without --regexp-ignore-case git matches case-SENSITIVELY, so
        // `--committer=GRACE` finds nothing in a repository full of commits by `grace`. In a
        // filter box the user types what they remember, not what was typed originally.
        using Harness harness = await CreateAsync();

        (await harness.SubjectsAsync(new CommitLogQuery { Committer = "GRACE" }))
            .ShouldBe(["fix(auth): token bug", "add login page"]);

        (await harness.SubjectsAsync(new CommitLogQuery { MessageContains = "LOGIN" }))
            .ShouldBe(["add login page"]);
    }

    [Fact]
    public async Task Duyarsizlik_KAPATILABILIYOR()
    {
        // The counter-evidence: were the flag ignored, the test above would prove nothing.
        using Harness harness = await CreateAsync();

        (await harness.SubjectsAsync(new CommitLogQuery { Committer = "GRACE", IgnoreCase = false }))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Duz_metin_kipi_TUM_desenleri_birden_etkiliyor()
    {
        // 🔴 MEASURED, and this is why the switch is query-wide: git's --fixed-strings applies to
        // --grep, --author and --committer at once. The earlier code added it whenever a message
        // filter was present, so switching on a message filter silently turned an author filter
        // into literal matching — with no error and no empty-looking result to warn anybody.
        using Harness harness = await CreateAsync();

        // As a regular expression the dots are wildcards, so "a.b.c" matches "a.b.c".
        (await harness.SubjectsAsync(new CommitLogQuery { MessageContains = "a.b.c" }))
            .ShouldBe(["revert: a.b.c thing"]);

        // Literally too, because the subject really does contain "a.b.c".
        (await harness.SubjectsAsync(new CommitLogQuery { MessageContains = "a.b.c", LiteralPatterns = true }))
            .ShouldBe(["revert: a.b.c thing"]);

        // But a pattern that only matches AS A REGEX disappears in literal mode — and that is
        // exactly what silently happened to the author filter.
        (await harness.SubjectsAsync(new CommitLogQuery { Author = "a.a" }))
            .ShouldBe(["chore: bump deps", "add login page"]);

        (await harness.SubjectsAsync(new CommitLogQuery { Author = "a.a", LiteralPatterns = true }))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Fark_icerigi_filtresi_TASINAN_satiri_da_buluyor()
    {
        // 🔴 The reason -G was chosen over -S (GitExtensions makes the same choice): -S only
        // reacts when the NUMBER of occurrences changes, so a commit that merely moves a line
        // containing the text is invisible to it. For someone asking "which commit touched this
        // text", that commit is a hit.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("f.txt", "beta token\nsecond token\n");
        harness.Repository.Git("add", "f.txt");
        harness.Repository.Commit("add second token line");

        harness.Repository.WriteFile("f.txt", "second token\nbeta token\n");
        harness.Repository.Git("add", "f.txt");
        harness.Repository.Commit("reorder token lines");

        string[] subjects = await harness.SubjectsAsync(new CommitLogQuery { DiffContains = "token" });

        subjects.ShouldContain("reorder token lines");
        subjects.ShouldContain("add second token line");
        subjects.ShouldContain("fix(auth): token bug");
    }

    [Fact]
    public async Task Filtreler_birlikte_kullanilabiliyor()
    {
        using Harness harness = await CreateAsync();

        (await harness.SubjectsAsync(new CommitLogQuery { Author = "grace", MessageContains = "token" }))
            .ShouldBe(["fix(auth): token bug"]);

        // …and the pair really does narrow: the author alone would also return this commit.
        (await harness.SubjectsAsync(new CommitLogQuery { Author = "ada", MessageContains = "token" }))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Filtresiz_sorgu_HERSEYI_donduruyor()
    {
        using Harness harness = await CreateAsync();

        (await harness.SubjectsAsync(new CommitLogQuery())).Length.ShouldBe(4);
    }
}
