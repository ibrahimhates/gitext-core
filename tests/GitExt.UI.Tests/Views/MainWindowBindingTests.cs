using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// Ana menünün etkinliği gerçekten güncelleniyor mu?
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Bu dosyanın varlık sebebi gerçek bir hata.</b> <c>HasRepository</c> hesaplanan bir
/// özellikti ve <c>PropertyChanged</c> yalnızca <b>kapanışta</b> gönderiliyordu; açılışta
/// hiç. Bağlama ilk değerinde (<see langword="false"/>) donuyordu ve ana menünün
/// <b>iki bölümü birden</b> (<i>Depo</i>, <i>Komutlar</i>) depo açıkken de soluk kalıyordu —
/// yani commit ekranı, dal komutları ve uzak depo yönetimi menüden ulaşılamıyordu.
/// </para>
/// <para>
/// ⚠️ <b>Mevcut ViewModel testi bunu yakalamıyordu</b> ve yakalayamazdı:
/// <c>model.HasRepository.ShouldBeTrue()</c> geçiyor, çünkü kırık olan özelliğin
/// <b>değeri</b> değil <b>bildirimi</b>. Faz 03'teki <c>IsVisible="{Binding …Count}"</c>
/// tuzağıyla aynı sınıf. Bu yüzden testler burada <b>gerçek pencere</b> üzerinden
/// <c>MenuItem.IsEnabled</c> okuyor: baktığın yer, doğruladığın şeyin yeri olmalı
/// (P04-T09 render testi kuralı).
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
        // Bildirimi tek tek çağrı yollarına koymak yerine `Commits.Repository` aboneliğine
        // koymanın sebebi bu yol: uygulama, yol verilmediğinde çalışma dizinini sessizce
        // deniyor (P03-T16) ve `OpenRepositoryAsync`'e yamanmış bir bildirim burada
        // çalışmazdı.
        (Window window, MainWindowViewModel model) = await ShowAsync();

        await model.StartAsync(explicitPath: "/tmp/depo");
        Dispatcher.UIThread.RunJobs();

        TopLevelMenu(window, "_Repository").IsEnabled.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Depoya_bagli_komutlar_acilista_CanExecute_bildiriyor()
    {
        // Menü alt öğeleri her açılışta yeniden kuruluyor, ama kalıcı bağlamalar
        // (araç çubuğu, kısayol) `CanExecuteChanged` gelmedikçe sormuyor.
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
