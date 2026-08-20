using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core.Git;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P03-T16 — Verifies that the welcome screen really is <b>visible</b>.
/// </summary>
/// <remarks>
/// These tests exist because of a measured bug: binding an <c>int</c> to a <c>bool</c> with
/// <c>IsVisible="{Binding Recent…Count}"</c> <b>silently does not work</b> in Avalonia — the recent
/// section was never visible and no ViewModel test would have caught it.
/// Whether the section really reaches the screen is verified from the visual tree.
/// </remarks>
public class WelcomeViewRenderTests
{
    private static MainWindowViewModel Create(params string[] recent) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(new GitException(
                    GitFailureKind.NotARepository,
                    "This folder is not a Git repository.",
                    "git rev-parse", 128, "fatal: not a git repository")),
                new FakeCommitLogReader(),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),new FakeDiffReader()),
            new FakeRecentRepositoryStore(recent));

    private static MainWindow Show(MainWindowViewModel viewModel)
    {
        MainWindow window = new() { DataContext = viewModel, Width = 1000, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>The repository tiles on the dashboard.</summary>
    private static IReadOnlyList<Control> RecentButtons(Visual root) =>
        [.. root.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.DataContext is DashboardRepositoryItem)];

    [AvaloniaFact]
    public async Task Son_acilanlar_ekranda_gercekten_gorunur()
    {
        MainWindowViewModel viewModel = Create("/tmp/bir", "/tmp/iki");
        MainWindow window = Show(viewModel);

        await viewModel.StartAsync("/tmp/depo-degil");
        Dispatcher.UIThread.RunJobs();

        IReadOnlyList<Control> buttons = RecentButtons(window);

        buttons.Count.ShouldBe(2);

        // IsVisible is not enough: when a parent container is hidden the element is still not drawn.
        buttons.ShouldAllBe(b => b.IsEffectivelyVisible);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Son_acilan_yokken_bolum_hic_cikmaz()
    {
        MainWindowViewModel viewModel = Create();
        MainWindow window = Show(viewModel);

        await viewModel.StartAsync("/tmp/depo-degil");
        Dispatcher.UIThread.RunJobs();

        RecentButtons(window).ShouldBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Acilamayan_yolun_hatasi_karsilama_ekraninda_gosterilir()
    {
        MainWindowViewModel viewModel = Create();
        MainWindow window = Show(viewModel);

        await viewModel.StartAsync("/tmp/depo-degil");
        Dispatcher.UIThread.RunJobs();

        bool errorShown = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Any(t => t.IsEffectivelyVisible
                && t.Text?.Contains("This folder is not a git repository", StringComparison.Ordinal) == true);

        errorShown.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Commitsiz_depoda_aciklama_gosterilir()
    {
        // P03-T17: in a freshly `git init`ed repository the user must not be looking at a blank screen.
        MainWindowViewModel viewModel = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),new FakeDiffReader()),
            new FakeRecentRepositoryStore());

        MainWindow window = Show(viewModel);

        await viewModel.StartAsync("/tmp/bos-depo");
        Dispatcher.UIThread.RunJobs();

        bool messageShown = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Any(t => t.IsEffectivelyVisible
                && t.Text?.Contains("No commits in this repository", StringComparison.Ordinal) == true);

        messageShown.ShouldBeTrue();

        window.Close();
    }
    [AvaloniaFact]
    public async Task Depo_acilamadiginda_AYRINTILAR_dugmesi_gorunur()
    {
        // P05-T07 — "this folder is not a Git repository" is the MOST FREQUENT error path, and because
        // the repository could not be opened, what is on screen is the welcome screen and not the commit
        // list. Had the button been put only on the commit list, git's actual output would be invisible
        // at exactly the moment it is most needed.
        MainWindowViewModel viewModel = Create();
        MainWindow window = Show(viewModel);

        await viewModel.StartAsync("/tmp/depo-degil");
        Dispatcher.UIThread.RunJobs();

        GitErrorDetailsButton button = window.GetVisualDescendants()
            .OfType<GitErrorDetailsButton>()
            .Single();

        button.IsEffectivelyVisible.ShouldBeTrue();
        button.Details.ShouldNotBeNull().Output.ShouldContain("fatal: not a git repository");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Hata_yokken_AYRINTILAR_dugmesi_GORUNMEZ()
    {
        // The counter-evidence: were the button always visible, the test above would prove nothing.
        MainWindowViewModel viewModel = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore());

        MainWindow window = Show(viewModel);
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants()
            .OfType<GitErrorDetailsButton>()
            .All(b => !b.IsEffectivelyVisible)
            .ShouldBeTrue();

        window.Close();
    }
}
