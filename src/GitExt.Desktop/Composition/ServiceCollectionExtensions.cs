using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Storage;
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
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitExtServices(this IServiceCollection services)
    {
        // Eski kod sayfaları (windows-1254, Shift-JIS…) kullanılabilir olsun; kullanıcının
        // dosyaları UTF-8 olmayabilir (P04-T07).
        TextEncodings.EnsureRegistered();

        services.AddLogging();

        // git çalıştırılabiliri bir kez bulunur ve doğrulanır (ADR-0002).
        // Örneğin var olması, git'in kurulu ve sürümünün yeterli olduğunun kanıtıdır.
        services.AddSingleton(_ => GitExecutable.LocateAsync().GetAwaiter().GetResult());

        // "Komutu göster" panelinin (Faz 08) besleneceği günlük.
        services.AddSingleton<IGitCommandLog>(_ => new InMemoryGitCommandLog());

        services.AddSingleton<IGitProcessRunner>(provider => new GitProcessRunner(
            provider.GetRequiredService<GitExecutable>(),
            provider.GetRequiredService<IGitCommandLog>()));

        // Yazma işlemleri depo başına serileştirilir (P05-T01). Singleton olması ŞART:
        // her istekte yeni kuyruk üretilseydi kilit hiçbir şeyi korumazdı.
        services.AddSingleton<IGitWriteQueue, GitWriteQueue>();

        // Yazma yolunun tek girişi: serileştirme + kilit yeniden denemesi burada birleşiyor
        // (P05-T03). Yazan her servis bunu kullanmalı, runner'ı doğrudan çağırmamalı.
        services.AddSingleton<IGitWriter, GitWriter>();
        services.AddSingleton<IStagingWriter, StagingWriter>();
        services.AddSingleton<ICommitWriter, CommitWriter>();
        services.AddSingleton<IWorkingTreeWriter, WorkingTreeWriter>();

        services.AddSingleton<IRepositoryLocator, RepositoryLocator>();
        services.AddSingleton<ICommitLogReader, CommitLogReader>();
        services.AddSingleton<IRefReader, RefReader>();
        services.AddSingleton<ICommitSignatureReader, CommitSignatureReader>();
        services.AddSingleton<IDiffReader, DiffReader>();
        services.AddSingleton<IRecentRepositoryStore>(_ => new RecentRepositoryStore());
        services.AddSingleton<IStatusReader, StatusReader>();
        services.AddSingleton<IObjectReader, ObjectReader>();

        // Commit mesajı yardımcıları (P05-T13). Taslak deposu depo başına git dizinini
        // önbelleğe aldığı için singleton; her istekte yenisi üretmek her taslak kaydında
        // fazladan bir `git rev-parse` demek olurdu.
        services.AddSingleton<IGitConfigReader, GitConfigReader>();
        services.AddSingleton<ICommitMessageReader, CommitMessageReader>();
        services.AddSingleton<ICommitMessageStore, CommitMessageStore>();

        services.AddSingleton<CommitListViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
