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
/// P05-T07 — hook çıktısının gerçekten <b>çizildiğini</b> doğrular.
/// </summary>
/// <remarks>
/// <para>
/// Bu test ViewModel testlerinden ayrı duruyor çünkü bu projede aynı hata iki kez yaşandı:
/// <c>IsVisible="{Binding …Count}"</c> öğeyi sessizce gizledi (P03-T17) ve parçasız üretilen
/// hunk başlıkları ekranda <b>boş gri şerit</b> oldu (P04-T09). İkisinde de ViewModel
/// doğruydu, ekran boştu.
/// </para>
/// <para>
/// Kural (P04-T09): bakılan bölge, doğrulanan öğenin <b>kendi dikdörtgeni</b> olmalı —
/// aksi halde komşu bir öğenin pikselleri testi hatalı sürümde de geçirir.
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

        // Bölge, doğrulanan öğenin KENDİ dikdörtgeni olmalı (P04-T09'un dersi); pencere
        // koordinatına çevriliyor. Öğe hiç çizilmiyorsa `TranslatePoint` null döner —
        // "çıktı yok" durumunun kanıtı da bu.
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
    /// Verilen dikdörtgen içinde metin sayılabilecek koyu piksel sayısı.
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

                // RGBA sıralı kare; little-endian uint olarak 0xAABBGGRR (P03-T10'da ölçüldü).
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
        // Karşı kanıt: pikseller çerçeveden ya da komşu öğelerden gelseydi iki render aynı
        // sayıyı verirdi. Satır sayısı arttıkça metin pikselinin artması, sayılan şeyin
        // gerçekten çıktı metni olduğunu gösterir.
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
        // Boş bir çerçeve göstermek "bir şey var ama okunamıyor" izlenimi verirdi.
        Render(Failure(string.Empty), out PixelSize _, out Rect? bounds);

        bounds.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Kaydedilen_mesaj_bolumu_cizilir()
    {
        // P04-T09'un dersi: şablonun bir bölümü veriyi hiç çizmezse ViewModel testleri
        // geçmeye devam eder. Mesaj bölümü ayrıca doğrulanıyor.
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
