using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using GitExt.Core;
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
    /// <summary>Puts the process-wide language back to English when the test ends.</summary>
    private sealed class EnglishAfterwards : IDisposable
    {
        public void Dispose()
        {
            Translator english = new(new InMemorySettingsStore());
            english.Use("en");
            TranslateExtension.Attach(english);
            Loc.Attach(english);
        }
    }

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
    private static MainWindowViewModel BuildModel(Fakes.FakeRecentRepositoryStore? recent = null)
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

        // The refs are given for real, not left empty: the badges, the square nodes of the
        // commits that carry refs and the outline around HEAD are all part of the picture, and an
        // image without them shows a graph the application never actually draws (P12-T10).
        RepositoryRefs refs = Fakes.FakeGitData.Refs(
            localBranches: [Fakes.FakeGitData.LocalBranch("main", shas[0], isCurrent: true)],
            remoteBranches: [Fakes.FakeGitData.RemoteBranch("origin/main", shas[2])],
            tags: [Fakes.FakeGitData.Tag("v0.9.0", shas[6])],
            head: new HeadState
            {
                IsDetached = false,
                IsUnborn = false,
                BranchName = "main",
                Commit = CommitId.Parse(shas[0]),
            });

        CommitListViewModel list = new(
            new Fakes.FakeRepositoryLocator(),
            new Fakes.FakeCommitLogReader(commits),
            new Fakes.FakeRefReader(refs),
            new Fakes.FakeCommitSignatureReader(),
            new Fakes.FakeDiffReader());

        MainWindowViewModel model = new(list, recent ?? new Fakes.FakeRecentRepositoryStore());

        // The left panel is filled too (P12-T13): its six sections are part of the picture, and a
        // screenshot with an empty panel shows a window the application never actually draws.
        model.RefTree.Load(new RefTreeData
        {
            Refs = refs,
            RootPath = "/repo",
            WorkTrees =
            [
                new Core.WorkTree { Path = "/repo", BranchName = "main", IsMain = true },
                new Core.WorkTree { Path = "/repo-hotfix", BranchName = "hotfix/crash" },
            ],
            Submodules =
            [
                new Core.Submodule
                {
                    Path = RepositoryPath.Parse("externals/skia"),
                    ObjectId = shas[3],
                    Status = Core.SubmoduleStatusKind.UpToDate,
                },
            ],
            Stashes =
            [
                new Core.StashEntry
                {
                    Selector = "stash@{0}",
                    ObjectId = shas[4],
                    Message = "WIP: partial staging",
                    Index = 0,
                },
            ],
        });

        return model;
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
        //
        // 🔴 The language is PROCESS-WIDE, so this test used to leave whatever it last set behind
        // it and the next test asserting a translated string passed or failed depending on the
        // order the tests happened to run in. Whatever is set here is put back to English at the
        // end of the test.
        Translator translator = new(settings);
        translator.Use(language);
        TranslateExtension.Attach(translator);
        Loc.Attach(translator);

        using EnglishAfterwards _ = new();

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

    /// <summary>
    /// The dashboard — the screen the application starts on (P12-T03).
    /// </summary>
    /// <remarks>
    /// It gets its own screenshot because it is the <b>first</b> thing anyone sees, of the
    /// application and of the README alike. The repository list, the branch names and the
    /// categories are fixed data: with real repositories the image would change with whatever
    /// happens to be on the machine.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(ThemePreference.Light, "screenshot-dashboard-light.png")]
    [InlineData(ThemePreference.Dark, "screenshot-dashboard-dark.png")]
    public async Task Kontrol_paneli_ekran_goruntusu_uretiliyor(ThemePreference theme, string fileName)
    {
        InMemorySettingsStore settings = new();
        AppearanceService appearance = new(Application.Current!, settings);
        appearance.SetTheme(theme);

        Translator translator = new(settings);
        translator.Use("en");
        TranslateExtension.Attach(translator);
        Loc.Attach(translator);

        Fakes.FakeRecentRepositoryStore store = new(
            "/home/dev/projects/gitext-core",
            "/home/dev/projects/avalonia",
            "/home/dev/projects/roslyn",
            "/home/dev/work/payments-api",
            "/media/backup/archived-tools");

        store.WithCategory("/home/dev/work/payments-api", "Work");

        MainWindowViewModel model = BuildModel(store);

        // The probe is replaced BEFORE loading: the list is built while it is being read, and a
        // probe handed over afterwards would arrive too late.
        model.Dashboard.Probe = HeadOf;
        await model.StartAsync(explicitPath: null);

        MainWindow window = new() { DataContext = model, Width = 1440, Height = 900 };
        window.Show();

        using Bitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Render edilmiş kare alınamadı.");

        Directory.CreateDirectory(AssetDirectory);
        string path = Path.Combine(AssetDirectory, fileName);
        frame.Save(path, new PngBitmapEncoderOptions());

        new FileInfo(path).Length.ShouldBeGreaterThan(
            5_000,
            $"{fileName} boş görünüyor — pencere çizilmemiş olabilir");

        static RepositoryHeadInfo HeadOf(string path) => path switch
        {
            // One entry is deliberately unreachable: that is the state the dashboard draws with
            // the struck-out folder, and a screenshot that never shows it hides half the design.
            "/media/backup/archived-tools" => RepositoryHeadInfo.NotARepository,
            "/home/dev/projects/gitext-core" => new RepositoryHeadInfo(true, "main"),
            "/home/dev/projects/avalonia" => new RepositoryHeadInfo(true, "release/11.2"),
            "/home/dev/projects/roslyn" => new RepositoryHeadInfo(true, null),
            _ => new RepositoryHeadInfo(true, "feature/checkout-flow"),
        };
    }
}
