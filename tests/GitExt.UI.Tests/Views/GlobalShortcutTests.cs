using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.UI.Commands;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P08-T01 — küresel kısayollar <b>gerçek pencerede</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Bu dosyanın varlık sebebi gerçek bir kusur.</b> Faz 08'e girilirken
/// <c>MainWindow.axaml</c>'de <c>InputGesture="F5"</c> yazıyordu ve başka hiçbir bağlama
/// yoktu. P08-T00/M03'te ölçüldü: <c>InputGesture</c> komutu <b>çalıştırmıyor</b>, yalnızca
/// menüye yazı yazıyor. Yani <b>F5 ölü bir tuştu</b> — menüde kısayolu görünüyor, basınca
/// hiçbir şey olmuyordu ve bunu görecek hiçbir test yoktu.
/// </para>
/// <para>
/// ViewModel testi bunu yakalayamazdı: kırık olan <c>RefreshCommand</c> değil, ona giden
/// <b>yol</b>. Bu yüzden testler gerçek pencereye gerçek tuş gönderiyor.
/// </para>
/// </remarks>
public class GlobalShortcutTests
{
    private static MainWindowViewModel CreateViewModel()
    {
        FakeRemoteReader remotes = new();

        return new MainWindowViewModel(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            branchWriter: new FakeBranchWriter(),
            remoteReader: remotes,
            remoteWriter: new FakeRemoteWriter(remotes),
            pushWriter: new FakePushWriter());
    }

    private static async Task<(MainWindow Window, MainWindowViewModel Model)> ShowAsync(
        CommandRegistry? registry = null)
    {
        MainWindowViewModel model = CreateViewModel();
        MainWindow window = new();

        window.AttachShortcuts(registry ?? TestCommands.Registry());
        window.DataContext = model;

        await model.StartAsync(explicitPath: "/tmp/depo");

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, model);
    }

    /// <summary>
    /// Menü öğesini <b>mantıksal</b> ağaçtan bulur.
    /// </summary>
    /// <remarks>
    /// ⚠️ Görsel ağaçta aranamaz: alt menüler ancak <b>açıldıklarında</b> gerçekleşiyor, o
    /// yüzden <c>GetVisualDescendants()</c> kapalı bir menünün öğelerini hiç görmüyor.
    /// </remarks>
    private static MenuItem Menu(Window window, string name)
    {
        Menu root = window.GetVisualDescendants().OfType<Menu>().Single();

        return Descend(root.Items.OfType<MenuItem>()).Single(m => m.Name == name);

        static IEnumerable<MenuItem> Descend(IEnumerable<MenuItem> items)
        {
            foreach (MenuItem item in items)
            {
                yield return item;

                foreach (MenuItem child in Descend(item.Items.OfType<MenuItem>()))
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>
    /// 🔴 <c>F5</c> gerçekten yeniliyor mu?
    /// </summary>
    [AvaloniaFact]
    public async Task F5_gercekten_yeniliyor()
    {
        (MainWindow window, MainWindowViewModel model) = await ShowAsync();

        int before = model.Commits.LoadedCount;
        model.Commits.Rows.Clear();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F5, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        model.Commits.Rows.Count.ShouldBe(before, "F5 listeyi yeniden yüklemeli");

        window.Close();
    }

    /// <summary>
    /// <c>F6</c> odağı bir sonraki panele taşıyor (P08-T05).
    /// </summary>
    /// <remarks>
    /// P08-T00/M09'da ölçüldü: <c>F6</c> Avalonia'da varsayılan olarak <b>hiçbir şey
    /// yapmıyor</b>. Bağlanmasaydı sessizce ölü bir tuş olurdu — tam da <c>F5</c>'in Faz 08
    /// öncesindeki hâli.
    /// </remarks>
    [AvaloniaFact]
    public async Task F6_odagi_sonraki_panele_tasiyor()
    {
        (MainWindow window, _) = await ShowAsync();

        window.GetControl<CommitListView>("CommitList").FocusPanel();
        Dispatcher.UIThread.RunJobs();

        PanelNavigator.ContainsFocus(window.GetControl<CommitListView>("CommitList"))
            .ShouldBeTrue("başlangıç odağı commit listesinde olmalı");

        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F6, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        PanelNavigator.ContainsFocus(window.GetControl<CommitListView>("CommitList"))
            .ShouldBeFalse("odak commit listesinden çıkmalı");

        window.Close();
    }

    /// <summary>
    /// Menüde yazan kısayol ile gerçekten çalışan kısayol <b>aynı kaynaktan</b> geliyor.
    /// </summary>
    /// <remarks>
    /// İkisi ayrı yazılsaydı sessizce ayrışırlardı: kullanıcı menüde <c>F5</c> görür, başka
    /// bir tuş çalışır. Eski kodun tam olarak yaptığı buydu (etiket vardı, bağlama yoktu).
    /// </remarks>
    [AvaloniaFact]
    public async Task Menudeki_etiket_kayitla_ayni()
    {
        CommandRegistry registry = TestCommands.Registry();
        (MainWindow window, _) = await ShowAsync(registry);

        Menu(window, "MenuRepositoryRefresh").InputGesture
            .ShouldBe(registry.GetGesture(CommandIds.RepositoryRefresh));
        Menu(window, "MenuOpen").InputGesture
            .ShouldBe(registry.GetGesture(CommandIds.RepositoryOpen));
        Menu(window, "MenuPush").InputGesture
            .ShouldBe(registry.GetGesture(CommandIds.RemotePush));

        window.Close();
    }

    /// <summary>
    /// Kullanıcı kısayolu yeniden atayınca hem bağlama hem etiket <b>anında</b> güncelleniyor.
    /// </summary>
    [AvaloniaFact]
    public async Task Yeniden_atama_bagi_ve_etiketi_birlikte_gunceller()
    {
        CommandRegistry registry = TestCommands.Registry();
        (MainWindow window, _) = await ShowAsync(registry);

        registry.SetGesture(CommandIds.RepositoryRefresh, new KeyGesture(Key.F9));
        Dispatcher.UIThread.RunJobs();

        Menu(window, "MenuRepositoryRefresh").InputGesture.ShouldBe(new KeyGesture(Key.F9));

        window.KeyBindings
            .Select(b => b.Gesture)
            .ShouldContain(new KeyGesture(Key.F9));

        window.KeyBindings
            .Select(b => b.Gesture)
            .ShouldNotContain(new KeyGesture(Key.F5), "eski jest kaldırılmalı");

        window.Close();
    }

    /// <summary>
    /// Kısayolu olmayan komutlar için menüde <b>hiçbir jest yazmıyor</b>.
    /// </summary>
    [AvaloniaFact]
    public async Task Kisayolsuz_komutun_menusunde_jest_yazmaz()
    {
        (MainWindow window, _) = await ShowAsync();

        Menu(window, "MenuResetToCommit").InputGesture.ShouldBeNull();
        Menu(window, "MenuCherryPick").InputGesture.ShouldBeNull();

        window.Close();
    }

    /// <summary>
    /// 🔴 Aynı jeste iki komut kayıtlıysa pencereye <b>bir</b> bağlama giriyor.
    /// </summary>
    /// <remarks>
    /// P08-T00/M10: Avalonia ikinciyi sessizce yutuyor. Hiç kaydetmemek, "bazen çalışıyor"
    /// davranışından dürüst — çakışma zaten kayıtta raporlanıp kullanıcıya gösteriliyor.
    /// </remarks>
    [AvaloniaFact]
    public async Task Cakisan_jest_pencereye_bir_kez_giriyor()
    {
        CommandRegistry registry = TestCommands.Registry();
        (MainWindow window, _) = await ShowAsync(registry);

        registry.SetGesture(CommandIds.RemotePush, new KeyGesture(Key.F5));
        Dispatcher.UIThread.RunJobs();

        window.KeyBindings
            .Count(b => Equals(b.Gesture, new KeyGesture(Key.F5)))
            .ShouldBe(1);

        registry.Conflicts.ShouldNotBeEmpty("çakışma kullanıcıya raporlanabilmeli");

        window.Close();
    }
}
