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
/// P04-T09 — Unified diff'in <b>gerçekten çizildiğini</b> doğrular.
/// </summary>
/// <remarks>
/// <para>
/// Bu testin ViewModel testlerinden ayrı var olma sebebi ölçülmüş bir hatadır: satır şablonu
/// metni <b>yalnızca</b> <c>Segments</c> üzerinden çiziyor, hunk başlığı ise parçasız
/// üretiliyordu — sonuç, ekranda <b>boş gri bir şerit</b>. ViewModel testleri
/// (<c>Text</c> doğruydu) bunu görmedi; yalnızca gerçek depo render'ında fark edildi.
/// </para>
/// <para>
/// Renk sabitleri <c>DiffView.axaml</c>'den kopyadır. Amaç belli bir tonu korumak değil,
/// <b>ayrımın</b> korunması: satır arka planı ile satır içi vurgulama farklı olmalı.
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
    /// Kare RGBA sıralı; little-endian <c>uint</c> olarak <c>0xAABBGGRR</c> okunur.
    /// Bu dönüşüm olmadan kırmızı ile mavi yer değiştirir (bkz. <c>CommitGraphCellTests</c>).
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
        // Karşı kanıt: yukarıdaki renkler gerçekten parçalardan geliyor.
        Frame frame = await RenderAsync(Modified(
            new DiffLine(DiffLineKind.Removed, "iki") { OldLineNumber = 2 },
            new DiffLine(DiffLineKind.Added, "IKI") { NewLineNumber = 2 }));

        frame.Contains(ToPixel(AddedSegment)).ShouldBeFalse();
        frame.Contains(ToPixel(RemovedSegment)).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Hunk_basliginin_metni_cizilir()
    {
        // ÖLÇÜLEN HATA: başlık parçasız üretilince şablon hiçbir metin çizmiyordu; satır
        // vardı, gri arka plan vardı, YAZI yoktu.
        //
        // ⚠️ Yalnızca "başlık satırlarında koyu piksel var mı" diye bakmak YETMİYOR: aynı
        // y'lerde SOLDAKİ dosya listesinin yazısı da var ve hatalı sürüm testi geçiyordu
        // (denendi). Bu yüzden arama, başlık şeridinin kendi dikdörtgeniyle sınırlı.
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

    // ---- P04-T10: yan yana görünüm ----

    [AvaloniaFact]
    public async Task Yan_yana_gorunumde_silinen_solda_eklenen_sagda_cizilir()
    {
        // Taraflar yer değiştirirse ekranda "her şey var" görünür ama diff TERS okunur.
        // Bu yüzden renk sayısı değil, rengin HANGİ YARIDA olduğu kontrol ediliyor.
        Frame frame = await RenderAsync(
            Modified(
                new DiffLine(DiffLineKind.Removed, "eski satir") { OldLineNumber = 1 },
                new DiffLine(DiffLineKind.Added, "yeni satir") { NewLineNumber = 1 }),
            sideBySide: true);

        // Yarı sınırını piksel aritmetiğiyle tahmin etmek yerine iki bölgenin KONUMLARI
        // karşılaştırılıyor: silinenin tamamı, eklenenin tamamının solunda kalmalı.
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

        // Dolgu kendi rengiyle çiziliyor: boş bir bağlam satırından ayırt edilebilmeli.
        frame.Count(ToPixel(FillerBackground)).ShouldBeGreaterThan(100);
    }

    [AvaloniaFact]
    public async Task Birlesik_gorunumde_dolgu_rengi_hic_cizilmez()
    {
        // Karşı kanıt: dolgu rengi yalnızca yan yana görünümden geliyor.
        Frame frame = await RenderAsync(
            Modified(new DiffLine(DiffLineKind.Added, "yeni satir") { NewLineNumber = 1 }));

        frame.Contains(ToPixel(FillerBackground)).ShouldBeFalse();
    }

    // ---- P04-T13: görsel ayarlar ----

    [AvaloniaFact]
    public async Task Satir_kaydirma_acikken_satir_yukselir()
    {
        // ÖLÇÜLDÜ: değişken satır yüksekliği sanallaştırmayı bozmuyor. Burada korunan şey
        // şablondaki sabit yüksekliğin geri gelmemesi — gelirse wrap sessizce ETKİSİZ olur
        // (metin sarılır ama satır büyümediği için alt satırlar KIRPILIR).
        string longLine = string.Join(' ', Enumerable.Repeat("kelime", 60));

        FileDiff diff = Modified(new DiffLine(DiffLineKind.Added, longLine) { NewLineNumber = 1 });

        int WrappedRows(Frame frame) => Enumerable
            .Range(0, frame.Height)
            .Count(y => Enumerable.Range(0, frame.Width).Any(x => frame.At(x, y) == ToPixel(AddedLineBackground)));

        Frame off = await RenderAsync(diff);
        Frame on = await RenderAsync(diff, configure: vm => vm.WordWrap = true);

        // Sarılan satır ekranda BİRDEN ÇOK satır yüksekliği kaplamalı.
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

        // Punto kalıtsal; kök öğede verilen değer satır şablonlarına geçmeli.
        DarkPixels(large).ShouldBeGreaterThan(DarkPixels(small));
    }
}
