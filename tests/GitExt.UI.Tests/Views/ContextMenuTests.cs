using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T14 — the context menus.
/// </summary>
/// <remarks>
/// § 9: the menu items sit where they do in GitExtensions; the unimplemented ones are <b>not
/// removed</b>, they are disabled. These tests pin down both the order and the <b>enabled state</b> —
/// an item silently staying dead really did happen in P06-T07.
/// </remarks>
public class ContextMenuTests
{
    private static RepositoryRefs Sample() => FakeGitData.Refs(
        localBranches:
        [
            FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true),
            FakeGitData.LocalBranch("feature/login", FakeGitData.Sha(2)),
        ],
        remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1))],
        tags: [FakeGitData.Tag("v1.0", FakeGitData.Sha(1))]);

    private static RefTreeView CreateTree()
    {
        RefTreeViewModel model = new();
        model.Load(Sample());

        RefTreeView view = new() { DataContext = model, Width = 220, Height = 400 };

        Window window = new() { Content = view, Width = 300, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return view;
    }

    private static RefNodeViewModel Find(RefTreeView view, string fullName)
    {
        RefTreeViewModel model = (RefTreeViewModel)view.DataContext!;

        return Walk(model.Roots).Single(node => node.FullName == fullName);

        static IEnumerable<RefNodeViewModel> Walk(IEnumerable<RefNodeViewModel> nodes)
        {
            foreach (RefNodeViewModel node in nodes)
            {
                yield return node;

                foreach (RefNodeViewModel child in Walk(node.Children))
                {
                    yield return child;
                }
            }
        }
    }

    private static void Select(RefTreeView view, string fullName) =>
        ((RefTreeViewModel)view.DataContext!).Selected = Find(view, fullName);

    /// <remarks>
    /// ⚠️ The menu is REALLY opened: because a <c>ContextMenu</c> is not in the visual tree until it
    /// opens, its bindings are not evaluated either. Measured — headless, the <c>Opening</c> event never
    /// fires at all; the first implementation set the enabled state by hand in that event and
    /// <b>could not be tested</b>. The decision was moved into the ViewModel.
    /// </remarks>
    private static void Open(RefTreeView view)
    {
        ContextMenu menu = view.GetControl<ContextMenu>("RefContextMenu");
        menu.Open(view.GetControl<TreeView>("RefTree"));
        Dispatcher.UIThread.RunJobs();
    }

    private static MenuItem Item(RefTreeView view, string name) =>
        view.GetControl<MenuItem>(name);

    [AvaloniaFact]
    public void Yerel_dalda_TUM_oge_etkin()
    {
        RefTreeView view = CreateTree();

        Select(view, "feature/login");
        Open(view);

        Item(view, "MenuRefCheckout").IsEnabled.ShouldBeTrue();
        Item(view, "MenuRefMerge").IsEnabled.ShouldBeTrue();
        Item(view, "MenuRefRename").IsEnabled.ShouldBeTrue();
        Item(view, "MenuRefDelete").IsEnabled.ShouldBeTrue();
        Item(view, "MenuRefPush").IsEnabled.ShouldBeTrue();
        Item(view, "MenuRefCopyName").IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void MEVCUT_dal_silinemiyor_ve_kendine_birlestirilemiyor()
    {
        // Deleting the branch you are on, or merging it into itself, are things git refuses as well;
        // leaving them enabled in the menu would lead the user to an error message.
        RefTreeView view = CreateTree();

        Select(view, "main");
        Open(view);

        Item(view, "MenuRefDelete").IsEnabled.ShouldBeFalse();
        Item(view, "MenuRefMerge").IsEnabled.ShouldBeFalse();
        Item(view, "MenuRefCheckout").IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void UZAK_dal_yeniden_adlandirilamiyor()
    {
        // `git branch -m` does not change a remote branch; offering it in the menu would be a false
        // promise.
        RefTreeView view = CreateTree();

        Select(view, "origin/main");
        Open(view);

        Item(view, "MenuRefRename").IsEnabled.ShouldBeFalse();
        Item(view, "MenuRefDelete").IsEnabled.ShouldBeFalse();
        Item(view, "MenuRefCheckout").IsEnabled.ShouldBeTrue("uzak dala geçilebilir");
    }

    [AvaloniaFact]
    public void ETIKETTE_dal_islemleri_kapali()
    {
        RefTreeView view = CreateTree();

        Select(view, "v1.0");
        Open(view);

        Item(view, "MenuRefCheckout").IsEnabled.ShouldBeFalse();
        Item(view, "MenuRefDelete").IsEnabled.ShouldBeFalse();
        Item(view, "MenuRefCopyName").IsEnabled.ShouldBeTrue("adı yine de kopyalanabilir");
    }

    [AvaloniaFact]
    public void KLASORDE_hicbir_dal_islemi_yok()
    {
        RefTreeView view = CreateTree();

        RefTreeViewModel model = (RefTreeViewModel)view.DataContext!;

        model.Selected = model.Roots
            .Single(node => node.Name == "Branches")
            .Children
            .Single(node => node.Kind == RefNodeKind.Folder);

        Open(view);

        Item(view, "MenuRefCheckout").IsEnabled.ShouldBeFalse();
        Item(view, "MenuRefDelete").IsEnabled.ShouldBeFalse();
        Item(view, "MenuRefCopyName").IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Uygulanmamis_ogeler_YERINDE_ama_devre_disi()
    {
        // § 9: disabled rather than removed. Rebase and setting the upstream are Phase 07's subject.
        RefTreeView view = CreateTree();

        Select(view, "feature/login");
        Open(view);

        Item(view, "MenuRefRebase").IsEnabled.ShouldBeFalse();
        Item(view, "MenuRefSetUpstream").IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Dal_menusunun_SIRASI_sabit()
    {
        RefTreeView view = CreateTree();

        ContextMenu menu = view.GetControl<ContextMenu>("RefContextMenu");

        IReadOnlyList<string> names =
        [
            .. menu.Items.OfType<MenuItem>().Select(item => item.Name ?? string.Empty),
        ];

        names.ShouldBe(
        [
            "MenuRefCheckout",
            "MenuRefCreateBranch",
            "MenuRefMerge",
            "MenuRefRename",
            "MenuRefDelete",
            "MenuRefPush",
            "MenuRefRebase",
            "MenuRefSetUpstream",
            "MenuRefCopyName",
        ]);
    }

    // ------------------------------------------------------- the commit menu

    [AvaloniaFact]
    public async Task Commit_menusundeki_push_ve_merge_artik_ETKIN()
    {
        // In P08-T27 these two items were in place but disabled; their commands arrived in T08 and T11.
        // An item staying dead even after its command arrives is a bug that really happened in this
        // project (P06-T07's menu binding).
        FakePushWriter push = new();
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://e.com/a.git"] });

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(Sample()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            remoteReader: remotes,
            pushWriter: push,
            mergeWriter: new FakeMergeWriter())
        {
            PushPrompt = new FakePushPrompt(),
            MergePrompt = new FakeMergePrompt(),
        };

        await model.OpenRepositoryAsync("/depo");

        MainWindow window = new() { DataContext = model, Width = 1000, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetControl<MenuItem>("MenuPush").IsEnabled.ShouldBeTrue();
        window.GetControl<MenuItem>("MenuMerge").IsEnabled.ShouldBeTrue();

        window.Close();
    }
}
