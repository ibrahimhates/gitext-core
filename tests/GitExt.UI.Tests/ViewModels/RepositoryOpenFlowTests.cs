using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T16 — The repository open flow · P03-T17 — Loading and empty states.
/// </summary>
public class RepositoryOpenFlowTests
{
    private static GitException NotARepository() =>
        new(GitFailureKind.NotARepository,
            "This folder is not a Git repository.",
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
                new FakeCommitSignatureReader(),new FakeDiffReader()),
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
        // The user asked for this path explicitly; swallowing it silently would puzzle them.
        MainWindowViewModel viewModel = Create(locateFailure: NotARepository());

        await viewModel.StartAsync("/tmp/depo-degil");

        viewModel.Commits.ErrorMessage.ShouldNotBeNullOrEmpty();
        viewModel.ShowWelcome.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Calisma_dizini_depo_degilse_hata_gosterilmez()
    {
        // When the application is started from the desktop the working directory is somewhere arbitrary;
        // showing a "this is not a repository" error would be meaningless.
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
        // The user may drag a file from their file manager. Because git already searches upwards for the
        // repository root, any file inside the repository is enough.
        string file = Path.Combine(Path.GetTempPath(), $"gitext-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);

        try
        {
            MainWindowViewModel viewModel = Create();

            (await viewModel.TryOpenDroppedAsync([file])).ShouldBeTrue();

            // The folder the file is in must have been opened, not the file itself.
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
        // A fresh `git init`: not an error but a state that needs explaining (P03-T17).
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
        // Otherwise the path of a repository whose rows have been cleared stays in the title and the user
        // thinks it is still open.
        CommitListViewModel commits = new(
            new FailingAfterFirstLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader());

        MainWindowViewModel viewModel = new(commits, new FakeRecentRepositoryStore());

        await viewModel.OpenRepositoryAsync("/tmp/iyi-depo");
        commits.Repository.ShouldNotBeNull();

        await viewModel.OpenRepositoryAsync("/tmp/kotu-depo");

        commits.Repository.ShouldBeNull();
        viewModel.ShowWelcome.ShouldBeTrue();
    }

    /// <summary>A locator that succeeds on the first call and fails on the ones after.</summary>
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
