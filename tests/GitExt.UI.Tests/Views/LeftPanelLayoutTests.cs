using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P12-T13/T14 — the left panel's toolbar and its per-node menus.
/// </summary>
/// <remarks>
/// The expected orders come from GitExtensions' <c>RepoObjectsTree.Designer.cs</c>: the toolbar
/// (<c>leftPanelToolStrip.Items</c>) and the single <c>menuMain</c> whose items appear according
/// to the node that was clicked.
/// </remarks>
public class LeftPanelLayoutTests
{
    private static RefTreeData Data() => new()
    {
        Refs = FakeGitData.Refs(
            localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true)],
            remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1))],
            tags: [FakeGitData.Tag("v1.0", FakeGitData.Sha(1))]),
        RootPath = "/tmp/depo",
        WorkTrees = [new WorkTree { Path = "/tmp/depo-hotfix", BranchName = "hotfix" }],
        Submodules =
        [
            new Submodule
            {
                Path = RepositoryPath.Parse("libs/core"),
                ObjectId = FakeGitData.Sha(2),
                Status = SubmoduleStatusKind.UpToDate,
            },
        ],
        Stashes = [new StashEntry { Selector = "stash@{0}", ObjectId = FakeGitData.Sha(3), Message = "iş", Index = 0 }],
    };

    private static (Window Window, RefTreeView View, RefTreeViewModel Model) Show()
    {
        RefTreeViewModel model = new();
        model.Load(Data());

        RefTreeView view = new() { DataContext = model, Width = 240, Height = 500 };
        Window window = new() { Width = 300, Height = 520, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, view, model);
    }

    private static string[] ToolbarNames(Visual toolbar) =>
        [.. toolbar.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.Name is { Length: > 0 } name
                && !name.StartsWith("PART_", StringComparison.Ordinal)
                && c is Button or ToggleButton)
            .Select(c => c.Name!)];

    /// <summary>The menu items visible for the selected node, in order.</summary>
    private static string[] VisibleMenu(RefTreeView view)
    {
        ContextMenu menu = view.GetControl<ContextMenu>("RefContextMenu");
        menu.Open(view.GetControl<TreeView>("RefTree"));
        Dispatcher.UIThread.RunJobs();

        string[] names = [.. menu.Items.OfType<MenuItem>()
            .Where(item => item.IsVisible)
            .Select(item => item.Name ?? string.Empty)];

        menu.Close();
        Dispatcher.UIThread.RunJobs();

        return names;
    }

    [AvaloniaFact]
    public void Panel_arac_cubugu_GitExtensions_sirasinda()
    {
        // collapse all · branches · remotes · worktrees · tags · submodules · stashes
        (Window window, RefTreeView view, _) = Show();

        ToolbarNames(view.GetControl<Border>("PanelToolbar")).ShouldBe([
            "PanelCollapseAll",
            "PanelShowBranches",
            "PanelShowRemotes",
            "PanelShowWorkTrees",
            "PanelShowTags",
            "PanelShowSubmodules",
            "PanelShowStashes",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public void Arac_cubugu_dugmesi_bolumu_GERCEKTEN_gizliyor()
    {
        (Window window, RefTreeView view, RefTreeViewModel model) = Show();

        model.Roots.Select(r => r.Name).ShouldContain("Stashes");

        ToggleButton toggle = view.GetControl<ToggleButton>("PanelShowStashes");
        toggle.IsChecked.ShouldBe(true);

        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        model.Roots.Select(r => r.Name).ShouldNotContain("Stashes");

        window.Close();
    }

    [AvaloniaFact]
    public void Stash_dugumunun_menusu_GitExtensions_sirasinda()
    {
        // `menuMain`: Open · Apply · Pop · Drop.
        (Window window, RefTreeView view, RefTreeViewModel model) = Show();

        model.Selected = model.Roots.Single(r => r.Name == "Stashes").Children.Single();
        Dispatcher.UIThread.RunJobs();

        string[] visible = VisibleMenu(view);

        visible.ShouldContain("MenuRefStashOpen");
        visible.ShouldContain("MenuRefStashApply");
        visible.ShouldContain("MenuRefStashPop");
        visible.ShouldContain("MenuRefStashDrop");

        // …and the items of the other kinds are NOT there.
        visible.ShouldNotContain("MenuRefFetchAll");
        visible.ShouldNotContain("MenuRefUpdateSubmodule");

        int open = Array.IndexOf(visible, "MenuRefStashOpen");
        int drop = Array.IndexOf(visible, "MenuRefStashDrop");
        open.ShouldBeLessThan(drop);

        window.Close();
    }

    [AvaloniaFact]
    public void Uzaklar_basliginin_menusu_fetch_ogelerini_tasiyor()
    {
        // This is where someone looking for "fetch everything" right-clicks.
        (Window window, RefTreeView view, RefTreeViewModel model) = Show();

        model.Selected = model.Roots.Single(r => r.Name == "Remotes");
        Dispatcher.UIThread.RunJobs();

        string[] visible = VisibleMenu(view);

        visible.ShouldContain("MenuRefManageRemotes");
        visible.ShouldContain("MenuRefFetchAll");
        visible.ShouldContain("MenuRefFetchPruneAll");
        visible.ShouldNotContain("MenuRefStashApply");

        window.Close();
    }

    [AvaloniaFact]
    public void Alt_modul_ve_calisma_agaci_ACILABILIR()
    {
        (Window window, RefTreeView view, RefTreeViewModel model) = Show();

        model.Selected = model.Roots.Single(r => r.Name == "Submodules").Children.Single();
        Dispatcher.UIThread.RunJobs();
        VisibleMenu(view).ShouldContain("MenuRefOpenSubmodule");

        model.Selected = model.Roots.Single(r => r.Name == "Worktrees").Children.Single();
        Dispatcher.UIThread.RunJobs();
        VisibleMenu(view).ShouldContain("MenuRefOpenWorkTree");

        window.Close();
    }

    [AvaloniaFact]
    public void Tumunu_daralt_dugmesi_agaci_GERCEKTEN_kapatiyor()
    {
        // 🔴 The node's `IsExpanded` was never bound to the container: the property existed, the
        // panel ignored it, and every section was drawn collapsed. "Collapse all" would have been
        // a dead button — the kind of defect that only shows up when someone looks at the screen.
        (Window window, RefTreeView view, _) = Show();

        TreeView tree = view.GetControl<TreeView>("RefTree");

        TreeViewItem[] Items() => [.. tree.GetVisualDescendants().OfType<TreeViewItem>()];

        Items().ShouldAllBe(item => item.IsExpanded);

        Click(window, view.GetControl<Button>("PanelCollapseAll"));

        Items().ShouldAllBe(item => !item.IsExpanded);

        window.Close();
    }

    /// <summary>Clicks a control the way a person does: press and release over it.</summary>
    private static void Click(Window window, Control control)
    {
        Point centre = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window)!.Value;

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Bolum_secimleri_AYARLARA_yaziliyor_ve_geri_okunuyor()
    {
        // 🔴 A toggle that is forgotten on the next start is worse than no toggle: the user
        // switches the same section off every morning.
        InMemorySettingsStore settings = new();
        settings.Update(s => s.Layout.LeftPanel.Stashes = false);

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore());

        MainWindow window = new() { DataContext = model, Width = 900, Height = 600 };
        window.AttachLayout(settings);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // What was saved is applied…
        model.RefTree.ShowStashes.ShouldBeFalse();
        model.RefTree.ShowBranches.ShouldBeTrue();

        // …and a change is written back.
        model.RefTree.ShowTags = false;
        Dispatcher.UIThread.RunJobs();

        settings.Current.Layout.LeftPanel.Tags.ShouldBeFalse();

        window.Close();
    }
}
