using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P05-T07 — verifies that the hook output really is <b>drawn</b>.
/// </summary>
/// <remarks>
/// <para>
/// This test stands apart from the ViewModel tests because the same bug happened twice in this
/// project: <c>IsVisible="{Binding …Count}"</c> silently hid the element (P03-T17), and hunk headers
/// produced without segments came out as an <b>empty grey strip</b> on screen (P04-T09). In both cases
/// the ViewModel was right and the screen was blank.
/// </para>
/// <para>
/// The rule (P04-T09): the region examined must be the verified element's <b>own rectangle</b> —
/// otherwise a neighbouring element's pixels let the test pass on a broken version too.
/// </para>
/// </remarks>
public class GitOutputViewRenderTests
{
    private static uint[] Render(GitOutputViewModel viewModel, out PixelSize size, out Rect? bounds)
    {
        GitOutputView view = new() { DataContext = viewModel };

        Window window = new()
        {
            Width = 640,
            Height = 320,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.White, Child = view },
        };

        window.Show();

        SelectableTextBlock output = view.GetControl<SelectableTextBlock>("OutputText");

        // The region has to be the verified element's OWN rectangle (the lesson of P04-T09); it is
        // converted to window coordinates. When the element is not drawn at all, `TranslatePoint`
        // returns null — which is itself the proof of the "no output" case.
        bounds = output.TranslatePoint(default, window) is { } origin
            ? new Rect(origin, output.Bounds.Size)
            : null;

        using Bitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Kare alınamadı.");

        size = frame.PixelSize;
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

    /// <summary>
    /// The number of dark pixels within the given rectangle that could count as text.
    /// </summary>
    private static int DarkPixelsIn(uint[] pixels, PixelSize size, Rect region)
    {
        int left = Math.Max(0, (int)region.X);
        int top = Math.Max(0, (int)region.Y);
        int right = Math.Min(size.Width, (int)region.Right);
        int bottom = Math.Min(size.Height, (int)region.Bottom);

        int count = 0;

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                uint pixel = pixels[(y * size.Width) + x];

                // An RGBA-ordered frame; as a little-endian uint it is 0xAABBGGRR (measured in P03-T10).
                int r = (int)(pixel & 0xFF);
                int g = (int)((pixel >> 8) & 0xFF);
                int b = (int)((pixel >> 16) & 0xFF);

                if (r < 120 && g < 120 && b < 120)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static GitOutputViewModel Failure(string standardError) =>
        GitOutputViewModel.ForFailure(new GitException(
            GitFailureKind.Unknown,
            "Git komutu başarısız oldu.",
            "git commit -F -",
            exitCode: 1,
            standardError));

    [AvaloniaFact]
    public void Hook_ciktisi_metin_alaninda_gercekten_cizilir()
    {
        uint[] pixels = Render(
            Failure("pre-commit HATASI: src/a.cs:12 bicim bozuk"),
            out PixelSize size,
            out Rect? bounds);

        bounds.ShouldNotBeNull();
        DarkPixelsIn(pixels, size, bounds.Value).ShouldBeGreaterThan(50);
    }

    [AvaloniaFact]
    public void Koyu_piksel_sayisi_ciktinin_KENDISINDEN_geliyor()
    {
        // The counter-evidence: had the pixels come from the frame or from neighbouring elements, the
        // two renders would give the same count. Text pixels increasing with the line count shows that
        // what is being counted really is the output text.
        uint[] few = Render(Failure("tek satir"), out PixelSize size, out Rect? bounds);
        int fewCount = DarkPixelsIn(few, size, bounds.ShouldNotBeNull());

        string many = string.Join('\n', Enumerable.Repeat("tek satir", 8));
        uint[] manyPixels = Render(Failure(many), out size, out bounds);
        int manyCount = DarkPixelsIn(manyPixels, size, bounds.ShouldNotBeNull());

        manyCount.ShouldBeGreaterThan(fewCount * 4);
    }

    [AvaloniaFact]
    public void Cikti_yoksa_metin_alani_HIC_CIZILMEZ()
    {
        // Showing an empty frame would give the impression "there is something but it is unreadable".
        Render(Failure(string.Empty), out PixelSize _, out Rect? bounds);

        bounds.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Kaydedilen_mesaj_bolumu_cizilir()
    {
        // The lesson of P04-T09: when a section of the template draws no data at all, the ViewModel
        // tests carry on passing. The message section is verified separately.
        GitOutputViewModel viewModel = GitOutputViewModel.ForCommit(new CommitResult
        {
            Id = CommitId.Parse(new string('a', 40)),
            Message = "konu\n\nChange-Id: I0001",
            RequestedMessage = "konu",
            Output = string.Empty,
        });

        GitOutputView view = new() { DataContext = viewModel };

        Window window = new()
        {
            Width = 640,
            Height = 320,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.White, Child = view },
        };

        window.Show();

        SelectableTextBlock message = view.GetControl<SelectableTextBlock>("FinalMessageText");

        message.IsVisible.ShouldBeTrue();
        message.Bounds.Height.ShouldBeGreaterThan(0);
        message.Text.ShouldNotBeNull().ShouldContain("Change-Id: I0001");

        ((Border)window.Content!).Child = null;
        window.Close();
    }
}
