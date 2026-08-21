using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P12-T13 — the left panel's sections, in GitExtensions' order.
/// </summary>
/// <remarks>
/// GitExtensions' <c>RepoObjectsTree</c> has six trees and creates them in this order:
/// Branches · Remotes · Worktrees · Tags · Submodules · Stashes. The order is not decoration —
/// someone who has used the application for years finds a section by where it sits.
/// </remarks>
public class LeftPanelTests
{
    private static RepositoryRefs Refs() => FakeGitData.Refs(
        localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true)],
        remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1))],
        tags: [FakeGitData.Tag("v1.0", FakeGitData.Sha(1))]);

    private static RefTreeData Everything() => new()
    {
        Refs = Refs(),
        RootPath = "/tmp/depo",
        WorkTrees =
        [
            new WorkTree { Path = "/tmp/depo", BranchName = "main", IsMain = true },
            new WorkTree { Path = "/tmp/depo-hotfix", BranchName = "hotfix" },
        ],
        Submodules =
        [
            new Submodule
            {
                Path = RepositoryPath.Parse("libs/core"),
                ObjectId = FakeGitData.Sha(2),
                Status = SubmoduleStatusKind.Modified,
            },
        ],
        Stashes =
        [
            new StashEntry { Selector = "stash@{0}", ObjectId = FakeGitData.Sha(3), Message = "yarım kalan iş", Index = 0 },
        ],
    };

    private static RefTreeViewModel Loaded()
    {
        RefTreeViewModel model = new();
        model.Load(Everything());
        return model;
    }

    [AvaloniaFact]
    public void Bolumler_GitExtensions_sirasinda()
    {
        Loaded().Roots.Select(r => r.Name).ShouldBe(
            ["Branches", "Remotes", "Worktrees", "Tags", "Submodules", "Stashes"]);
    }

    [AvaloniaFact]
    public void Bos_bolum_GOSTERILMIYOR()
    {
        // A repository with no submodules should not carry the heading for the rest of its life.
        RefTreeViewModel model = new();
        model.Load(new RefTreeData { Refs = Refs(), RootPath = "/tmp/depo" });

        model.Roots.Select(r => r.Name).ShouldBe(["Branches", "Remotes", "Tags"]);
    }

    [AvaloniaFact]
    public void Calisma_agaclari_ANA_olani_isaretliyor()
    {
        // "Which one am I looking at" is the first question the list raises.
        RefNodeViewModel worktrees = Loaded().Roots.Single(r => r.Name == "Worktrees");

        worktrees.Children.Select(c => c.Name).ShouldBe(["depo", "depo-hotfix"]);
        worktrees.Children[0].IsCurrent.ShouldBeTrue();
        worktrees.Children[1].AheadBehind.ShouldBe("hotfix");
    }

    [AvaloniaFact]
    public void Alt_modulun_durumu_ve_MUTLAK_yolu_var()
    {
        // The path matters: the node's name is relative to the repository, but "Open" needs the
        // absolute path — a submodule is a repository of its own.
        RefNodeViewModel submodule = Loaded().Roots.Single(r => r.Name == "Submodules").Children.Single();

        submodule.Name.ShouldBe("core");
        submodule.FullName.ShouldBe("libs/core");

        // The path is built from RootPath + the submodule's relative path.
        // Use GetFullPath so it matches the production code's normalization on all platforms.
        string expected = Path.GetFullPath(Path.Combine("/tmp/depo", "libs", "core"));
        submodule.Path.ShouldBe(expected);

        submodule.AheadBehind.ShouldBe("modified");
        submodule.IsOpenable.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Stash_MESAJIYLA_gorunuyor_secici_saklaniyor()
    {
        // GitExtensions shows the message; every command needs the selector.
        RefNodeViewModel stash = Loaded().Roots.Single(r => r.Name == "Stashes").Children.Single();

        stash.Name.ShouldBe("yarım kalan iş");
        stash.FullName.ShouldBe("stash@{0}");
        stash.Kind.ShouldBe(RefNodeKind.Stash);
    }

    [AvaloniaFact]
    public void Kapatilan_bolum_agactan_cikiyor_ve_BILDIRILIYOR()
    {
        RefTreeViewModel model = Loaded();

        RefTreeSections? reported = null;
        model.SectionsChanged += (_, sections) => reported = sections;

        model.ShowStashes = false;

        model.Roots.Select(r => r.Name).ShouldNotContain("Stashes");

        // The window stores the choice; the panel only reports it (ADR-0004).
        reported.ShouldNotBeNull().Stashes.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Arama_yeni_bolumlerde_de_calisiyor()
    {
        RefTreeViewModel model = Loaded();

        model.Filter = "hotfix";

        model.Roots.Select(r => r.Name).ShouldBe(["Worktrees"]);
        model.Roots.Single().Children.Single().Name.ShouldBe("depo-hotfix");
    }

    [AvaloniaFact]
    public void Tumunu_daralt_HER_dugumu_kapatiyor()
    {
        RefTreeViewModel model = Loaded();

        model.Roots.ShouldAllBe(r => r.IsExpanded);

        model.CollapseAll();

        model.Roots.ShouldAllBe(r => !r.IsExpanded);
        model.Roots.SelectMany(r => r.Children).ShouldAllBe(c => !c.IsExpanded);
    }

    [AvaloniaFact]
    public void Baslik_secimleri_DOGRU_menuyu_aciyor()
    {
        // The headings carry their own actions in GitExtensions (fetch all remotes, stash all…);
        // the decision about which ones are visible is made here, not in an `Opening` handler that
        // never fires headless.
        RefTreeViewModel model = Loaded();

        model.Selected = model.Roots.Single(r => r.Name == "Remotes");
        model.IsRemotesSectionSelected.ShouldBeTrue();
        model.IsStashesSectionSelected.ShouldBeFalse();

        model.Selected = model.Roots.Single(r => r.Name == "Stashes");
        model.IsStashesSectionSelected.ShouldBeTrue();
        model.IsRemotesSectionSelected.ShouldBeFalse();

        model.Selected = model.Roots.Single(r => r.Name == "Stashes").Children.Single();
        model.IsStashSelected.ShouldBeTrue();
        model.IsStashesSectionSelected.ShouldBeFalse();
        model.CanOpenSelected.ShouldBeFalse("a stash is not a folder that can be opened");
    }

    [AvaloniaFact]
    public void Dal_dugumunde_stash_ogeleri_GORUNMUYOR()
    {
        // The counter-evidence: were the flags always true, the test above would prove nothing.
        RefTreeViewModel model = Loaded();

        model.Selected = model.Roots.Single(r => r.Name == "Branches").Children.Single();

        model.IsStashSelected.ShouldBeFalse();
        model.IsSubmoduleSelected.ShouldBeFalse();
        model.IsWorkTreeSelected.ShouldBeFalse();
        model.IsRemotesSectionSelected.ShouldBeFalse();
    }
}
