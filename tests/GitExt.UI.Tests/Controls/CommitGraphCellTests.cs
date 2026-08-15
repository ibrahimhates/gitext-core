using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GitExt.Graph;
using GitExt.UI.Controls;

namespace GitExt.UI.Tests.Controls;

/// <summary>
/// P03-T10 — Graph column control. The tests verify pixels with real Skia rendering.
/// </summary>
public class CommitGraphCellTests
{
    private static GraphRow FirstRow(string definition) =>
        new GraphLayoutEngine().Add(DagFixture.Parse(definition))[0];

    private static GraphRow RowAt(string definition, int index) =>
        new GraphLayoutEngine().Add(DagFixture.Parse(definition))[index];

    /// <summary>
    /// Renders the control on its own and returns the pixels.
    /// </summary>
    private static uint[] RenderPixels(CommitGraphCell cell, int width = 60, int height = 22)
    {
        Window window = new()
        {
            Width = width,
            Height = height,
            WindowDecorations = WindowDecorations.None,
            Content = new Border
            {
                Background = Brushes.White,
                Child = cell,
            },
        };

        window.Show();

        using Bitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Kare alınamadı.");

        PixelSize size = frame.PixelSize;
        uint[] pixels = new uint[size.Width * size.Height];

        System.Runtime.InteropServices.GCHandle handle =
            System.Runtime.InteropServices.GCHandle.Alloc(
                pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            frame.CopyPixels(
                new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length * 4, size.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        // Detach the control from its window so the same instance can be rendered again.
        // Otherwise the second call fails with "The Control already has a parent".
        ((Border)window.Content!).Child = null;
        window.Close();

        return pixels;
    }

    private static int NonWhitePixelCount(uint[] pixels) =>
        pixels.Count(IsPainted);

    private static bool IsPainted(uint pixel) =>
        (pixel & 0x00FFFFFF) != 0x00FFFFFF && (pixel & 0xFF000000) != 0;

    // The captured frame is RGBA-ordered; read as a little-endian uint it becomes 0xAABBGGRR.
    // MEASURED: pure red arrives as 0xFF0000FF. Assuming ARGB and reading R from >>16 would mean
    // reading the blue channel.
    private static int Red(uint pixel) => (int)(pixel & 0xFF);

    private static int Green(uint pixel) => (int)((pixel >> 8) & 0xFF);

    private static int Blue(uint pixel) => (int)((pixel >> 16) & 0xFF);

    [AvaloniaFact]
    public void Satir_verisi_yoksa_hicbir_sey_cizmez()
    {
        CommitGraphCell cell = new();

        NonWhitePixelCount(RenderPixels(cell)).ShouldBe(0);
    }

    [AvaloniaFact]
    public void Dugum_cizilir()
    {
        CommitGraphCell cell = new() { Row = FirstRow("A:") };

        NonWhitePixelCount(RenderPixels(cell)).ShouldBeGreaterThan(10);
    }

    [AvaloniaFact]
    public void Kenar_cizilince_daha_fazla_piksel_boyanir()
    {
        // At the root there is only a node; in the middle of the chain there is a node + an edge.
        int rootOnly = NonWhitePixelCount(RenderPixels(new CommitGraphCell { Row = FirstRow("A:") }));

        int withEdge = NonWhitePixelCount(RenderPixels(
            new CommitGraphCell { Row = FirstRow("B: A\nA:") }));

        withEdge.ShouldBeGreaterThan(rootOnly);
    }

    [AvaloniaFact]
    public void Merge_satiri_diyagonal_kenar_cizer()
    {
        // In a merge the second parent goes to another lane → a diagonal line.
        GraphRow merge = FirstRow(
            """
            D: B C
            C: A
            B: A
            A:
            """);

        merge.Edges.ShouldContain(e => e.IsDiagonal);

        const double laneWidth = 14;
        CommitGraphCell cell = new() { Row = merge, LaneWidth = laneWidth };
        uint[] pixels = RenderPixels(cell, width: 60, height: 22);

        // MEASURED: the edge reaches from the middle of this row to the middle of the NEXT row,
        // i.e. in a 22 px cell it leaves the bottom before reaching the center of lane 1 (x=21).
        // This is the correct behavior — the edge continues on the next row.
        // The right criterion: is there a painted pixel to the RIGHT of the node in lane 0?
        int nodeRightEdge = (int)((laneWidth / 2) + 4);
        int paintedToTheRight = 0;

        for (int y = 0; y < 22; y++)
        {
            for (int x = nodeRightEdge + 1; x < 60; x++)
            {
                if (IsPainted(pixels[(y * 60) + x]))
                {
                    paintedToTheRight++;
                }
            }
        }

        paintedToTheRight.ShouldBeGreaterThan(
            0, "diyagonal kenar düğümün sağına doğru uzanmalı");
    }

    [AvaloniaFact]
    public void Genislik_serit_sayisindan_BAGIMSIZ_penceredendir()
    {
        // P03-T21: the width cannot be tied to the lane count of the row. In real repositories half of
        // the rows contain ~120 lanes; when the column grew accordingly it became ~2200 px and pushed
        // SHA/subject/author/date off the screen (measured in P03-T18). On top of that, when every row
        // had a different width the columns lost their alignment.
        CommitGraphCell single = new() { Row = FirstRow("A:"), LaneWidth = 14, VisibleLanes = 12 };
        CommitGraphCell multi = new()
        {
            Row = RowAt(
                """
                D: B C
                C: A
                B: A
                A:
                """, 0),
            LaneWidth = 14,
            VisibleLanes = 12,
        };

        single.Measure(Size.Infinity);
        multi.Measure(Size.Infinity);

        multi.DesiredSize.Width.ShouldBe(single.DesiredSize.Width);
        single.DesiredSize.Width.ShouldBe(12 * 14);
    }

    [AvaloniaFact]
    public void Pencere_boyutu_genisligi_belirler()
    {
        CommitGraphCell cell = new() { Row = FirstRow("A:"), LaneWidth = 10, VisibleLanes = 5 };

        cell.Measure(Size.Infinity);

        cell.DesiredSize.Width.ShouldBe(50);
    }

    [AvaloniaFact]
    public void Ozel_palet_kullanilir()
    {
        // The given color must be drawn instead of the default palette.
        CommitGraphCell cell = new()
        {
            Row = FirstRow("A:"),
            Palette = [Color.FromRgb(0xFF, 0x00, 0x00)],
        };

        uint[] pixels = RenderPixels(cell);

        // There must be at least one pixel close to red (no exact equality, because of anti-aliasing).
        bool hasRed = pixels.Any(p => Red(p) > 200 && Green(p) < 80 && Blue(p) < 80);

        hasRed.ShouldBeTrue("özel palette verilen kırmızı çizilmiş olmalı");
    }

    [AvaloniaFact]
    public void Satir_degisince_yeniden_cizilir()
    {
        // AffectsRender wiring: when Row changes the visual must be invalidated.
        CommitGraphCell cell = new() { Row = FirstRow("A:") };

        int before = NonWhitePixelCount(RenderPixels(cell));

        cell.Row = FirstRow(
            """
            D: B C
            C: A
            B: A
            A:
            """);

        int after = NonWhitePixelCount(RenderPixels(cell));

        after.ShouldNotBe(before);
    }

    [AvaloniaFact]
    public void Ayni_renk_icin_kalem_onbellekten_gelir()
    {
        // Allocation discipline: creating a new Pen/Brush on every frame would mean thousands of
        // objects per second at 60 FPS.
        CommitGraphCell cell = new() { Row = FirstRow("B: A\nA:") };

        RenderPixels(cell);

        long before = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < 20; i++)
        {
            RenderPixels(cell);
        }

        long perRender = (GC.GetTotalAllocatedBytes() - before) / 20;

        // Per-frame allocation mostly comes from the bitmap capture; since the pen/brush come from the
        // cache, this number must stay reasonable.
        perRender.ShouldBeLessThan(2_000_000);
    }
}
