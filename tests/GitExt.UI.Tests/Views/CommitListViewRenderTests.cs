using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P03-T12 — Ref rozetlerinin <b>görsel olarak</b> ayrıştığını doğrular.
/// </summary>
/// <remarks>
/// <para>
/// Bu testin ViewModel testlerinden ayrı olarak var olma sebebi şu: rozet türü doğru
/// üretilse bile XAML tarafındaki <c>Classes.local="{Binding IsLocalBranch}"</c> bağlaması
/// sessizce çalışmayabilir — o durumda tüm rozetler aynı görünür ve hiçbir ViewModel testi
/// bunu yakalamaz. Tek dürüst doğrulama piksele bakmaktır.
/// </para>
/// <para>
/// Renk sabitleri <c>CommitListView.axaml</c>'deki değerlerin kopyasıdır. Orası
/// değişirse burası da değişmeli; testin amacı belli bir tonu korumak değil,
/// <b>türlerin birbirinden ayrıştığını</b> korumak.
/// </para>
/// </remarks>
public class CommitListViewRenderTests
{
    private const uint LocalBranchBackground = 0xDCEBF9;
    private const uint RemoteBranchBackground = 0xEDE7F6;
    private const uint TagBackground = 0xFCF0D0;

    /// <summary>
    /// Yakalanan kare RGBA sıralı; little-endian <c>uint</c> olarak <c>0xAABBGGRR</c> okunur
    /// (ölçüldü — bkz. <c>CommitGraphCellTests</c>). Bu dönüşüm olmadan kırmızı ve mavi yer değiştirir.
    /// </summary>
    private static uint ToPixel(uint rgb) =>
        0xFF000000u | ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    private static async Task<uint[]> RenderAsync(RepositoryRefs refs, int commitCount = 3)
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(commitCount)),
            new FakeRefReader(refs),
            new FakeCommitSignatureReader());

        await viewModel.OpenAsync("/tmp/depo");

        Window window = new()
        {
            Width = 900,
            Height = 120,
            WindowDecorations = WindowDecorations.None,
            Content = new Border
            {
                Background = Brushes.White,
                Child = new CommitListView { DataContext = viewModel },
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

        ((Border)window.Content!).Child = null;
        window.Close();

        return pixels;
    }

    [AvaloniaFact]
    public async Task Yerel_dal_uzak_dal_ve_tag_farkli_renklerde_cizilir()
    {
        uint[] pixels = await RenderAsync(FakeGitData.Refs(
            localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(3), isCurrent: true)],
            remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(2))],
            tags: [FakeGitData.Tag("v0.1.0", FakeGitData.Sha(1))]));

        pixels.Count(p => p == ToPixel(LocalBranchBackground)).ShouldBeGreaterThan(20);
        pixels.Count(p => p == ToPixel(RemoteBranchBackground)).ShouldBeGreaterThan(20);
        pixels.Count(p => p == ToPixel(TagBackground)).ShouldBeGreaterThan(20);
    }

    [AvaloniaFact]
    public async Task Rozet_yoksa_rozet_rengi_hic_cizilmez()
    {
        // Karşı kanıt: yukarıdaki testin renkleri gerçekten rozetlerden geldiğini,
        // sayfadaki başka bir öğeden gelmediğini gösterir.
        uint[] pixels = await RenderAsync(FakeGitData.NoRefs());

        pixels.ShouldNotContain(ToPixel(LocalBranchBackground));
        pixels.ShouldNotContain(ToPixel(RemoteBranchBackground));
        pixels.ShouldNotContain(ToPixel(TagBackground));
    }
}
