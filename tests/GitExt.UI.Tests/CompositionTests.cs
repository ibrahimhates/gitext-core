using Avalonia.Headless.XUnit;
using GitExt.Desktop.Composition;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GitExt.UI.Tests;

/// <summary>
/// Verifies that the composition root can actually be built.
/// </summary>
/// <remarks>
/// <para>
/// A missing DI registration <b>does not break the build</b>: the error only surfaces while the app
/// is starting. So an application that never opens could be released with the whole test suite green.
/// This test closes that gap — it is checked automatically every time the composition root changes.
/// </para>
/// <para>
/// <c>ValidateOnBuild</c> checks every registration in one go; on top of that the main window chain
/// is resolved explicitly, because that is the genuinely fragile part.
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

        // The write queue (P05-T01) must be a SINGLETON: if a new one were produced on every request
        // the lock would protect nothing and serialization would silently be disabled.
        GitExt.Core.Git.IGitWriteQueue queue = provider.GetRequiredService<GitExt.Core.Git.IGitWriteQueue>();
        queue.ShouldBeSameAs(provider.GetRequiredService<GitExt.Core.Git.IGitWriteQueue>());

        // The write path (P05-T03) must resolve; a missing registration does not break the build, the
        // application would just crash on the first stage attempt.
        provider.GetRequiredService<GitExt.Core.Git.IGitWriter>().ShouldNotBeNull();
        provider.GetRequiredService<GitExt.Core.IStagingWriter>().ShouldNotBeNull();
        provider.GetRequiredService<GitExt.Core.ICommitWriter>().ShouldNotBeNull();
        provider.GetRequiredService<GitExt.Core.IWorkingTreeWriter>().ShouldNotBeNull();
        provider.GetRequiredService<GitExt.Core.IBranchWriter>().ShouldNotBeNull();
        provider.GetRequiredService<GitExt.Core.IInProgressOperationReader>().ShouldNotBeNull();

        // The commit screen (P05-T09) is built through MainWindowViewModel; if its dependencies are
        // not registered the factory silently returns `null` and the menu item does nothing.
        provider.GetRequiredService<GitExt.UI.ViewModels.MainWindowViewModel>()
            .CreateWorkingTree()
            .ShouldNotBeNull();

        // 🔴 The message helpers (P05-T13) are passed as OPTIONAL parameters: if the registration were
        // missing DI would silently hand over `null`, the build would pass, most tests would pass — and
        // the user's draft would never be saved. Silently going out of service is worse than a feature
        // that does not work.
        provider.GetRequiredService<GitExt.Core.Git.IGitConfigReader>().ShouldNotBeNull();
        provider.GetRequiredService<GitExt.Core.ICommitMessageReader>().ShouldNotBeNull();

        // The draft store must be a SINGLETON: it caches the git directory per repository; if a new one
        // were produced on every request an extra `git rev-parse` would run on every single
        // keystroke.
        GitExt.Core.ICommitMessageStore store =
            provider.GetRequiredService<GitExt.Core.ICommitMessageStore>();

        store.ShouldBeSameAs(provider.GetRequiredService<GitExt.Core.ICommitMessageStore>());

        // 🔴 The watcher (P05-T14) must be a SINGLETON too: if a new one were produced on every request
        // each of them would hold a separate `inotify` watch for EVERY DIRECTORY in the repository tree
        // (measured: 11,512 watches in an 11,512-directory tree) and hit the instance limit (1024 here).
        GitExt.Core.IRepositoryWatcher watcher =
            provider.GetRequiredService<GitExt.Core.IRepositoryWatcher>();

        watcher.ShouldBeSameAs(provider.GetRequiredService<GitExt.Core.IRepositoryWatcher>());
    }

    // Creating a window requires the Avalonia platform; that is why [AvaloniaFact].
    [AvaloniaFact]
    public void Ana_pencere_cozumlenebilir()
    {
        ServiceCollection services = new();
        services.AddGitExtServices();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<MainWindow>().ShouldNotBeNull();
    }
}
