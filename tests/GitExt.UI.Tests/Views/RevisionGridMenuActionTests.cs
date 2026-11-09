using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P14-T03 … P14-T05 — the context menu entries that stopped being dead.
/// </summary>
/// <remarks>
/// A menu entry can be enabled and still do nothing: the model says it may be pressed, and the
/// view's dispatcher has no case for it. That is exactly what "Go to commit" was before P14-T05,
/// so these tests press the <b>real menu item</b> and check that the action ran.
/// </remarks>
public class RevisionGridMenuActionTests
{
    private static async Task<CommitListViewModel> LoadedAsync(FakeTagWriter? tags = null)
    {
        CommitListViewModel model = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(6)),
            new FakeRefReader(FakeGitData.Refs(
                localBranches:
                [
                    FakeGitData.LocalBranch("main", FakeGitData.Sha(6), isCurrent: true),
                    FakeGitData.LocalBranch("feature", FakeGitData.Sha(4)),
                ],
                remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(6))],
                tags: [FakeGitData.Tag("v1.0", FakeGitData.Sha(2), annotated: true)])),
            new FakeCommitSignatureReader(),
            new FakeDiffReader([FakeGitData.Diff("a.cs")]),
            tags ?? new FakeTagWriter());

        await model.OpenAsync("/tmp/depo");

        return model;
    }

    private static (CommitListView View, Window Window) Show(CommitListViewModel model)
    {
        CommitListView view = new() { DataContext = model };
        view.AttachShortcuts(TestCommands.Registry());

        Window window = new() { Width = 900, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (view, window);
    }

    /// <summary>Opens the real context menu and hands back its materialised items.</summary>
    private static IEnumerable<MenuItem> OpenMenu(CommitListView view)
    {
        ContextMenu menu = view.GetControl<ContextMenu>("CommitContextMenu");

        // `ContextMenu.Open` does not raise `Opening` in the headless backend (measured: the menu
        // came back empty), so the handler the XAML binds is called directly.
        view.OnContextMenuOpening(menu, new System.ComponentModel.CancelEventArgs());
        Dispatcher.UIThread.RunJobs();

        return Walk(menu.ItemsSource!.Cast<object>());

        static IEnumerable<MenuItem> Walk(IEnumerable<object> items)
        {
            foreach (object item in items)
            {
                if (item is not MenuItem menuItem)
                {
                    continue;
                }

                yield return menuItem;

                if (menuItem.ItemsSource is { } children)
                {
                    foreach (MenuItem child in Walk(children.Cast<object>()))
                    {
                        yield return child;
                    }
                }
            }
        }
    }

    /// <summary>Selects two rows the way the user would with Ctrl-click.</summary>
    private static void SelectTwo(CommitListView view, CommitListViewModel model)
    {
        ListBox list = view.GetControl<ListBox>("CommitList");

        model.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        list.SelectedItems!.Add(model.Rows[2]);
        Dispatcher.UIThread.RunJobs();
    }

    private static MenuItem Find(CommitListView view, RevisionGridMenuId id) =>
        OpenMenu(view).First(item => (RevisionGridMenuId?)item.Tag == id);

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    // ---------------------------------------------------------------- P14-T04: compare

    [AvaloniaFact]
    public async Task Dal_ile_karsilastir_secilen_dali_SOL_tarafa_koyar()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        model.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        IReadOnlyList<string>? offered = null;
        view.BranchToComparePrompt = branches =>
        {
            offered = branches;
            return Task.FromResult<string?>("feature");
        };

        CompareViewModel? requested = null;
        view.ComparisonRequested += (_, compare) => requested = compare;

        Click(Find(view, RevisionGridMenuId.CompareToBranch));

        // GitExtensions offers local branches AND remote ones
        // (`branchSelector.Initialize(remote: true, …)`).
        offered.ShouldBe(["main", "feature", "origin/main"]);

        requested.ShouldNotBeNull();

        // `ShowFormDiff(baseCommit /* the branch */, headCommit /* the clicked commit */, …)`;
        // swapping the sides would show every change inverted.
        requested.FromRevision.ShouldBe("feature");
        requested.ToRevision.ShouldBe(model.Rows[1].Commit.Id.Value);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Dal_secilmezse_karsilastirma_penceresi_acilmaz()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        model.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        view.BranchToComparePrompt = _ => Task.FromResult<string?>(null);

        bool opened = false;
        view.ComparisonRequested += (_, _) => opened = true;

        Click(Find(view, RevisionGridMenuId.CompareToBranch));

        opened.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Taban_sec_sonra_tabanla_karsilastir()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        model.SelectedIndex = 4;
        Dispatcher.UIThread.RunJobs();

        Click(Find(view, RevisionGridMenuId.SelectAsBase));

        model.BaseCommitToCompare.ShouldBe(model.Rows[4].Commit.Id);

        // Only now does the entry come alive — before this it was visible but dead.
        Find(view, RevisionGridMenuId.CompareToBase).IsEnabled.ShouldBeTrue();

        model.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        CompareViewModel? requested = null;
        view.ComparisonRequested += (_, compare) => requested = compare;

        Click(Find(view, RevisionGridMenuId.CompareToBase));

        requested.ShouldNotBeNull();
        requested.FromRevision.ShouldBe(model.Rows[4].Commit.Id.Value);
        requested.ToRevision.ShouldBe(model.Rows[1].Commit.Id.Value);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Taban_secilmeden_menu_ogesi_basilamaz()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        model.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        MenuItem item = Find(view, RevisionGridMenuId.CompareToBase);

        item.IsVisible.ShouldBeTrue();
        item.IsEnabled.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Difftool_ogesi_ikili_secimde_basilabilir()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        SelectTwo(view, model);

        Find(view, RevisionGridMenuId.OpenCommitsWithDiffTool).IsEnabled.ShouldBeTrue();

        window.Close();
    }

    // ---------------------------------------------------------------- P14-T05: go to commit

    [AvaloniaFact]
    public async Task Commite_git_secimi_tasir()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        model.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        string target = model.Rows[3].Commit.Id.Value;
        view.GoToCommitPrompt = _ => Task.FromResult<string?>(target);

        Click(Find(view, RevisionGridMenuId.GoToCommit));

        model.SelectedIndex.ShouldBe(3);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Commite_git_iptal_edilirse_secim_yerinde_kalir()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        model.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        view.GoToCommitPrompt = _ => Task.FromResult<string?>(null);

        Click(Find(view, RevisionGridMenuId.GoToCommit));

        model.SelectedIndex.ShouldBe(2);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Commite_git_dialoga_cozumleyiciyi_verir()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        model.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        Func<string?, bool>? resolver = null;
        view.GoToCommitPrompt = canResolve =>
        {
            resolver = canResolve;
            return Task.FromResult<string?>(null);
        };

        Click(Find(view, RevisionGridMenuId.GoToCommit));

        // Without this the dialog cannot tell a typo apart from a valid ref and would close on
        // both, dropping what the user typed.
        resolver.ShouldNotBeNull();
        resolver("feature").ShouldBeTrue();
        resolver("boyle-bir-sey-yok").ShouldBeFalse();

        window.Close();
    }

    // ---------------------------------------------------------------- P14-T03: tags

    [AvaloniaFact]
    public async Task Etiket_sil_alt_menusu_ADI_KOPYALAMAZ_siler()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        // The tag sits on Sha(2); rows run newest first, so that is index 4.
        model.SelectedIndex = 4;
        Dispatcher.UIThread.RunJobs();

        RecordingTagPrompt prompt = new();
        model.TagPrompt = prompt;

        MenuItem deleteTag = Find(view, RevisionGridMenuId.DeleteTag);
        MenuItem tagItem = deleteTag.ItemsSource!.Cast<MenuItem>().Single();

        // 🔴 The same DynamicRef id is a branch under "Checkout branch" and a tag here; without the
        // parent the click landed on the generic "copy the name" case and the tag was never deleted.
        ((RevisionGridMenuId?)tagItem.Tag).ShouldBe(RevisionGridMenuId.DynamicRef);

        Click(tagItem);

        prompt.DeleteRequested.ShouldBe("v1.0");

        window.Close();
    }

    private sealed class RecordingTagPrompt : ITagPrompt
    {
        public string? DeleteRequested { get; private set; }

        public bool CreateRequested { get; private set; }

        public Task<CreateTagDecision> RequestCreateAsync(CreateTagRequest request)
        {
            CreateRequested = true;
            return Task.FromResult(CreateTagDecision.Cancelled);
        }

        public Task<DeleteTagDecision> RequestDeleteAsync(DeleteTagRequest request)
        {
            DeleteRequested = request.Name;
            return Task.FromResult(DeleteTagDecision.Cancelled);
        }
    }

    [AvaloniaFact]
    public async Task Tek_secimde_ikili_ogeler_menude_YOK()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        model.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        RevisionGridMenuId?[] shown = [.. OpenMenu(view).Select(item => (RevisionGridMenuId?)item.Tag)];

        shown.ShouldNotContain(RevisionGridMenuId.CompareSelectedCommits);
        shown.ShouldNotContain(RevisionGridMenuId.OpenCommitsWithDiffTool);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Iki_secimde_secili_commitleri_karsilastir_beliriyor()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        SelectTwo(view, model);

        MenuItem compare = Find(view, RevisionGridMenuId.CompareSelectedCommits);

        compare.IsEnabled.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Iki_secimde_karsilastirma_ESKIDEN_YENIYE_aciliyor()
    {
        CommitListViewModel model = await LoadedAsync();
        (CommitListView view, Window window) = Show(model);

        CompareViewModel? opened = null;
        view.ComparisonRequested += (_, compare) => opened = compare;

        SelectTwo(view, model);

        Click(Find(view, RevisionGridMenuId.CompareSelectedCommits));

        opened.ShouldNotBeNull();

        window.Close();
    }
}
