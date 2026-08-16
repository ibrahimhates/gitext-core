using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace GitExt.Desktop;

/// <summary>
/// Measures the cold start time (P09-T04).
/// </summary>
/// <remarks>
/// <para>
/// Usage: <c>gitext-core --bench-startup [repository-path]</c>
/// </para>
/// <para>
/// The first item of the performance budget says "&lt; 1.5 s until the window is visible".
/// Measuring this from an external shell script is impossible: only the application itself can see
/// the gap between the process starting and the <b>window being drawn</b>.
/// </para>
/// <para>
/// ⚠️ The moment measured is not the moment the window is <b>first created</b>, but the moment the
/// compositor delivers the first frame: <see cref="TopLevel.RequestAnimationFrame"/> fires exactly
/// at that point. Using the <c>Opened</c> event would say "opened" while the window is ready but not
/// a single pixel has been drawn yet — that is not what the user sees.
/// </para>
/// </remarks>
internal static class StartupBenchmark
{
    internal const string Flag = "--bench-startup";

    /// <summary>Process start — stamped on the first line of <c>Main</c>.</summary>
    internal static long ProcessStartTimestamp { get; set; }

    internal static int Run(string[] args, AppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // When did the process really start? The time given by the operating system also includes
        // the .NET runtime's own load cost — the user waits for that too, so it is part of the
        // measurement.
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
                // The window may not be set up yet during Startup; requesting the first frame is
                // deferred to the Dispatcher.
                Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.MainWindow is not { } window)
                    {
                        return;
                    }

                    window.RequestAnimationFrame(_ =>
                    {
                        firstFrameMs ??= (DateTime.Now - osStart).TotalMilliseconds;

                        // Measurement taken; close the session.
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
                $"cold start : {elapsed:0} ms (from process start to first frame)"));

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
            Console.Error.WriteLine("No measurement: the first frame was never drawn.");
            return 1;
        }

        return exitCode;
    }
}
