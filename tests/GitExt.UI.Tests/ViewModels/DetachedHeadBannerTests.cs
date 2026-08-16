using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T04 — the detached HEAD and in-progress operation strips (the ViewModel side).
/// </summary>
public class DetachedHeadBannerTests
{
    private static MainWindowViewModel Create(
        bool detached,
        InProgressOperation operation = InProgressOperation.None)
    {
        RepositoryRefs refs = detached
            ? FakeGitData.Refs(
                head: new HeadState
                {
                    IsDetached = true,
                    IsUnborn = false,
                    Commit = CommitId.Parse(FakeGitData.Sha(3)),
                })
            : FakeGitData.Refs(
                localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(3), isCurrent: true)]);

        return new MainWindowViewModel(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(refs),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            branchWriter: new FakeBranchWriter(),
            operations: new FakeInProgressOperationReader(operation));
    }

    [AvaloniaFact]
    public async Task Normal_dalda_HICBIR_serit_yok()
    {
        MainWindowViewModel model = Create(detached: false);

        await model.OpenRepositoryAsync("/tmp/depo");

        model.IsDetachedHead.ShouldBeFalse();
        model.ShowDetachedBanner.ShouldBeFalse();
        model.ShowOperationBanner.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Ayrik_HEAD_te_serit_GORUNUYOR()
    {
        MainWindowViewModel model = Create(detached: true);

        await model.OpenRepositoryAsync("/tmp/depo");

        model.IsDetachedHead.ShouldBeTrue();
        model.ShowDetachedBanner.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Rebase_sirasinda_ayrik_serit_GOSTERILMIYOR()
    {
        // 🔴 P06-T04's central decision. MEASURED: during a rebase HEAD really is detached.
        // A plain warning would pop up here too and say "create a branch here" — whereas the user is
        // deliberately in the middle of an operation and creating a branch is not the thing to do.
        MainWindowViewModel model = Create(detached: true, operation: InProgressOperation.Rebase);

        await model.OpenRepositoryAsync("/tmp/depo");

        model.IsDetachedHead.ShouldBeTrue();
        model.ShowDetachedBanner.ShouldBeFalse();
        model.ShowOperationBanner.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Bisect_sirasinda_da_ISLEM_seridi_gosteriliyor()
    {
        MainWindowViewModel model = Create(detached: true, operation: InProgressOperation.Bisect);

        await model.OpenRepositoryAsync("/tmp/depo");

        model.ShowDetachedBanner.ShouldBeFalse();
        model.OperationText.ShouldContain("Bisect");
    }

    [AvaloniaFact]
    public async Task Merge_cakismasi_dal_uzerindeyken_de_bildiriliyor()
    {
        // A merge conflict does not detach HEAD; the strip must appear all the same.
        MainWindowViewModel model = Create(detached: false, operation: InProgressOperation.Merge);

        await model.OpenRepositoryAsync("/tmp/depo");

        model.ShowOperationBanner.ShouldBeTrue();
        model.OperationText.ShouldContain("Merge");
    }

    [AvaloniaFact]
    public async Task Her_islem_KENDI_metnini_veriyor()
    {
        // If they all fall back to a single "an operation is in progress" text the user cannot tell what
        // to do; the real information is which operation is in progress and how to get out of it.
        List<string> texts = [];

        foreach (InProgressOperation operation in Enum.GetValues<InProgressOperation>())
        {
            if (operation == InProgressOperation.None)
            {
                continue;
            }

            MainWindowViewModel model = Create(detached: false, operation: operation);

            await model.OpenRepositoryAsync("/tmp/depo");

            model.OperationText.ShouldNotBeNullOrWhiteSpace();
            texts.Add(model.OperationText);
        }

        texts.Distinct().Count().ShouldBe(texts.Count);
    }

    [AvaloniaFact]
    public async Task Depo_kapaninca_seritler_TEMIZLENIYOR()
    {
        MainWindowViewModel model = Create(detached: true, operation: InProgressOperation.Rebase);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.ShowOperationBanner.ShouldBeTrue();

        model.CloseRepositoryCommand.Execute(null);

        // Saying "a rebase is in progress" for a closed repository would be leftover information.
        await model.OpenRepositoryAsync(string.Empty);

        model.ShowOperationBanner.ShouldBeFalse();
        model.ShowDetachedBanner.ShouldBeFalse();
    }
}
