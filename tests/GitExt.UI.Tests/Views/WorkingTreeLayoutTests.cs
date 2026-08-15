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
/// P05-T09 — çalışma dizini ekranının yerleşimi GitExtensions'ı takip ediyor mu?
/// </summary>
/// <remarks>
/// <para>
/// CLAUDE.md § 9: görsel tasarım birebir aynı olmak zorunda değil ama <b>öğelerin yeri ve
/// sırası</b> aynı olmalı. Kaynak: <c>FormCommit.Designer.cs</c> —
/// <c>splitLeft.Panel1 = Unstaged</c> (üst), <c>splitLeft.Panel2 = Staged</c> (alt),
/// aradaki <c>toolbarStaged</c>, sağda <c>SelectedDiff</c>.
/// </para>
/// <para>
/// Test <b>konum</b> karşılaştırıyor, görünüm değil: renk ya da yazı tipi değişebilir,
/// listelerin yer değiştirmesi kullanıcının kas hafızasını kırar.
/// </para>
/// </remarks>
public class WorkingTreeLayoutTests
{
    /// <remarks>
    /// ⚠️ <c>async</c> olmak zorunda: <c>OpenAsync</c> UI iş parçacığına dönüyor
    /// (<c>ConfigureAwait(true)</c>) ve <c>GetAwaiter().GetResult()</c> ile beklemek
    /// headless testte <b>kilitlenme</b> üretiyor (ölçüldü — test takımı hiç bitmedi).
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
        // `toolbarStaged`'de stage öğeleri `ToolStripItemAlignment.Right`, unstage öğeleri
        // varsayılan (sol) hizalı. Yön listelerin konumuyla uyumlu.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        Rect unstageAll = BoundsIn(window, view.GetControl<Button>("UnstageAllButton"));
        Rect unstage = BoundsIn(window, view.GetControl<Button>("UnstageButton"));
        Rect stage = BoundsIn(window, view.GetControl<Button>("StageButton"));
        Rect stageAll = BoundsIn(window, view.GetControl<Button>("StageAllButton"));

        // Soldan sağa: [Tümünü geri al][Geri al] … [Stage][Tümünü stage'le]
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
        // ViewModel testinde `ShowFileList = false` doğrulanıyor; burada bunun ekranda da
        // karşılığı olduğu görülüyor (bağlama sessizce çalışmayabilir — Faz 03'ün dersi).
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
        // FormCommit: splitRight.Panel1 = SelectedDiff, Panel2 = commit mesajı.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        DiffView diff = view.GetVisualDescendants().OfType<DiffView>().Single();
        Rect message = BoundsIn(window, view.GetControl<TextBox>("MessageBox"));

        BoundsIn(window, diff).Bottom.ShouldBeLessThanOrEqualTo(message.Y);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Commit_dugmeleri_SOLDA_mesaj_kutusu_SAGDA()
    {
        // `tableLayoutPanel1`: sol sütun `flowCommitButtons`, sağ sütun `Message`.
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
        // Uygulanmamış olanlar devre dışı ama YERİNDE (§ 9).
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

        // P05-T15'te açıldılar; sıradaki yerleri değişmedi.
        view.GetControl<Button>("ResetAllButton").IsEnabled.ShouldBeTrue();
        view.GetControl<Button>("ResetUnstagedButton").IsEnabled.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Elli_ve_yetmis_iki_kilavuzu_gercekten_CIZILIYOR()
    {
        // P04-T09'un dersi: bağlama sessizce hiçbir şey çizmeyebilir. Kılavuz çizilmezse
        // kullanıcı sınırı göremez ve özellik hiç yokmuş gibi olur.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        ColumnGuides guides = view.GetControl<ColumnGuides>("MessageGuides");

        guides.IsEffectivelyVisible.ShouldBeTrue();
        guides.Columns.ShouldBe([50, 72]);
        guides.Bounds.Width.ShouldBeGreaterThan(0);

        window.Close();
    }

    // ---- P05-T13: mesaj yardımcıları araç çubuğu ----

    [AvaloniaFact]
    public async Task Mesaj_araci_cubugu_GitExtensions_sirasinda()
    {
        // `toolbarCommit` sırası: commit message ▾ · options ▾ · templates ▾ · create branch.
        // Uygulanmamış olanlar (Seçenekler, Dal oluştur) devre dışı ama YERİNDE (§ 9);
        // sonradan araya sokmak sırayı bozar ve kas hafızasını kırar.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        double Left(string name) => BoundsIn(
            window,
            view.GetVisualDescendants().OfType<Control>().First(c => c.Name == name)).X;

        Left("MessageHistoryButton").ShouldBeLessThan(Left("CommitOptionsButton"));
        Left("CommitOptionsButton").ShouldBeLessThan(Left("CommitTemplatesButton"));
        Left("CommitTemplatesButton").ShouldBeLessThan(Left("CreateBranchButton"));

        // P05-T13'te ikisi, P05-T15'te "Seçenekler" açıldı; "Create branch" Faz 06'da.
        view.GetControl<Button>("MessageHistoryButton").IsEnabled.ShouldBeTrue();
        view.GetControl<Button>("CommitTemplatesButton").IsEnabled.ShouldBeTrue();
        view.GetControl<Button>("CommitOptionsButton").IsEnabled.ShouldBeTrue();
        view.GetControl<Button>("CreateBranchButton").IsEnabled.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Gecmis_menusu_GERCEKTEN_ogeler_ciziyor()
    {
        // 🔑 Fazın tekrar eden dersi: ViewModel testi yeşilken menü ekranda BOŞ olabilir
        // (P04-T09'daki boş hunk başlıkları, P03'teki `IsVisible` bağlaması). Burada
        // doğrulanan şey koleksiyonun dolması değil, menünün öğe ÜRETMESİ.
        FakeCommitMessageReader reader = new();
        reader.Recent.AddRange(["ikinci konu", "ilk konu"]);

        (Window window, WorkingTreeView view) = await ShowAsync(reader, Unstaged("a.txt"));

        Button historyButton = view.GetControl<Button>("MessageHistoryButton");
        MenuFlyout flyout = historyButton.Flyout.ShouldBeOfType<MenuFlyout>();

        flyout.ShowAt(historyButton);
        Dispatcher.UIThread.RunJobs();

        // Menü asenkron dolduğu için (git okuması) bir tur daha gerekiyor.
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        List<string> headers =
        [
            .. flyout.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? string.Empty),
        ];

        headers.ShouldContain("ikinci konu");
        headers.ShouldContain("ilk konu");

        // Sabit öğe (filtre) korunuyor — geçmiş satırları onun üzerine yazmamalı.
        headers.ShouldContain("Only my messages");

        flyout.Hide();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Sablon_menusu_ayarsizken_de_NEDENINI_soyluyor()
    {
        // Boş bir menü, kullanıcıya "burada bir şey olmalıydı" dedirtir. Şablon yoksa
        // sebebi yazıyor; bozuksa yolu yazıyor (git'in kendisi de o durumda commit'i
        // reddediyor — ölçüldü).
        //
        // ⚠️ ÖLÇÜLDÜ: menü AÇILMADAN bakmak yanıltıcı. Flyout kapalıyken içindeki öğeler
        // görsel ağaçta değil, `DataContext` akmıyor ve `IsEnabled` bağlaması hiç
        // değerlendirilmemiş varsayılanında (true) duruyor. Doğrulanacak şey kullanıcının
        // gördüğü durum, yani menü açıkken.
        (Window window, WorkingTreeView view) = await ShowAsync(Unstaged("a.txt"));

        Button templatesButton = view.GetControl<Button>("CommitTemplatesButton");
        MenuFlyout flyout = templatesButton.Flyout.ShouldBeOfType<MenuFlyout>();

        flyout.ShowAt(templatesButton);
        Dispatcher.UIThread.RunJobs();

        MenuItem item = flyout.Items.OfType<MenuItem>().First(menuItem => menuItem.Name == "TemplateItem");

        item.Header?.ToString().ShouldBe("commit.template ayarlı değil");
        item.IsEnabled.ShouldBeFalse();

        flyout.Hide();
        window.Close();
    }
}
