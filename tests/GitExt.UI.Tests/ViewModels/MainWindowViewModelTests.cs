using Avalonia.Headless.XUnit;
using GitExt.Core.Git;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// The main window's ViewModel.
/// </summary>
/// <remarks>
/// This replaced Phase 01's "Hello World" tests: those existed to prove the test infrastructure worked
/// and they have done their job.
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
                "This folder is not a Git repository.",
                "git rev-parse",
                128,
                string.Empty));

        await viewModel.OpenRepositoryAsync("/tmp/duz-klasor");

        viewModel.Subtitle.ShouldContain("not a git repository");
    }

    [AvaloniaFact]
    public async Task PropertyChanged_baslik_degisince_tetiklenir()
    {
        // Verifies that CommunityToolkit.Mvvm's source generator ran (ADR-0004).
        MainWindowViewModel viewModel = Create();
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await viewModel.OpenRepositoryAsync("/tmp/depo");

        changed.ShouldContain(nameof(MainWindowViewModel.Subtitle));
    }
}
