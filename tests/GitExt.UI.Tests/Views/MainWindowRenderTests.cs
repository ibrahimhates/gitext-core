using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P01-T16 — Verifies that the window really is drawn and that text is rendered.
/// </summary>
/// <remarks>
/// This test is the answer to ADR-0001's text shaping question (is <c>UseHarfBuzz()</c> needed in
/// Avalonia 12?): if there are non-blank pixels in the window, text is being drawn.
/// <para>
/// Because it is headless it runs in CI too — it needs no desktop session.
/// </para>
/// </remarks>
public class MainWindowRenderTests
{
    [AvaloniaFact]
    public void Pencere_cizilir_ve_metin_render_edilir()
    {
        MainWindow window = new()
        {
            DataContext = new MainWindowViewModel(new CommitListViewModel(
                new Fakes.FakeRepositoryLocator(),
                new Fakes.FakeCommitLogReader(),
                new Fakes.FakeRefReader(),
                new Fakes.FakeCommitSignatureReader(),new Fakes.FakeDiffReader()),
                new Fakes.FakeRecentRepositoryStore()),
        };
        window.Show();

        using Bitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Render edilmiş kare alınamadı.");

        frame.PixelSize.Width.ShouldBeGreaterThan(0);
        frame.PixelSize.Height.ShouldBeGreaterThan(0);

        // A debugging aid: write the render result to disk. In CI this is the quickest way to see why
        // a test failed.
        string artifact = Path.Combine(AppContext.BaseDirectory, "render-mainwindow.png");
        frame.Save(artifact, new PngBitmapEncoderOptions());

        // The number of pixels that differ from the background: if no text was drawn this comes out
        // close to zero.
        int distinctPixels = CountNonBackgroundPixels(frame);

        distinctPixels.ShouldBeGreaterThan(
            500,
            "pencerede metin render edilmiş olmalı (ADR-0001 text shaping doğrulaması)");
    }

    private static int CountNonBackgroundPixels(Bitmap frame)
    {
        PixelSize size = frame.PixelSize;
        int stride = size.Width * 4;
        byte[] buffer = new byte[stride * size.Height];

        System.Runtime.InteropServices.GCHandle handle =
            System.Runtime.InteropServices.GCHandle.Alloc(
                buffer, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            frame.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        // Take the top-left corner as the background reference.
        (byte B, byte G, byte R) background = (buffer[0], buffer[1], buffer[2]);

        int count = 0;
        for (int i = 0; i + 3 < buffer.Length; i += 4)
        {
            if (buffer[i] != background.B || buffer[i + 1] != background.G || buffer[i + 2] != background.R)
            {
                count++;
            }
        }

        return count;
    }
}
