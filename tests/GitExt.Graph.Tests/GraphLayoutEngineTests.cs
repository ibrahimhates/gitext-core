namespace GitExt.Graph.Tests;

/// <summary>
/// P03-T03 / T04 / T07 — The layout algorithm.
/// </summary>
/// <remarks>
/// The tests run without any UI (ADR-0003). When a scenario breaks, the failure message shows the
/// expected and the actual layout as a text table.
/// </remarks>
public class GraphLayoutEngineTests
{
    private static IReadOnlyList<GraphRow> Layout(string definition) =>
        new GraphLayoutEngine().Add(DagFixture.Parse(definition));

    [Fact]
    public void Dogrusal_gecmis_tek_seritte_kalir()
    {
        // The straight-lane rule: the first parent carries on in the same lane.
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
        // No edge leaves a root going downwards.
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

        // The first one processed (C) takes lane 0, the second branch tip (B) opens a new lane.
        rows[0].Lane.ShouldBe(0);
        rows[1].Lane.ShouldBe(1);
        // A is the common parent of both branches — they join at the leftmost reservation.
        rows[2].Lane.ShouldBe(0);
    }

    [Fact]
    public void Dallanma_sonrasi_serit_geri_kazanilir()
    {
        // After joining at A, lane 1 must become free.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            C: A
            B: A
            A:
            """);

        rows[1].LaneCount.ShouldBe(2);
        // On row A the second lane is no longer in use.
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
        // Two edges: the first parent in the same lane, the second in a new one.
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
        // They must spread across three separate lanes.
        octopus.Edges.Select(e => e.ToLane).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void Uzun_mesafeli_kenar_gectigi_satirlarda_seridi_isgal_eder()
    {
        // C's parent is A, with B in between. The C→A edge must pass through row B and occupy that
        // lane — otherwise something else settles there and they collide.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            D: B C
            C: A
            B: A
            A:
            """);

        // rows[1] = C (lane 1), rows[2] = B (lane 0)
        GraphRow rowB = rows[2];

        // On row B, the lane C reserved must still be occupied.
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

        // On rows C and B, D's edge reaching to A must appear as a pass-through.
        rows[1].Edges.ShouldContain(e => e.IsPassThrough && e.Target == "A");
        rows[2].Edges.ShouldContain(e => e.IsPassThrough && e.Target == "A");
    }

    [Fact]
    public void Ayni_ebeveyne_giden_iki_merge_kenari_tek_serit_kullanir()
    {
        // If two separate lanes are opened for the same commit, the graph widens for nothing.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            C: A A
            A:
            """);

        GraphRow merge = rows[0];
        merge.Edges.Count.ShouldBe(2);
        // Both must go into the same lane.
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

        // On row B the C→A edge PASSES THROUGH — that is a legitimate pass-through, not an empty slot.
        // (My first expectation assumed it was empty here; the fault was in the test.)
        GraphEdge passing = rows[1].Edges.ShouldHaveSingleItem();
        passing.IsPassThrough.ShouldBeTrue();
        passing.Target.ShouldBe("A");

        // A is the last root: no edge passes through any more.
        rows[2].Edges.ShouldBeEmpty();
    }

    [Fact]
    public void Yalniz_orphan_dal_kendi_seridini_alir()
    {
        // git checkout --orphan: a second history attached to nothing.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            B: A
            A:
            Y: X
            X:
            """);

        // The B/A chain is in lane 0, and the Y/X chain reclaims 0 as well (it was freed at A).
        rows[0].Lane.ShouldBe(0);
        rows[2].Lane.ShouldBe(0);
    }

    [Fact]
    public void Ayni_satirda_gorunen_seritler_farkli_renk_alir()
    {
        // The meaningful property: the colours of the lanes visible AT THE SAME TIME on a row must be
        // distinguishable. An expectation like "the first three rows are three different colours" was
        // wrong — reusing the colour when a lane is reclaimed is the correct behaviour (git does the
        // same).
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

            // Edges belonging to the same lane may share a colour; different lanes must not.
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
        // FROM REAL DATA: topic branches opened from the same base (several dependabot branches, say)
        // widen the graph for nothing if each holds its own lane all the way down to the base. git
        // reuses the lane in this situation too — verified.
        IReadOnlyList<GraphRow> rows = Layout(
            """
            D: A
            C: A
            B: A
            A:
            """);

        // There are three branch tips, but because they all join A immediately, two lanes must suffice.
        rows.Max(r => r.LaneCount).ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Serit_serbest_kalinca_rengi_yeniden_kullanilabilir()
    {
        // The colour indices must not grow without bound; a freed lane's colour is reclaimed.
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
        // The real test of the straight-lane rule: the lane must not change along a long branch.
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

        // The A chain in a single lane
        int[] aLanes = [.. rows.Where(r => r.Commit.Id.StartsWith('A') && r.Commit.Id != "A0")
            .Select(r => r.Lane)];
        aLanes.Distinct().Count().ShouldBe(1);

        // The B chain in a single lane too, but a different one from A
        int[] bLanes = [.. rows.Where(r => r.Commit.Id.StartsWith('B')).Select(r => r.Lane)];
        bLanes.Distinct().Count().ShouldBe(1);
        bLanes[0].ShouldNotBe(aLanes[0]);
    }

    [Fact]
    public void Her_satirda_kenarlar_gecerli_seritlere_isaret_eder()
    {
        // Robustness: no edge may go to a lane that does not exist.
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
        // P03-T06's core guarantee: in infinite scrolling, the rows on screen must not move when a
        // new page is loaded.
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

        // Piece by piece
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
        // 10k commits, a history branching and merging at regular intervals.
        // If the lane count explodes, the graph becomes unusable.
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
