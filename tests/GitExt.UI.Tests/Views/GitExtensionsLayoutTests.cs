using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P08-T25…T27 — Yerleşimin <b>GitExtensions'ı takip ettiğini</b> sabitler.
/// </summary>
/// <remarks>
/// <para>
/// Kullanıcının kuralı: görsel birebir aynı olmak zorunda değil ama <b>öğelerin yeri ve
/// sırası</b> aynı olmalı — GitExtensions'a alışkın biri aynı şeyi aynı yerde bulmalı.
/// </para>
/// <para>
/// Bu yüzden burada <b>sıra</b> test ediliyor, görünüm değil. Beklenen diziler
/// GitExtensions kaynağından çıkarıldı: menüler <c>FormBrowse.Designer.cs</c> ve
/// <c>StartToolStripMenuItem.Designer.cs</c>, commit bağlam menüsü
/// <c>RevisionGridControl.Designer.cs</c> (<c>mainContextMenu.Items.AddRange</c>).
/// </para>
/// <para>
/// Henüz uygulanmamış komutlar <b>devre dışı ama yerinde</b>; testler bunu da koruyor,
/// çünkü sonradan araya sokmak sırayı bozar.
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

    /// <summary>Ayraçlar dahil, menü öğelerinin görünen sırası.</summary>
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

        Headers(menu).ShouldBe(["_Başlangıç", "_Pano", "_Depo", "_Komutlar", "_Araçlar", "_Yardım"]);

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
            "Yeni depo oluştur…",
            "_Aç…",
            "Depo k_lonla…",
            "──",
            "Son _depolar",
            "──",
            "Çı_kış",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public void Uygulanmamis_komutlar_KALDIRILMIYOR_devre_disi_duruyor()
    {
        // Sırayı korumanın bedeli bu: öğe görünür ama tıklanamaz. Sonradan araya sokmak
        // kullanıcının kas hafızasını bozardı.
        MainWindow window = new() { DataContext = CreateViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        MenuItem start = window.GetVisualDescendants().OfType<Menu>().Single()
            .Items.OfType<MenuItem>().First();

        MenuItem create = start.Items.OfType<MenuItem>().First();
        MenuItem open = start.Items.OfType<MenuItem>().ElementAt(1);

        create.Header!.ToString().ShouldBe("Yeni depo oluştur…");
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

        items[2].Header!.ToString().ShouldBe("_Depo");
        items[2].IsEnabled.ShouldBeFalse();

        items[3].Header!.ToString().ShouldBe("_Komutlar");
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

        string[] links = [.. view.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("link"))
            .Select(b => b.Content?.ToString() ?? string.Empty)];

        links.ShouldBe(["Yeni depo oluştur", "Depo aç", "Depo klonla", "Geliştir", "Sorun bildir"]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Karsilama_ekraninda_son_depolar_ORTADA_listeleniyor()
    {
        // Sol panel bağlantılar için; depo listesi ortada/sağda — GitExtensions'taki
        // `userRepositoriesList` gibi.
        MainWindowViewModel model = CreateViewModel("/tmp/bir", "/tmp/iki");
        await model.StartAsync(explicitPath: null);

        WelcomeView view = new() { DataContext = model };
        Window window = new() { Width = 900, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Grid root = view.GetVisualDescendants().OfType<Grid>().First();
        root.ColumnDefinitions.Count.ShouldBe(2);

        // Sol panel sabit genişlikte, sağ taraf esner.
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
            "Revizyonu bozuk işaretle",
            "Revizyonu sağlam işaretle",
            "Revizyonu atla",
            "──",
            "Panoya _kopyala",
            "──",
            "Stash'i uygula",
            "Stash'i pop'la",
            "Stash'i sil…",
            "──",
            "Dala geç (chec_kout)…",
            "Dalı pus_hla…",
            "Mevcut dala _birleştir…",
            "Mevcut dalı buna _rebase et",
            "Mevcut dalı buraya sıfırla…",
            "──",
            "Değişiklikleri sıfırla",
            "_Commit",
            "Burada yeni dal oluştur…",
            "Başka bir dalı buraya sıfırla…",
            "Dalı yeniden adlandır…",
            "Dalı sil…",
            "──",
            "Burada yeni etiket oluştur…",
            "Etiketi sil…",
            "──",
            "Bu commit'e geç…",
            "Bu commit'i geri al…",
            "Bu commit'i cherry pick'le…",
            "Bu commit'i arşivle…",
            "──",
            "Karşılaştı_r",
            "──",
            "_Git",
        ]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Panoya_kopyala_alt_menusu_GitExtensions_alanlariyla_ayni()
    {
        // GitExtensions `CopyContextMenuItem`: hash · message · author · date, ayrıca
        // satırdaki dal/etiket adları.
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
        MenuItem copy = menu.Items.OfType<MenuItem>().Single(i => i.Header!.ToString() == "Panoya _kopyala");

        Rows(copy).ShouldBe(["Commit hash", "Mesaj", "Yazar", "Tarih", "──", "Dal adı"]);

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
        //   copy · copyPatch · copyNewVersion · copyOldVersion · ayraç · gösterim seçenekleri
        //
        // Gösterim seçenekleri bizde araç çubuğunda (P04-T13); menünün ilk yedi öğesi
        // birebir aynı sırada.
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
            "Seçili satırları _stage'le",
            "Seçili satırları _geri al",
            "Seçili satırları sıfırla…",
            "_Kopyala",
            "_Yama olarak kopyala",
            "Yeni sürümü kopyala",
            "Eski sürümü kopyala",
        ]);

        // ⚠️ Burada kullanılabilirlik SINANMIYOR, yalnızca sıra. Menü açılmadan bağlamalar
        // değerlendirilmiyor ve `IsEnabled` değerlendirilmemiş varsayılanında (true) kalıyor
        // — P05-T13'te ölçülen tuzak. Üç eylemin kullanılabilirliği, kaynağı olan
        // `IPartialStagingHost` üzerinden `PartialStagingTests` içinde test ediliyor.
        // "Sıfırla" P05-T15'te açıldı; sıradaki yeri değişmedi (§ 9).

        window.Close();

        await Task.CompletedTask;
    }
}
