using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P08-T25…T27 — Pins down that the layout <b>follows GitExtensions</b>.
/// </summary>
/// <remarks>
/// <para>
/// The user's rule: it does not have to look identical, but the <b>position and order of the
/// elements</b> must be the same — someone used to GitExtensions must find the same thing in the same
/// place.
/// </para>
/// <para>
/// So what is tested here is <b>order</b>, not appearance. The expected sequences were taken from the
/// GitExtensions source: the menus from <c>FormBrowse.Designer.cs</c> and
/// <c>StartToolStripMenuItem.Designer.cs</c>, the commit context menu from
/// <c>RevisionGridControl.Designer.cs</c> (<c>mainContextMenu.Items.AddRange</c>).
/// </para>
/// <para>
/// Commands not implemented yet are <b>disabled but in place</b>; the tests protect that too, because
/// slotting one in later breaks the order.
/// </para>
/// </remarks>
public class GitExtensionsLayoutTests
{
    private static MainWindowViewModel CreateViewModel(params string[] recent) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(5)),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(recent));

    private static string[] Headers(ItemsControl menu) =>
        [.. menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? string.Empty)];

    /// <summary>The visible order of the menu items, separators included.</summary>
    private static string[] Rows(ItemsControl menu) =>
        [.. menu.Items.Select(item => item switch
        {
            Separator => "──",
            MenuItem entry => entry.Header?.ToString() ?? string.Empty,
            _ => "?",
        })];

    [AvaloniaFact]
    public void Ana_menu_GitExtensions_sirasinda()
    {
        // GitExtensions: Start · Dashboard · Repository · Commands · (hosts) · Plugins ·
        // Tools · Help. Bizde eklenti ve repository-host yok.
        MainWindow window = new() { DataContext = CreateViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Menu menu = window.GetVisualDescendants().OfType<Menu>().Single();

        Headers(menu).ShouldBe(["_Start", "_Dashboard", "_Repository", "_Commands", "_Tools", "_Help"]);

        window.Close();
    }

    [AvaloniaFact]
    public void Baslangic_menusu_GitExtensions_sirasinda()
    {
        // GitExtensions `StartToolStripMenuItem`: Create new repository · Open ·
        // Clone repository · Favourite/Recent repositories · Exit.
        MainWindow window = new() { DataContext = CreateViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        MenuItem start = window.GetVisualDescendants().OfType<Menu>().Single()
            .Items.OfType<MenuItem>().First();

        Rows(start).ShouldBe([
            "Create new repository…",
            "_Open…",
            "C_lone repository…",
            "──",
            "_Recent repositories",
            "──",
            "E_xit",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public void Uygulanmamis_komutlar_KALDIRILMIYOR_devre_disi_duruyor()
    {
        // This is the price of keeping the order: the item is visible but cannot be clicked. Slotting
        // one in later would break the user's muscle memory.
        MainWindow window = new() { DataContext = CreateViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        MenuItem start = window.GetVisualDescendants().OfType<Menu>().Single()
            .Items.OfType<MenuItem>().First();

        MenuItem create = start.Items.OfType<MenuItem>().First();
        MenuItem open = start.Items.OfType<MenuItem>().ElementAt(1);

        create.Header!.ToString().ShouldBe("Create new repository…");
        create.IsEnabled.ShouldBeFalse();

        open.IsEnabled.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public void Depo_ve_komut_menuleri_depo_yokken_kapali()
    {
        MainWindow window = new() { DataContext = CreateViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        MenuItem[] items = [.. window.GetVisualDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>()];

        items[2].Header!.ToString().ShouldBe("_Repository");
        items[2].IsEnabled.ShouldBeFalse();

        items[3].Header!.ToString().ShouldBe("_Commands");
        items[3].IsEnabled.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public void Karsilama_ekraninda_baslangic_baglantilari_GitExtensions_sirasinda()
    {
        // GitExtensions `Dashboard`: solda `flpnlStart` →
        // Create new repository · Open repository · Clone repository.
        WelcomeView view = new() { DataContext = CreateViewModel("/tmp/a") };
        Window window = new() { Width = 900, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The label lives in a TextBlock next to the icon, so `Content` is the panel, not the text.
        string[] links = [.. view.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("link"))
            .Select(b => b.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty)];

        // GitExtensions' order, top to bottom: the three start links, then Contribute →
        // Develop · Donate · Translate · Issues. "Donate" is left out — this project takes no
        // donations, and an item that leads nowhere is worse than a missing one.
        links.ShouldBe([
            "Create new repository",
            "Open repository",
            "Clone repository",
            "Develop",
            "Translate",
            "Report an issue",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Karsilama_ekraninda_son_depolar_ORTADA_listeleniyor()
    {
        // The left panel is for the links; the repository list is in the middle/right — like
        // `userRepositoriesList` in GitExtensions.
        MainWindowViewModel model = CreateViewModel("/tmp/bir", "/tmp/iki");
        await model.StartAsync(explicitPath: null);

        WelcomeView view = new() { DataContext = model };
        Window window = new() { Width = 900, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Grid root = view.GetVisualDescendants().OfType<Grid>().First();
        root.ColumnDefinitions.Count.ShouldBe(2);

        // The left panel is a fixed width, the right side stretches.
        root.ColumnDefinitions[0].Width.IsAbsolute.ShouldBeTrue();
        root.ColumnDefinitions[1].Width.IsStar.ShouldBeTrue();

        model.HasRecentRepositories.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Commit_baglam_menusu_GitExtensions_sirasinda()
    {
        // Kaynak: RevisionGridControl.Designer.cs → mainContextMenu.Items.AddRange.
        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(5)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        await list.OpenAsync("/tmp/depo");

        CommitListView view = new() { DataContext = list };
        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ListBox commitList = view.GetVisualDescendants().OfType<ListBox>().First();
        ContextMenu menu = commitList.ContextMenu!;

        Rows(menu).ShouldBe([
            "Mark revision bad",
            "Mark revision good",
            "Skip revision",
            "──",
            "_Copy to clipboard",
            "──",
            "Apply stash",
            "Pop stash",
            "Drop stash…",
            "──",
            "Chec_k out branch…",
            "Pus_h branch…",
            "_Merge into current branch…",
            "_Rebase current branch onto this",
            "Reset current branch here…",
            "──",
            "Reset changes",
            "_Commit",
            "Create new branch here…",
            "Reset another branch here…",
            "Rename branch…",
            "Delete branch…",
            "──",
            "Create new tag here…",
            "Delete tag…",
            "──",
            "Check out this commit…",
            "Revert this commit…",
            "Cherry-pick this commit…",
            "Archive this commit…",
            "──",
            "Compa_re",
            "──",
            "_Go",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Panoya_kopyala_alt_menusu_GitExtensions_alanlariyla_ayni()
    {
        // GitExtensions `CopyContextMenuItem`: hash · message · author · date, plus the branch/tag
        // names on the row.
        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(5)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        await list.OpenAsync("/tmp/depo");

        CommitListView view = new() { DataContext = list };
        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ContextMenu menu = view.GetVisualDescendants().OfType<ListBox>().First().ContextMenu!;
        MenuItem copy = menu.Items.OfType<MenuItem>().Single(i => i.Header!.ToString() == "_Copy to clipboard");

        Rows(copy).ShouldBe(["Commit hash", "Message", "Author", "Date", "──", "Branch name"]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Depo_kapatilinca_karsilama_ekranina_donulur()
    {
        // GitExtensions: Repository → "Close (go to Dashboard)".
        MainWindowViewModel model = CreateViewModel();

        await model.OpenRepositoryAsync("/tmp/depo");

        model.HasRepository.ShouldBeTrue();
        model.ShowWelcome.ShouldBeFalse();

        model.CloseRepositoryCommand.Execute(null);

        model.HasRepository.ShouldBeFalse();
        model.ShowWelcome.ShouldBeTrue();
        model.Commits.Rows.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Diff_baglam_menusu_GitExtensions_sirasinda()
    {
        // GitExtensions `FileViewer.Designer.cs` → `contextMenu.Items.AddRange`:
        //   stageSelectedLines · unstageSelectedLines · resetSelectedLines ·
        //   copy · copyPatch · copyNewVersion · copyOldVersion · separator · display options
        //
        // The display options live in our toolbar (P04-T13); the menu's first seven items are in
        // exactly the same order.
        DiffViewModel model = new(new FakeDiffReader());

        DiffView view = new() { DataContext = model };

        Window window = new()
        {
            Width = 900,
            Height = 500,
            WindowDecorations = WindowDecorations.None,
            Content = view,
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        ListBox lines = view.GetControl<ListBox>("DiffLines");

        ContextMenu menu = lines.ContextMenu.ShouldNotBeNull();

        Headers(menu).ShouldBe(
        [
            "_Stage selected lines",
            "_Revert selected lines",
            "Reset selected lines…",
            "_Copy",
            "Copy as _patch",
            "Copy new version",
            "Copy old version",
        ]);

        // ⚠️ Availability is NOT tested here, only the order. Until the menu is opened the bindings are
        // not evaluated and `IsEnabled` stays at its never-evaluated default (true) — the trap measured
        // in P05-T13. The availability of the three actions is tested in `PartialStagingTests` through
        // their source, `IPartialStagingHost`.
        // "Reset" was enabled in P05-T15; its place in the order did not change (§ 9).

        window.Close();

        await Task.CompletedTask;
    }
}
