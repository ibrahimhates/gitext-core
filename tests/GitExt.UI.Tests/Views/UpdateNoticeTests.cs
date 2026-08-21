using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.UI.Updates;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P13-T01 — the version notice on screen.
/// </summary>
/// <remarks>
/// A strip rather than a dialog, for the same reason as every other notice here (P05-T15): a modal
/// box at startup is an obstacle to be dismissed, and this news is worth a line, not a click.
/// </remarks>
public class UpdateNoticeTests
{
    private sealed class FakeFeed : IReleaseFeed
    {
        private readonly ReleaseNote? _release;

        public FakeFeed(string? version) =>
            _release = version is null
                ? null
                : new ReleaseNote(version, "https://example.invalid/releases/tag/" + version);

        public Task<ReleaseNote?> GetLatestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_release);
    }

    private static MainWindowViewModel CreateViewModel(string? published, string current = "0.1.0")
    {
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new Fakes.FakeRepositoryLocator(),
                new Fakes.FakeCommitLogReader(Fakes.FakeGitData.LinearHistory(2)),
                new Fakes.FakeRefReader(),
                new Fakes.FakeCommitSignatureReader(),
                new Fakes.FakeDiffReader()),
            new Fakes.FakeRecentRepositoryStore());

        model.VersionLabel = current;
        model.Updates = new UpdateService(new FakeFeed(published), new InMemorySettingsStore(), current);

        return model;
    }

    private static (MainWindow Window, MainWindowViewModel Model) Show(MainWindowViewModel model)
    {
        MainWindow window = new() { DataContext = model, Width = 1000, Height = 620 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, model);
    }

    [AvaloniaFact]
    public void Yardim_menusunde_guncelleme_ogesi_About_ten_ONCE()
    {
        // GitExtensions' Help menu ends with "Check for updates" and then "About"
        // (`HelpToolStripMenuItem.Designer.cs`).
        (MainWindow window, _) = Show(CreateViewModel(published: null));

        MenuItem help = window.GetVisualDescendants().OfType<Menu>().Single()
            .Items.OfType<MenuItem>().Last();

        string[] items = [.. help.Items.OfType<MenuItem>().Select(i => i.Name ?? string.Empty)];

        items.ShouldBe(["MenuCheckForUpdates", "MenuAbout"]);

        window.Close();
    }

    [AvaloniaFact]
    public void Haber_yokken_serit_GORUNMUYOR()
    {
        (MainWindow window, _) = Show(CreateViewModel(published: null));

        window.GetControl<Border>("UpdateNoticeBar").IsVisible.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Yeni_surum_bulununca_serit_ve_baglanti_geliyor()
    {
        (MainWindow window, MainWindowViewModel model) = Show(CreateViewModel(published: "v0.2.0"));

        await model.CheckForUpdatesAsync(userRequested: false);
        Dispatcher.UIThread.RunJobs();

        window.GetControl<Border>("UpdateNoticeBar").IsVisible.ShouldBeTrue();
        (window.GetControl<TextBlock>("UpdateNoticeText").Text ?? string.Empty).ShouldContain("v0.2.0");
        window.GetControl<Button>("UpdateNotesButton").IsVisible.ShouldBeTrue();

        model.UpdateUrl.ShouldContain("v0.2.0");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Guncelken_kullaniciya_YALNIZCA_sordugunda_cevap_veriliyor()
    {
        // 🔴 The automatic check must not interrupt anyone to say "nothing new" — that is a
        // notification with no news in it. A person who clicked the menu item is owed an answer.
        (MainWindow window, MainWindowViewModel model) = Show(CreateViewModel(published: "v0.1.0"));

        await model.CheckForUpdatesAsync(userRequested: false);
        Dispatcher.UIThread.RunJobs();
        window.GetControl<Border>("UpdateNoticeBar").IsVisible.ShouldBeFalse();

        await model.CheckForUpdatesAsync(userRequested: true);
        Dispatcher.UIThread.RunJobs();

        window.GetControl<Border>("UpdateNoticeBar").IsVisible.ShouldBeTrue();
        (window.GetControl<TextBlock>("UpdateNoticeText").Text ?? string.Empty).ShouldContain("0.1.0");

        // Nothing to open: there is no newer release.
        window.GetControl<Button>("UpdateNotesButton").IsVisible.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Serit_KAPATILABILIYOR()
    {
        (MainWindow window, MainWindowViewModel model) = Show(CreateViewModel(published: "v0.2.0"));

        await model.CheckForUpdatesAsync(userRequested: false);
        Dispatcher.UIThread.RunJobs();

        Button dismiss = window.GetControl<Button>("DismissUpdateNoticeButton");
        Point centre = dismiss.TranslatePoint(
            new Point(dismiss.Bounds.Width / 2, dismiss.Bounds.Height / 2),
            window)!.Value;

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        window.GetControl<Border>("UpdateNoticeBar").IsVisible.ShouldBeFalse();
        model.UpdateNotice.ShouldBeNull();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Guncelleme_servisi_yoksa_COKMUYOR()
    {
        // Tests and the XAML designer build the ViewModel without one; it simply never checks.
        MainWindowViewModel model = CreateViewModel(published: "v0.2.0");
        model.Updates = null;

        await model.CheckForUpdatesAsync(userRequested: true);

        model.UpdateNotice.ShouldBeNull();
    }
}
