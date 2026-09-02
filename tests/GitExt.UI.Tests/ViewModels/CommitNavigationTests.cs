using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T21 — the Navigate submenu: back/forward, parents, HEAD, merge base.
/// </summary>
/// <remarks>
/// The history records <b>where the user went</b>, not every selection change — stepping back must
/// not itself become a step to come back from, or Back and Forward would fight each other.
/// </remarks>
public class CommitNavigationTests
{
    private sealed class FakeMergeBaseReader : IMergeBaseReader
    {
        public List<IReadOnlyList<string>> Asked { get; } = [];

        public CommitId? Result { get; set; }

        public Exception? Failure { get; set; }

        public Task<CommitId?> FindAsync(
            string workingDirectory,
            IReadOnlyList<string> revisions,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(revisions);

            return Failure is not null
                ? Task.FromException<CommitId?>(Failure)
                : Task.FromResult(Result);
        }
    }

    private static async Task<CommitListViewModel> LoadedAsync(
        IMergeBaseReader? mergeBase = null,
        int commits = 6)
    {
        CommitListViewModel model = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(commits)),
            new FakeRefReader(FakeGitData.Refs(
                localBranches:
                [
                    FakeGitData.LocalBranch("main", FakeGitData.Sha(commits), isCurrent: true),
                ])),
            new FakeCommitSignatureReader(),
            new FakeDiffReader(),
            mergeBase: mergeBase);

        await model.OpenAsync("/tmp/depo");

        return model;
    }

    [AvaloniaFact]
    public async Task Bastayken_geri_gidilemez()
    {
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 0;

        model.CanNavigateBackward.ShouldBeFalse();
        model.CanNavigateForward.ShouldBeFalse();
        model.NavigateBackward().ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Gezdikten_sonra_geri_donuluyor()
    {
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 0;
        model.SelectedIndex = 3;

        model.CanNavigateBackward.ShouldBeTrue();

        model.NavigateBackward().ShouldBeTrue();
        model.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Geri_gittikten_sonra_ileri_donuluyor()
    {
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 0;
        model.SelectedIndex = 3;
        model.NavigateBackward();

        model.CanNavigateForward.ShouldBeTrue();

        model.NavigateForward().ShouldBeTrue();
        model.SelectedIndex.ShouldBe(3);
    }

    [AvaloniaFact]
    public async Task Geri_gitmek_YENI_bir_adim_saymiyor()
    {
        // 🔴 Without this, Back would push its own destination onto the history and the two
        // buttons would bounce between the same two rows for ever.
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 0;
        model.SelectedIndex = 2;
        model.SelectedIndex = 4;

        model.NavigateBackward().ShouldBeTrue();
        model.SelectedIndex.ShouldBe(2);

        model.NavigateBackward().ShouldBeTrue();
        model.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Iki_geri_sonra_iki_ileri_baslangica_donuyor()
    {
        // 🔴 This is the one that catches a missing `_navigating` guard. With Back recording its
        // own destination the history is rewritten as it is walked, and the SECOND forward step
        // finds nothing left to go to — even though the first three steps still look right.
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 0;
        model.SelectedIndex = 2;
        model.SelectedIndex = 4;

        model.NavigateBackward().ShouldBeTrue();
        model.NavigateBackward().ShouldBeTrue();
        model.SelectedIndex.ShouldBe(0);

        model.NavigateForward().ShouldBeTrue();
        model.SelectedIndex.ShouldBe(2);

        model.NavigateForward().ShouldBeTrue();
        model.SelectedIndex.ShouldBe(4);
    }

    [AvaloniaFact]
    public async Task Geri_gidip_baska_yere_atlayinca_ileri_KAYBOLUYOR()
    {
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 0;
        model.SelectedIndex = 3;
        model.NavigateBackward();

        model.CanNavigateForward.ShouldBeTrue();

        model.SelectedIndex = 5;

        model.CanNavigateForward.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Ayni_satiri_tekrar_secmek_gecmise_ikinci_kez_eklenmiyor()
    {
        // The list opens with a row already selected, so that first entry is in the history
        // before the test touches anything; what is asserted here is that selecting the same row
        // twice does not add a second step to walk back through.
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 2;
        model.SelectedIndex = 2;

        model.NavigateBackward().ShouldBeTrue();
        model.SelectedIndex.ShouldBe(0);

        model.CanNavigateBackward.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Son_ebeveyne_gidiliyor()
    {
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 0;

        CommitInfo commit = model.Rows[0].Commit;

        model.GoToLastParent().ShouldBeTrue();
        model.SelectedRow!.Commit.Id.ShouldBe(commit.Parents[^1]);
    }

    [AvaloniaFact]
    public async Task Ebeveyni_olmayan_committe_gidilemez()
    {
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = model.Rows.Count - 1;

        model.Rows[^1].Commit.Parents.ShouldBeEmpty();
        model.GoToLastParent().ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Bulunulan_surume_gidiliyor()
    {
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 4;

        model.GoToCurrentRevision().ShouldBeTrue();
        model.SelectedRow!.Commit.Id.ShouldBe(model.Refs!.CurrentBranch!.Ref.ObjectId);
    }

    [AvaloniaFact]
    public async Task Ortak_ata_bulunup_ona_gidiliyor()
    {
        FakeMergeBaseReader mergeBase = new();
        CommitListViewModel model = await LoadedAsync(mergeBase);

        model.SelectedIndex = 0;
        mergeBase.Result = model.Rows[3].Commit.Id;

        (await model.GoToMergeBaseAsync()).ShouldBeTrue();

        model.SelectedIndex.ShouldBe(3);
        mergeBase.Asked.Single().ShouldContain("HEAD");
    }

    [AvaloniaFact]
    public async Task Ortak_ata_yoksa_HATA_DEGIL_bilgi_veriliyor()
    {
        FakeMergeBaseReader mergeBase = new() { Result = null };
        CommitListViewModel model = await LoadedAsync(mergeBase);

        model.SelectedIndex = 0;

        (await model.GoToMergeBaseAsync()).ShouldBeFalse();

        model.SearchStatus.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public async Task Ortak_ata_yuklu_gecmisin_disindaysa_soyleniyor()
    {
        FakeMergeBaseReader mergeBase = new()
        {
            Result = CommitId.Parse(new string('e', 40)),
        };

        CommitListViewModel model = await LoadedAsync(mergeBase);

        model.SelectedIndex = 0;

        (await model.GoToMergeBaseAsync()).ShouldBeFalse();

        model.SearchStatus.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public async Task Merge_base_okuyucusu_yokken_sessizce_geciliyor()
    {
        CommitListViewModel model = await LoadedAsync();

        model.SelectedIndex = 0;

        (await model.GoToMergeBaseAsync()).ShouldBeFalse();
    }
}
