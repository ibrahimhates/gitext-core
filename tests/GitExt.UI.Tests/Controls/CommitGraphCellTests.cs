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
/// P03-T10 — Grafik sütunu kontrolü. Testler gerçek Skia render'ıyla piksel doğrular.
/// </summary>
public class CommitGraphCellTests
{
    private static GraphRow FirstRow(string definition) =>
        new GraphLayoutEngine().Add(DagFixture.Parse(definition))[0];

    private static GraphRow RowAt(string definition, int index) =>
        new GraphLayoutEngine().Add(DagFixture.Parse(definition))[index];

    /// <summary>
    /// Kontrolü tek başına render edip pikselleri döndürür.
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

        // Kontrolü penceresinden ayır ki aynı örnek tekrar render edilebilsin.
        // Aksi halde ikinci çağrıda "The Control already has a parent" hatası gelir.
        ((Border)window.Content!).Child = null;
        window.Close();

        return pixels;
    }

    private static int NonWhitePixelCount(uint[] pixels) =>
        pixels.Count(IsPainted);

    private static bool IsPainted(uint pixel) =>
        (pixel & 0x00FFFFFF) != 0x00FFFFFF && (pixel & 0xFF000000) != 0;

    // Yakalanan kare RGBA sıralı; little-endian uint olarak okununca 0xAABBGGRR olur.
    // ÖLÇÜLDÜ: saf kırmızı 0xFF0000FF geliyor. ARGB varsayıp R'yi >>16'dan okumak
    // mavi kanalı okumak demek olurdu.
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
        // Kökte yalnızca düğüm var; zincirin ortasında düğüm + kenar var.
        int rootOnly = NonWhitePixelCount(RenderPixels(new CommitGraphCell { Row = FirstRow("A:") }));

        int withEdge = NonWhitePixelCount(RenderPixels(
            new CommitGraphCell { Row = FirstRow("B: A\nA:") }));

        withEdge.ShouldBeGreaterThan(rootOnly);
    }

    [AvaloniaFact]
    public void Merge_satiri_diyagonal_kenar_cizer()
    {
        // Merge'de ikinci ebeveyn başka bir şeride gider → diyagonal çizgi.
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

        // ÖLÇÜLDÜ: kenar bu satırın ortasından bir SONRAKİ satırın ortasına uzanıyor,
        // yani 22 px'lik hücrede 1. şerit merkezine (x=21) ulaşmadan alttan çıkıyor.
        // Bu doğru davranış — kenar bir sonraki satırda devam eder.
        // Doğru ölçüt: 0. şeridin düğümünün SAĞINDA boyalı piksel var mı?
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
    public void Genislik_serit_sayisiyla_olculur()
    {
        // İki şeritli bir satır, tek şeritliden geniş ölçülmeli.
        CommitGraphCell single = new() { Row = FirstRow("A:"), LaneWidth = 14 };
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
        };

        single.Measure(Size.Infinity);
        multi.Measure(Size.Infinity);

        multi.DesiredSize.Width.ShouldBeGreaterThan(single.DesiredSize.Width);
    }

    [AvaloniaFact]
    public void Ozel_palet_kullanilir()
    {
        // Varsayılan palet yerine verilen renk çizilmeli.
        CommitGraphCell cell = new()
        {
            Row = FirstRow("A:"),
            Palette = [Color.FromRgb(0xFF, 0x00, 0x00)],
        };

        uint[] pixels = RenderPixels(cell);

        // Kırmızıya yakın en az bir piksel olmalı (kenar yumuşatma nedeniyle tam eşitlik aranmaz).
        bool hasRed = pixels.Any(p => Red(p) > 200 && Green(p) < 80 && Blue(p) < 80);

        hasRed.ShouldBeTrue("özel palette verilen kırmızı çizilmiş olmalı");
    }

    [AvaloniaFact]
    public void Satir_degisince_yeniden_cizilir()
    {
        // AffectsRender bağlantısı: Row değişince görsel geçersiz kılınmalı.
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
        // Tahsis disiplini: her karede yeni Pen/Brush yaratmak 60 FPS'te
        // saniyede binlerce nesne demek olurdu.
        CommitGraphCell cell = new() { Row = FirstRow("B: A\nA:") };

        RenderPixels(cell);

        long before = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < 20; i++)
        {
            RenderPixels(cell);
        }

        long perRender = (GC.GetTotalAllocatedBytes() - before) / 20;

        // Kare başına tahsis çoğunlukla bitmap yakalamadan gelir; kalem/fırça
        // önbellekten geldiği için bu sayı makul kalmalı.
        perRender.ShouldBeLessThan(2_000_000);
    }
}
