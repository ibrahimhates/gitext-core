using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P12-T03 — the dashboard's layout and its menus follow GitExtensions.
/// </summary>
/// <remarks>
/// <para>
/// The expected orders come from the GitExtensions source:
/// <c>UserRepositoriesList.Designer.cs</c> → <c>contextMenuStripRepository.Items.AddRange</c> and
/// <c>contextMenuStripCategory.Items.AddRange</c>.
/// </para>
/// <para>
/// The menus are <b>really opened</b>: a <c>ContextMenu</c> is not in the visual tree until it
/// opens and its bindings are not evaluated before that (measured in P06-T14) — a test that only
/// read the item objects would prove nothing about what the user sees.
/// </para>
/// </remarks>
public class DashboardLayoutTests
{
    private static MainWindowViewModel CreateViewModel(
        FakeRecentRepositoryStore store,
        Func<string, RepositoryHeadInfo>? probe = null)
    {
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            store);

        model.Dashboard.Probe = probe ?? (_ => new RepositoryHeadInfo(IsRepository: true, BranchName: "main"));

        return model;
    }

    private static async Task<(Window Window, WelcomeView View)> ShowAsync(MainWindowViewModel model)
    {
        await model.StartAsync(explicitPath: null);

        WelcomeView view = new() { DataContext = model };
        Window window = new() { Width = 1000, Height = 600, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, view);
    }

    /// <summary>The repository tiles on screen.</summary>
    private static IReadOnlyList<Button> Tiles(Visual root) =>
        [.. root.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.DataContext is DashboardRepositoryItem)];

    /// <summary>The visible rows of an open menu, separators included.</summary>
    private static string[] Rows(ItemsControl menu) =>
        [.. menu.Items
            .OfType<Control>()
            .Where(item => item.IsVisible)
            .Select(item => item switch
            {
                Separator => "──",
                MenuItem entry => entry.Header?.ToString() ?? string.Empty,
                _ => "?",
            })];

    private static string[] OpenMenu(Button tile)
    {
        ContextMenu menu = tile.ContextMenu!;
        menu.Open(tile);
        Dispatcher.UIThread.RunJobs();

        string[] rows = Rows(menu);

        menu.Close();
        Dispatcher.UIThread.RunJobs();

        return rows;
    }

    [AvaloniaFact]
    public async Task Depo_baglam_menusu_GitExtensions_sirasinda()
    {
        MainWindowViewModel model = CreateViewModel(new FakeRecentRepositoryStore("/r/bir"));
        (Window window, WelcomeView view) = await ShowAsync(model);

        // Show in folder · ─ · Categories · ─ · Remove project from the list
        // ("Remove missing projects" only appears when there is one — see below.)
        OpenMenu(Tiles(view).Single()).ShouldBe([
            "Show in folder",
            "──",
            "Categories",
            "──",
            "Remove project from the list",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Kayip_depo_varken_temizleme_ogesi_beliriyor()
    {
        // GitExtensions shows this item only when there is an unreachable entry
        // (`tsmiRemoveMissingReposFromList.Visible = _hasInvalidRepos`) — an action that would do
        // nothing is not offered.
        MainWindowViewModel model = CreateViewModel(
            new FakeRecentRepositoryStore("/r/bir", "/r/yok"),
            probe: path => path == "/r/yok"
                ? RepositoryHeadInfo.NotARepository
                : new RepositoryHeadInfo(IsRepository: true, BranchName: "main"));

        (Window window, WelcomeView view) = await ShowAsync(model);

        OpenMenu(Tiles(view)[0]).ShouldContain("Remove missing projects from the list");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Sag_tiklanan_kayit_secili_hale_geliyor()
    {
        // The menu items act on the selection, so opening the menu has to make that selection —
        // otherwise "Remove project from the list" would work on whatever was selected last.
        MainWindowViewModel model = CreateViewModel(new FakeRecentRepositoryStore("/r/bir", "/r/iki"));
        (Window window, WelcomeView view) = await ShowAsync(model);

        Button tile = Tiles(view).Single(t => ((DashboardRepositoryItem)t.DataContext!).Path == "/r/iki");

        tile.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        model.Dashboard.SelectedItem.ShouldNotBeNull().Path.ShouldBe("/r/iki");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Arama_kutusunda_Enter_ilk_depoyu_aciyor()
    {
        // GitExtensions' `TextBoxSearch_KeyDown`: typing part of a name and pressing Enter is the
        // fastest way in, without ever touching the mouse.
        MainWindowViewModel model = CreateViewModel(new FakeRecentRepositoryStore("/r/bir", "/r/iki"));
        (Window window, WelcomeView view) = await ShowAsync(model);

        model.Dashboard.SearchText = "iki";
        Dispatcher.UIThread.RunJobs();

        TextBox search = view.GetControl<TextBox>("SearchBox");
        search.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();

        // The commit list is fake, so what is asserted is that the OPEN really was attempted.
        model.Commits.Repository.ShouldNotBeNull().WorkingDirectory.ShouldBe("/r/iki");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Panel_yerlesimi_GitExtensions_olculerinde()
    {
        // `Dashboard.Designer.cs`: pnlLeft is 213 wide and the repository list takes the rest;
        // the logo strip on the left and the header band on the right are the same height, so
        // the two lines meet.
        MainWindowViewModel model = CreateViewModel(new FakeRecentRepositoryStore("/r/bir"));
        (Window window, WelcomeView view) = await ShowAsync(model);

        Grid root = view.GetVisualDescendants().OfType<Grid>().First();

        root.ColumnDefinitions[0].Width.Value.ShouldBe(213);
        root.ColumnDefinitions[1].Width.IsStar.ShouldBeTrue();

        Border logo = view.GetControl<Border>("LogoPanel");
        Border header = view.GetControl<Border>("RecentHeaderPanel");

        logo.Bounds.Height.ShouldBe(header.Bounds.Height);

        // The heading sits on the header band, and the band is at the top of the right column.
        view.GetControl<TextBlock>("RecentHeader").IsEffectivelyVisible.ShouldBeTrue();
        header.Bounds.Y.ShouldBe(0);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Arama_kutusu_listenin_USTUNDE()
    {
        MainWindowViewModel model = CreateViewModel(new FakeRecentRepositoryStore("/r/bir"));
        (Window window, WelcomeView view) = await ShowAsync(model);

        TextBox search = view.GetControl<TextBox>("SearchBox");
        Button tile = Tiles(view).Single();

        search.TranslatePoint(default, view).ShouldNotBeNull().Y
            .ShouldBeLessThan(tile.TranslatePoint(default, view).ShouldNotBeNull().Y);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Depo_yokken_arama_kutusu_GORUNMUYOR()
    {
        // Searching an empty list is an empty gesture; GitExtensions' dashboard shows the search
        // bar with the list, not on its own.
        MainWindowViewModel model = CreateViewModel(new FakeRecentRepositoryStore());
        (Window window, WelcomeView view) = await ShowAsync(model);

        view.GetControl<TextBox>("SearchBox").IsEffectivelyVisible.ShouldBeFalse();
        view.GetControl<TextBlock>("EmptyDashboardText").IsEffectivelyVisible.ShouldBeTrue();

        window.Close();
    }
}
