using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Controls;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P05-T09 — does the working directory screen's layout follow GitExtensions?
/// </summary>
/// <remarks>
/// <para>
/// CLAUDE.md § 9: the visual design does not have to be identical, but the <b>position and order of
/// the elements</b> must be. Source: <c>FormCommit.Designer.cs</c> —
/// <c>splitLeft.Panel1 = Unstaged</c> (top), <c>splitLeft.Panel2 = Staged</c> (bottom), with
/// <c>toolbarStaged</c> between them and <c>SelectedDiff</c> on the right.
/// </para>
/// <para>
/// The test compares <b>position</b>, not appearance: colour or font may change, but the lists
/// swapping places breaks the user's muscle memory.
/// </para>
/// </remarks>
public class WorkingTreeLayoutTests
{
    /// <remarks>
    /// ⚠️ It has to be <c>async</c>: <c>OpenAsync</c> returns to the UI thread
    /// (<c>ConfigureAwait(true)</c>) and waiting on it with <c>GetAwaiter().GetResult()</c> produces
    /// a <b>deadlock</b> in a headless test (measured — the test suite never finished).
    /// </remarks>
    private static Task<(Window Window, WorkingTreeView View)> ShowAsync(
        params FileStatus[] entries) =>
        ShowAsync(null, entries);

    private static async Task<(Window Window, WorkingTreeView View)> ShowAsync(
        FakeCommitMessageReader? messages,
        params FileStatus[] entries)
    {
        FakeStatusReader status = new(entries);

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()),
            messages,
            new FakeCommitMessageStore());

        await model.OpenAsync("/tmp/depo");

        WorkingTreeView view = new() { DataContext = model };

        Window window = new()
        {
            Width = 918,
            Height = 622,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.White, Child = view },
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, view);
    }

    private static Rect BoundsIn(Window window, Control control)
    {
        Point origin = control.TranslatePoint(default, window)
            ?? throw new InvalidOperationException($"'{control.Name}' görsel ağaçta değil.");

        return new Rect(origin, control.Bounds.Size);
    }

    private static FileStatus Unstaged(string path) =>
        new() { Path = RepositoryPath.Parse(path), UnstagedChange = FileChangeKind.Modified };

    private static FileStatus Staged(string path) =>
        new() { Path = RepositoryPath.Parse(path), StagedChange = FileChangeKind.Modified };

    [AvaloniaFact]
    public async Task Unstaged_USTTE_staged_ALTTA()
    {
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"), Staged("b.txt"));

        Rect unstaged = BoundsIn(window, view.GetControl<ListBox>("UnstagedList"));
        Rect staged = BoundsIn(window, view.GetControl<ListBox>("StagedList"));

        unstaged.Bottom.ShouldBeLessThanOrEqualTo(staged.Y);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Stage_unstage_dugmeleri_iki_listenin_ARASINDA()
    {
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        Rect unstaged = BoundsIn(window, view.GetControl<ListBox>("UnstagedList"));
        Rect staged = BoundsIn(window, view.GetControl<ListBox>("StagedList"));
        Rect stage = BoundsIn(window, view.GetControl<Button>("StageButton"));

        stage.Y.ShouldBeGreaterThanOrEqualTo(unstaged.Bottom);
        stage.Bottom.ShouldBeLessThanOrEqualTo(staged.Y);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Unstage_SOLDA_stage_SAGDA()
    {
        // In `toolbarStaged` the stage items are `ToolStripItemAlignment.Right` and the unstage items
        // are default (left) aligned. The direction matches where the lists are.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        Rect unstageAll = BoundsIn(window, view.GetControl<Button>("UnstageAllButton"));
        Rect unstage = BoundsIn(window, view.GetControl<Button>("UnstageButton"));
        Rect stage = BoundsIn(window, view.GetControl<Button>("StageButton"));
        Rect stageAll = BoundsIn(window, view.GetControl<Button>("StageAllButton"));

        // Left to right: [Unstage all][Unstage] … [Stage][Stage all]
        unstageAll.X.ShouldBeLessThan(unstage.X);
        unstage.Right.ShouldBeLessThan(stage.X);
        stage.X.ShouldBeLessThan(stageAll.X);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Dosya_listeleri_SOLDA_diff_SAGDA()
    {
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        Rect unstaged = BoundsIn(window, view.GetControl<ListBox>("UnstagedList"));
        DiffView diff = view.GetVisualDescendants().OfType<DiffView>().Single();

        BoundsIn(window, diff).X.ShouldBeGreaterThanOrEqualTo(unstaged.Right);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Diff_bileseninin_kendi_dosya_listesi_CIZILMEZ()
    {
        // The ViewModel test verifies `ShowFileList = false`; here we see that it has a counterpart on
        // screen too (a binding can silently not work — the lesson of Phase 03).
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        DiffView diff = view.GetVisualDescendants().OfType<DiffView>().Single();

        diff.GetControl<ListBox>("FileList").IsEffectivelyVisible.ShouldBeFalse();
        diff.GetControl<TreeView>("FileTree").IsEffectivelyVisible.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Sol_sutun_ekranin_yaklasik_yuzde_43_u()
    {
        // FormCommit: splitMain.SplitterDistance = 397 / 918.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        Rect unstaged = BoundsIn(window, view.GetControl<ListBox>("UnstagedList"));

        double ratio = unstaged.Right / window.Width;

        ratio.ShouldBeInRange(0.35, 0.50);

        window.Close();
    }

    // ---- P05-T12: commit paneli ----

    [AvaloniaFact]
    public async Task Diff_USTTE_commit_mesaji_ALTTA()
    {
        // FormCommit: splitRight.Panel1 = SelectedDiff, Panel2 = the commit message.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        DiffView diff = view.GetVisualDescendants().OfType<DiffView>().Single();
        Rect message = BoundsIn(window, view.GetControl<TextBox>("MessageBox"));

        BoundsIn(window, diff).Bottom.ShouldBeLessThanOrEqualTo(message.Y);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Commit_dugmeleri_SOLDA_mesaj_kutusu_SAGDA()
    {
        // `tableLayoutPanel1`: the left column is `flowCommitButtons`, the right column `Message`.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        Rect commit = BoundsIn(window, view.GetControl<Button>("CommitButton"));
        Rect message = BoundsIn(window, view.GetControl<TextBox>("MessageBox"));

        commit.Right.ShouldBeLessThanOrEqualTo(message.X);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Commit_dugmeleri_GitExtensions_sirasinda()
    {
        // `flowCommitButtons`: Commit · Commit&Push · StageInSuperproject · Amend ·
        // [ResetAuthor · ResetSoft] · StashStaged · ResetAll · ResetUnstaged.
        // The unimplemented ones are disabled but IN PLACE (§ 9).
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        double Top(string name) => BoundsIn(
            window,
            view.GetVisualDescendants().OfType<Control>().First(c => c.Name == name)).Y;

        Top("CommitButton").ShouldBeLessThan(Top("CommitAndPushButton"));
        Top("CommitAndPushButton").ShouldBeLessThan(Top("StageInSuperprojectBox"));
        Top("StageInSuperprojectBox").ShouldBeLessThan(Top("AmendBox"));
        Top("AmendBox").ShouldBeLessThan(Top("StashStagedButton"));
        Top("StashStagedButton").ShouldBeLessThan(Top("ResetAllButton"));
        Top("ResetAllButton").ShouldBeLessThan(Top("ResetUnstagedButton"));

        view.GetControl<Button>("CommitAndPushButton").IsEnabled.ShouldBeFalse();

        // They were enabled in P05-T15; their place in the order did not change.
        view.GetControl<Button>("ResetAllButton").IsEnabled.ShouldBeTrue();
        view.GetControl<Button>("ResetUnstagedButton").IsEnabled.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Elli_ve_yetmis_iki_kilavuzu_gercekten_CIZILIYOR()
    {
        // The lesson of P04-T09: a binding can silently draw nothing. If the guide is not drawn the
        // user cannot see the limit and the feature might as well not exist.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        ColumnGuides guides = view.GetControl<ColumnGuides>("MessageGuides");

        guides.IsEffectivelyVisible.ShouldBeTrue();
        guides.Columns.ShouldBe([50, 72]);
        guides.Bounds.Width.ShouldBeGreaterThan(0);

        window.Close();
    }

    // ---- P05-T13: the message helpers toolbar ----

    [AvaloniaFact]
    public async Task Mesaj_araci_cubugu_GitExtensions_sirasinda()
    {
        // `toolbarCommit` order: commit message ▾ · options ▾ · templates ▾ · create branch.
        // The unimplemented ones (Options, Create branch) are disabled but IN PLACE (§ 9);
        // slotting one in later breaks the order and the muscle memory.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        double Left(string name) => BoundsIn(
            window,
            view.GetVisualDescendants().OfType<Control>().First(c => c.Name == name)).X;

        Left("MessageHistoryButton").ShouldBeLessThan(Left("CommitOptionsButton"));
        Left("CommitOptionsButton").ShouldBeLessThan(Left("CommitTemplatesButton"));
        Left("CommitTemplatesButton").ShouldBeLessThan(Left("CreateBranchButton"));

        // Two of them were enabled in P05-T13 and "Options" in P05-T15; "Create branch" is Phase 06.
        view.GetControl<Button>("MessageHistoryButton").IsEnabled.ShouldBeTrue();
        view.GetControl<Button>("CommitTemplatesButton").IsEnabled.ShouldBeTrue();
        view.GetControl<Button>("CommitOptionsButton").IsEnabled.ShouldBeTrue();
        view.GetControl<Button>("CreateBranchButton").IsEnabled.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Gecmis_menusu_GERCEKTEN_ogeler_ciziyor()
    {
        // 🔑 The phase's recurring lesson: the ViewModel test can be green while the menu is EMPTY on
        // screen (the empty hunk headers in P04-T09, the `IsVisible` binding in Phase 03). What is
        // verified here is not that the collection fills up but that the menu PRODUCES items.
        FakeCommitMessageReader reader = new();
        reader.Recent.AddRange(["ikinci konu", "ilk konu"]);

        (Window window, WorkingTreeView view) = await ShowAsync(reader, Unstaged("a.txt"));

        Button historyButton = view.GetControl<Button>("MessageHistoryButton");
        MenuFlyout flyout = historyButton.Flyout.ShouldBeOfType<MenuFlyout>();

        flyout.ShowAt(historyButton);
        Dispatcher.UIThread.RunJobs();

        // Because the menu fills asynchronously (a git read), one more turn is needed.
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        List<string> headers =
        [
            .. flyout.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? string.Empty),
        ];

        headers.ShouldContain("ikinci konu");
        headers.ShouldContain("ilk konu");

        // The fixed item (the filter) is preserved — the history rows must not overwrite it.
        headers.ShouldContain("Only my messages");

        flyout.Hide();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Sablon_menusu_ayarsizken_de_NEDENINI_soyluyor()
    {
        // An empty menu makes the user think "something should have been here". When there is no
        // template the reason is written out; when it is broken the path is written out (git itself
        // rejects the commit in that case — measured).
        //
        // ⚠️ MEASURED: looking at the menu WITHOUT opening it is misleading. While the flyout is
        // closed its items are not in the visual tree, the `DataContext` does not flow and the
        // `IsEnabled` binding sits at its never-evaluated default (true). What has to be verified is
        // the state the user sees, which is with the menu open.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        Button templatesButton = view.GetControl<Button>("CommitTemplatesButton");
        MenuFlyout flyout = templatesButton.Flyout.ShouldBeOfType<MenuFlyout>();

        flyout.ShowAt(templatesButton);
        Dispatcher.UIThread.RunJobs();

        MenuItem item = flyout.Items.OfType<MenuItem>().First(menuItem => menuItem.Name == "TemplateItem");

        item.Header?.ToString().ShouldBe("commit.template is not set");
        item.IsEnabled.ShouldBeFalse();

        flyout.Hide();
        window.Close();
    }
}
