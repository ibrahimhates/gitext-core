using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T14 — dosya sistemi izlemeye ViewModel tarafının tepkisi.
/// </summary>
/// <remarks>
/// İzleyicinin kendisi <c>RepositoryWatcherTests</c> içinde gerçek <c>git</c> ile test
/// ediliyor; burada test edilen şey <b>olay geldiğinde ne yapıldığı</b>.
/// </remarks>
public class AutoRefreshTests
{
    private static FileStatus Unstaged(string path) =>
        new() { Path = RepositoryPath.Parse(path), UnstagedChange = FileChangeKind.Modified };

    private static MainWindowViewModel CreateMain(FakeRepositoryWatcher watcher) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            watcher: watcher);

    private static async Task<(WorkingTreeViewModel Model, FakeStatusReader Status)> CreateWorkingTreeAsync(
        FakeRepositoryWatcher watcher)
    {
        FakeStatusReader status = new([Unstaged("a.txt")]);

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()),
            watcher: watcher);

        await model.OpenAsync("/tmp/depo");

        return (model, status);
    }

    /// <summary>Olay <c>Dispatcher</c>'a post ediliyor; kuyruğun boşalmasını bekler.</summary>
    /// <param name="until">
    /// Beklenen sonuç; sağlanınca erken çıkılır. Verilmezse yalnızca kuyruk boşaltılır.
    /// </param>
    private static async Task DrainAsync(Func<bool>? until = null)
    {
        for (int i = 0; i < 40; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            if (until?.Invoke() == true)
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    [AvaloniaFact]
    public async Task Depo_acilinca_izleme_baslar()
    {
        FakeRepositoryWatcher watcher = new();
        MainWindowViewModel model = CreateMain(watcher);

        await model.OpenRepositoryAsync("/tmp/depo");

        watcher.IsRunning.ShouldBeTrue();
        watcher.WorkingTreeRoot.ShouldBe("/tmp/depo");

        // ⚠️ Üç yol da veriliyor: bağlı çalışma ağacında ref'ler ortak dizinde, HEAD ve
        // index ise o ağacın kendi git dizininde (CLAUDE.md § 5, madde 9).
        watcher.GitDirectory.ShouldNotBeNullOrEmpty();
        watcher.CommonDirectory.ShouldNotBeNullOrEmpty();
    }

    [AvaloniaFact]
    public async Task Depo_kapaninca_izleme_durur()
    {
        FakeRepositoryWatcher watcher = new();
        MainWindowViewModel model = CreateMain(watcher);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.CloseRepository();

        watcher.IsRunning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Ref_degisimi_commit_listesini_tazeler()
    {
        // Başka bir terminalde yapılan commit: kullanıcının elle tazelemesi gerekmemeli.
        FakeRepositoryWatcher watcher = new();
        MainWindowViewModel model = CreateMain(watcher);

        await model.OpenRepositoryAsync("/tmp/depo");

        int before = watcher.StartCount;

        watcher.Raise(RepositoryChangeKind.Repository);
        await DrainAsync(() => watcher.StartCount > before);

        // Tazeleme depoyu yeniden açıyor; izleme de yeniden kuruluyor.
        watcher.StartCount.ShouldBeGreaterThan(before);
    }

    [AvaloniaFact]
    public async Task Calisma_agaci_degisimi_commit_listesini_tazelemez()
    {
        // 🔴 Her dosya kaydedişinde commit geçmişini yeniden okumak, ölçülen sürelerle
        // (git/git 2,1 sn · Linux 31,6 sn) uygulamayı kullanılamaz hale getirirdi.
        FakeRepositoryWatcher watcher = new();
        MainWindowViewModel model = CreateMain(watcher);

        await model.OpenRepositoryAsync("/tmp/depo");

        int before = watcher.StartCount;

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync();

        watcher.StartCount.ShouldBe(before);
    }

    [AvaloniaFact]
    public async Task Calisma_agaci_degisimi_commit_ekranini_tazeler()
    {
        FakeRepositoryWatcher watcher = new();
        (WorkingTreeViewModel model, FakeStatusReader status) = await CreateWorkingTreeAsync(watcher);

        int before = status.ReadCallCount;

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync(() => status.ReadCallCount > before);

        status.ReadCallCount.ShouldBeGreaterThan(before);

        model.Dispose();
    }

    [AvaloniaFact]
    public async Task Otomatik_tazeleme_YAZILAN_MESAJI_silmez()
    {
        // 🔴 P05-T13'ün değişmezi: hiçbir arka plan olayı kullanıcının yazdığı metni
        // ezmez. Buradaki tazelemeyi kullanıcı istememişti — bir dosya değişti diye
        // commit mesajının kaybolması kabul edilemez.
        FakeRepositoryWatcher watcher = new();
        (WorkingTreeViewModel model, _) = await CreateWorkingTreeAsync(watcher);

        model.Message.Text = "yarım kalmış mesaj";

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync();

        model.Message.Text.ShouldBe("yarım kalmış mesaj");

        model.Dispose();
    }

    [AvaloniaFact]
    public async Task Tazeleme_sirasinda_izleme_ASKIYA_ALINIR()
    {
        // 🔴 Sonsuz döngünün ViewModel tarafındaki kapısı. ÖLÇÜLDÜ: `git status` bir depoda
        // ilk çalıştığında index'i yeniden yazıyor — tazelemenin kendisi yeni bir olay
        // doğuruyor. Askı olmasaydı bu zincir kendini beslerdi.
        FakeRepositoryWatcher watcher = new();
        FakeStatusReader status = new([Unstaged("a.txt")]);
        bool suspendedDuringRead = false;

        status.OnRead = () => suspendedDuringRead |= watcher.IsSuspended;

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()),
            watcher: watcher);

        await model.OpenAsync("/tmp/depo");

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync(() => suspendedDuringRead);

        suspendedDuringRead.ShouldBeTrue();

        model.Dispose();
    }

    [AvaloniaFact]
    public async Task Pencere_kapaninca_abonelik_birakilir()
    {
        // İzleyici uygulama ömrü boyunca yaşıyor; bırakılmayan abonelik kapalı bir ekran
        // için `git status` çalıştırmaya devam ederdi.
        FakeRepositoryWatcher watcher = new();
        (WorkingTreeViewModel model, FakeStatusReader status) = await CreateWorkingTreeAsync(watcher);

        model.Dispose();

        int after = status.ReadCallCount;

        watcher.Raise(RepositoryChangeKind.WorkingTree);
        await DrainAsync();

        status.ReadCallCount.ShouldBe(after);
    }

    [AvaloniaFact]
    public void Izleyici_verilmeden_de_calisir()
    {
        // Otomatik tazeleme bir kolaylık; izleyici kurulamamış olabilir (inotify sınırı,
        // izin hatası) ve uygulama o zaman da açılmalı.
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(1)),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore());

        Should.NotThrow(model.CloseRepository);
    }
}
