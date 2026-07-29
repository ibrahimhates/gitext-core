using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T16 — Depo açma akışı · P03-T17 — Yükleme ve boş durumlar.
/// </summary>
public class RepositoryOpenFlowTests
{
    private static GitException NotARepository() =>
        new(GitFailureKind.NotARepository,
            "Bu klasör bir Git deposu değil.",
            "git rev-parse",
            128,
            "fatal: not a git repository");

    private static MainWindowViewModel Create(
        int commitCount = 3,
        Exception? locateFailure = null,
        FakeRecentRepositoryStore? recent = null) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(locateFailure),
                new FakeCommitLogReader(FakeGitData.LinearHistory(commitCount)),
                new FakeRefReader(),
                new FakeCommitSignatureReader()),
            recent ?? new FakeRecentRepositoryStore());

    [AvaloniaFact]
    public void Depo_acilmadan_once_karsilama_ekrani_gosterilir()
    {
        Create().ShowWelcome.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Depo_acilinca_karsilama_ekrani_gizlenir()
    {
        MainWindowViewModel viewModel = Create();

        await viewModel.OpenRepositoryAsync("/tmp/depo");

        viewModel.ShowWelcome.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Acik_yol_verildiginde_basarisizlik_hata_olarak_gosterilir()
    {
        // Kullanıcı bu yolu açıkça istedi; sessizce yutmak onu şaşırtır.
        MainWindowViewModel viewModel = Create(locateFailure: NotARepository());

        await viewModel.StartAsync("/tmp/depo-degil");

        viewModel.Commits.ErrorMessage.ShouldNotBeNullOrEmpty();
        viewModel.ShowWelcome.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Calisma_dizini_depo_degilse_hata_gosterilmez()
    {
        // Uygulama masaüstünden başlatıldığında çalışma dizini rastgele bir yerdir;
        // "burası depo değil" hatası göstermek anlamsız olur.
        MainWindowViewModel viewModel = Create(locateFailure: NotARepository());

        await viewModel.StartAsync(explicitPath: null);

        viewModel.Commits.ErrorMessage.ShouldBeNull();
        viewModel.ShowWelcome.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Acilan_depo_son_acilanlara_eklenir()
    {
        FakeRecentRepositoryStore recent = new();
        MainWindowViewModel viewModel = Create(recent: recent);

        await viewModel.OpenRepositoryAsync("/tmp/depo");

        viewModel.RecentRepositories.Count.ShouldBe(1);
        viewModel.RecentRepositories[0].Path.ShouldBe("/tmp/depo");
    }

    [AvaloniaFact]
    public async Task Son_acilanlar_acilista_yuklenir()
    {
        MainWindowViewModel viewModel = Create(
            locateFailure: NotARepository(),
            recent: new FakeRecentRepositoryStore("/tmp/bir", "/tmp/iki"));

        await viewModel.StartAsync(explicitPath: null);

        viewModel.RecentRepositories.Select(r => r.Path).ShouldBe(["/tmp/bir", "/tmp/iki"]);
    }

    [AvaloniaFact]
    public async Task Acilamayan_depo_son_acilanlara_yazilmaz()
    {
        FakeRecentRepositoryStore recent = new();
        MainWindowViewModel viewModel = Create(locateFailure: NotARepository(), recent: recent);

        await viewModel.OpenRepositoryAsync("/tmp/depo-degil");

        viewModel.RecentRepositories.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Son_acilan_girdisi_klasor_adini_gosterir()
    {
        MainWindowViewModel viewModel = Create();

        await viewModel.OpenRepositoryAsync("/home/kullanici/projeler/benim-depom");

        viewModel.RecentRepositories[0].Name.ShouldBe("benim-depom");
    }

    [AvaloniaFact]
    public async Task Birakilan_klasor_acilir()
    {
        MainWindowViewModel viewModel = Create();

        string directory = Path.GetTempPath();

        (await viewModel.TryOpenDroppedAsync([directory])).ShouldBeTrue();

        viewModel.Commits.Repository.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task Birakilan_DOSYA_icin_bulundugu_klasor_denenir()
    {
        // Kullanıcı dosya yöneticisinden bir dosya sürükleyebilir. git zaten üst klasörlere
        // doğru depo kökünü aradığı için deponun içindeki herhangi bir dosya yeterlidir.
        string file = Path.Combine(Path.GetTempPath(), $"gitext-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);

        try
        {
            MainWindowViewModel viewModel = Create();

            (await viewModel.TryOpenDroppedAsync([file])).ShouldBeTrue();

            // Dosyanın kendisi değil, bulunduğu klasör açılmış olmalı.
            viewModel.Commits.Repository!.WorkingDirectory
                .ShouldBe(Path.GetDirectoryName(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [AvaloniaFact]
    public async Task Bos_birakma_hicbir_sey_yapmaz()
    {
        MainWindowViewModel viewModel = Create();

        (await viewModel.TryOpenDroppedAsync([])).ShouldBeFalse();

        viewModel.Commits.Repository.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Commitsiz_depo_bos_olarak_isaretlenir()
    {
        // Yeni `git init`: hata değil, anlatılması gereken durum (P03-T17).
        MainWindowViewModel viewModel = Create(commitCount: 0);

        await viewModel.OpenRepositoryAsync("/tmp/bos-depo");

        viewModel.Commits.IsEmptyRepository.ShouldBeTrue();
        viewModel.Commits.ErrorMessage.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Commitli_depo_bos_isaretlenmez()
    {
        MainWindowViewModel viewModel = Create(commitCount: 5);

        await viewModel.OpenRepositoryAsync("/tmp/depo");

        viewModel.Commits.IsEmptyRepository.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Yuklenen_satir_sayisi_bildirilir()
    {
        MainWindowViewModel viewModel = Create(commitCount: 300);

        await viewModel.OpenRepositoryAsync("/tmp/depo");

        viewModel.Commits.LoadedCount.ShouldBe(300);
    }

    [AvaloniaFact]
    public async Task Basarisiz_acilis_onceki_depoyu_ekranda_birakmaz()
    {
        // Aksi halde satırları temizlenmiş bir deponun yolu başlıkta kalır ve kullanıcı
        // hâlâ onun açık olduğunu sanır.
        CommitListViewModel commits = new(
            new FailingAfterFirstLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
            new FakeRefReader(),
            new FakeCommitSignatureReader());

        MainWindowViewModel viewModel = new(commits, new FakeRecentRepositoryStore());

        await viewModel.OpenRepositoryAsync("/tmp/iyi-depo");
        commits.Repository.ShouldNotBeNull();

        await viewModel.OpenRepositoryAsync("/tmp/kotu-depo");

        commits.Repository.ShouldBeNull();
        viewModel.ShowWelcome.ShouldBeTrue();
    }

    /// <summary>İlk çağrıda başarılı, sonrakilerde başarısız olan konumlandırıcı.</summary>
    private sealed class FailingAfterFirstLocator : IRepositoryLocator
    {
        private int _calls;

        public Task<RepositoryLocation> LocateAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            _calls++ == 0
                ? Task.FromResult(FakeGitData.Location(path))
                : Task.FromException<RepositoryLocation>(NotARepository());
    }
}
