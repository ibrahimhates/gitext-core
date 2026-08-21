using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P12-T01 — the branch name read from the file system.
/// </summary>
/// <remarks>
/// <para>
/// Every test here compares the answer with <b>what real git says</b>
/// (<c>git symbolic-ref --short -q HEAD</c>). The point of the class is to skip starting git, so a
/// test that only checked our own parser would be testing our assumption instead of the behaviour;
/// git itself is the reference.
/// </para>
/// <para>
/// 🔴 The linked-worktree test is not decoration: the first implementation resolved <c>gitdir:</c>
/// relative to the repository and reported "detached" there while git said <c>wtbranch</c>. It was
/// the comparison that caught it.
/// </para>
/// </remarks>
public class RepositoryHeadTests
{
    /// <summary>What git itself reports, or <see langword="null"/> when HEAD is detached.</summary>
    private static string? BranchAccordingToGit(TestRepository repository)
    {
        (int exitCode, string _) = repository.TryGit("symbolic-ref", "--short", "-q", "HEAD");

        return exitCode == 0
            ? repository.Git("symbolic-ref", "--short", "HEAD").Trim()
            : null;
    }

    [Fact]
    public void Dal_adi_gitin_cevabiyla_ayni()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        RepositoryHeadInfo head = RepositoryHead.Read(repository.Path);

        head.IsRepository.ShouldBeTrue();
        head.BranchName.ShouldBe(BranchAccordingToGit(repository));
    }

    [Fact]
    public void Egik_cizgili_dal_adi_BOLUNMUYOR()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("checkout", "-b", "feature/deep/name");

        RepositoryHeadInfo head = RepositoryHead.Read(repository.Path);

        head.BranchName.ShouldBe("feature/deep/name");
        head.BranchName.ShouldBe(BranchAccordingToGit(repository));
    }

    [Fact]
    public void Ayrik_HEAD_dal_adi_vermiyor_ama_depo_olarak_taniniyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("checkout", "--detach");

        RepositoryHeadInfo head = RepositoryHead.Read(repository.Path);

        head.IsRepository.ShouldBeTrue();
        head.IsDetached.ShouldBeTrue();
        head.BranchName.ShouldBeNull();
        BranchAccordingToGit(repository).ShouldBeNull();
    }

    [Fact]
    public void Bagli_calisma_agacinin_KENDI_dali_okunuyor()
    {
        // 🔴 The measurement that found the bug: `.git` here is a FILE and the path inside it is
        // absolute. Resolving it relative to the worktree makes the branch read as "detached" —
        // silently, because a detached HEAD is a legitimate answer.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using TestRepository worktree = repository.AddWorkTree("wtbranch");

        RepositoryHeadInfo head = RepositoryHead.Read(worktree.Path);

        head.BranchName.ShouldBe("wtbranch");
        head.BranchName.ShouldBe(BranchAccordingToGit(worktree));

        // The host repository keeps its own branch; the two must not be confused.
        RepositoryHead.Read(repository.Path).BranchName.ShouldBe(BranchAccordingToGit(repository));
    }

    [Fact]
    public void Alt_modulun_dali_okunuyor()
    {
        // A submodule's `.git` is also a file, but the path inside it is RELATIVE.
        using TestRepository parent = TestRepository.CreateWithSingleCommit();
        using TestRepository child = TestRepository.CreateWithSingleCommit();

        parent.AddSubmodule(child, "child");

        string submodulePath = Path.Combine(parent.Path, "child");

        RepositoryHeadInfo head = RepositoryHead.Read(submodulePath);

        head.IsRepository.ShouldBeTrue();
        head.BranchName.ShouldBe(parent.Git("-C", "child", "symbolic-ref", "--short", "HEAD").Trim());
    }

    [Fact]
    public void Ciplak_depo_da_depo_sayiliyor()
    {
        using TestRepository bare = TestRepository.CreateBare();

        RepositoryHeadInfo head = RepositoryHead.Read(bare.Path);

        head.IsRepository.ShouldBeTrue();
        head.BranchName.ShouldBe("main");
    }

    [Fact]
    public void Depo_olmayan_klasor_depo_degil()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gitext-not-a-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            RepositoryHead.Read(directory).IsRepository.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Var_olmayan_yol_ISTISNA_ATMIYOR()
    {
        // The dashboard reads entries for drives that may not be mounted; throwing here would
        // take down the whole list for a single unreachable path.
        RepositoryHead.Read("/gitext/no/such/place").IsRepository.ShouldBeFalse();
        RepositoryHead.Read(null).IsRepository.ShouldBeFalse();
        RepositoryHead.Read("   ").IsRepository.ShouldBeFalse();
    }
}
