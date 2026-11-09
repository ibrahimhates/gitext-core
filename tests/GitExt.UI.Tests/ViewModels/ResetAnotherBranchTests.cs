using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

public sealed class ResetAnotherBranchTests
{
    private sealed class FixedMergeBase(CommitId? result) : IMergeBaseReader
    {
        public Task<CommitId?> FindAsync(
            string workingDirectory,
            IReadOnlyList<string> revisions,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class AnsweringPrompt(bool answer, Action<ResetAnotherBranchViewModel>? fill = null)
        : IResetAnotherBranchPrompt
    {
        public ResetAnotherBranchViewModel? Shown { get; private set; }

        public async Task<bool> ShowAsync(ResetAnotherBranchViewModel model)
        {
            Shown = model;
            fill?.Invoke(model);

            if (model.Validation is { } validation)
            {
                await validation;
            }

            return answer;
        }
    }

    private static readonly CommitInfo Target =
        FakeGitData.Commit(FakeGitData.Sha(3), [FakeGitData.Sha(2)], "commit 3");

    private static RepositoryRefs Refs(bool detached = false) => FakeGitData.Refs(
        localBranches:
        [
            FakeGitData.LocalBranch("main", FakeGitData.Sha(3), isCurrent: !detached),
            FakeGitData.LocalBranch("behind", FakeGitData.Sha(1)),
            FakeGitData.LocalBranch("here", FakeGitData.Sha(3)),
        ],
        head: detached ? new HeadState { IsDetached = true, IsUnborn = false } : null);

    [Fact]
    public void Bulunulan_dal_ve_zaten_oradaki_dal_listelenmiyor()
    {
        ResetAnotherBranchViewModel.Candidates(Refs(), Target.Id)
            .Select(c => c.Name)
            .ShouldBe(["behind"]);
    }

    [Fact]
    public void Ayrik_HEADde_bulunulan_dal_da_listeleniyor()
    {
        RepositoryRefs refs = FakeGitData.Refs(
            localBranches:
            [
                FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: false),
                FakeGitData.LocalBranch("behind", FakeGitData.Sha(1)),
            ],
            head: new HeadState { IsDetached = true, IsUnborn = false });

        ResetAnotherBranchViewModel.Candidates(refs, Target.Id)
            .Select(c => c.Name)
            .ShouldBe(["main", "behind"]);
    }

    [Fact]
    public async Task Ileri_sarma_ise_tamam_etkin()
    {
        ResetAnotherBranchViewModel model = new(
            "/depo",
            Target,
            [new ResetAnotherBranchCandidate("behind", CommitId.Parse(FakeGitData.Sha(1)))],
            new FixedMergeBase(CommitId.Parse(FakeGitData.Sha(1))));

        model.SelectedBranch = model.Branches[0];
        await model.Validation!;

        model.IsFastForward.ShouldBeTrue();
        model.CanReset.ShouldBeTrue();
    }

    [Fact]
    public async Task Ileri_sarma_degilse_yalniz_zorla_ile_etkin()
    {
        ResetAnotherBranchViewModel model = new(
            "/depo",
            Target,
            [new ResetAnotherBranchCandidate("diverged", CommitId.Parse(FakeGitData.Sha(5)))],
            new FixedMergeBase(CommitId.Parse(FakeGitData.Sha(1))));

        model.SelectedBranch = model.Branches[0];
        await model.Validation!;

        model.IsFastForward.ShouldBeFalse();
        model.CanReset.ShouldBeFalse();

        model.Force = true;

        model.CanReset.ShouldBeTrue();
    }

    [Fact]
    public void Dal_secilmeden_tamam_kapali()
    {
        ResetAnotherBranchViewModel model = new("/depo", Target, [], new FixedMergeBase(null)) { Force = true };

        model.CanReset.ShouldBeFalse();
    }

    private static async Task<(MainWindowViewModel Model, FakeBranchWriter Writer)> OpenAsync(
        IResetAnotherBranchPrompt prompt,
        CommitId? mergeBase)
    {
        FakeBranchWriter writer = new();
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader(),
                mergeBase: new FixedMergeBase(mergeBase)),
            new FakeRecentRepositoryStore(),
            branchWriter: writer)
        {
            ResetAnotherBranchPrompt = prompt,
        };

        await model.OpenRepositoryAsync("/depo");
        model.Commits.SelectedIndex = 0;

        return (model, writer);
    }

    [AvaloniaFact]
    public async Task Onaylaninca_secilen_dal_secili_commite_kaydiriliyor()
    {
        AnsweringPrompt prompt = new(true, model => model.SelectedBranch = model.Branches.Single(b => b.Name == "behind"));
        (MainWindowViewModel model, FakeBranchWriter writer) = await OpenAsync(prompt, CommitId.Parse(FakeGitData.Sha(1)));

        await model.ResetAnotherBranchAsync();

        prompt.Shown!.Target.Id.ShouldBe(CommitId.Parse(FakeGitData.Sha(3)));
        writer.Reset.ShouldBe([("behind", FakeGitData.Sha(3))]);
    }

    [AvaloniaFact]
    public async Task Vazgecilince_dal_kaydirilmiyor()
    {
        AnsweringPrompt prompt = new(false, model => model.SelectedBranch = model.Branches[0]);
        (MainWindowViewModel model, FakeBranchWriter writer) = await OpenAsync(prompt, CommitId.Parse(FakeGitData.Sha(1)));

        await model.ResetAnotherBranchAsync();

        writer.Reset.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Ileri_sarma_olmayan_zorlanmadan_kaydirilmiyor()
    {
        AnsweringPrompt prompt = new(true, model => model.SelectedBranch = model.Branches[0]);
        (MainWindowViewModel model, FakeBranchWriter writer) = await OpenAsync(prompt, CommitId.Parse(FakeGitData.Sha(2)));

        await model.ResetAnotherBranchAsync();

        writer.Reset.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Baska_worktreede_acik_dal_seritte_bildiriliyor()
    {
        AnsweringPrompt prompt = new(true, model =>
        {
            model.SelectedBranch = model.Branches[0];
            model.Force = true;
        });
        (MainWindowViewModel model, FakeBranchWriter writer) = await OpenAsync(prompt, null);
        writer.Failure = new BranchCheckedOutException("behind", "/elsewhere");

        await model.ResetAnotherBranchAsync();

        model.BranchNotice.ShouldBe("Branch 'behind' is checked out at '/elsewhere'; switch that working tree away from it first.");
    }

    [Fact]
    public void Menu_ogeleri_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(new RevisionGridMenuContext
        {
            CommitHash = FakeGitData.Sha(1),
            CurrentBranch = "main",
            Refs = [new RevisionGridRef("feature", RevisionGridRefKind.LocalBranch)],
        });

        menu.First(e => e.Id == RevisionGridMenuId.ResetAnotherBranchToHere).IsEnabled.ShouldBeTrue();
        menu.First(e => e.Id == RevisionGridMenuId.SelectInLeftPanel).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Sol_panelde_secim_klasoru_acip_dugumu_seciyor()
    {
        RefTreeViewModel tree = new();
        tree.Load(FakeGitData.Refs(
            localBranches:
            [
                FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true),
                FakeGitData.LocalBranch("feature/deep/x", FakeGitData.Sha(2)),
            ],
            tags: [FakeGitData.Tag("feature/deep/x", FakeGitData.Sha(2))]));

        foreach (RefNodeViewModel node in Flatten(tree.Roots))
        {
            node.IsExpanded = false;
        }

        tree.Reveal("feature/deep/x", RefNodeKind.LocalBranch).ShouldBeTrue();

        tree.Selected!.Kind.ShouldBe(RefNodeKind.LocalBranch);
        tree.Selected.FullName.ShouldBe("feature/deep/x");
        Ancestors(tree.Roots, tree.Selected).ShouldAllBe(node => node.IsExpanded);

        tree.Reveal("feature/deep/x", RefNodeKind.Tag).ShouldBeTrue();
        tree.Selected!.Kind.ShouldBe(RefNodeKind.Tag);

        tree.Reveal("yok", RefNodeKind.LocalBranch).ShouldBeFalse();
    }

    private static IEnumerable<RefNodeViewModel> Flatten(IEnumerable<RefNodeViewModel> nodes) =>
        nodes.SelectMany(node => Flatten(node.Children).Prepend(node));

    private static List<RefNodeViewModel> Ancestors(IEnumerable<RefNodeViewModel> roots, RefNodeViewModel target)
    {
        foreach (RefNodeViewModel root in roots)
        {
            if (Path(root) is { } found)
            {
                return found;
            }
        }

        return [];

        List<RefNodeViewModel>? Path(RefNodeViewModel node)
        {
            if (node == target)
            {
                return [];
            }

            foreach (RefNodeViewModel child in node.Children)
            {
                if (Path(child) is { } below)
                {
                    below.Insert(0, node);
                    return below;
                }
            }

            return null;
        }
    }
}
