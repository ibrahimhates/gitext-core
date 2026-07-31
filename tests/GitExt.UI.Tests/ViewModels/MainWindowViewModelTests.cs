using Avalonia.Headless.XUnit;
using GitExt.Core.Git;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// Ana pencere ViewModel'ı.
/// </summary>
/// <remarks>
/// Faz 01'in "Hello World" testlerinin yerini aldı: o testler test altyapısının çalıştığını
/// kanıtlamak içindi ve görevlerini tamamladılar.
/// </remarks>
public class MainWindowViewModelTests
{
    private static MainWindowViewModel Create(
        int commitCount = 3,
        Exception? locateFailure = null) =>
        new(new CommitListViewModel(
            new FakeRepositoryLocator(locateFailure),
            new FakeCommitLogReader(FakeGitData.LinearHistory(commitCount)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader()),
            new FakeRecentRepositoryStore());

    [AvaloniaFact]
    public void Depo_acilmadan_once_bilgilendirici_baslik_gosterir()
    {
        Create().Subtitle.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public async Task Depo_acilinca_baslik_yol_ve_commit_sayisini_gosterir()
    {
        MainWindowViewModel viewModel = Create(commitCount: 7);

        await viewModel.OpenRepositoryAsync("/tmp/benim-depom");

        viewModel.Subtitle.ShouldContain("/tmp/benim-depom");
        viewModel.Subtitle.ShouldContain("7");
        viewModel.Commits.Rows.Count.ShouldBe(7);
    }

    [AvaloniaFact]
    public async Task Acilis_basarisizsa_baslik_hatayi_gosterir()
    {
        MainWindowViewModel viewModel = Create(
            locateFailure: new GitException(
                GitFailureKind.NotARepository,
                "Bu klasör bir Git deposu değil.",
                "git rev-parse",
                128,
                string.Empty));

        await viewModel.OpenRepositoryAsync("/tmp/duz-klasor");

        viewModel.Subtitle.ShouldContain("Git deposu değil");
    }

    [AvaloniaFact]
    public async Task PropertyChanged_baslik_degisince_tetiklenir()
    {
        // CommunityToolkit.Mvvm source generator'ının çalıştığını doğrular (ADR-0004).
        MainWindowViewModel viewModel = Create();
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await viewModel.OpenRepositoryAsync("/tmp/depo");

        changed.ShouldContain(nameof(MainWindowViewModel.Subtitle));
    }
}
