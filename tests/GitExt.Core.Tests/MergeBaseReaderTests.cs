using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P14-T21 — <c>git merge-base</c>, against real git.
/// </summary>
/// <remarks>
/// 🔴 <b>MEASURED — "no common ancestor" leaves through the exit code.</b> For unrelated histories
/// git exits <b>1</b> with empty output, the same shape as a real failure. It is an answer, not an
/// error, and the tests below hold that line.
/// </remarks>
public class MergeBaseReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<MergeBaseReader> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);

        return new MergeBaseReader(new GitProcessRunner(executable));
    }

    private static TestRepository Branched()
    {
        TestRepository repository = TestRepository.CreateWithSingleCommit();

        repository.Git("checkout", "-q", "-b", "feature");
        repository.WriteFile("f.txt", "dal\n");
        repository.Git("add", "f.txt");
        repository.Commit("dalda");

        repository.Git("checkout", "-q", "main");
        repository.WriteFile("g.txt", "ana\n");
        repository.Git("add", "g.txt");
        repository.Commit("anada");

        return repository;
    }

    [Fact]
    public async Task Ortak_ata_bulunuyor()
    {
        using TestRepository repository = Branched();
        MergeBaseReader reader = await CreateAsync();

        CommitId? found = await reader.FindAsync(repository.Path, ["main", "feature"], Ct);

        found.ShouldNotBeNull();

        string expected = repository.Git("merge-base", "main", "feature").Trim();

        found!.Value.Value.ShouldBe(expected);
    }

    [Fact]
    public async Task Ilgisiz_gecmislerde_HATA_DEGIL_bos_donuyor()
    {
        using TestRepository repository = Branched();
        MergeBaseReader reader = await CreateAsync();

        repository.Git("checkout", "-q", "--orphan", "lonely");
        repository.Git("rm", "-q", "-rf", ".");
        repository.WriteFile("z.txt", "yalniz\n");
        repository.Git("add", "z.txt");
        repository.Commit("oksuz");

        CommitId? found = await reader.FindAsync(repository.Path, ["main", "lonely"], Ct);

        found.ShouldBeNull();
    }

    [Fact]
    public async Task Ayni_sey_iki_kez_verilince_kendisi_donuyor()
    {
        using TestRepository repository = Branched();
        MergeBaseReader reader = await CreateAsync();

        CommitId? found = await reader.FindAsync(repository.Path, ["main", "main"], Ct);

        found.ShouldNotBeNull();
        found!.Value.Value.ShouldBe(repository.Git("rev-parse", "main").Trim());
    }

    [Fact]
    public async Task Ikiden_az_sürüm_verilince_hicbir_sey_yapilmiyor()
    {
        using TestRepository repository = Branched();
        MergeBaseReader reader = await CreateAsync();

        (await reader.FindAsync(repository.Path, [], Ct)).ShouldBeNull();
        (await reader.FindAsync(repository.Path, ["main"], Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Bos_ve_bosluk_girdiler_atlaniyor()
    {
        using TestRepository repository = Branched();
        MergeBaseReader reader = await CreateAsync();

        CommitId? found = await reader
            .FindAsync(repository.Path, ["main", "   ", "feature", ""], Ct);

        found.ShouldNotBeNull();
    }

    [Fact]
    public async Task Uc_surumde_octopus_kullaniliyor()
    {
        using TestRepository repository = Branched();

        repository.Git("checkout", "-q", "-b", "third", "main");
        repository.WriteFile("h.txt", "ucuncu\n");
        repository.Git("add", "h.txt");
        repository.Commit("ucuncude");

        MergeBaseReader reader = await CreateAsync();

        CommitId? found = await reader
            .FindAsync(repository.Path, ["main", "feature", "third"], Ct);

        // Without --octopus git refuses three arguments; getting an answer at all is the assertion.
        found.ShouldNotBeNull();
    }

    [Fact]
    public async Task Olmayan_surum_HATA_veriyor()
    {
        using TestRepository repository = Branched();
        MergeBaseReader reader = await CreateAsync();

        await Should.ThrowAsync<GitException>(() =>
            reader.FindAsync(repository.Path, ["main", "boyle-bir-dal-yok"], Ct));
    }
}
