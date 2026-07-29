using Avalonia.Headless.XUnit;
using GitExt.Desktop.Composition;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GitExt.UI.Tests;

/// <summary>
/// Composition root'un gerçekten kurulabildiğini doğrular.
/// </summary>
/// <remarks>
/// <para>
/// Eksik bir DI kaydı <b>derlemeyi kırmaz</b>: hata ancak uygulama açılırken ortaya çıkar.
/// Yani tüm test takımı yeşilken açılmayan bir uygulama yayınlanabilir. Bu test o boşluğu
/// kapatıyor — composition root her değiştiğinde otomatik kontrol edilir.
/// </para>
/// <para>
/// <c>ValidateOnBuild</c> ile tüm kayıtlar tek seferde denetleniyor; ayrıca ana pencere
/// zinciri açıkça çözümleniyor çünkü asıl kırılgan yer orası.
/// </para>
/// </remarks>
public class CompositionTests
{
    [Fact]
    public void Tum_servisler_cozumlenebilir()
    {
        ServiceCollection services = new();
        services.AddGitExtServices();

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        provider.GetRequiredService<MainWindowViewModel>().ShouldNotBeNull();
        provider.GetRequiredService<CommitListViewModel>().ShouldNotBeNull();
    }

    // Pencere oluşturmak Avalonia platformu gerektiriyor; bu yüzden [AvaloniaFact].
    [AvaloniaFact]
    public void Ana_pencere_cozumlenebilir()
    {
        ServiceCollection services = new();
        services.AddGitExtServices();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<MainWindow>().ShouldNotBeNull();
    }
}
