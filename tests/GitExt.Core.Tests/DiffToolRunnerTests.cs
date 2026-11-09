using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

public sealed class DiffToolRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<DiffToolRunner> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);

        return new DiffToolRunner(new GitProcessRunner(executable));
    }

    private static TestRepository TwoCommits()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "1\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "first");
        repository.WriteFile("a.txt", "2\n");
        repository.Git("commit", "-q", "-am", "second");
        return repository;
    }

    [Fact]
    public async Task Arac_yoksa_git_calistirilmiyor()
    {
        using TestRepository repository = TwoCommits();
        DiffToolRunner runner = await CreateAsync();

        (await runner.GetConfiguredToolAsync(repository.Path, Ct)).ShouldBeNull();
        await Should.ThrowAsync<NoDiffToolConfiguredException>(
            () => runner.RunDirDiffAsync(repository.Path, "HEAD~1", "HEAD", Ct));
    }

    [Theory]
    [InlineData("diff.guitool")]
    [InlineData("merge.guitool")]
    [InlineData("diff.tool")]
    [InlineData("merge.tool")]
    public async Task Gui_geri_dusus_sirasindaki_her_ayar_taniniyor(string key)
    {
        using TestRepository repository = TwoCommits();
        repository.Git("config", key, "fake");

        (await (await CreateAsync()).GetConfiguredToolAsync(repository.Path, Ct)).ShouldBe("fake");
    }

    [Fact]
    public async Task Dizin_karsilastirmasi_iki_tarafi_araca_veriyor()
    {
        using TestRepository repository = TwoCommits();
        string seen = Path.Combine(repository.Path, ".git", "seen.txt");
        repository.Git("config", "diff.tool", "fake");
        repository.Git("config", "difftool.fake.cmd", $"cat \"$LOCAL/a.txt\" \"$REMOTE/a.txt\" > '{seen}'");

        await (await CreateAsync()).RunDirDiffAsync(repository.Path, "HEAD~1", "HEAD", Ct);

        File.ReadAllText(seen).ShouldBe("1\n2\n");
    }
}
