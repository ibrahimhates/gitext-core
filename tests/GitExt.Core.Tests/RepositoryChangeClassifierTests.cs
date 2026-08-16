

namespace GitExt.Core.Tests;

/// <summary>
/// Classification of file system events (P05-T14).
/// </summary>
/// <remarks>
/// Every rule here comes from <b>an event sequence measured with real <c>git</c></b>; the tests are
/// the summary of those measurements. When the classifier is wrong the symptom is silent: either
/// nothing ever refreshes (a stale screen) or it refreshes non-stop (an infinite loop).
/// </remarks>
public class RepositoryChangeClassifierTests
{
    [Fact]
    public void Calisma_agacindaki_dosya_calisma_agaci_degisimidir()
    {
        RepositoryChangeClassifier.ClassifyWorkingTreePath("src/Program.cs")
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Ref_guncellemesi_depo_degisimidir()
    {
        // MEASURED: an external `git commit` produced 64 events and ALL of them were under .git;
        // zero events in the working tree. If this path is filtered out, a commit made from outside
        // is never seen.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/refs/heads/main")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/HEAD")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/packed-refs")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Kilit_dosyalari_yok_sayilir()
    {
        // MEASURED: even a read-only `git status` creates and deletes .git/index.lock (2 events).
        // This filter is the only thing that closes the infinite refresh loop.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/index.lock").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/refs/heads/main.lock").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/config.lock").ShouldBeNull();
    }

    [Fact]
    public void Kilidin_kaldirilmasiyla_gelen_gercek_ref_sinyali_YENMEZ()
    {
        // MEASURED: `git branch x` produced five events; the real signal is the RENAME
        // `refs/heads/x.lock → refs/heads/x`. Because the watcher uses the NEW name on a rename, it
        // does not get caught by the lock filter.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/refs/heads/gecici-dal")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Index_calisma_agaci_degisimidir_depo_degisimi_degil()
    {
        // The staged state changed; the commit list is the same. Refreshing the commit list too
        // would be a pointless log read after every `git add`.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/index")
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Nesne_ve_reflog_yazimlari_yok_sayilir()
    {
        // MEASURED: a single `git commit` produced most of its 19 Created events under objects/.
        // An object having been written is not visible to the user until a ref is updated.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/objects/3c/1a2b").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/objects/pack/pack-abc.pack").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/logs/refs/heads/main").ShouldBeNull();
    }

    [Fact]
    public void Kendi_taslak_dosyamiz_yok_sayilir()
    {
        // The P05-T13 draft is written continuously while typing; if it is not filtered out, every
        // keystroke while the user writes a commit message would trigger a refresh.
        RepositoryChangeClassifier
            .ClassifyWorkingTreePath($".git/{CommitMessageStore.DraftFileName}")
            .ShouldBeNull();

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/COMMIT_EDITMSG").ShouldBeNull();
    }

    [Fact]
    public void Suregelen_islem_durumu_depo_degisimidir()
    {
        // A merge/rebase/cherry-pick starting or finishing changes the screen.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/MERGE_HEAD")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/rebase-merge/done")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/CHERRY_PICK_HEAD")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Ic_ice_depolarin_git_dizini_de_ayni_kurala_tabi()
    {
        // A submodule's own .git lives under the working tree; object writes are still noise.
        RepositoryChangeClassifier.ClassifyWorkingTreePath("alt/modul/.git/objects/ab/cd")
            .ShouldBeNull();

        RepositoryChangeClassifier.ClassifyWorkingTreePath("alt/modul/.git/refs/heads/main")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Alt_modulun_git_DOSYASI_depo_degisimidir()
    {
        // In a submodule `.git` is a file, not a directory; it appearing or changing alters the
        // repository structure.
        RepositoryChangeClassifier.ClassifyWorkingTreePath("alt/modul/.git")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Git_dizinine_goreli_yollar_ayri_siniflandirilir()
    {
        // In a linked working tree the git directory is OUTSIDE the working tree; the paths from
        // that watcher do not carry the `.git/` prefix.
        RepositoryChangeClassifier.ClassifyGitDirectoryPath("HEAD")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyGitDirectoryPath("index")
            .ShouldBe(RepositoryChangeKind.WorkingTree);

        RepositoryChangeClassifier.ClassifyGitDirectoryPath("index.lock").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyGitDirectoryPath("objects/ab/cd").ShouldBeNull();
    }

    [Fact]
    public void Windows_ayraci_da_kabul_edilir()
    {
        RepositoryChangeClassifier.ClassifyWorkingTreePath(@".git\refs\heads\main")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(@"src\Program.cs")
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }
}
