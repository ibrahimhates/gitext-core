using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T13 — dal paneli.
/// </summary>
public class RefTreeTests
{
    private static RepositoryRefs Sample() => FakeGitData.Refs(
        localBranches:
        [
            FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true) with
            {
                Upstream = "origin/main",
                Tracking = new UpstreamTracking(2, 1, IsGone: false),
            },
            FakeGitData.LocalBranch("feature/login", FakeGitData.Sha(2)),
            FakeGitData.LocalBranch("feature/logout", FakeGitData.Sha(3)),
        ],
        remoteBranches:
        [
            FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1)),
            FakeGitData.RemoteBranch("origin/feature/login", FakeGitData.Sha(2)),
            FakeGitData.SymbolicRemoteHead("origin", "refs/remotes/origin/main", FakeGitData.Sha(1)),
        ],
        tags: [FakeGitData.Tag("v1.0", FakeGitData.Sha(1))]);

    private static RefTreeViewModel Create()
    {
        RefTreeViewModel model = new();
        model.Load(Sample());

        return model;
    }

    private static RefNodeViewModel Root(RefTreeViewModel model, string name) =>
        model.Roots.Single(node => node.Name == name);

    [AvaloniaFact]
    public void Uc_bolum_de_kuruluyor()
    {
        RefTreeViewModel model = Create();

        model.Roots.Select(node => node.Name).ShouldBe(["Branches", "Remotes", "Tags"]);
    }

    [AvaloniaFact]
    public void Egik_cizgili_adlar_KLASORLENIYOR()
    {
        // `feature/login` and `feature/logout` would be two rows in a flat list; GitExtensions' tree
        // gathers them under a single "feature" node too (§ 9).
        RefNodeViewModel branches = Root(Create(), "Branches");

        RefNodeViewModel folder = branches.Children.Single(node => node.Kind == RefNodeKind.Folder);

        folder.Name.ShouldBe("feature");
        folder.Children.Select(node => node.Name).ShouldBe(["login", "logout"]);
        folder.Children[0].FullName.ShouldBe("feature/login", "tam ad korunmalı");
    }

    [AvaloniaFact]
    public void Uzak_dallar_UZAK_DEPO_altinda_gruplaniyor()
    {
        RefNodeViewModel remotes = Root(Create(), "Remotes");

        RefNodeViewModel origin = remotes.Children.Single();
        origin.Name.ShouldBe("origin");
        origin.Kind.ShouldBe(RefNodeKind.Remote);

        origin.Children.Select(node => node.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["feature", "main"]);
    }

    [AvaloniaFact]
    public void Sembolik_origin_HEAD_agacta_YOK()
    {
        // The same trap for the fifth time (P03-T12, P06-T05, T06, T08): it would look like a second
        // branch on the same commit.
        RefNodeViewModel origin = Root(Create(), "Remotes").Children.Single();

        origin.Children.ShouldNotContain(node => node.Name == "HEAD");
    }

    [AvaloniaFact]
    public void Mevcut_dal_isaretli_ve_ahead_behind_yazili()
    {
        RefNodeViewModel branches = Root(Create(), "Branches");

        RefNodeViewModel main = branches.Children.Single(node => node.Name == "main");

        main.IsCurrent.ShouldBeTrue();
        main.AheadBehind.ShouldBe("↑2 ↓1");
        main.HasAheadBehind.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Upstream_i_olmayan_dalda_rozet_YOK()
    {
        RefNodeViewModel branches = Root(Create(), "Branches");
        RefNodeViewModel folder = branches.Children.Single(node => node.Kind == RefNodeKind.Folder);

        folder.Children[0].HasAheadBehind.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Upstream_SILINMISSE_ayri_yaziliyor()
    {
        // Showing "0/0" would suggest the branch is up to date.
        RefTreeViewModel model = new();

        model.Load(FakeGitData.Refs(localBranches:
        [
            FakeGitData.LocalBranch("eski", FakeGitData.Sha(1)) with
            {
                Upstream = "origin/eski",
                Tracking = new UpstreamTracking(0, 0, IsGone: true),
            },
        ]));

        Root(model, "Branches").Children.Single().AheadBehind.ShouldBe("upstream gone");
    }

    [AvaloniaFact]
    public void Suzme_eslesmeyenleri_ve_BOS_bolumleri_eliyor()
    {
        RefTreeViewModel model = Create();

        model.Filter = "logout";

        model.Roots.Select(node => node.Name).ShouldBe(["Branches"], "boş bölüm gösterilmemeli");

        RefNodeViewModel folder = Root(model, "Branches").Children.Single();
        folder.Children.Single().Name.ShouldBe("logout");
    }

    [AvaloniaFact]
    public void Suzme_BUYUK_kucuk_harf_ayirmiyor()
    {
        RefTreeViewModel model = Create();

        model.Filter = "LOGIN";

        model.IsEmpty.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Hicbir_sey_eslesmezse_bos_bildiriliyor()
    {
        RefTreeViewModel model = Create();

        model.Filter = "boyle-bir-sey-yok";

        model.IsEmpty.ShouldBeTrue();
        model.Roots.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Suzme_temizlenince_agac_GERI_geliyor()
    {
        RefTreeViewModel model = Create();

        model.Filter = "logout";
        model.Filter = string.Empty;

        model.Roots.Count.ShouldBe(3);
    }

    [AvaloniaFact]
    public void Baslik_ve_klasor_CHECKOUT_edilemez()
    {
        // Changing branch by accident while moving through the tree must not happen.
        RefTreeViewModel model = Create();

        Root(model, "Branches").IsCheckoutable.ShouldBeFalse();
        Root(model, "Branches").Children.Single(node => node.Kind == RefNodeKind.Folder)
            .IsCheckoutable.ShouldBeFalse();
        Root(model, "Tags").Children.Single().IsCheckoutable.ShouldBeFalse("etiket dal değil");

        Root(model, "Branches").Children.Single(node => node.Name == "main")
            .IsCheckoutable.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Depo_kapaninca_agac_BOSALIYOR()
    {
        RefTreeViewModel model = Create();

        model.Load(refs: null);

        model.Roots.ShouldBeEmpty();
        model.HasRefs.ShouldBeFalse();
    }

    // ------------------------------------------------------- ana pencere

    [AvaloniaFact]
    public async Task Depo_acilinca_panel_DOLUYOR()
    {
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(Sample()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore());

        model.RefTree.HasRefs.ShouldBeFalse();

        await model.OpenRepositoryAsync("/depo");

        model.RefTree.HasRefs.ShouldBeTrue();
        model.RefTree.Roots.Count.ShouldBe(3);
    }

    [AvaloniaFact]
    public async Task Cift_tiklama_MENUDEKI_checkout_akisini_cagiriyor()
    {
        // 🔑 Writing a second checkout path meant one of them ending up without the dirty-tree guard
        // (P06-T02: there must be no path that loses changes).
        FakeCheckoutPrompt prompt = new(new CheckoutDecision { Confirmed = false });
        FakeBranchWriter writer = new();

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(Sample()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            branchWriter: writer)
        {
            CheckoutPrompt = prompt,
        };

        await model.OpenRepositoryAsync("/depo");
        await model.CheckoutRefAsync("feature/login");

        prompt.LastRequest.ShouldNotBeNull();
        prompt.LastRequest!.Target.ShouldBe("feature/login");
        writer.Switched.ShouldBeEmpty("iptal edildiğinde geçiş yapılmamalı");
    }

    // ---------------------------------------------------------- layout

    [AvaloniaFact]
    public void Panel_SOLDA_ve_arama_kutusu_ustte()
    {
        RefTreeView view = new() { DataContext = Create(), Width = 220, Height = 400 };

        Window window = new() { Content = view, Width = 300, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The name scope belongs to the UserControl, not to the hosting window.
        TextBox filter = view.GetControl<TextBox>("FilterBox");
        TreeView tree = view.GetControl<TreeView>("RefTree");

        double filterTop = filter.TranslatePoint(default, window)!.Value.Y;
        double treeTop = tree.TranslatePoint(default, window)!.Value.Y;

        filterTop.ShouldBeLessThan(treeTop);

        window.Close();
    }
}
