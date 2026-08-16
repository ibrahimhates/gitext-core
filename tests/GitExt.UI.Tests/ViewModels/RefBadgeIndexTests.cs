using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T12 — Producing the ref badges by kind.
/// </summary>
/// <remarks>
/// These tests exist because of a measurement: <c>git log --format=%D</c> carries no kind information
/// (a local branch is <c>ikinci</c>, a remote branch <c>origin/main</c> — both bare names). The badges
/// are therefore produced from <c>for-each-ref</c> data, and the tests here protect that mapping.
/// </remarks>
public class RefBadgeIndexTests
{
    private static string Sha(int index) => FakeGitData.Sha(index);

    private static CommitId Id(int index) => CommitId.Parse(Sha(index));

    [Fact]
    public void Yerel_ve_uzak_dal_farkli_turde_rozet_uretir()
    {
        RepositoryRefs refs = FakeGitData.Refs(
            localBranches: [FakeGitData.LocalBranch("main", Sha(1), isCurrent: true)],
            remoteBranches: [FakeGitData.RemoteBranch("origin/main", Sha(1))]);

        IReadOnlyList<RefBadge> badges = RefBadgeIndex.Build(refs).For(Id(1));

        badges.Count.ShouldBe(2);
        badges.ShouldContain(b => b.Text == "main" && b.Kind == RefBadgeKind.LocalBranch);
        badges.ShouldContain(b => b.Text == "origin/main" && b.Kind == RefBadgeKind.RemoteBranch);
    }

    [Fact]
    public void Checkout_edilmis_dal_isaretlenir()
    {
        RepositoryRefs refs = FakeGitData.Refs(
            localBranches:
            [
                FakeGitData.LocalBranch("main", Sha(1), isCurrent: true),
                FakeGitData.LocalBranch("deneme", Sha(1)),
            ]);

        IReadOnlyList<RefBadge> badges = RefBadgeIndex.Build(refs).For(Id(1));

        badges.Single(b => b.Text == "main").IsCurrent.ShouldBeTrue();
        badges.Single(b => b.Text == "deneme").IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public void Gecerli_dal_once_gosterilir()
    {
        // The ordering is not a visual decision but a data one: the badge the user cares about most
        // (where they are) must sit on the left.
        RepositoryRefs refs = FakeGitData.Refs(
            localBranches:
            [
                FakeGitData.LocalBranch("aaa-once-gelir", Sha(1)),
                FakeGitData.LocalBranch("main", Sha(1), isCurrent: true),
            ],
            remoteBranches: [FakeGitData.RemoteBranch("origin/main", Sha(1))],
            tags: [FakeGitData.Tag("v1.0", Sha(1))]);

        IReadOnlyList<RefBadge> badges = RefBadgeIndex.Build(refs).For(Id(1));

        badges[0].Text.ShouldBe("main");
        badges[^1].Kind.ShouldBe(RefBadgeKind.Tag);
    }

    [Fact]
    public void Annotated_tag_cozulmus_commite_baglanir()
    {
        // On an annotated tag the ref points at the tag OBJECT; unless the badge lands on the commit it
        // shows on the wrong row in the graph.
        RepositoryRefs refs = FakeGitData.Refs(tags: [FakeGitData.Tag("v2.0", Sha(5), annotated: true)]);

        RefBadgeIndex index = RefBadgeIndex.Build(refs);

        index.For(Id(5)).Single().Text.ShouldBe("v2.0");
        index.For(Id(999_999)).ShouldBeEmpty();
    }

    [Fact]
    public void Detached_head_kendi_rozetini_alir()
    {
        RepositoryRefs refs = FakeGitData.Refs(
            localBranches: [FakeGitData.LocalBranch("main", Sha(1))],
            head: new HeadState
            {
                IsDetached = true,
                IsUnborn = false,
                Commit = Id(3),
            });

        RefBadgeIndex index = RefBadgeIndex.Build(refs);

        index.For(Id(3)).Single().Kind.ShouldBe(RefBadgeKind.Head);

        // In a detached state no branch is "current".
        index.For(Id(1)).Single().IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public void Dogmamis_depoda_bos_rozet_uretilir()
    {
        RefBadgeIndex index = RefBadgeIndex.Build(FakeGitData.NoRefs());

        index.Count.ShouldBe(0);
        index.For(Id(1)).ShouldBeEmpty();
    }

    [Fact]
    public void Sembolik_uzak_ref_rozet_uretmez()
    {
        // origin/HEAD exists in EVERY cloned repository and points at the same commit as origin/main
        // (measured in this repository). On top of that its short name is "origin" — unless it is
        // filtered out, the user sees a meaningless badge reading "origin" next to origin/main.
        RepositoryRefs refs = FakeGitData.Refs(
            remoteBranches:
            [
                FakeGitData.SymbolicRemoteHead("origin", "refs/remotes/origin/main", Sha(1)),
                FakeGitData.RemoteBranch("origin/main", Sha(1)),
            ]);

        IReadOnlyList<RefBadge> badges = RefBadgeIndex.Build(refs).For(Id(1));

        badges.Single().Text.ShouldBe("origin/main");
    }

    [Fact]
    public void Rozetsiz_commit_icin_ayni_bos_liste_donulur()
    {
        // Across 500 thousand rows, commits without badges dominate; allocating a new list for each of
        // them would be a needless allocation.
        RefBadgeIndex index = RefBadgeIndex.Build(FakeGitData.NoRefs());

        index.For(Id(1)).ShouldBeSameAs(index.For(Id(2)));
    }
}
