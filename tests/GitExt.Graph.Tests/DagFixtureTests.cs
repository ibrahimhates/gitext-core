
namespace GitExt.Graph.Tests;

/// <summary>
/// P03-T02 — Fixture formatının kendisi. Bu format tüm yerleşim testlerinin temeli olduğu için
/// önce kendisi doğrulanmalı: bozuk bir fixture, testleri yanlış şeyi doğrular hale getirir.
/// </summary>
public class DagFixtureTests
{
    [Fact]
    public void Dogrusal_gecmis_ayristirilir()
    {
        IReadOnlyList<DagCommit> commits = DagFixture.Parse(
            """
            C: B
            B: A
            A:
            """);

        commits.Select(c => c.Id).ShouldBe(["C", "B", "A"]);
        commits[0].Parents.ShouldBe(["B"]);
        commits[2].IsRoot.ShouldBeTrue();
        commits[2].Parents.ShouldBeEmpty();
    }

    [Fact]
    public void Merge_birden_fazla_ebeveyn_alir()
    {
        IReadOnlyList<DagCommit> commits = DagFixture.Parse(
            """
            D: B C
            C: A
            B: A
            A:
            """);

        commits[0].IsMerge.ShouldBeTrue();
        commits[0].Parents.ShouldBe(["B", "C"]);
        commits[1].IsMerge.ShouldBeFalse();
    }

    [Fact]
    public void Octopus_merge_ikiden_fazla_ebeveyn_alir()
    {
        IReadOnlyList<DagCommit> commits = DagFixture.Parse(
            """
            E: B C D
            D: A
            C: A
            B: A
            A:
            """);

        commits[0].Parents.Count.ShouldBe(3);
        commits[0].IsMerge.ShouldBeTrue();
    }

    [Fact]
    public void Virgul_ve_bosluk_ayraci_ikisi_de_calisir()
    {
        DagFixture.Parse("D: B, C\nC: A\nB: A\nA:")[0].Parents.ShouldBe(["B", "C"]);
        DagFixture.Parse("D: B C\nC: A\nB: A\nA:")[0].Parents.ShouldBe(["B", "C"]);
    }

    [Fact]
    public void Yorumlar_ve_bos_satirlar_yok_sayilir()
    {
        IReadOnlyList<DagCommit> commits = DagFixture.Parse(
            """
            # Bu bir yorum satırı

            C: B      # satır sonu yorumu
            B: A

            A:
            """);

        commits.Select(c => c.Id).ShouldBe(["C", "B", "A"]);
    }

    [Fact]
    public void Tanimlanmamis_ebeveyne_izin_verilir()
    {
        // Sayfalama sınırı: geçmişin kesildiği yerde ebeveyn tanımsız kalır.
        IReadOnlyList<DagCommit> commits = DagFixture.Parse("B: A");

        commits.ShouldHaveSingleItem().Parents.ShouldBe(["A"]);
    }

    [Fact]
    public void Topolojik_sira_ihlali_yakalanir()
    {
        // ADR-0007'nin değişmezi: her ebeveyn çocuğundan SONRA gelmeli.
        // Bu ihlal fixture'da fark edilmezse, algoritma testi kendi hatasını doğrular.
        FormatException exception = Should.Throw<FormatException>(() => DagFixture.Parse(
            """
            A:
            B: A
            """));

        exception.Message.ShouldContain("Topolojik sıra ihlali");
        exception.Message.ShouldContain("A");
        exception.Message.ShouldContain("B");
    }

    [Fact]
    public void Merge_de_topolojik_sira_ihlali_yakalanir()
    {
        Should.Throw<FormatException>(() => DagFixture.Parse(
            """
            D: B C
            B: A
            A:
            C: A
            """)).Message.ShouldContain("Topolojik sıra ihlali");
    }

    [Fact]
    public void Tekrarlanan_kimlik_reddedilir()
    {
        Should.Throw<FormatException>(() => DagFixture.Parse("A:\nA:"))
            .Message.ShouldContain("birden fazla kez");
    }

    [Theory]
    [InlineData("iki nokta yok")]
    [InlineData(": ebeveyn")]
    public void Bozuk_satirlar_reddedilir(string definition)
    {
        Should.Throw<FormatException>(() => DagFixture.Parse(definition));
    }

    [Fact]
    public void Bos_tanim_bos_liste_dondurur()
    {
        DagFixture.Parse(string.Empty).ShouldBeEmpty();
        DagFixture.Parse("# yalnızca yorum\n\n").ShouldBeEmpty();
    }

    [Fact]
    public void Coklu_kok_commit_desteklenir()
    {
        // İlişkisiz geçmişler (git merge --allow-unrelated-histories) böyle görünür.
        IReadOnlyList<DagCommit> commits = DagFixture.Parse(
            """
            C: A B
            B:
            A:
            """);

        commits.Count(c => c.IsRoot).ShouldBe(2);
    }
}
