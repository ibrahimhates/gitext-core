using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T08 — string pool (interning) in commit reading.
/// </summary>
/// <remarks>
/// <para>
/// Measurement: on a 500,000-commit repository the author/committer fields held 46 MB but
/// the number of unique values was <b>2</b>. Retained memory after pooling: 460 MB → 368 MB.
/// </para>
/// <para>
/// The job of the tests here is not the gain but <b>verifying that the gain does not break correctness</b>:
/// returning a shared instance must not change the data that was read.
/// </para>
/// </remarks>
public class CommitLogInterningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<CommitLogReader> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct)
            .ConfigureAwait(true);

        return new CommitLogReader(new GitProcessRunner(executable));
    }

    private static TestRepository RepositoryWithTwoAuthors()
    {
        TestRepository repo = TestRepository.CreateWithSingleCommit();

        repo.WriteFile("a.txt", "a");
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=Ayşe Yılmaz", "-c", "user.email=ayse@example.com",
            "commit", "-q", "-m", "ilk");

        repo.WriteFile("b.txt", "b");
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=Ayşe Yılmaz", "-c", "user.email=ayse@example.com",
            "commit", "-q", "-m", "ikinci");

        return repo;
    }

    /// <remarks>
    /// This is the pool's job: the same author name must be the <b>same instance</b> in both commits.
    /// Reference equality is checked because that is exactly the measured gain — value
    /// equality would hold without the pool too and would prove nothing.
    /// </remarks>
    [Fact]
    public async Task Ayni_yazar_ayni_ORNEGI_paylasiyor()
    {
        using TestRepository repo = RepositoryWithTwoAuthors();
        CommitLogReader reader = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        CommitInfo[] byAyse = [.. commits.Where(c => c.Author.Name == "Ayşe Yılmaz")];
        byAyse.Length.ShouldBeGreaterThanOrEqualTo(2);

        ReferenceEquals(byAyse[0].Author.Name, byAyse[1].Author.Name)
            .ShouldBeTrue("yazar adı iki commit arasında paylaşılmıyor");
        ReferenceEquals(byAyse[0].Author.Email, byAyse[1].Author.Email)
            .ShouldBeTrue("yazar e-postası paylaşılmıyor");
    }

    /// <remarks>
    /// 🔴 The pool must not change the <b>value</b>. A mapping bug would merge two different authors
    /// under a single name; the commit list would silently show the wrong person —
    /// a bug that no amount of memory gain could ever justify.
    /// </remarks>
    [Fact]
    public async Task Farkli_yazarlar_birbirine_karismiyor()
    {
        using TestRepository repo = RepositoryWithTwoAuthors();
        CommitLogReader reader = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        string[] names = [.. commits.Select(c => c.Author.Name).Distinct(StringComparer.Ordinal)];

        names.ShouldContain("Ayşe Yılmaz");
        names.Length.ShouldBeGreaterThanOrEqualTo(2, "ikinci yazar kaybolmuş");
    }

    /// <remarks>
    /// The streaming path is a separate code path and the graph is fed from exactly there; the pool must
    /// be active there too.
    /// </remarks>
    [Fact]
    public async Task Akis_yolunda_da_paylasim_var()
    {
        using TestRepository repo = RepositoryWithTwoAuthors();
        CommitLogReader reader = await CreateAsync().ConfigureAwait(true);

        List<CommitInfo> commits = [];

        await foreach (CommitInfo commit in reader
            .StreamAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true))
        {
            commits.Add(commit);
        }

        CommitInfo[] byAyse = [.. commits.Where(c => c.Author.Name == "Ayşe Yılmaz")];
        byAyse.Length.ShouldBeGreaterThanOrEqualTo(2);

        ReferenceEquals(byAyse[0].Author.Name, byAyse[1].Author.Name)
            .ShouldBeTrue("akış yolunda yazar adı paylaşılmıyor");
    }

    /// <remarks>
    /// An empty field (a commit with no encoding) must not enter the pool; <see cref="string.Empty"/>
    /// is already a single instance and putting an empty key in the dictionary would only be noise.
    /// </remarks>
    [Fact]
    public async Task Bos_kodlama_alani_bos_dize_kaliyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        CommitLogReader reader = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 1 }, Ct)
            .ConfigureAwait(true);

        commits[0].Encoding.ShouldBe(string.Empty);
    }
}
