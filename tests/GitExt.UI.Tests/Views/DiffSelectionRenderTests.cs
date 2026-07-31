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
/// P05-T11 — satır seçimi <b>görsel olarak net</b> mi?
/// </summary>
/// <remarks>
/// Kısmi staging seçime dayanıyor: kullanıcı hangi satırların stage'leneceğini yalnızca
/// ekrandan anlıyor. Seçim görünmezse yanlış satırları stage'ler ve git bunu kabul eder —
/// yama geçerlidir, içerik <b>sessizce</b> yanlış olur (P05-T04'ün bulgusu).
/// <para>
/// Değişiklik satırlarının kendi arka planı var (yeşil/kırmızı); seçim vurgusu onların
/// <b>altında</b> kalırsa tam da en çok seçilen satırlarda görünmez olur. Test bunu
/// piksel karşılaştırmasıyla ölçüyor.
/// </para>
/// </remarks>
public class DiffSelectionRenderTests
{
    private static FileDiff Sample() => new()
    {
        Path = RepositoryPath.Parse("a.cs"),
        Change = FileChangeKind.Modified,
        Hunks =
        [
            new DiffHunk
            {
                Header = "@@ -1,3 +1,3 @@",
                OldStart = 1,
                OldLength = 3,
                NewStart = 1,
                NewLength = 3,
                Lines =
                [
                    new DiffLine(DiffLineKind.Context, "bir") { OldLineNumber = 1, NewLineNumber = 1 },
                    new DiffLine(DiffLineKind.Removed, "iki eski") { OldLineNumber = 2 },
                    new DiffLine(DiffLineKind.Added, "iki yeni") { NewLineNumber = 2 },
                ],
            },
        ],
    };

    /// <summary>Verilen satır seçiliyken ekranı yakalar.</summary>
    private static async Task<uint[]> RenderAsync(int? selectedRow)
    {
        DiffViewModel model = new(new FakeDiffReader([Sample()]));

        await model.ShowCommitAsync("/tmp/depo", CommitId.Parse(new string('a', 40)));

        DiffView view = new() { DataContext = model };

        Window window = new()
        {
            Width = 700,
            Height = 220,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.White, Child = view },
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        ListBox lines = view.GetControl<ListBox>("DiffLines");

        if (selectedRow is { } row)
        {
            lines.SelectedIndex = row;
            Dispatcher.UIThread.RunJobs();
        }

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

        return pixels;
    }

    private static int Differences(uint[] left, uint[] right) =>
        left.Length != right.Length
            ? left.Length
            : left.Where((pixel, index) => pixel != right[index]).Count();

    [AvaloniaFact]
    public async Task Secim_DEGISIKLIK_satirinda_gorunur()
    {
        // 🔴 Asıl risk burada: eklenen/silinen satırların kendi arka planı var. Seçim vurgusu
        // onların altında kalırsa, kullanıcının en çok seçtiği satırlarda seçim görünmez.
        // Satır 2 = "iki eski" (silinen), satır 3 = "iki yeni" (eklenen).
        uint[] plain = await RenderAsync(selectedRow: null);
        uint[] selected = await RenderAsync(selectedRow: 3);

        Differences(plain, selected).ShouldBeGreaterThan(200);
    }

    [AvaloniaFact]
    public async Task Secim_BAGLAM_satirinda_da_gorunur()
    {
        uint[] plain = await RenderAsync(selectedRow: null);
        uint[] selected = await RenderAsync(selectedRow: 1);

        Differences(plain, selected).ShouldBeGreaterThan(200);
    }

    [AvaloniaFact]
    public async Task Iki_farkli_satirin_secimi_AYNI_gorunmez()
    {
        // Karşı kanıt: yukarıdaki farklar gerçekten seçimden geliyor. Aynı çıkarsa test
        // "bir şey değişti" der ama ne değiştiğini bilmez.
        uint[] first = await RenderAsync(selectedRow: 2);
        uint[] second = await RenderAsync(selectedRow: 3);

        Differences(first, second).ShouldBeGreaterThan(200);
    }
}
