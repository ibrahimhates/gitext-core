using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T12 — Ref rozetlerinin türe göre üretilmesi.
/// </summary>
/// <remarks>
/// Bu testlerin var olma sebebi ölçüm: <c>git log --format=%D</c> tür bilgisi taşımıyor
/// (yerel dal <c>ikinci</c>, uzak dal <c>origin/main</c> — ikisi de çıplak isim). Rozetler
/// bu yüzden <c>for-each-ref</c> verisinden üretiliyor ve buradaki testler o eşlemeyi korur.
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
        // Sıralama görsel bir karar değil, veri kararı: kullanıcının en çok umursadığı
        // rozet (nerede olduğu) solda durmalı.
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
        // Annotated tag'de ref tag NESNESİNE işaret eder; rozet commit'e düşmezse
        // grafikte yanlış satırda görünür.
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

        // Detached durumda hiçbir dal "geçerli" değil.
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
        // origin/HEAD klonlanan HER depoda var ve origin/main ile aynı commit'i gösteriyor
        // (bu depoda ölçüldü). Üstelik kısa adı "origin" — elenmezse kullanıcı origin/main'in
        // yanında "origin" yazan anlamsız bir rozet görür.
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
        // 500 bin satırda rozetsiz commit'ler baskın; her biri için yeni liste ayırmak
        // gereksiz tahsis olurdu.
        RefBadgeIndex index = RefBadgeIndex.Build(FakeGitData.NoRefs());

        index.For(Id(1)).ShouldBeSameAs(index.For(Id(2)));
    }
}
