using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P02-T06 — Repository discovery. The behaviour of all four scenarios was measured with real
/// <c>git</c> and written accordingly: normal repo, bare repo, linked worktree, submodule.
/// </summary>
public class RepositoryLocatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<RepositoryLocator> CreateLocatorAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new RepositoryLocator(new GitProcessRunner(executable));
    }

    [Fact]
    public async Task Normal_repoyu_kokten_bulur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        RepositoryLocator locator = await CreateLocatorAsync();

        RepositoryLocation location = await locator.LocateAsync(repository.Path, Ct);

        location.IsBare.ShouldBeFalse();
        location.IsLinkedWorkTree.ShouldBeFalse();
        location.IsSubmodule.ShouldBeFalse();
        RealPath(location.WorkTreeRoot!).ShouldBe(RealPath(repository.Path));
        // In a normal repository the two must be the same.
        location.CommonDirectory.ShouldBe(location.GitDirectory);
    }

    [Fact]
    public async Task Alt_dizinden_cagrilinca_depo_kokunu_bulur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.WriteFile("bir/iki/uc.txt", "içerik\n");
        string nested = Path.Combine(repository.Path, "bir", "iki");

        RepositoryLocator locator = await CreateLocatorAsync();

        RepositoryLocation location = await locator.LocateAsync(nested, Ct);

        RealPath(location.WorkTreeRoot!).ShouldBe(RealPath(repository.Path));
    }

    [Fact]
    public async Task Dosya_yolu_verilirse_iceren_dizini_kullanir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        string file = Path.Combine(repository.Path, "README.md");

        RepositoryLocator locator = await CreateLocatorAsync();

        RepositoryLocation location = await locator.LocateAsync(file, Ct);

        RealPath(location.WorkTreeRoot!).ShouldBe(RealPath(repository.Path));
    }

    [Fact]
    public async Task Bare_repo_calisma_agaci_olmadan_bulunur()
    {
        // This scenario is why --show-toplevel was removed from the main call:
        // in a bare repository that flag returns 128 with "must be run in a work tree".
        using TestRepository repository = TestRepository.CreateBare();
        RepositoryLocator locator = await CreateLocatorAsync();

        RepositoryLocation location = await locator.LocateAsync(repository.Path, Ct);

        location.IsBare.ShouldBeTrue();
        location.WorkTreeRoot.ShouldBeNull();
        location.WorkingDirectory.ShouldNotBeNullOrWhiteSpace();
        RealPath(location.GitDirectory).ShouldBe(RealPath(repository.Path));
    }

    [Fact]
    public async Task Bagli_worktree_paylasilan_git_dizinini_ayirt_eder()
    {
        // Critical: refs and objects live in CommonDirectory; GitDirectory only holds this
        // worktree's HEAD and index. Mixing them up breaks ref reading.
        using TestRepository main = TestRepository.CreateWithSingleCommit();
        using TestRepository worktree = main.AddWorkTree("feature");

        RepositoryLocator locator = await CreateLocatorAsync();

        RepositoryLocation location = await locator.LocateAsync(worktree.Path, Ct);

        location.IsLinkedWorkTree.ShouldBeTrue();
        location.IsBare.ShouldBeFalse();
        RealPath(location.WorkTreeRoot!).ShouldBe(RealPath(worktree.Path));

        // The shared directory must be the main repository's .git, not the worktree-specific one.
        RealPath(location.CommonDirectory)
            .ShouldBe(RealPath(Path.Combine(main.Path, ".git")));
        location.GitDirectory.ShouldNotBe(location.CommonDirectory);
    }

    [Fact]
    public async Task Submodule_ust_projeyi_bildirir()
    {
        using TestRepository super = TestRepository.CreateWithSingleCommit();
        using TestRepository inner = TestRepository.CreateWithSingleCommit();
        super.AddSubmodule(inner, "mysub");

        RepositoryLocator locator = await CreateLocatorAsync();

        RepositoryLocation location = await locator.LocateAsync(
            Path.Combine(super.Path, "mysub"), Ct);

        location.IsSubmodule.ShouldBeTrue();
        RealPath(location.SuperprojectWorkTree!).ShouldBe(RealPath(super.Path));
    }

    [Fact]
    public async Task Normal_repo_submodule_olarak_isaretlenmez()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        RepositoryLocator locator = await CreateLocatorAsync();

        RepositoryLocation location = await locator.LocateAsync(repository.Path, Ct);

        // --show-superproject-working-tree returns empty output + 0 when it is not a submodule.
        location.IsSubmodule.ShouldBeFalse();
        location.SuperprojectWorkTree.ShouldBeNull();
    }

    [Fact]
    public async Task Depo_olmayan_dizin_icin_net_hata()
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("gitext-plain-");

        try
        {
            RepositoryLocator locator = await CreateLocatorAsync();

            GitException exception = await Should.ThrowAsync<GitException>(
                locator.LocateAsync(temporary.FullName, Ct));

            exception.Kind.ShouldBe(GitFailureKind.NotARepository);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Var_olmayan_dizin_icin_DirectoryNotFound()
    {
        RepositoryLocator locator = await CreateLocatorAsync();

        await Should.ThrowAsync<DirectoryNotFoundException>(
            locator.LocateAsync(Path.Combine(Path.GetTempPath(), "yok-boyle-bir-dizin-12345"), Ct));
    }

    /// <summary>
    /// 🔴 Regression: a repository reached through a symlink is NOT a linked worktree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the macOS CI failure, reproduced on every platform. There the temporary directory
    /// lives under <c>/var/folders/…</c> and <c>/var</c> is a symlink to <c>/private/var</c>, so the
    /// condition was met in every single test — but the cause is the symlink, not the operating
    /// system, and it is set up explicitly here.
    /// </para>
    /// <para>
    /// MEASURED: git answers <c>--absolute-git-dir</c> and <c>--show-toplevel</c> with the links
    /// resolved, while <c>--git-common-dir</c> stays relative (<c>.git</c>) and used to be resolved
    /// against the UNRESOLVED path we were handed. The two names of the same directory then compared
    /// as different and <see cref="RepositoryLocation.IsLinkedWorkTree"/> came out true.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Symlink_uzerinden_acilan_depo_worktree_sanilmaz()
    {
        // Windows requires a privilege to create symlinks; the cause is platform-independent.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "symlink creation needs a privilege on Windows");

        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        string link = Path.Combine(
            Path.GetTempPath(), "gitext-link-" + Guid.NewGuid().ToString("N")[..12]);

        Directory.CreateSymbolicLink(link, repository.Path);

        try
        {
            RepositoryLocator locator = await CreateLocatorAsync();

            RepositoryLocation location = await locator.LocateAsync(link, Ct);

            location.IsLinkedWorkTree.ShouldBeFalse();
            location.IsBare.ShouldBeFalse();
            location.CommonDirectory.ShouldBe(location.GitDirectory);
            RealPath(location.WorkTreeRoot!).ShouldBe(RealPath(repository.Path));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    /// <summary>
    /// Resolves symbolic links.
    /// </summary>
    /// <remarks>
    /// On macOS the temporary directory sits behind a symlink (<c>/var</c> → <c>/private/var</c>);
    /// git returns the resolved path while <see cref="Path.GetTempPath"/> gives the unresolved one.
    /// We bring both into the same form before comparing — with the very helper the production code
    /// uses, so that a comparison here means the same thing as a comparison there.
    /// </remarks>
    private static string RealPath(string path) => FileSystemPath.Resolve(path);
}
