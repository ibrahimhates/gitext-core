using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P04-T09 — verifies that the unified diff <b>really is drawn</b>.
/// </summary>
/// <remarks>
/// <para>
/// The reason this test exists separately from the ViewModel tests is a measured bug: the line
/// template draws text <b>only</b> through <c>Segments</c>, while the hunk header was produced
/// without any segments — the result being an <b>empty grey strip</b> on screen. The ViewModel tests
/// (where <c>Text</c> was correct) did not see it; it was noticed only in a render of a real
/// repository.
/// </para>
/// <para>
/// The colour constants are copies from <c>DiffView.axaml</c>. The aim is not to preserve a
/// particular shade but to preserve the <b>distinction</b>: the line background and the intra-line
/// highlight must differ.
/// </para>
/// </remarks>
public class DiffViewRenderTests
{
    private const uint AddedLineBackground = 0xE6FFEC;
    private const uint RemovedLineBackground = 0xFFEBE9;
    private const uint AddedSegment = 0xABF2BC;
    private const uint RemovedSegment = 0xFFC9C6;
    private const uint HunkHeaderBackground = 0xEEF2F7;
    private const uint FillerBackground = 0xE9E9EF;

    /// <summary>
    /// The frame is RGBA-ordered; read as a little-endian <c>uint</c> it is <c>0xAABBGGRR</c>.
    /// Without this conversion red and blue swap places (see <c>CommitGraphCellTests</c>).
    /// </summary>
    private static uint ToPixel(uint rgb) =>
        0xFF000000u | ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    private static FileDiff Modified(params DiffLine[] lines) =>
        FakeGitData.Diff("src/a.cs") with
        {
            Hunks =
            [
                new DiffHunk
                {
                    Header = "@@ -1,2 +1,2 @@",
                    OldStart = 1,
                    OldLength = 2,
                    NewStart = 1,
                    NewLength = 2,
                    Lines = lines,
                },
            ],
        };

    private sealed record Frame(uint[] Pixels, int Width)
    {
        public uint At(int x, int y) => Pixels[(y * Width) + x];

        public int Height => Pixels.Length / Width;

        public int Count(uint pixel) => Pixels.Count(p => p == pixel);

        public bool Contains(uint pixel) => Pixels.Contains(pixel);
    }

    private static async Task<Frame> RenderAsync(
        FileDiff diff,
        bool sideBySide = false,
        Action<DiffViewModel>? configure = null)
    {
        DiffViewModel viewModel = new(new FakeDiffReader([diff])) { ShowSideBySide = sideBySide };

        configure?.Invoke(viewModel);

        await viewModel.ShowCommitAsync("/tmp/depo", CommitId.Parse(FakeGitData.Sha(7)));

        Window window = new()
        {
            Width = 900,
            Height = 200,
            WindowDecorations = WindowDecorations.None,
            Content = new Border
            {
                Background = Brushes.White,
                Child = new DiffView { DataContext = viewModel },
            },
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

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

        ((Border)window.Content!).Child = null;
        window.Close();

        return new Frame(pixels, size.Width);
    }

    [AvaloniaFact]
    public async Task Eklenen_ve_silinen_satirlar_farkli_renklerde_cizilir()
    {
        Frame frame = await RenderAsync(Modified(
            new DiffLine(DiffLineKind.Context, "bir") { OldLineNumber = 1, NewLineNumber = 1 },
            new DiffLine(DiffLineKind.Removed, "iki") { OldLineNumber = 2 },
            new DiffLine(DiffLineKind.Added, "IKI") { NewLineNumber = 2 }));

        frame.Count(ToPixel(AddedLineBackground)).ShouldBeGreaterThan(100);
        frame.Count(ToPixel(RemovedLineBackground)).ShouldBeGreaterThan(100);
        frame.Count(ToPixel(HunkHeaderBackground)).ShouldBeGreaterThan(100);
    }

    [AvaloniaFact]
    public async Task Satir_ici_vurgulama_satir_arka_planindan_ayrisir()
    {
        Frame frame = await RenderAsync(Modified(
            new DiffLine(DiffLineKind.Removed, "bir iki uc")
            {
                OldLineNumber = 1,
                Segments =
                [
                    new DiffSegment(DiffLineKind.Context, "bir "),
                    new DiffSegment(DiffLineKind.Removed, "iki"),
                    new DiffSegment(DiffLineKind.Context, " uc"),
                ],
            },
            new DiffLine(DiffLineKind.Added, "bir IKI uc")
            {
                NewLineNumber = 1,
                Segments =
                [
                    new DiffSegment(DiffLineKind.Context, "bir "),
                    new DiffSegment(DiffLineKind.Added, "IKI"),
                    new DiffSegment(DiffLineKind.Context, " uc"),
                ],
            }));

        frame.Count(ToPixel(AddedSegment)).ShouldBeGreaterThan(20);
        frame.Count(ToPixel(RemovedSegment)).ShouldBeGreaterThan(20);
    }

    [AvaloniaFact]
    public async Task Parca_yoksa_vurgulama_rengi_hic_cizilmez()
    {
        // The counter-evidence: the colours above really do come from the segments.
        Frame frame = await RenderAsync(Modified(
            new DiffLine(DiffLineKind.Removed, "iki") { OldLineNumber = 2 },
            new DiffLine(DiffLineKind.Added, "IKI") { NewLineNumber = 2 }));

        frame.Contains(ToPixel(AddedSegment)).ShouldBeFalse();
        frame.Contains(ToPixel(RemovedSegment)).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Hunk_basliginin_metni_cizilir()
    {
        // THE MEASURED BUG: with the header produced without segments the template drew no text at
        // all; there was a row, there was a grey background, there was NO WRITING.
        //
        // ⚠️ Looking only at "is there a dark pixel on the header rows" is NOT ENOUGH: at the same y
        // values there is also the text of the file list ON THE LEFT, and the faulty version passed the
        // test (it was tried). So the search is confined to the header strip's own rectangle.
        Frame frame = await RenderAsync(Modified(
            new DiffLine(DiffLineKind.Added, "kod") { NewLineNumber = 1 }));

        uint header = ToPixel(HunkHeaderBackground);

        List<(int X, int Y)> headerPixels = [.. Enumerable
            .Range(0, frame.Height)
            .SelectMany(y => Enumerable.Range(0, frame.Width).Select(x => (X: x, Y: y)))
            .Where(p => frame.At(p.X, p.Y) == header)];

        headerPixels.ShouldNotBeEmpty();

        int left = headerPixels.Min(p => p.X);
        int right = headerPixels.Max(p => p.X);
        int top = headerPixels.Min(p => p.Y);
        int bottom = headerPixels.Max(p => p.Y);

        int darkPixels = 0;

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                uint p = frame.At(x, y);

                if ((p & 0xFF) < 0x80 && ((p >> 8) & 0xFF) < 0x80 && ((p >> 16) & 0xFF) < 0x80)
                {
                    darkPixels++;
                }
            }
        }

        darkPixels.ShouldBeGreaterThan(20);
    }

    // ---- P04-T10: the side-by-side view ----

    [AvaloniaFact]
    public async Task Yan_yana_gorunumde_silinen_solda_eklenen_sagda_cizilir()
    {
        // If the sides swap, everything looks present on screen but the diff READS BACKWARDS.
        // That is why what is checked is not the number of colours but WHICH HALF the colour is in.
        Frame frame = await RenderAsync(
            Modified(
                new DiffLine(DiffLineKind.Removed, "eski satir") { OldLineNumber = 1 },
                new DiffLine(DiffLineKind.Added, "yeni satir") { NewLineNumber = 1 }),
            sideBySide: true);

        // Rather than guessing the half boundary with pixel arithmetic, the POSITIONS of the two
        // regions are compared: all of the removal must lie to the left of all of the addition.
        List<int> removedX = [];
        List<int> addedX = [];

        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                uint p = frame.At(x, y);

                if (p == ToPixel(RemovedLineBackground))
                {
                    removedX.Add(x);
                }
                else if (p == ToPixel(AddedLineBackground))
                {
                    addedX.Add(x);
                }
            }
        }

        removedX.Count.ShouldBeGreaterThan(100);
        addedX.Count.ShouldBeGreaterThan(100);

        removedX.Max().ShouldBeLessThan(addedX.Min());
    }

    [AvaloniaFact]
    public async Task Karsiligi_olmayan_tarafa_dolgu_cizilir()
    {
        Frame frame = await RenderAsync(
            Modified(new DiffLine(DiffLineKind.Added, "yeni satir") { NewLineNumber = 1 }),
            sideBySide: true);

        // The filler is drawn in its own colour: it has to be distinguishable from an empty context line.
        frame.Count(ToPixel(FillerBackground)).ShouldBeGreaterThan(100);
    }

    [AvaloniaFact]
    public async Task Birlesik_gorunumde_dolgu_rengi_hic_cizilmez()
    {
        // The counter-evidence: the filler colour comes only from the side-by-side view.
        Frame frame = await RenderAsync(
            Modified(new DiffLine(DiffLineKind.Added, "yeni satir") { NewLineNumber = 1 }));

        frame.Contains(ToPixel(FillerBackground)).ShouldBeFalse();
    }

    // ---- P04-T13: display settings ----

    [AvaloniaFact]
    public async Task Satir_kaydirma_acikken_satir_yukselir()
    {
        // MEASURED: a variable row height does not break virtualisation. What is protected here is that
        // the fixed height in the template does not come back — if it does, wrapping is silently
        // INEFFECTIVE (the text wraps, but because the row does not grow the lower lines are CLIPPED).
        string longLine = string.Join(' ', Enumerable.Repeat("kelime", 60));

        FileDiff diff = Modified(new DiffLine(DiffLineKind.Added, longLine) { NewLineNumber = 1 });

        int WrappedRows(Frame frame) => Enumerable
            .Range(0, frame.Height)
            .Count(y => Enumerable.Range(0, frame.Width).Any(x => frame.At(x, y) == ToPixel(AddedLineBackground)));

        Frame off = await RenderAsync(diff);
        Frame on = await RenderAsync(diff, configure: vm => vm.WordWrap = true);

        // A wrapped line must occupy MORE THAN ONE line height on screen.
        WrappedRows(on).ShouldBeGreaterThan(WrappedRows(off) * 2);
    }

    [AvaloniaFact]
    public async Task Punto_degisince_satirlar_buyur()
    {
        FileDiff diff = Modified(new DiffLine(DiffLineKind.Added, "kod") { NewLineNumber = 1 });

        int DarkPixels(Frame frame) => frame.Pixels.Count(p =>
            (p & 0xFF) < 0x80 && ((p >> 8) & 0xFF) < 0x80 && ((p >> 16) & 0xFF) < 0x80);

        Frame small = await RenderAsync(diff, configure: vm => vm.FontSize = 10);
        Frame large = await RenderAsync(diff, configure: vm => vm.FontSize = 22);

        // Font size is inherited; the value given on the root element must reach the row templates.
        DarkPixels(large).ShouldBeGreaterThan(DarkPixels(small));
    }
}
