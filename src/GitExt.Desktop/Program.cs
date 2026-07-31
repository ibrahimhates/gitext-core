using Avalonia;
using GitExt.Desktop.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace GitExt.Desktop;

internal static class Program
{
    /// <summary>
    /// Linux'ta pencere backend'ini seçen ortam değişkeni: <c>x11</c> | <c>wayland</c> | <c>auto</c>.
    /// </summary>
    private const string BackendEnvironmentVariable = "GITEXT_BACKEND";

    // Avalonia başlatılmadan önce hiçbir Avalonia API'si veya SynchronizationContext'e
    // bağımlı kod çalıştırılmamalıdır.
    [STAThread]
    public static int Main(string[] args)
    {
        // Teşhis modu: arayüz açmadan çekirdek katmanı bir depoya karşı çalıştırır.
        // Avalonia hiç başlatılmaz, bu yüzden masaüstü oturumu gerektirmez.
        if (args.Contains(HeadlessDiagnostics.Flag, StringComparer.Ordinal))
        {
            return HeadlessDiagnostics.RunAsync(args, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        // Ölçüm modu: commit grafiği hattını bir depoya karşı çalıştırıp süre ve bellek
        // raporlar (P03-T18). Arayüz açılmaz.
        if (args.Contains(GraphBenchmark.Flag, StringComparer.Ordinal))
        {
            return GraphBenchmark.RunAsync(args, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia görsel tasarımcısı da bu metodu kullanır; imzası değiştirilmemeli.
    public static AppBuilder BuildAvaloniaApp()
    {
        // Composition root — servis kayıtları YALNIZCA burada (ADR-0004).
        ServiceProvider services = new ServiceCollection()
            .AddGitExtServices()
            .BuildServiceProvider();

        AppBuilder builder = AppBuilder.Configure(() => new App(services));

        return ConfigurePlatform(builder)
            .WithInterFont()
            .LogToTrace();
    }

    /// <summary>
    /// Pencere backend'ini yapılandırır.
    /// </summary>
    /// <remarks>
    /// Avalonia 12.1'de Linux varsayılanı X11'dir; native Wayland backend'i opt-in'dir
    /// (<c>Avalonia.Wayland</c> paketi + <c>UseWayland()</c>). X11, Wayland oturumlarında
    /// XWayland üzerinden çalıştığı için her yerde güvenli varsayılandır.
    /// <para>
    /// Native Wayland daha düşük gecikme ve daha iyi HiDPI sunar, ancak 12.1 ile yeni
    /// kararlılaştığı için şimdilik açık tercih gerektirir. Varsayılan yapılıp yapılmayacağı
    /// Faz 08/09'da gerçek kullanım verisiyle karara bağlanacak.
    /// </para>
    /// </remarks>
    private static AppBuilder ConfigurePlatform(AppBuilder builder)
    {
        if (!OperatingSystem.IsLinux())
        {
            return builder.UsePlatformDetect();
        }

        string backend = Environment.GetEnvironmentVariable(BackendEnvironmentVariable)?.Trim().ToLowerInvariant()
                         ?? "auto";

        // ÖNEMLİ (ADR-0001): UsePlatformDetect() render sistemini ve text shaping'i kendisi
        // yapılandırır. Backend AÇIKÇA seçildiğinde bunlar otomatik gelmez; UseSkia() ve
        // UseHarfBuzz() elle çağrılmalıdır. UseSkia() eksikse uygulama
        // "No rendering system configured" ile açılışta çöker; UseHarfBuzz() eksikse metin bozulur.
        return backend switch
        {
            "wayland" => builder.UseWayland().UseSkia().UseHarfBuzz(),
            "x11" => builder.UseX11().UseSkia().UseHarfBuzz(),
            _ => builder.UsePlatformDetect(),
        };
    }
}
