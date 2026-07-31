using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
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
    private static async Task<(Window Window, WorkingTreeView View)> ShowAsync(
        params FileStatus[] entries)
    {
        FakeStatusReader status = new(entries);

        WorkingTreeViewModel model = new(
            status, new FakeStagingWriter(status), new DiffViewModel(new FakeDiffReader()));

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
}
