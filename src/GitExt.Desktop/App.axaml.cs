using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GitExt.Desktop;

public partial class App : Application
{
    /// <summary>
    /// <c>1</c> ise pencere açıldığında soğuk başlatma süresini stderr'e yazar.
    /// Faz 09'daki teşhis modunun (P09-T03) çekirdeği.
    /// </summary>
    private const string StartupTraceVariable = "GITEXT_STARTUP_TRACE";

    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Süreç başlangıcından pencerenin açılmasına kadar geçen süreyi raporlar.
    /// Süreç başlangıcını temel aldığı için .NET çalışma zamanı başlatma maliyetini de içerir.
    /// </summary>
    private static void ReportStartupTime(object? sender, EventArgs e)
    {
        using System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
        TimeSpan elapsed = DateTime.Now - current.StartTime;

        Console.Error.WriteLine(
            $"[gitext-core] soğuk başlatma: {elapsed.TotalMilliseconds:F0} ms (süreç başlangıcından pencere açılışına)");
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = _services.GetRequiredService<MainWindow>();
            MainWindowViewModel viewModel = _services.GetRequiredService<MainWindowViewModel>();
            window.DataContext = viewModel;

            // Komut satırında bir yol verildiyse o açılır ve açılamazsa hata gösterilir.
            // Verilmediyse çalışma dizini SESSİZCE denenir; depo değilse karşılama ekranı
            // açılır (masaüstünden başlatıldığında çalışma dizini rastgele bir yerdir).
            string? path = desktop.Args?.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

            _ = viewModel.StartAsync(path);

            if (Environment.GetEnvironmentVariable(StartupTraceVariable) == "1")
            {
                window.Opened += ReportStartupTime;
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
