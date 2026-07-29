using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P02-T06 — Depo keşfi. Dört senaryonun da davranışı gerçek <c>git</c> ile ölçülüp
/// buna göre yazıldı: normal repo, bare repo, bağlı worktree, submodule.
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
        // Normal depoda ikisi aynı olmalı.
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
        // Bu senaryo --show-toplevel'ı ana çağrıdan çıkarmamızın sebebi:
        // bare depoda o bayrak "must be run in a work tree" ile 128 döndürüyor.
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
        // Kritik: ref'ler ve nesneler CommonDirectory'de; GitDirectory yalnızca
        // bu worktree'nin HEAD ve index'ini tutar. Karıştırılırsa ref okuma bozulur.
        using TestRepository main = TestRepository.CreateWithSingleCommit();
        using TestRepository worktree = main.AddWorkTree("feature");

        RepositoryLocator locator = await CreateLocatorAsync();

        RepositoryLocation location = await locator.LocateAsync(worktree.Path, Ct);

        location.IsLinkedWorkTree.ShouldBeTrue();
        location.IsBare.ShouldBeFalse();
        RealPath(location.WorkTreeRoot!).ShouldBe(RealPath(worktree.Path));

        // Paylaşılan dizin ana deponun .git'i olmalı, worktree'ye özel dizin değil.
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

        // --show-superproject-working-tree submodule değilse boş çıktı + 0 döner.
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
    /// Sembolik bağları çözer.
    /// </summary>
    /// <remarks>
    /// macOS'ta <c>/tmp</c> aslında <c>/private/tmp</c>'ye bir semboliktir; git çözülmüş yolu
    /// döndürürken <see cref="Path.GetTempPath"/> çözülmemişini verir. Karşılaştırmadan önce
    /// ikisini de aynı biçime getiriyoruz.
    /// </remarks>
    private static string RealPath(string path) =>
        Path.TrimEndingDirectorySeparator(
            Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName
            ?? Path.GetFullPath(path));
}
