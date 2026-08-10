using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace GitExt.Desktop;

/// <summary>
/// Soğuk başlatma süresini ölçer (P09-T04).
/// </summary>
/// <remarks>
/// <para>
/// Kullanım: <c>gitext-core --bench-startup [depo-yolu]</c>
/// </para>
/// <para>
/// Performans bütçesinin ilk maddesi "pencere görünene kadar &lt; 1.5 sn" diyor.
/// Bunu dışarıdan bir kabuk betiğiyle ölçmek mümkün değil: sürecin başlaması ile
/// <b>pencerenin çizilmesi</b> arasındaki farkı ancak uygulamanın kendisi görebilir.
/// </para>
/// <para>
/// ⚠️ Ölçülen an, pencerenin <b>ilk kez oluşturulduğu</b> an değil, kompozitörün ilk
/// kareyi verdiği an: <see cref="TopLevel.RequestAnimationFrame"/> tam olarak o noktada
/// tetikleniyor. <c>Opened</c> olayını kullanmak, pencere hazır ama henüz hiçbir piksel
/// çizilmemişken "açıldı" derdi — kullanıcının gördüğü şey bu değil.
/// </para>
/// </remarks>
internal static class StartupBenchmark
{
    internal const string Flag = "--bench-startup";

    /// <summary>Süreç başlangıcı — <c>Main</c>'in ilk satırında damgalanıyor.</summary>
    internal static long ProcessStartTimestamp { get; set; }

    internal static int Run(string[] args, AppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Süreç gerçekten ne zaman başladı? İşletim sisteminin verdiği zaman, .NET
        // çalışma zamanının kendi yüklenme maliyetini de içeriyor — kullanıcı onu da
        // bekliyor, dolayısıyla ölçüme dahil.
        DateTime osStart;

        try
        {
            using Process current = Process.GetCurrentProcess();
            osStart = current.StartTime;
        }
        catch (PlatformNotSupportedException)
        {
            osStart = DateTime.Now;
        }

        double? firstFrameMs = null;

        builder.AfterSetup(_ =>
        {
            if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return;
            }

            desktop.Startup += (_, _) =>
            {
                // Pencere Startup sırasında henüz kurulmamış olabilir; ilk kareyi
                // istemek için Dispatcher'a bırakılıyor.
                Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.MainWindow is not { } window)
                    {
                        return;
                    }

                    window.RequestAnimationFrame(_ =>
                    {
                        firstFrameMs ??= (DateTime.Now - osStart).TotalMilliseconds;

                        // Ölçüm alındı; oturumu kapat.
                        desktop.Shutdown();
                    });
                });
            };
        });

        int exitCode = builder.StartWithClassicDesktopLifetime(args);

        if (firstFrameMs is { } elapsed)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"soğuk başlatma : {elapsed:0} ms (süreç başlangıcından ilk kareye)"));

            using Process current = Process.GetCurrentProcess();
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"bellek (RSS)   : {current.WorkingSet64 / (1024 * 1024)} MB"));
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"bellek (GC)    : {GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024)} MB"));
        }
        else
        {
            Console.Error.WriteLine("Ölçüm alınamadı: ilk kare hiç çizilmedi.");
            return 1;
        }

        return exitCode;
    }
}
