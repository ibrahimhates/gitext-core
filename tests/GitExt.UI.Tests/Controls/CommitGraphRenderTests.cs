using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GitExt.Graph;
using GitExt.UI.Controls;

namespace GitExt.UI.Tests.Controls;

/// <summary>
/// P12-T09/T10 — what the graph column actually paints.
/// </summary>
/// <remarks>
/// <para>
/// These tests read <b>pixels</b>. That is unusual here, and it is deliberate: the defect they
/// exist for was invisible to every other kind of test. The layout was right, the edges were
/// right, the control drew "successfully" — and the graph on screen was a column of dashes,
/// because the half of each connection above a commit belonged to no row and was never painted.
/// Only looking at the picture catches that.
/// </para>
/// <para>
/// Real Skia rendering is on in the test application (<c>UseHeadlessDrawing = false</c>), so
/// <see cref="RenderTargetBitmap"/> produces the same pixels the user sees.
/// </para>
/// </remarks>
public class CommitGraphRenderTests
{
    private const int Width = 40;
    private const int Height = 22;

    private static GraphRow Row(int lane, params GraphEdge[] edges) =>
        new()
        {
            Commit = new DagCommit("a".PadRight(40, '0'), []),
            Lane = lane,
            ColorIndex = 0,
            Edges = edges,
            LaneCount = 2,
        };

    private static GraphEdge Edge(int from, int to) =>
        new() { FromLane = from, ToLane = to, Target = "b".PadRight(40, '0'), ColorIndex = 0 };

    /// <summary>Renders the cell and returns its pixels (ARGB, row-major).</summary>
    private static uint[] Render(CommitGraphCell cell)
    {
        cell.Measure(new Size(Width, Height));
        cell.Arrange(new Rect(0, 0, Width, Height));

        using RenderTargetBitmap bitmap = new(new PixelSize(Width, Height), new Vector(96, 96));
        bitmap.Render(cell);

        uint[] pixels = new uint[Width * Height];
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, Width, Height),
                handle.AddrOfPinnedObject(),
                pixels.Length * 4,
                Width * 4);
        }
        finally
        {
            handle.Free();
        }

        return pixels;
    }

    /// <summary>Is anything painted on this row of pixels?</summary>
    private static bool RowIsPainted(uint[] pixels, int y)
    {
        for (int x = 0; x < Width; x++)
        {
            if ((pixels[(y * Width) + x] >> 24) > 0x20)
            {
                return true;
            }
        }

        return false;
    }

    private static CommitGraphCell Cell() =>
        new()
        {
            LaneWidth = 14,
            NodeRadius = 4,
            LineThickness = 2,
            VisibleLanes = 2,
            Palette = [Colors.SteelBlue, Colors.IndianRed],
        };

    [AvaloniaFact]
    public void Cizgi_satirin_UST_kenarina_kadar_uzaniyor()
    {
        // 🔴 THE defect: with only the outgoing half drawn, the top of every row was blank and the
        // lanes looked cut off above every commit — exactly what the user reported. The row above
        // supplies the incoming half.
        CommitGraphCell cell = Cell();
        cell.PreviousRow = Row(0, Edge(0, 0));
        cell.Row = Row(0, Edge(0, 0));

        uint[] pixels = Render(cell);

        RowIsPainted(pixels, 0).ShouldBeTrue("the line has to reach the top edge of the row");
        RowIsPainted(pixels, Height - 1).ShouldBeTrue("…and the bottom edge");
    }

    [AvaloniaFact]
    public void Satirin_HER_pikselinde_cizgi_var()
    {
        // No gaps anywhere between the two edges: a lane is continuous or it is not a lane.
        CommitGraphCell cell = Cell();
        cell.PreviousRow = Row(0, Edge(0, 0));
        cell.Row = Row(0, Edge(0, 0));

        uint[] pixels = Render(cell);

        for (int y = 0; y < Height; y++)
        {
            RowIsPainted(pixels, y).ShouldBeTrue($"y={y} boş kaldı");
        }
    }

    [AvaloniaFact]
    public void Ustte_satir_yoksa_ust_yari_BOS_kaliyor()
    {
        // The counter-evidence. The first row of the list has nothing above it, so its top half is
        // legitimately empty — and this is what the whole graph used to look like.
        CommitGraphCell cell = Cell();
        cell.PreviousRow = null;
        cell.Row = Row(0, Edge(0, 0));

        uint[] pixels = Render(cell);

        RowIsPainted(pixels, 0).ShouldBeFalse();
        RowIsPainted(pixels, Height - 1).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Referansi_olan_commit_KARE_digerleri_daire()
    {
        // GitExtensions draws a square node when the commit carries refs; that is how a branch tip
        // or a tag is told from an ordinary commit at a glance.
        CommitGraphCell round = Cell();
        round.Row = Row(0);

        CommitGraphCell square = Cell();
        square.Row = Row(0);
        square.HasRefs = true;

        int roundPixels = Painted(Render(round));
        int squarePixels = Painted(Render(square));

        // A square of side 2r covers more than a circle of radius r (4r² vs πr²).
        squarePixels.ShouldBeGreaterThan(roundPixels);

        static int Painted(uint[] pixels) => pixels.Count(p => (p >> 24) > 0x20);
    }

    [AvaloniaFact]
    public void HEAD_dugumu_CERCEVELI()
    {
        // "Which branch am I on" has to be answerable from the graph itself.
        CommitGraphCell plain = Cell();
        plain.Row = Row(0);

        CommitGraphCell head = Cell();
        head.Row = Row(0);
        head.IsHead = true;

        Painted(Render(head)).ShouldBeGreaterThan(Painted(Render(plain)));

        static int Painted(uint[] pixels) => pixels.Count(p => (p >> 24) > 0x20);
    }

    [AvaloniaFact]
    public void HEAD_gecmisinde_OLMAYAN_satir_GRI_ciziliyor()
    {
        // GitExtensions' DrawNonRelativesGray: the history you are on keeps its colour, the rest
        // steps back. Without it the branch you are on is not visible in the graph at all.
        CommitGraphCell coloured = Cell();
        coloured.Row = Row(0, Edge(0, 0));

        CommitGraphCell grey = Cell();
        grey.Row = Row(0, Edge(0, 0));
        grey.IsRelative = false;

        uint colouredNode = NodePixel(Render(coloured));
        uint greyNode = NodePixel(Render(grey));

        colouredNode.ShouldNotBe(greyNode);

        // Grey means R == G == B; the palette colour does not.
        (byte r, byte g, byte b) = Channels(greyNode);
        r.ShouldBe(g);
        g.ShouldBe(b);

        (byte cr, byte cg, byte cb) = Channels(colouredNode);
        (cr == cg && cg == cb).ShouldBeFalse();

        static uint NodePixel(uint[] pixels) => pixels[((Height / 2) * Width) + 7];

        static (byte R, byte G, byte B) Channels(uint pixel) =>
            ((byte)((pixel >> 16) & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)(pixel & 0xFF));
    }

    [AvaloniaFact]
    public void Pencere_disindaki_serit_cizilmiyor()
    {
        // The lane window (P03-T21) still holds: a segment entirely outside it costs nothing.
        CommitGraphCell cell = Cell();
        cell.FirstLane = 0;
        cell.VisibleLanes = 1;
        cell.Row = new GraphRow
        {
            Commit = new DagCommit("a".PadRight(40, '0'), []),
            Lane = 5,
            ColorIndex = 0,
            Edges = [Edge(5, 5)],
            LaneCount = 6,
        };

        uint[] pixels = Render(cell);

        // Only the overflow marker may appear, not the lane itself: the marker is grey and thin.
        pixels.Count(p => (p >> 24) > 0x20).ShouldBeLessThan(40);
    }
}
