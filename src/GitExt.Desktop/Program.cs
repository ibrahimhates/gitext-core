using Avalonia;
using GitExt.Desktop.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace GitExt.Desktop;

internal static class Program
{
    /// <summary>
    /// Environment variable selecting the window backend on Linux: <c>x11</c> | <c>wayland</c> | <c>auto</c>.
    /// </summary>
    private const string BackendEnvironmentVariable = "GITEXT_BACKEND";

    // Before Avalonia is initialized, no Avalonia API and no code that depends on a
    // SynchronizationContext may be executed.
    [STAThread]
    public static int Main(string[] args)
    {
        // Version query (P10-T01). Right at the top: packaging scripts and package managers call
        // this, and it must be answered without doing any heavy startup work.
        if (args.Contains(VersionInfo.Flag, StringComparer.Ordinal))
        {
            return VersionInfo.Run();
        }

        // Diagnostics mode: runs the core layer against a repository without opening the UI.
        // Avalonia is never started, so it does not require a desktop session.
        if (args.Contains(HeadlessDiagnostics.Flag, StringComparer.Ordinal))
        {
            return HeadlessDiagnostics.RunAsync(args, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        // Measurement mode: runs the commit graph pipeline against a repository and reports time and
        // memory (P03-T18). No UI is opened.
        if (args.Contains(GraphBenchmark.Flag, StringComparer.Ordinal))
        {
            return GraphBenchmark.RunAsync(args, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        // Startup measurement: the time until the window draws its first frame (P09-T04).
        // It cannot be measured from the outside — only the application itself can see the gap
        // between process start and the first pixel.
        if (args.Contains(StartupBenchmark.Flag, StringComparer.Ordinal))
        {
            return StartupBenchmark.Run(args, BuildAvaloniaApp());
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // The Avalonia visual designer uses this method too; its signature must not be changed.
    public static AppBuilder BuildAvaloniaApp()
    {
        // Composition root — service registrations go ONLY here (ADR-0004).
        ServiceProvider services = new ServiceCollection()
            .AddGitExtServices()
            .BuildServiceProvider();

        AppBuilder builder = AppBuilder.Configure(() => new App(services));

        return ConfigurePlatform(builder)
            .WithInterFont()
            .LogToTrace();
    }

    /// <summary>
    /// Configures the window backend.
    /// </summary>
    /// <remarks>
    /// In Avalonia 12.1 the Linux default is X11; the native Wayland backend is opt-in
    /// (the <c>Avalonia.Wayland</c> package + <c>UseWayland()</c>). Since X11 runs over XWayland in
    /// Wayland sessions, it is the safe default everywhere.
    /// <para>
    /// Native Wayland offers lower latency and better HiDPI, but as it has only just stabilized with
    /// 12.1 it requires an explicit choice for now. Whether it will be made the default is to be
    /// decided in Phase 08/09 with real usage data.
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

        // IMPORTANT (ADR-0001): UsePlatformDetect() configures the render system and text shaping
        // itself. When the backend is chosen EXPLICITLY these do not come automatically; UseSkia() and
        // UseHarfBuzz() must be called by hand. Without UseSkia() the application crashes at startup
        // with "No rendering system configured"; without UseHarfBuzz() text is broken.
        return backend switch
        {
            "wayland" => builder.UseWayland().UseSkia().UseHarfBuzz(),
            "x11" => builder.UseX11().UseSkia().UseHarfBuzz(),
            _ => builder.UsePlatformDetect(),
        };
    }
}
