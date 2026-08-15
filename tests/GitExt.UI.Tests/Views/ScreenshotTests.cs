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
/// README ve AppStream için ekran görüntüsü üretir (P10-T26).
/// </summary>
/// <remarks>
/// <para>
/// Ekran görüntüsü bir <b>test</b> olarak üretiliyor, elle alınmıyor. Elle alınan
/// görüntü ilk arayüz değişikliğinde eskiyor ve kimse fark etmiyor; burada üretilen
/// her CI koşusunda tazeleniyor ve arayüz kırılırsa test de kırılıyor.
/// </para>
/// <para>
/// Çıktılar <c>docs/assets/</c> altına yazılıyor. Görüntülerin <b>içeriği</b>
/// doğrulanmıyor — bu bir görsel regresyon testi değil; doğrulanan şey pencerenin
/// gerçekten çizildiği ve boş olmadığı.
/// </para>
/// </remarks>
public class ScreenshotTests
{
    /// <summary>
    /// README'ye giren görüntülerin yazıldığı yer. Depo köküne göre sabit.
    /// </summary>
    private static string AssetDirectory
    {
        get
        {
            // Test ikilisi bin/Release/net10.0 altında koşuyor; depo kökü dört üstte.
            DirectoryInfo directory = new(AppContext.BaseDirectory);

            while (directory.Parent is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory.FullName, "docs", "assets");
        }
    }

    /// <summary>
    /// Ekran görüntüsü için gerçekçi bir commit listesi.
    /// </summary>
    /// <remarks>
    /// Gerçek bir depo klonlamak yerine sabit veri kullanılıyor: ekran görüntüsü
    /// her koşuda AYNI çıkmalı. Gerçek bir depo kullanılsaydı görüntü commit
    /// geçmişiyle birlikte değişir ve README'deki resim her sürümde farklı olurdu.
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

        // Ebeveyn bağları gerçek: her commit bir sonrakine bağlı, dördüncüsü merge.
        // Grafiğin ekran görüntüsünde şerit ve birleşme çizgisi görünsün diye.
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
        // Sabit tarih: görüntü her koşuda aynı çıkmalı. "3 gün önce" biçiminde
        // gösterilen bir sütun, gerçek zamanla birlikte her gün değişirdi.
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

        // Türkçe görüntü, çevirinin GERÇEKTEN arayüze ulaştığının gözle görülür kanıtı.
        Translator translator = new(settings);
        translator.Use(language);
        TranslateExtension.Attach(translator);
        Loc.Attach(translator);

        MainWindowViewModel model = BuildModel();

        // 🔴 Depo AÇILMADAN ekran görüntüsü alınırsa karşılama ekranı çıkıyor — README'ye
        // "commit grafiği" diye boş bir başlangıç sayfası konurdu. İlk üretilen görüntü
        // tam olarak buydu ve gözle bakılmasaydı fark edilmezdi.
        //
        // ⚠️ `await` ŞART, `GetAwaiter().GetResult()` DEĞİL: headless UI thread'inde
        // senkron bekleme kilitleniyor (ölçüldü — test 5 dakika sonra hâlâ koşuyordu).
        await model.Commits.OpenAsync("/repo");

        MainWindow window = new()
        {
            DataContext = model,

            // HiDPI: README'deki görüntü 2x çözünürlükte olmalı, yoksa yüksek yoğunluklu
            // ekranlarda bulanık görünüyor. Headless render mantıksal boyutu kullandığı
            // için pencere büyütülüyor.
            Width = 1440,
            Height = 900,
        };

        window.Show();

        using Bitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Render edilmiş kare alınamadı.");

        Directory.CreateDirectory(AssetDirectory);
        string path = Path.Combine(AssetDirectory, fileName);
        frame.Save(path, new PngBitmapEncoderOptions());

        // İçerik doğrulanmıyor (bu görsel regresyon testi değil), ama BOŞ bir görüntü
        // README'ye girmemeli: temanın uygulanmadığı veya pencerenin çizilmediği
        // durumlar tam olarak böyle görünürdü.
        new FileInfo(path).Length.ShouldBeGreaterThan(
            5_000,
            $"{fileName} boş görünüyor — pencere çizilmemiş olabilir");

        frame.PixelSize.Width.ShouldBeGreaterThan(1000);
    }
}
