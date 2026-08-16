using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T14 — The selection and navigation logic.
/// </summary>
/// <remarks>
/// Because the navigation <b>decision</b> lives in the ViewModel it can be tested here; the key
/// mapping and the page size are the view's job (see <c>CommitListView.axaml.cs</c>) and are verified
/// with real key events in <c>CommitListKeyboardTests</c>.
/// </remarks>
public class CommitListNavigationTests
{
    private static async Task<CommitListViewModel> LoadedAsync(int commitCount = 10)
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(commitCount)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader());

        await viewModel.OpenAsync("/tmp/depo");
        return viewModel;
    }

    [AvaloniaFact]
    public async Task Depo_acilinca_en_yeni_commit_secilir()
    {
        // Rather than meeting an empty details panel, let the user see something straight away.
        CommitListViewModel viewModel = await LoadedAsync();

        viewModel.SelectedIndex.ShouldBe(0);
        viewModel.SelectedRow.ShouldBeSameAs(viewModel.Rows[0]);
    }

    [AvaloniaFact]
    public async Task Bos_depoda_secim_olusmaz()
    {
        CommitListViewModel viewModel = await LoadedAsync(0);

        viewModel.SelectedIndex.ShouldBe(-1);
        viewModel.SelectedRow.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Secili_satir_indeksten_turetilir()
    {
        CommitListViewModel viewModel = await LoadedAsync();

        viewModel.SelectedIndex = 3;

        viewModel.SelectedRow.ShouldBeSameAs(viewModel.Rows[3]);
    }

    [AvaloniaFact]
    public async Task Seciliyken_sayfa_kadar_ilerlenir()
    {
        CommitListViewModel viewModel = await LoadedAsync(50);
        viewModel.SelectedIndex = 10;

        viewModel.MoveSelection(20).ShouldBeTrue();

        viewModel.SelectedIndex.ShouldBe(30);
    }

    [AvaloniaFact]
    public async Task Liste_sinirlarinda_durulur_sarmalanmaz()
    {
        // Wrapping around means the user losing their place at the end of a long list.
        CommitListViewModel viewModel = await LoadedAsync(10);

        viewModel.SelectedIndex = 8;
        viewModel.MoveSelection(100).ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(9);

        viewModel.MoveSelection(100).ShouldBeFalse();
        viewModel.SelectedIndex.ShouldBe(9);

        viewModel.MoveSelection(-100).ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Secim_yokken_ilk_hareket_listenin_ucundan_baslar()
    {
        // The first row is selected when a repository is opened; the no-selection state arises when the
        // user clears the selection (dropping everything in a multiple selection, say).
        CommitListViewModel viewModel = await LoadedAsync(10);

        viewModel.SelectedIndex = -1;
        viewModel.MoveSelection(1).ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(0);

        viewModel.SelectedIndex = -1;
        viewModel.MoveSelection(-1).ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(9);
    }

    [AvaloniaFact]
    public async Task Bos_listede_gezinme_cokmez()
    {
        CommitListViewModel viewModel = await LoadedAsync(0);

        viewModel.MoveSelection(5).ShouldBeFalse();
        viewModel.GoToParent().ShouldBeFalse();
        viewModel.GoToChild().ShouldBeFalse();
        viewModel.SelectedIndex.ShouldBe(-1);
    }

    [AvaloniaFact]
    public async Task Ebeveyne_atlanir()
    {
        // LinearHistory is newest to oldest: row 0 = commit 10, its parent row 1.
        CommitListViewModel viewModel = await LoadedAsync(10);
        viewModel.SelectedIndex = 0;

        viewModel.GoToParent().ShouldBeTrue();

        viewModel.SelectedIndex.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Cocuga_atlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(10);
        viewModel.SelectedIndex = 5;

        viewModel.GoToChild().ShouldBeTrue();

        viewModel.SelectedIndex.ShouldBe(4);
    }

    [AvaloniaFact]
    public async Task Kok_commitin_ebeveyni_yok()
    {
        CommitListViewModel viewModel = await LoadedAsync(10);
        viewModel.SelectedIndex = 9;

        viewModel.GoToParent().ShouldBeFalse();
        viewModel.SelectedIndex.ShouldBe(9);
    }

    [AvaloniaFact]
    public async Task Dal_ucunun_cocugu_yok()
    {
        CommitListViewModel viewModel = await LoadedAsync(10);
        viewModel.SelectedIndex = 0;

        viewModel.GoToChild().ShouldBeFalse();
        viewModel.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Birlesme_commitinde_ilk_ebeveyne_gidilir()
    {
        // The merge's second parent is further down the list; the "mainline" is the first parent.
        //   row 0: merge (parents: 3, 2)
        //   row 1: commit 3   ← the first parent
        //   row 2: commit 2
        //   row 3: commit 1
        CommitInfo[] commits =
        [
            FakeGitData.Commit(FakeGitData.Sha(4), [FakeGitData.Sha(3), FakeGitData.Sha(2)], "merge"),
            FakeGitData.Commit(FakeGitData.Sha(3), [FakeGitData.Sha(1)], "ust dal"),
            FakeGitData.Commit(FakeGitData.Sha(2), [FakeGitData.Sha(1)], "yan dal"),
            FakeGitData.Commit(FakeGitData.Sha(1), [], "kok"),
        ];

        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(commits),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader());

        await viewModel.OpenAsync("/tmp/depo");

        viewModel.SelectedIndex = 0;
        viewModel.GoToParent().ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(1);

        // The child scan must work for a commit whose parent has more than one child as well.
        viewModel.SelectedIndex = 3;
        viewModel.GoToChild().ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task Tam_sha_ile_atlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);

        viewModel.TryGoToCommit(FakeGitData.Sha(15)).ShouldBeTrue();

        viewModel.SelectedRow!.Commit.Id.Value.ShouldBe(FakeGitData.Sha(15));
    }

    [AvaloniaFact]
    public async Task Kisa_sha_onekiyle_atlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);

        // Sha(7) = 39 zeros plus "7"; its prefix is 4 zeros — all 20 commits start with zeros, so the
        // first matching row should be selected (row 0 = commit 20).
        viewModel.TryGoToCommit("0000").ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Cok_kisa_veya_bulunamayan_onek_secimi_bozmaz()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);
        viewModel.SelectedIndex = 5;

        // git's lower bound is 4 characters.
        viewModel.TryGoToCommit("00").ShouldBeFalse();

        // A valid length, but matching no commit.
        viewModel.TryGoToCommit("dead").ShouldBeFalse();

        viewModel.SelectedIndex.ShouldBe(5);
    }

    [AvaloniaFact]
    public async Task Arama_bulunamayinca_durum_bildirir()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);

        viewModel.SearchText = "dead";
        viewModel.ApplySearch();

        viewModel.SearchStatus.ShouldNotBeNullOrEmpty();

        // The warning must disappear once typing resumes; the old error must not stick to the new search.
        viewModel.SearchText = "0000";
        viewModel.SearchStatus.ShouldBeNull();

        viewModel.ApplySearch();
        viewModel.SearchStatus.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Yeni_depo_acilinca_secim_ve_indeks_sifirlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);
        viewModel.SelectedIndex = 7;

        await viewModel.OpenAsync("/tmp/baska-depo");

        // A new repository opens on its own newest commit; it does not stay on the old row 7.
        viewModel.SelectedIndex.ShouldBe(0);

        // Had the old repository's index remained, the same SHAs would lead to the wrong rows.
        viewModel.TryGoToCommit(FakeGitData.Sha(15)).ShouldBeTrue();
        viewModel.SelectedRow!.Commit.Id.Value.ShouldBe(FakeGitData.Sha(15));
    }
}
