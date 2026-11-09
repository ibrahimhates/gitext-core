using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

public sealed class RevisionGridSortAndDefaultsTests
{
    private static (CommitListViewModel List, FakeCommitLogReader Log) Create(InMemorySettingsStore? store = null)
    {
        FakeCommitLogReader log = new(FakeGitData.LinearHistory(3));
        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            log,
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        if (store is not null)
        {
            list.AttachSettings(store);
        }

        return (list, log);
    }

    private static IReadOnlyList<RevisionGridMenuEntry> ViewMenu(RevisionGridMenuContext context) =>
        RevisionGridContextMenu.Build(context).First(entry => entry.Id == RevisionGridMenuId.View).Children;

    [AvaloniaFact]
    public async Task Varsayilan_sira_tarih_sirasi_ve_cocuk_once()
    {
        (CommitListViewModel list, FakeCommitLogReader log) = Create();

        await list.OpenAsync("/tmp/depo");

        list.SortOrder.ShouldBe(RevisionSortOrder.GitDefault);
        log.LastQuery!.TopologicalOrder.ShouldBeTrue();
        log.LastQuery.Order.ShouldBe(CommitLogOrder.Date);
        log.LastQuery.IncludeReflog.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Siralama_anahtarlari_birbirini_birakiyor_ve_yeniden_okuyor()
    {
        (CommitListViewModel list, FakeCommitLogReader log) = Create();
        await list.OpenAsync("/tmp/depo");

        await list.ToggleSortOrderAsync(RevisionSortOrder.AuthorDate);
        log.LastQuery!.Order.ShouldBe(CommitLogOrder.AuthorDate);

        await list.ToggleSortOrderAsync(RevisionSortOrder.Topology);
        list.SortOrder.ShouldBe(RevisionSortOrder.Topology);
        log.LastQuery!.Order.ShouldBe(CommitLogOrder.Topological);

        await list.ToggleSortOrderAsync(RevisionSortOrder.Topology);
        list.SortOrder.ShouldBe(RevisionSortOrder.GitDefault);
        log.LastQuery!.Order.ShouldBe(CommitLogOrder.Date);
    }

    [AvaloniaFact]
    public async Task Reflog_anahtari_sorguya_geciyor()
    {
        (CommitListViewModel list, FakeCommitLogReader log) = Create();
        await list.OpenAsync("/tmp/depo");

        await list.ToggleShowReflogReferencesAsync();

        log.LastQuery!.IncludeReflog.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Anahtarlar_kaydet_denmeden_ayara_yazilmiyor()
    {
        InMemorySettingsStore store = new();
        (CommitListViewModel list, _) = Create(store);
        await list.OpenAsync("/tmp/depo");

        await list.ToggleSortOrderAsync(RevisionSortOrder.Topology);
        await list.ToggleShowReflogReferencesAsync();
        await list.SetBranchModeCommand.ExecuteAsync(BranchFilterMode.CurrentBranch);

        store.Current.RevisionGrid.SortOrder.ShouldBe("");
        store.Current.RevisionGrid.ShowReflogReferences.ShouldBeFalse();
        store.Current.RevisionGrid.BranchScope.ShouldBe("");

        list.SaveViewAsDefault();

        store.Current.RevisionGrid.SortOrder.ShouldBe("Topology");
        store.Current.RevisionGrid.ShowReflogReferences.ShouldBeTrue();
        store.Current.RevisionGrid.BranchScope.ShouldBe("CurrentBranch");
    }

    [AvaloniaFact]
    public async Task Kaydedilen_varsayilan_acilista_okunuyor()
    {
        InMemorySettingsStore store = new();
        store.Current.RevisionGrid.SortOrder = "AuthorDate";
        store.Current.RevisionGrid.ShowReflogReferences = true;
        store.Current.RevisionGrid.BranchScope = "CurrentBranch";

        (CommitListViewModel list, FakeCommitLogReader log) = Create(store);
        await list.OpenAsync("/tmp/depo");

        list.BranchMode.ShouldBe(BranchFilterMode.CurrentBranch);
        log.LastQuery!.Order.ShouldBe(CommitLogOrder.AuthorDate);
        log.LastQuery.IncludeReflog.ShouldBeTrue();
        log.LastQuery.IncludeAllRefs.ShouldBeFalse();
    }

    [Fact]
    public void Suzulmus_kapsam_suzgecsiz_kaydedildiyse_tum_dallar_aciliyor()
    {
        InMemorySettingsStore store = new();
        store.Current.RevisionGrid.BranchScope = "FilteredBranches";

        (CommitListViewModel list, _) = Create(store);

        list.BranchMode.ShouldBe(BranchFilterMode.AllBranches);
    }

    [Fact]
    public void Bozuk_kayit_yalniz_o_alani_varsayilana_dusuruyor()
    {
        InMemorySettingsStore store = new();
        store.Current.RevisionGrid.SortOrder = "yanlis";
        store.Current.RevisionGrid.ShowReflogReferences = true;

        (CommitListViewModel list, _) = Create(store);

        list.SortOrder.ShouldBe(RevisionSortOrder.GitDefault);
        list.ShowReflogReferences.ShouldBeTrue();
    }

    [Fact]
    public void Menu_siralama_ve_reflog_isaretlerini_gosteriyor()
    {
        RevisionGridMenuContext context = new()
        {
            CommitHash = FakeGitData.Sha(1),
            SortOrder = RevisionSortOrder.AuthorDate,
            ShowReflogReferences = true,
        };

        IReadOnlyList<RevisionGridMenuEntry> view = ViewMenu(context);

        RevisionGridMenuEntry authorDate = view.First(e => e.Id == RevisionGridMenuId.AuthorDateSort);
        RevisionGridMenuEntry topo = view.First(e => e.Id == RevisionGridMenuId.TopoOrder);
        RevisionGridMenuEntry reflog = view.First(e => e.Id == RevisionGridMenuId.ShowReflogReferences);

        authorDate.IsEnabled.ShouldBeTrue();
        authorDate.IsChecked.ShouldBeTrue();
        topo.IsEnabled.ShouldBeTrue();
        topo.IsChecked.ShouldBeFalse();
        reflog.IsEnabled.ShouldBeTrue();
        reflog.IsChecked.ShouldBeTrue();
        view.First(e => e.Id == RevisionGridMenuId.SaveAsDefault).IsEnabled.ShouldBeTrue();
    }
}
