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
/// P08-T01 — global shortcuts <b>in a real window</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This file exists because of a real defect.</b> Going into Phase 08, <c>MainWindow.axaml</c>
/// said <c>InputGesture="F5"</c> and there was no other binding at all. Measured in P08-T00/M03:
/// <c>InputGesture</c> <b>does not run</b> the command, it only writes text into the menu. So
/// <b>F5 was a dead key</b> — the shortcut showed in the menu, nothing happened when it was pressed,
/// and there was no test that would see it.
/// </para>
/// <para>
/// A ViewModel test could not have caught this: what was broken was not <c>RefreshCommand</c> but the
/// <b>path</b> to it. That is why these tests send real keys to a real window.
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
    /// Finds the menu item in the <b>logical</b> tree.
    /// </summary>
    /// <remarks>
    /// ⚠️ It cannot be searched for in the visual tree: submenus are only realised when they are
    /// <b>opened</b>, so <c>GetVisualDescendants()</c> never sees the items of a closed menu.
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
    /// 🔴 Does <c>F5</c> really refresh?
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
    /// <c>F6</c> moves focus to the next panel (P08-T05).
    /// </summary>
    /// <remarks>
    /// Measured in P08-T00/M09: by default <c>F6</c> <b>does nothing</b> in Avalonia. Unless it were
    /// bound it would be a silently dead key — exactly what <c>F5</c> was before Phase 08.
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
    /// The shortcut written in the menu and the shortcut that actually works come from <b>the same
    /// source</b>.
    /// </summary>
    /// <remarks>
    /// Written separately, they would silently diverge: the user sees <c>F5</c> in the menu while
    /// another key works. That is exactly what the old code did (there was a label, there was no
    /// binding).
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
    /// When the user rebinds a shortcut, both the binding and the label update <b>immediately</b>.
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
    /// For commands with no shortcut, <b>no gesture is written</b> in the menu.
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
    /// 🔴 When two commands are registered on the same gesture, <b>one</b> binding goes to the window.
    /// </summary>
    /// <remarks>
    /// P08-T00/M10: Avalonia swallows the second one silently. Not registering it at all is more honest
    /// than "it works sometimes" behaviour — the conflict is reported in the registry and shown to the
    /// user anyway.
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
