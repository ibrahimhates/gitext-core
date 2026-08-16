using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// Is the main menu's enabled state really being updated?
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This file exists because of a real bug.</b> <c>HasRepository</c> was a computed property and
/// <c>PropertyChanged</c> was raised only <b>on close</b>, never on open. The binding froze at its
/// initial value (<see langword="false"/>) and <b>two whole sections</b> of the main menu
/// (<i>Repository</i>, <i>Commands</i>) stayed greyed out even with a repository open — meaning the
/// commit screen, the branch commands and remote management were unreachable from the menu.
/// </para>
/// <para>
/// ⚠️ <b>The existing ViewModel test did not catch this</b> and could not have:
/// <c>model.HasRepository.ShouldBeTrue()</c> passes, because what is broken is not the property's
/// <b>value</b> but its <b>notification</b>. The same class as the
/// <c>IsVisible="{Binding …Count}"</c> trap in Phase 03. That is why the tests here read
/// <c>MenuItem.IsEnabled</c> from a <b>real window</b>: where you look must be where the thing you are
/// verifying lives (the P04-T09 render test rule).
/// </para>
/// </remarks>
public class MainWindowBindingTests
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
            pushWriter: new FakePushWriter())
        {
            PushPrompt = new FakePushPrompt(),
        };
    }

    private static MenuItem TopLevelMenu(Window window, string header) =>
        window.GetVisualDescendants()
            .OfType<Menu>()
            .SelectMany(menu => menu.Items.OfType<MenuItem>())
            .Single(item => item.Header?.ToString() == header);

    private static async Task<(Window Window, MainWindowViewModel Model)> ShowAsync()
    {
        MainWindowViewModel model = CreateViewModel();
        MainWindow window = new() { DataContext = model, Width = 1000, Height = 600 };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return await Task.FromResult<(Window, MainWindowViewModel)>((window, model));
    }

    [AvaloniaTheory]
    [InlineData("_Repository")]
    [InlineData("_Commands")]
    public async Task Depo_acilinca_menu_ETKINLESIYOR(string header)
    {
        (Window window, MainWindowViewModel model) = await ShowAsync();

        MenuItem menu = TopLevelMenu(window, header);
        menu.IsEnabled.ShouldBeFalse("depo açılmadan menü etkin olmamalı");

        await model.OpenRepositoryAsync("/tmp/depo");
        Dispatcher.UIThread.RunJobs();

        menu.IsEnabled.ShouldBeTrue(
            $"'{header}' menüsü depo açıldıktan sonra da soluk kaldı — bağlama güncellenmiyor.");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Depo_kapaninca_menu_yeniden_SOLUYOR()
    {
        (Window window, MainWindowViewModel model) = await ShowAsync();

        await model.OpenRepositoryAsync("/tmp/depo");
        Dispatcher.UIThread.RunJobs();

        model.CloseRepositoryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        TopLevelMenu(window, "_Repository").IsEnabled.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Acilista_sessizce_acilan_depoda_da_menu_ETKIN()
    {
        // The reason for putting the notification on the `Commits.Repository` subscription rather than
        // on the individual call paths is this route: with no path given, the application silently tries
        // the working directory (P03-T16), and a notification bolted onto `OpenRepositoryAsync` would
        // not run here.
        (Window window, MainWindowViewModel model) = await ShowAsync();

        await model.StartAsync(explicitPath: "/tmp/depo");
        Dispatcher.UIThread.RunJobs();

        TopLevelMenu(window, "_Repository").IsEnabled.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Depoya_bagli_komutlar_acilista_CanExecute_bildiriyor()
    {
        // The menu's child items are rebuilt on every open, but persistent bindings (the toolbar, the
        // shortcut) do not ask again unless `CanExecuteChanged` arrives.
        (Window window, MainWindowViewModel model) = await ShowAsync();

        List<string> changed = [];

        model.CreateBranchCommand.CanExecuteChanged += (_, _) => changed.Add("dal");
        model.ManageRemotesCommand.CanExecuteChanged += (_, _) => changed.Add("remote");
        model.PushCommand.CanExecuteChanged += (_, _) => changed.Add("push");

        await model.OpenRepositoryAsync("/tmp/depo");
        Dispatcher.UIThread.RunJobs();

        changed.ShouldContain("dal");
        changed.ShouldContain("remote");
        changed.ShouldContain("push");

        window.Close();
    }
}
