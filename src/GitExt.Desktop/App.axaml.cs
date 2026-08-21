using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitExt.UI.Localization;
using GitExt.UI.Settings;
using GitExt.UI.Themes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GitExt.Desktop;

public partial class App : Application
{
    /// <summary>
    /// If <c>1</c>, writes the cold start time to stderr when the window opens.
    /// The core of the Phase 09 diagnostics mode (P09-T03).
    /// </summary>
    private const string StartupTraceVariable = "GITEXT_STARTUP_TRACE";

    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Reports the time elapsed from process start until the window opens.
    /// Since it is based on process start it also includes the .NET runtime startup cost.
    /// </summary>
    private static void ReportStartupTime(object? sender, EventArgs e)
    {
        using System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
        TimeSpan elapsed = DateTime.Now - current.StartTime;

        Console.Error.WriteLine(
            $"[gitext-core] cold start: {elapsed.TotalMilliseconds:F0} ms (from process start to window shown)");
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // The saved appearance settings are applied BEFORE the window opens (P08-T07…T10):
        // had it been left until afterwards, the application would first open with the default theme
        // and font and would visibly jump.
        _services.GetRequiredService<IAppearanceService>().ApplyStored();

        // Language is set here for the same reason (P11-T07): the texts must already be in the right
        // language while the window is built, else the app would open in English and jump to Turkish.
        _services.GetRequiredService<ITranslator>().ApplyStored();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = _services.GetRequiredService<MainWindow>();
            MainWindowViewModel viewModel = _services.GetRequiredService<MainWindowViewModel>();
            window.DataContext = viewModel;

            // If a path was given on the command line it is opened, and an error is shown if it fails.
            // If none was given the working directory is tried SILENTLY; if it is not a repository the
            // welcome screen opens (when started from the desktop the working directory is arbitrary).
            string? path = desktop.Args?.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

            _ = viewModel.StartAsync(path);

            // The version check (P13-T01) runs AFTER the window is on screen and is not awaited:
            // a network request must never sit between the user and their repository. It answers
            // at most once a week, and it stays silent when there is nothing to say.
            window.Opened += (_, _) => _ = viewModel.CheckForUpdatesAsync(userRequested: false);

            if (Environment.GetEnvironmentVariable(StartupTraceVariable) == "1")
            {
                window.Opened += ReportStartupTime;
            }

            // Must not exit before a pending settings save is written to disk (P08-T14). The save is
            // debounced: a user who resizes the window and closes it immediately WOULD LOSE their
            // layout, because they exit before the delay elapses.
            desktop.ShutdownRequested += (_, _) =>
                _services.GetRequiredService<ISettingsStore>().FlushAsync().GetAwaiter().GetResult();

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
