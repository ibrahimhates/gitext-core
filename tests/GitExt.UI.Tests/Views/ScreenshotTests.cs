using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using GitExt.Core.Model;
using GitExt.UI.Localization;
using GitExt.UI.Settings;
using GitExt.UI.Themes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// Produces the screenshots for the README and AppStream (P10-T26).
/// </summary>
/// <remarks>
/// <para>
/// The screenshot is produced as a <b>test</b> rather than taken by hand. A hand-taken image goes
/// stale at the first UI change and nobody notices; the one produced here is refreshed on every CI
/// run, and if the UI breaks the test breaks too.
/// </para>
/// <para>
/// The outputs are written under <c>docs/assets/</c>. The <b>content</b> of the images is not
/// verified — this is not a visual regression test; what is verified is that the window really was
/// drawn and is not blank.
/// </para>
/// </remarks>
public class ScreenshotTests
{
    /// <summary>
    /// Where the images that go into the README are written. Fixed relative to the repository root.
    /// </summary>
    private static string AssetDirectory
    {
        get
        {
            // The test binary runs under bin/Release/net10.0; the repository root is four levels up.
            DirectoryInfo directory = new(AppContext.BaseDirectory);

            while (directory.Parent is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory.FullName, "docs", "assets");
        }
    }

    /// <summary>
    /// A realistic commit list for the screenshot.
    /// </summary>
    /// <remarks>
    /// Fixed data is used rather than cloning a real repository: the screenshot must come out THE SAME
    /// on every run. With a real repository the image would change along with the commit history and
    /// the picture in the README would differ on every release.
    /// </remarks>
    private static MainWindowViewModel BuildModel()
    {
        string[] shas =
        [
            "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0",
            "b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1",
            "c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2",
            "d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3",
            "e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4",
            "f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5",
            "a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6",
            "b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7",
        ];

        // The parent links are real: every commit is linked to the next and the fourth is a merge.
        // So the lane and the merge line show up in the graph's screenshot.
        List<CommitInfo> commits =
        [
            Commit(shas[0], [shas[1]], "feat(graph): lane colours for merge commits", "Ada Lovelace", 0, ["HEAD -> main"]),
            Commit(shas[1], [shas[2]], "fix: detached HEAD showed the wrong branch name", "Grace Hopper", 3),
            Commit(shas[2], [shas[3]], "perf(core): intern commit text — 460 MB to 368 MB", "Ada Lovelace", 8, ["origin/main"]),
            Commit(shas[3], [shas[4], shas[5]], "Merge branch 'feature/staging'", "Grace Hopper", 14),
            Commit(shas[4], [shas[6]], "feat: stage individual lines from the diff view", "Alan Turing", 20),
            Commit(shas[5], [shas[6]], "test: cover the reflog reader against a real repository", "Alan Turing", 26),
            Commit(shas[6], [shas[7]], "docs: explain why we drive the git CLI (ADR-0002)", "Ada Lovelace", 31, ["v0.9.0"]),
            Commit(shas[7], [], "refactor: move lane assignment out of the view", "Grace Hopper", 40),
        ];

        CommitListViewModel list = new(
            new Fakes.FakeRepositoryLocator(),
            new Fakes.FakeCommitLogReader(commits),
            new Fakes.FakeRefReader(),
            new Fakes.FakeCommitSignatureReader(),
            new Fakes.FakeDiffReader());

        return new MainWindowViewModel(list, new Fakes.FakeRecentRepositoryStore());
    }

    private static CommitInfo Commit(
        string sha,
        string[] parents,
        string subject,
        string author,
        int daysAgo,
        string[]? refs = null)
    {
        // A fixed date: the image must come out the same on every run. A column shown as "3 days ago"
        // would change every day along with real time.
        DateTimeOffset when = new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero).AddDays(-daysAgo);
        string email = $"{author.Split(' ')[0].ToLowerInvariant()}@example.com";
        Signature signature = new(author, email, when);

        return new CommitInfo
        {
            Id = CommitId.Parse(sha),
            Parents = [.. parents.Select(CommitId.Parse)],
            Subject = subject,
            Body = string.Empty,
            Author = signature,
            Committer = signature,
            Refs = refs ?? [],
        };
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Light, "screenshot-main-light.png")]
    [InlineData(ThemePreference.Dark, "screenshot-main-dark.png")]
    [InlineData(ThemePreference.Light, "screenshot-main-tr.png", "tr")]
    public async Task Ana_pencere_ekran_goruntusu_uretiliyor(
        ThemePreference theme,
        string fileName,
        string language = "en")
    {
        InMemorySettingsStore settings = new();
        AppearanceService appearance = new(Application.Current!, settings);
        appearance.SetTheme(theme);

        // A Turkish image is visible proof that the translation ACTUALLY reaches the UI.
        Translator translator = new(settings);
        translator.Use(language);
        TranslateExtension.Attach(translator);
        Loc.Attach(translator);

        MainWindowViewModel model = BuildModel();

        // 🔴 Taking the screenshot WITHOUT opening a repository gives the welcome screen — an empty
        // start page would go into the README labelled "the commit graph". That is exactly what the
        // first generated image was, and it would have gone unnoticed had nobody looked at it.
        //
        // ⚠️ `await` is REQUIRED, NOT `GetAwaiter().GetResult()`: waiting synchronously on the
        // headless UI thread deadlocks (measured — the test was still running after 5 minutes).
        await model.Commits.OpenAsync("/repo");

        MainWindow window = new()
        {
            DataContext = model,

            // HiDPI: the image in the README should be at 2x resolution, otherwise it looks blurry on
            // high-density screens. Because headless rendering uses the logical size, the window is
            // enlarged.
            Width = 1440,
            Height = 900,
        };

        window.Show();

        using Bitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Render edilmiş kare alınamadı.");

        Directory.CreateDirectory(AssetDirectory);
        string path = Path.Combine(AssetDirectory, fileName);
        frame.Save(path, new PngBitmapEncoderOptions());

        // The content is not verified (this is not a visual regression test), but a BLANK image must
        // not go into the README: a theme that was not applied, or a window that was not drawn, would
        // look exactly like that.
        new FileInfo(path).Length.ShouldBeGreaterThan(
            5_000,
            $"{fileName} boş görünüyor — pencere çizilmemiş olabilir");

        frame.PixelSize.Width.ShouldBeGreaterThan(1000);
    }
}
