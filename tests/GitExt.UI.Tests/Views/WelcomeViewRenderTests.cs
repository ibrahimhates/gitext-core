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
/// P03-T16 — Karşılama ekranının gerçekten <b>görünür</b> olduğunu doğrular.
/// </summary>
/// <remarks>
/// Bu testlerin varlık sebebi ölçülmüş bir hata: <c>IsVisible="{Binding Recent…Count}"</c>
/// ile bir <c>int</c>'i <c>bool</c>'a bağlamak Avalonia'da <b>sessizce çalışmıyor</b> —
/// son açılanlar bölümü hiç görünmüyordu ve hiçbir ViewModel testi bunu yakalamazdı.
/// Bölüm gerçekten ekrana geliyor mu, görsel ağaçtan doğrulanıyor.
/// </remarks>
public class WelcomeViewRenderTests
{
    private static MainWindowViewModel Create(params string[] recent) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(new GitException(
                    GitFailureKind.NotARepository,
                    "Bu klasör bir Git deposu değil.",
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

    private static IReadOnlyList<Control> RecentButtons(Visual root) =>
        [.. root.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.DataContext is RecentRepositoryItem)];

    [AvaloniaFact]
    public async Task Son_acilanlar_ekranda_gercekten_gorunur()
    {
        MainWindowViewModel viewModel = Create("/tmp/bir", "/tmp/iki");
        MainWindow window = Show(viewModel);

        await viewModel.StartAsync("/tmp/depo-degil");
        Dispatcher.UIThread.RunJobs();

        IReadOnlyList<Control> buttons = RecentButtons(window);

        buttons.Count.ShouldBe(2);

        // IsVisible yetmez: üst bir kapsayıcı gizliyse öğe yine çizilmez.
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
                && t.Text?.Contains("not a git repository", StringComparison.Ordinal) == true);

        errorShown.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Commitsiz_depoda_aciklama_gosterilir()
    {
        // P03-T17: yeni `git init` edilmiş depoda kullanıcı boş ekrana bakmamalı.
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
        // P05-T07 — "bu klasör bir Git deposu değil" EN SIK hata yolu ve depo açılamadığı
        // için ekranda commit listesi değil karşılama ekranı var. Düğme yalnızca commit
        // listesine konsaydı git'in asıl çıktısı tam da en çok gerekli olduğu anda
        // görünmezdi.
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
        // Karşı kanıt: düğme her zaman görünseydi yukarıdaki test hiçbir şey kanıtlamazdı.
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
