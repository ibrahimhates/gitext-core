using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

public class CommitRewriteMenuActionTests
{
    private static async Task<(MainWindowViewModel Model, CommitListView View, Window Window)> ShowAsync()
    {
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore());

        await model.OpenRepositoryAsync("/depo");

        CommitListView view = new() { DataContext = model.Commits };
        view.AttachShortcuts(TestCommands.Registry());

        Window window = new() { Width = 900, Height = 300, Content = view, DataContext = model };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        model.Commits.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        return (model, view, window);
    }

    private static void Press(CommitListView view, RevisionGridMenuId id)
    {
        ContextMenu menu = view.GetControl<ContextMenu>("CommitContextMenu");
        view.OnContextMenuOpening(menu, new System.ComponentModel.CancelEventArgs());
        Dispatcher.UIThread.RunJobs();

        MenuItem item = Walk(menu.ItemsSource!.Cast<object>()).First(item => (RevisionGridMenuId?)item.Tag == id);
        item.IsEnabled.ShouldBeTrue();
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

    [AvaloniaTheory]
    [InlineData(RevisionGridMenuId.FixupCommit, CommitKind.Fixup)]
    [InlineData(RevisionGridMenuId.SquashCommit, CommitKind.Squash)]
    [InlineData(RevisionGridMenuId.AmendCommit, CommitKind.Amend)]
    public async Task Ozel_commit_ogeleri_commit_ekranini_secili_commitle_aciyor(RevisionGridMenuId id, CommitKind kind)
    {
        (MainWindowViewModel model, CommitListView view, Window window) = await ShowAsync();

        (CommitKind Kind, CommitInfo Target)? requested = null;
        model.SpecialCommitRequested = (k, target) =>
        {
            requested = (k, target);
            return Task.CompletedTask;
        };

        Press(view, id);

        requested.ShouldNotBeNull();
        requested.Value.Kind.ShouldBe(kind);
        requested.Value.Target.ShouldBe(model.Commits.Rows[1].Commit);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Yardim_ogesi_baglanti_aciyor()
    {
        (MainWindowViewModel _, CommitListView view, Window window) = await ShowAsync();

        string? opened = null;
        view.OpenUrl = url => opened = url;

        Press(view, RevisionGridMenuId.GetHelpOnHowToUseTheseFeatures);

        opened.ShouldBe(CommitListView.HistoryRewriteHelpUrl);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Hizli_arama_arama_kutusuna_odaklaniyor()
    {
        (MainWindowViewModel _, CommitListView view, Window window) = await ShowAsync();

        TextBox search = view.GetControl<TextBox>("ShaSearchBox");
        search.IsFocused.ShouldBeFalse();

        Press(view, RevisionGridMenuId.QuickSearch);

        search.IsFocused.ShouldBeTrue();

        window.Close();
    }
}
