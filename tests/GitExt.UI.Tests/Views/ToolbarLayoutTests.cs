using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P12-T06/T07 — the toolbars follow GitExtensions.
/// </summary>
/// <remarks>
/// <para>
/// The expected orders are GitExtensions' own: the main toolbar from
/// <c>FormBrowse.Designer.cs</c> (<c>ToolStripMain.Items.AddRange</c>) and the filter bar from
/// <c>FilterToolBar.Designer.cs</c> (<c>Items.AddRange</c>).
/// </para>
/// <para>
/// What is tested is <b>order and enabled state</b>, not appearance. Commands that are not
/// implemented yet stay in place, disabled — slotting one in later would move everything else and
/// break the muscle memory this whole phase exists to protect.
/// </para>
/// </remarks>
public class ToolbarLayoutTests
{
    private static RepositoryRefs Refs() => FakeGitData.Refs(
        localBranches:
        [
            FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true),
            FakeGitData.LocalBranch("feature/login", FakeGitData.Sha(2)),
        ],
        remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1))],
        tags: []);

    private static MainWindowViewModel CreateViewModel() =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore());

    private static async Task<(MainWindow Window, MainWindowViewModel Model)> ShowAsync(bool openRepository = true)
    {
        MainWindowViewModel model = CreateViewModel();

        MainWindow window = new() { Width = 1200, Height = 700 };

        // The registry has to be attached: the toolbar takes its commands AND its tooltips from
        // there, and without it `BuildShortcuts` never runs.
        window.AttachShortcuts(TestCommands.Registry());
        window.DataContext = model;

        window.Show();
        Dispatcher.UIThread.RunJobs();

        if (openRepository)
        {
            await model.OpenRepositoryAsync("/tmp/depo");
            Dispatcher.UIThread.RunJobs();
        }

        return (window, model);
    }

    /// <summary>The names of the toolbar's own controls, left to right.</summary>
    private static string[] ItemNames(Visual toolbar) =>
        [.. toolbar.GetVisualDescendants()
            .OfType<Control>()
            // A SplitButton carries two Buttons of its own inside its template
            // (PART_PrimaryButton / PART_SecondaryButton); those are not toolbar items.
            .Where(c => c.Name is { Length: > 0 } name
                && !name.StartsWith("PART_", StringComparison.Ordinal)
                && (c is Button or SplitButton or DropDownButton or ComboBox or TextBox or CheckBox))
            .Select(c => c.Name!)];

    private static string[] MenuRows(MenuFlyout flyout) =>
        [.. flyout.Items.Select(item => item switch
        {
            Separator => "──",
            MenuItem entry => entry.Header?.ToString() ?? string.Empty,
            _ => "?",
        })];

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
    public async Task Ana_arac_cubugu_GitExtensions_sirasinda()
    {
        (MainWindow window, _) = await ShowAsync();

        // Refresh · ─ · left panel · split view · commit info position · ─ · submodules ·
        // working dir · branch · ─ · pull · push · commit · stash · ─ · file explorer ·
        // terminal · settings.
        ItemNames(window.GetControl<Border>("MainToolbar")).ShouldBe([
            "ToolRefresh",
            "ToolToggleLeftPanel",
            "ToolToggleBottomPanel",
            "ToolCommitInfoPosition",
            "ToolSubmodules",
            "ToolWorkingDir",
            "ToolBranchSelect",
            "ToolPull",
            "ToolPush",
            "ToolCommit",
            "ToolStash",
            "ToolFileExplorer",
            "ToolTerminal",
            "ToolSettings",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Uygulanmamis_arac_dugmeleri_KALDIRILMIYOR_devre_disi()
    {
        (MainWindow window, _) = await ShowAsync();

        window.GetControl<Button>("ToolSubmodules").IsEnabled.ShouldBeFalse();
        window.GetControl<Button>("ToolTerminal").IsEnabled.ShouldBeFalse();
        window.GetControl<DropDownButton>("ToolCommitInfoPosition").IsEnabled.ShouldBeFalse();

        // …and the ones that ARE implemented are usable — otherwise the test above proves nothing.
        window.GetControl<Button>("ToolRefresh").IsEnabled.ShouldBeTrue();
        window.GetControl<Button>("ToolFileExplorer").IsEnabled.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Arac_ipuclari_kayittan_geliyor_ve_KISAYOLU_yaziyor()
    {
        // 🔴 A toolbar button shows no text, so its tooltip is the only place its name and its
        // shortcut are written. They come from the command registry — the same source as the key
        // that actually works, so the two cannot drift apart (this class exists because they did).
        (MainWindow window, _) = await ShowAsync();

        string tip = ToolTip.GetTip(window.GetControl<Button>("ToolRefresh"))?.ToString() ?? string.Empty;

        // The NAME is not asserted: the registry's titles are translated and the language is a
        // process-wide setting other tests move around. What matters here is that the tooltip is
        // filled in at all and that it carries the gesture.
        tip.ShouldNotBeNullOrWhiteSpace();
        tip.ShouldContain("F5");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Pull_menusu_GitExtensions_sirasinda()
    {
        (MainWindow window, _) = await ShowAsync();

        SplitButton pull = window.GetControl<SplitButton>("ToolPull");

        MenuRows((MenuFlyout)pull.Flyout!).ShouldBe([
            "Open pull dialog...",
            "──",
            "Pull - merge",
            "Pull - rebase",
            "Fetch",
            "Fetch all",
            "Fetch and prune all",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Stash_menusu_GitExtensions_sirasinda()
    {
        (MainWindow window, _) = await ShowAsync();

        SplitButton stash = window.GetControl<SplitButton>("ToolStash");

        MenuRows((MenuFlyout)stash.Flyout!).ShouldBe([
            "Stash",
            "Stash staged",
            "Stash pop",
            "──",
            "Manage stashes",
            "Create a stash...",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Filtre_cubugu_GitExtensions_sirasinda()
    {
        (MainWindow window, _) = await ShowAsync();

        // advanced filter · reflog · show branches · branch combo · branch type · filter box ·
        // filter type · first parent.
        ItemNames(window.GetControl<Border>("FilterToolbar")).ShouldBe([
            "FilterAdvanced",
            "FilterShowReflog",
            "FilterShowBranches",
            "BranchFilterBox",
            "BranchFilterType",
            "RevisionFilterBox",
            "RevisionFilterType",
            "FilterFirstParent",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Filtre_cubugu_depo_yokken_GORUNMUYOR()
    {
        // On the dashboard there is nothing to filter; the bar would be a row of dead controls.
        (MainWindow window, _) = await ShowAsync(openRepository: false);

        window.GetControl<Border>("FilterToolbar").IsVisible.ShouldBeFalse();
        window.GetControl<Border>("MainToolbar").IsVisible.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Dal_dugmesi_bulunulan_dali_gosteriyor()
    {
        (MainWindow window, MainWindowViewModel model) = await ShowAsync();

        window.GetControl<TextBlock>("ToolBranchName").Text.ShouldBe("main");

        // The dropdown lists the local branches and the current one cannot be checked out again.
        model.Branches.Select(b => b.Name).ShouldBe(["feature/login", "main"]);
        model.Branches.Single(b => b.Name == "main").CanCheckout.ShouldBeFalse();
        model.Branches.Single(b => b.Name == "feature/login").CanCheckout.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Pencere_basligi_depo_ve_dali_tasiyor()
    {
        // 🔴 The strip that used to say "path — N commits" is gone; the title says it instead,
        // and the title is the only place the repository is named while the window is not
        // focused (task bar, window switcher).
        (MainWindow window, _) = await ShowAsync();

        string title = window.Title.ShouldNotBeNull();

        title.ShouldContain("depo");
        title.ShouldContain("main");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Arac_cubugundaki_panel_dugmeleri_panelleri_aciyor_kapatiyor()
    {
        (MainWindow window, _) = await ShowAsync();

        Border branchPanel = window.GetControl<Border>("BranchPanelHost");
        TabControl bottomPanel = window.GetControl<TabControl>("BottomPanel");

        bool branchVisible = branchPanel.IsVisible;
        bool bottomVisible = bottomPanel.IsVisible;

        // 🔴 A REAL click, not the command by hand. The buttons had a Click handler on top of the
        // command the registry attaches, so one click ran the toggle TWICE and the panel appeared
        // frozen. Executing the command directly would still pass with that bug in place; only
        // clicking catches it.
        Click(window, window.GetControl<Button>("ToolToggleLeftPanel"));

        branchPanel.IsVisible.ShouldNotBe(branchVisible);

        Click(window, window.GetControl<Button>("ToolToggleBottomPanel"));

        bottomPanel.IsVisible.ShouldNotBe(bottomVisible);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Filtre_kutusunda_Enter_filtreyi_uyguluyor()
    {
        (MainWindow window, MainWindowViewModel model) = await ShowAsync();

        TextBox filter = window.GetControl<TextBox>("RevisionFilterBox");
        filter.Text = "token";
        Dispatcher.UIThread.RunJobs();

        filter.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();

        model.Commits.FilterText.ShouldBe("token");
        model.Commits.HasActiveFilter.ShouldBeTrue();

        window.Close();
    }
}
