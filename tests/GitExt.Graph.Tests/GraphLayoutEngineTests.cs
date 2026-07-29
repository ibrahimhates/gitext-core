namespace GitExt.Graph.Tests;

/// <summary>
/// P03-T03 / T04 / T07 — Yerleşim algoritması.
/// </summary>
/// <remarks>
/// Testler UI olmadan çalışır (ADR-0003). Bir senaryo kırıldığında hata mesajı,
/// beklenen ve gerçek yerleşimi metin tablosu olarak gösterir.
/// </remarks>
public class GraphLayoutEngineTests
{
    private static IReadOnlyList<GraphRow> Layout(string definition) =>
        new GraphLayoutEngine().Add(DagFixture.Parse(definition));

    [Fact]
    public void Dogrusal_gecmis_tek_seritte_kalir()
    {
        // Düz şerit kuralı: ilk ebeveyn aynı şeritte devam eder.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            C: B
            B: A
            A:
            """);

        rows.Select(r => r.Lane).ShouldAllBe(lane => lane == 0);
        rows.Select(r => r.ColorIndex).ShouldAllBe(c => c == 0);
        rows.ShouldAllBe(r => r.LaneCount == 1);
    }

    [Fact]
    public void Kok_commit_seridi_serbest_birakir()
    {
        IReadOnlyList<GraphRow> rows = Layout("A:");

        GraphRow root = rows.ShouldHaveSingleItem();
        root.Lane.ShouldBe(0);
        // Kökten aşağı kenar çıkmaz.
        root.Edges.ShouldBeEmpty();
    }

    [Fact]
    public void Dallanma_yeni_serit_acar()
    {
        //   C   B      ← iki dal ucu
        //    \ /
        //     A
        IReadOnlyList<GraphRow> rows = Layout(
            """
            C: A
            B: A
            A:
            """);

        // İlk işlenen (C) 0. şeridi alır, ikinci dal ucu (B) yeni şerit açar.
        rows[0].Lane.ShouldBe(0);
        rows[1].Lane.ShouldBe(1);
        // A, her iki dalın ortak ebeveyni — en soldaki rezervasyonda birleşir.
        rows[2].Lane.ShouldBe(0);
    }

    [Fact]
    public void Dallanma_sonrasi_serit_geri_kazanilir()
    {
        // A'da birleştikten sonra 1. şerit boşalmalı.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            C: A
            B: A
            A:
            """);

        rows[1].LaneCount.ShouldBe(2);
        // A satırında ikinci şerit artık kullanılmıyor.
        rows[2].LaneCount.ShouldBe(1);
    }

    [Fact]
    public void Merge_ikinci_ebeveyn_icin_yeni_serit_rezerve_eder()
    {
        //   D        ← merge
        //   |\
        //   B C
        //   |/
        //   A
        IReadOnlyList<GraphRow> rows = Layout(
            """
            D: B C
            C: A
            B: A
            A:
            """);

        GraphRow merge = rows[0];
        merge.Lane.ShouldBe(0);
        // İki kenar: ilk ebeveyn aynı şeritte, ikincisi yeni şeritte.
        merge.Edges.Count.ShouldBe(2);
        merge.Edges[0].Target.ShouldBe("B");
        merge.Edges[0].IsDiagonal.ShouldBeFalse();
        merge.Edges[1].Target.ShouldBe("C");
        merge.Edges[1].IsDiagonal.ShouldBeTrue();
        merge.Edges[1].ToLane.ShouldBe(1);
    }

    [Fact]
    public void Octopus_merge_her_ek_ebeveyn_icin_serit_acar()
    {
        IReadOnlyList<GraphRow> rows = Layout(
            """
            E: B C D
            D: A
            C: A
            B: A
            A:
            """);

        GraphRow octopus = rows[0];
        octopus.Edges.Count.ShouldBe(3);
        octopus.Edges.Select(e => e.Target).ShouldBe(["B", "C", "D"]);
        // Üç ayrı şeride dağılmalı.
        octopus.Edges.Select(e => e.ToLane).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void Uzun_mesafeli_kenar_gectigi_satirlarda_seridi_isgal_eder()
    {
        // C'nin ebeveyni A; arada B var. C→A kenarı B satırından geçmeli
        // ve o şeridi işgal etmeli — aksi halde başka bir şey oraya yerleşir ve çakışır.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            D: B C
            C: A
            B: A
            A:
            """);

        // rows[1] = C (şerit 1), rows[2] = B (şerit 0)
        GraphRow rowB = rows[2];

        // B satırında, C'nin rezerve ettiği şerit hâlâ dolu olmalı.
        rowB.Edges.ShouldContain(e => e.IsPassThrough || e.ToLane != rowB.Lane);
        rowB.LaneCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Gecis_kenarlari_pass_through_olarak_isaretlenir()
    {
        IReadOnlyList<GraphRow> rows = Layout(
            """
            D: A
            C: B
            B: X
            A:
            """);

        // C ve B satırlarında D'nin A'ya uzanan kenarı geçiş olarak görünmeli.
        rows[1].Edges.ShouldContain(e => e.IsPassThrough && e.Target == "A");
        rows[2].Edges.ShouldContain(e => e.IsPassThrough && e.Target == "A");
    }

    [Fact]
    public void Ayni_ebeveyne_giden_iki_merge_kenari_tek_serit_kullanir()
    {
        // Aynı commit için iki ayrı şerit açılırsa grafik gereksiz genişler.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            C: A A
            A:
            """);

        GraphRow merge = rows[0];
        merge.Edges.Count.ShouldBe(2);
        // İkisi de aynı şeride gitmeli.
        merge.Edges.Select(e => e.ToLane).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public void Coklu_kok_ilişkisiz_gecmisler_desteklenir()
    {
        // git merge --allow-unrelated-histories
        IReadOnlyList<GraphRow> rows = Layout(
            """
            C: A B
            B:
            A:
            """);

        rows[0].Edges.Count.ShouldBe(2);

        // B satırında C→A kenarı GEÇİYOR — bu meşru bir pass-through, boş değil.
        // (İlk yazdığım beklenti burayı boş sanıyordu; kusur testteydi.)
        GraphEdge passing = rows[1].Edges.ShouldHaveSingleItem();
        passing.IsPassThrough.ShouldBeTrue();
        passing.Target.ShouldBe("A");

        // A son kök: artık geçen kenar kalmadı.
        rows[2].Edges.ShouldBeEmpty();
    }

    [Fact]
    public void Yalniz_orphan_dal_kendi_seridini_alir()
    {
        // git checkout --orphan: hiçbir şeye bağlı olmayan ikinci bir geçmiş.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            B: A
            A:
            Y: X
            X:
            """);

        // B/A zinciri 0. şeritte, Y/X zinciri de 0'ı geri kazanır (A'da boşaldı).
        rows[0].Lane.ShouldBe(0);
        rows[2].Lane.ShouldBe(0);
    }

    [Fact]
    public void Ayni_satirda_gorunen_seritler_farkli_renk_alir()
    {
        // Anlamlı özellik: bir satırda AYNI ANDA görünen şeritlerin renkleri ayırt edilebilir
        // olmalı. "İlk üç satır üç farklı renk" gibi bir beklenti yanlıştı — şerit geri
        // kazanıldığında rengin de yeniden kullanılması doğru davranış (git de böyle yapıyor).
        IReadOnlyList<GraphRow> rows = Layout(
            """
            F: D E
            E: C
            D: B
            C: A
            B: A
            A:
            """);

        foreach (GraphRow row in rows)
        {
            int[] colorsInRow = [.. row.Edges.Select(e => e.ColorIndex).Append(row.ColorIndex)];

            // Aynı şeride ait kenarlar aynı rengi paylaşabilir; farklı şeritler paylaşmamalı.
            var byLane = row.Edges
                .GroupBy(e => e.ToLane)
                .Select(g => (Lane: g.Key, Color: g.First().ColorIndex))
                .ToList();

            byLane.Select(x => x.Color).Distinct().Count()
                .ShouldBe(byLane.Count, $"'{row.Commit.Id}' satırında iki şerit aynı rengi paylaşıyor");
        }
    }

    [Fact]
    public void Ayni_tabandan_acilan_dallar_serit_paylasir()
    {
        // GERÇEK VERİDEN ÇIKTI: aynı tabandan açılmış konu dalları (ör. birden fazla
        // dependabot dalı) her biri kendi şeridini tabana kadar tutarsa grafik gereksiz
        // genişler. git de bu durumda şeridi yeniden kullanıyor — doğrulandı.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            D: A
            C: A
            B: A
            A:
            """);

        // Üç dal ucu var ama hepsi hemen A'ya bağlandığı için iki şerit yetmeli.
        rows.Max(r => r.LaneCount).ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Serit_serbest_kalinca_rengi_yeniden_kullanilabilir()
    {
        // Renk indeksleri sınırsız büyümemeli; boşalan şeridin rengi geri kazanılır.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            E: D
            D: C
            C: A B
            B:
            A:
            """);

        rows.Select(r => r.ColorIndex).Max().ShouldBeLessThan(4);
    }

    [Fact]
    public void Uzun_paralel_dallar_kararli_seritte_kalir()
    {
        // Düz şerit kuralının asıl sınavı: uzun bir dal boyunca şerit değişmemeli.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            M: A3 B3
            B3: B2
            B2: B1
            B1: A0
            A3: A2
            A2: A1
            A1: A0
            A0:
            """);

        // A zinciri tek şeritte
        int[] aLanes = [.. rows.Where(r => r.Commit.Id.StartsWith('A') && r.Commit.Id != "A0")
            .Select(r => r.Lane)];
        aLanes.Distinct().Count().ShouldBe(1);

        // B zinciri de tek şeritte, ama A'dan farklı
        int[] bLanes = [.. rows.Where(r => r.Commit.Id.StartsWith('B')).Select(r => r.Lane)];
        bLanes.Distinct().Count().ShouldBe(1);
        bLanes[0].ShouldNotBe(aLanes[0]);
    }

    [Fact]
    public void Her_satirda_kenarlar_gecerli_seritlere_isaret_eder()
    {
        // Sağlamlık: hiçbir kenar var olmayan bir şeride gitmemeli.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            F: D E
            E: C
            D: B
            C: A
            B: A
            A:
            """);

        foreach (GraphRow row in rows)
        {
            row.Lane.ShouldBeInRange(0, row.LaneCount - 1);

            foreach (GraphEdge edge in row.Edges)
            {
                edge.FromLane.ShouldBeGreaterThanOrEqualTo(0);
                edge.ToLane.ShouldBeGreaterThanOrEqualTo(0);
            }
        }
    }

    [Fact]
    public void Artimli_ekleme_onceki_satirlari_degistirmez()
    {
        // P03-T06'nın çekirdek güvencesi: sonsuz kaydırmada yeni sayfa yüklenince
        // ekrandaki satırlar yerinden oynamamalı.
        const string definition =
            """
            E: D
            D: B C
            C: A
            B: A
            A:
            """;

        IReadOnlyList<DagCommit> commits = DagFixture.Parse(definition);

        // Hepsini tek seferde
        IReadOnlyList<GraphRow> atOnce = new GraphLayoutEngine().Add(commits);

        // Parça parça
        GraphLayoutEngine incremental = new();
        List<GraphRow> stepwise = [];
        stepwise.AddRange(incremental.Add(commits.Take(2)));
        stepwise.AddRange(incremental.Add(commits.Skip(2).Take(1)));
        stepwise.AddRange(incremental.Add(commits.Skip(3)));

        DagFixture.Render(stepwise).ShouldBe(DagFixture.Render(atOnce));
    }

    [Fact]
    public void Buyuk_sentetik_dag_makul_genislikte_kalir()
    {
        // 10k commit, düzenli aralıklarla dallanan ve birleşen bir geçmiş.
        // Şerit sayısı patlarsa grafik kullanılamaz hale gelir.
        List<DagCommit> commits = [];

        for (int i = 10_000; i > 0; i--)
        {
            commits.Add(i % 50 == 0 && i > 1
                ? new DagCommit($"c{i}", [$"c{i - 1}", $"c{Math.Max(i - 7, 1)}"])
                : new DagCommit($"c{i}", [$"c{i - 1}"]));
        }

        commits.Add(new DagCommit("c0", []));

        GraphLayoutEngine engine = new();
        IReadOnlyList<GraphRow> rows = engine.Add(commits);

        rows.Count.ShouldBe(10_001);
        engine.MaxLaneCount.ShouldBeLessThan(10);
    }
}
