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

        // Yazma kuyruğu (P05-T01) SINGLETON olmalı: her istekte yenisi üretilseydi kilit
        // hiçbir şeyi korumaz, serileştirme sessizce devre dışı kalırdı.
        GitExt.Core.Git.IGitWriteQueue queue = provider.GetRequiredService<GitExt.Core.Git.IGitWriteQueue>();
        queue.ShouldBeSameAs(provider.GetRequiredService<GitExt.Core.Git.IGitWriteQueue>());

        // Yazma yolu (P05-T03) çözümlenebilmeli; eksik kayıt derlemeyi kırmaz, uygulama
        // yalnızca ilk stage denemesinde çökerdi.
        provider.GetRequiredService<GitExt.Core.Git.IGitWriter>().ShouldNotBeNull();
        provider.GetRequiredService<GitExt.Core.IStagingWriter>().ShouldNotBeNull();
        provider.GetRequiredService<GitExt.Core.ICommitWriter>().ShouldNotBeNull();
        provider.GetRequiredService<GitExt.Core.IWorkingTreeWriter>().ShouldNotBeNull();

        // Commit ekranı (P05-T09) MainWindowViewModel üzerinden kuruluyor; bağımlılıkları
        // kayıtlı değilse fabrika sessizce `null` döner ve menü öğesi hiçbir şey yapmaz.
        provider.GetRequiredService<GitExt.UI.ViewModels.MainWindowViewModel>()
            .CreateWorkingTree()
            .ShouldNotBeNull();
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
