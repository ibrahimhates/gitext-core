using GitExt.UI.ViewModels;
using GitExt.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GitExt.Desktop.Composition;

/// <summary>
/// Uygulamanın servis kayıtları. Composition root'un tek yeri (ADR-0004).
/// </summary>
/// <remarks>
/// Service Locator deseni yasaktır: hiçbir sınıf <c>IServiceProvider</c>'ı enjekte alıp
/// içinden servis çözümlemez. Bağımlılıklar constructor'dan gelir.
/// <para>
/// Faz 06'da çoklu repo desteği geldiğinde repo'ya bağlı servisler <c>Scoped</c> olarak
/// kaydedilecek; repo kapandığında scope'la birlikte temizlenecekler.
/// </para>
/// </remarks>
internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitExtServices(this IServiceCollection services)
    {
        services.AddLogging();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
