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
/// P03-T12 — Verifies that the ref badges are <b>visually</b> distinct.
/// </summary>
/// <remarks>
/// <para>
/// The reason this test exists separately from the ViewModel tests is this: even when the badge kind is
/// produced correctly, the <c>Classes.local="{Binding IsLocalBranch}"</c> binding on the XAML side can
/// silently fail — in which case every badge looks the same and no ViewModel test catches it. The only
/// honest verification is to look at the pixels.
/// </para>
/// <para>
/// The colour constants are copies of the values in <c>CommitListView.axaml</c>. If that changes, this
/// has to change too; the test's aim is not to preserve a particular shade but to preserve that <b>the
/// kinds are distinct from one another</b>.
/// </para>
/// </remarks>
public class CommitListViewRenderTests
{
    private const uint LocalBranchBackground = 0xDCEBF9;
    private const uint RemoteBranchBackground = 0xEDE7F6;
    private const uint TagBackground = 0xFCF0D0;

    /// <summary>
    /// The captured frame is RGBA-ordered; read as a little-endian <c>uint</c> it is <c>0xAABBGGRR</c>
    /// (measured — see <c>CommitGraphCellTests</c>). Without this conversion red and blue swap places.
    /// </summary>
    private static uint ToPixel(uint rgb) =>
        0xFF000000u | ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    private static async Task<uint[]> RenderAsync(RepositoryRefs refs, int commitCount = 3)
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(commitCount)),
            new FakeRefReader(refs),
            new FakeCommitSignatureReader(),new FakeDiffReader());

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
        // The counter-evidence: it shows that the colours in the test above really do come from the
        // badges and not from some other element on the page.
        uint[] pixels = await RenderAsync(FakeGitData.NoRefs());

        pixels.ShouldNotContain(ToPixel(LocalBranchBackground));
        pixels.ShouldNotContain(ToPixel(RemoteBranchBackground));
        pixels.ShouldNotContain(ToPixel(TagBackground));
    }
}
