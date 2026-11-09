using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.UI.Controls;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

public class RevisionGridDisplayViewTests
{
    private static async Task<(CommitListViewModel Model, CommitListView View, Window Window)> ShowAsync()
    {
        CommitListViewModel model = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(
            [
                FakeGitData.Commit(FakeGitData.Sha(2), [FakeGitData.Sha(1)], "second") with { Body = "body text" },
                FakeGitData.Commit(FakeGitData.Sha(1), [], "first"),
            ]),
            new FakeRefReader(FakeGitData.Refs(
                localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(2), isCurrent: true)],
                remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(2))],
                tags: [FakeGitData.Tag("v1.0", FakeGitData.Sha(2))])),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        model.AttachSettings(new InMemorySettingsStore());
        await model.OpenAsync("/tmp/depo");

        CommitListView view = new() { DataContext = model };
        view.AttachShortcuts(TestCommands.Registry());

        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        model.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        return (model, view, window);
    }

    private static Grid FirstRow(CommitListView view) => view.GetVisualDescendants()
        .OfType<Grid>()
        .First(g => g.Children.OfType<CommitGraphCell>().Any());

    private static TextBlock Column(CommitListView view, int column) => FirstRow(view).Children
        .OfType<TextBlock>()
        .Single(text => Grid.GetColumn(text) == column);

    private static void Press(CommitListView view, RevisionGridMenuId id)
    {
        ContextMenu menu = view.GetControl<ContextMenu>("CommitContextMenu");
        view.OnContextMenuOpening(menu, new System.ComponentModel.CancelEventArgs());
        Dispatcher.UIThread.RunJobs();

        MenuItem item = Walk(menu.ItemsSource!.Cast<object>()).First(item => (RevisionGridMenuId?)item.Tag == id);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        static IEnumerable<MenuItem> Walk(IEnumerable<object> items)
        {
            foreach (MenuItem menuItem in items.OfType<MenuItem>())
            {
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

    private static string[] VisibleBadges(CommitListView view) => [.. FirstRow(view)
        .GetVisualDescendants()
        .OfType<Border>()
        .Where(border => border.Classes.Contains("badge") && border.IsVisible)
        .Select(border => border.GetVisualDescendants().OfType<TextBlock>().First().Text ?? "")];

    [AvaloniaFact]
    public async Task Sutun_anahtarlari_sutunu_gizliyor()
    {
        (CommitListViewModel _, CommitListView view, Window window) = await ShowAsync();

        Column(view, 3).IsVisible.ShouldBeTrue();
        Column(view, 4).IsVisible.ShouldBeTrue();
        Column(view, 5).IsVisible.ShouldBeTrue();
        FirstRow(view).Children.OfType<CommitGraphCell>().Single().IsVisible.ShouldBeTrue();

        Press(view, RevisionGridMenuId.ShowAuthorNameColumn);
        Press(view, RevisionGridMenuId.ShowDateColumn);
        Press(view, RevisionGridMenuId.ShowIdColumn);
        Press(view, RevisionGridMenuId.ShowRevisionGraphColumn);

        Column(view, 3).IsVisible.ShouldBeFalse();
        Column(view, 4).IsVisible.ShouldBeFalse();
        Column(view, 5).IsVisible.ShouldBeFalse();
        FirstRow(view).Children.OfType<CommitGraphCell>().Single().IsVisible.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Uzak_dal_ve_etiket_rozetleri_gizlenebiliyor()
    {
        (CommitListViewModel model, CommitListView view, Window window) = await ShowAsync();

        VisibleBadges(view).ShouldBe(["main", "origin/main", "v1.0"], ignoreOrder: true);

        Press(view, RevisionGridMenuId.ShowRemoteBranches);
        VisibleBadges(view).ShouldBe(["main", "v1.0"], ignoreOrder: true);

        Press(view, RevisionGridMenuId.ShowTags);
        VisibleBadges(view).ShouldBe(["main"]);

        // Hiding a label must not hide the ref from the menu: GitExtensions builds the menu from
        // the revision's refs, not from what the grid draws.
        model.Rows[0].Badges.Count.ShouldBe(3);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Tarih_bicimi_ve_govde_canli_degisiyor()
    {
        (CommitListViewModel model, CommitListView view, Window window) = await ShowAsync();

        string relative = Column(view, 4).Text!;
        relative.ShouldEndWith(" ago");

        Press(view, RevisionGridMenuId.ShowRelativeDate);

        Column(view, 4).Text.ShouldBe(
            model.Rows[0].Commit.Author.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture));

        TextBlock body = view.GetVisualDescendants().OfType<DockPanel>().First(panel => panel.Name == "MessageCell")
            .Children.OfType<TextBlock>().Single(text => text.Text == "body text");
        body.IsVisible.ShouldBeTrue();

        Press(view, RevisionGridMenuId.ShowCommitMessageBody);
        body.IsVisible.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Griye_boyama_kapatilabiliyor()
    {
        (CommitListViewModel _, CommitListView view, Window window) = await ShowAsync();

        CommitGraphCell cell = FirstRow(view).Children.OfType<CommitGraphCell>().Single();
        cell.GrayNonRelatives.ShouldBeTrue();

        Press(view, RevisionGridMenuId.DrawNonRelativesGray);

        cell.GrayNonRelatives.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Menudeki_isaret_son_durumu_gosteriyor()
    {
        (CommitListViewModel _, CommitListView view, Window window) = await ShowAsync();

        Press(view, RevisionGridMenuId.ShowTags);

        ContextMenu menu = view.GetControl<ContextMenu>("CommitContextMenu");
        view.OnContextMenuOpening(menu, new System.ComponentModel.CancelEventArgs());
        Dispatcher.UIThread.RunJobs();

        MenuItem viewMenu = menu.ItemsSource!.Cast<object>().OfType<MenuItem>()
            .First(item => (RevisionGridMenuId?)item.Tag == RevisionGridMenuId.View);
        MenuItem[] children = [.. viewMenu.ItemsSource!.Cast<object>().OfType<MenuItem>()];

        MenuItem tags = children.First(item => (RevisionGridMenuId?)item.Tag == RevisionGridMenuId.ShowTags);
        MenuItem remotes = children.First(item => (RevisionGridMenuId?)item.Tag == RevisionGridMenuId.ShowRemoteBranches);

        tags.ToggleType.ShouldBe(MenuItemToggleType.CheckBox);
        tags.IsChecked.ShouldBeFalse();
        remotes.IsChecked.ShouldBeTrue();

        window.Close();
    }
}
