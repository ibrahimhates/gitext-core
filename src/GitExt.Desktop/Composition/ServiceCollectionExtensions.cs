using GitExt.Core;
using GitExt.Core.Diagnostics;
using GitExt.Core.Git;
using GitExt.UI.Commands;
using GitExt.UI.Localization;
using GitExt.UI.Settings;
using GitExt.UI.Themes;
using GitExt.UI.Storage;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GitExt.Desktop.Composition;

/// <summary>
/// The application's service registrations. The sole composition root (ADR-0004).
/// </summary>
/// <remarks>
/// The Service Locator pattern is forbidden: no class injects <c>IServiceProvider</c> and
/// resolves services out of it. Dependencies come from the constructor.
/// <para>
/// When multi-repo support arrives in Phase 06, repo-bound services will be registered as
/// <c>Scoped</c>; they'll be cleaned up along with the scope when the repo closes.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitExtServices(this IServiceCollection services)
    {
        // Make legacy code pages (windows-1254, Shift-JIS…) usable; the user's files may not
        // be UTF-8 (P04-T07).
        TextEncodings.EnsureRegistered();

        services.AddLogging();

        // The git executable is located and verified once (ADR-0002).
        // Its existence, for instance, is proof that git is installed and its version is sufficient.
        services.AddSingleton(_ => GitExecutable.LocateAsync().GetAwaiter().GetResult());

        // The log the "show command" panel (Phase 08) will be fed from.
        services.AddSingleton<IGitCommandLog>(_ => new InMemoryGitCommandLog());

        // Performance diagnostics (P09-T03). Listens to the log to collect command
        // statistics; since every git call already passes through it per ADR-0002, there's no
        // need to add a separate measurement point — and risk producing paths where someone
        // forgot to add it.
        services.AddSingleton<IPerformanceDiagnostics>(provider =>
            new PerformanceDiagnostics(provider.GetRequiredService<IGitCommandLog>()));

        services.AddSingleton<IGitProcessRunner>(provider => new GitProcessRunner(
            provider.GetRequiredService<GitExecutable>(),
            provider.GetRequiredService<IGitCommandLog>(),
            logger: null,
            diagnostics: provider.GetRequiredService<IPerformanceDiagnostics>()));

        // commit-graph advisory (P09-T07). Only reads STATE; it does not write the file on
        // its own — adding a file to the user's repository uninvited would not be right.
        services.AddSingleton<ICommitGraphAdvisor, CommitGraphAdvisor>();

        // Write operations are serialized per repository (P05-T01). Being a singleton is a
        // MUST: if a new queue were created per request, the lock wouldn't protect anything.
        services.AddSingleton<IGitWriteQueue, GitWriteQueue>();

        // The single entry point of the write path: serialization + lock retry are combined
        // here (P05-T03). Every writing service must use this, and must not call the runner
        // directly.
        services.AddSingleton<IGitWriter, GitWriter>();
        services.AddSingleton<IStagingWriter, StagingWriter>();
        services.AddSingleton<ICommitWriter, CommitWriter>();
        services.AddSingleton<IWorkingTreeWriter, WorkingTreeWriter>();
        services.AddSingleton<IInProgressOperationReader, InProgressOperationReader>();
        // A destructive switch (`switch --discard-changes`) is never done without taking a
        // backup; the dependency is explicit for that reason (P06-T02).
        services.AddSingleton<IBranchWriter>(sp => new BranchWriter(
            sp.GetRequiredService<IGitWriter>(),
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<IWorkingTreeWriter>()));

        // Remote repository reading/writing (P06-T05). The writer depends on the reader: it
        // needs to see the multi-URL state BEFORE asking git (the single-step `set-url` fails
        // there).
        services.AddSingleton<IRemoteReader, RemoteReader>();
        services.AddSingleton<IRemoteWriter, RemoteWriter>();

        // Fetch (P06-T06). Since it computes what changed via a ref snapshot diff, it needs
        // both the writer and the reader.
        services.AddSingleton<IFetchWriter, FetchWriter>();

        // Pull (P06-T07). Depends on the config reader since it resolves the strategy from
        // settings; the strategy is not left to git (git rejects it in an unconfigured,
        // diverged repository).
        services.AddSingleton<IPullWriter, PullWriter>();

        // Push (P06-T08). Reads the result from stdout via `--porcelain` and gets the lease
        // anchor from local tracking refs — it needs the reader for both.
        services.AddSingleton<IPushWriter, PushWriter>();

        // Authentication diagnostics (P06-T09). Looks at the ENVIRONMENT, not git's text: the
        // remote URL's format, the `credential.helper` setting, and `ssh-add -l`'s exit code.
        services.AddSingleton<ISshAgentProbe, SshAgentProbe>();
        services.AddSingleton<IAuthenticationDiagnostics, AuthenticationDiagnostics>();

        // Merge (P06-T11, P06-T12). Reads the result from STATE, not from git's text
        // (`--squash` returns exit code 0 without advancing HEAD), so it depends on the reader.
        services.AddSingleton<IMergeWriter, MergeWriter>();

        // ------------------------------------------------- Phase 07: advanced operations
        //
        // Every writer that alters history depends on ISafetyPointRecorder: per the phase
        // rule, the position is saved BEFORE the operation and the user is given an undo
        // path. Making the dependency explicit turns skipping it into a build error.
        services.AddSingleton<ISafetyPointRecorder, SafetyPointRecorder>();
        services.AddSingleton<IReflogReader, ReflogReader>();
        services.AddSingleton<IConflictReader, ConflictReader>();
        services.AddSingleton<IConflictResolver, ConflictResolver>();
        services.AddSingleton<IMergeToolRunner, MergeToolRunner>();
        services.AddSingleton<IResetWriter, ResetWriter>();
        services.AddSingleton<ISequencerWriter, SequencerWriter>();
        services.AddSingleton<IRebaseWriter, RebaseWriter>();
        services.AddSingleton<IStashWriter, StashWriter>();
        services.AddSingleton<IBlameReader, BlameReader>();
        services.AddSingleton<IFileHistoryReader, FileHistoryReader>();
        services.AddSingleton<ITagWriter, TagWriter>();
        services.AddSingleton<IWorkTreeReader, WorkTreeReader>();
        services.AddSingleton<ISubmoduleReader, SubmoduleReader>();
        services.AddSingleton<ISearchReader, SearchReader>();

        // These are passed to screens as a single bundle; the rationale is in AdvancedOperationServices.
        services.AddSingleton(provider => new AdvancedOperationServices
        {
            Conflicts = provider.GetRequiredService<IConflictReader>(),
            Resolver = provider.GetRequiredService<IConflictResolver>(),
            MergeTools = provider.GetRequiredService<IMergeToolRunner>(),
            Reset = provider.GetRequiredService<IResetWriter>(),
            Sequencer = provider.GetRequiredService<ISequencerWriter>(),
            Rebase = provider.GetRequiredService<IRebaseWriter>(),
            Stash = provider.GetRequiredService<IStashWriter>(),
            Reflog = provider.GetRequiredService<IReflogReader>(),
            Blame = provider.GetRequiredService<IBlameReader>(),
            FileHistory = provider.GetRequiredService<IFileHistoryReader>(),
            Tags = provider.GetRequiredService<ITagWriter>(),
            WorkTrees = provider.GetRequiredService<IWorkTreeReader>(),
            Submodules = provider.GetRequiredService<ISubmoduleReader>(),
            Search = provider.GetRequiredService<ISearchReader>(),
        });

        services.AddSingleton<IRepositoryLocator, RepositoryLocator>();
        services.AddSingleton<ICommitLogReader, CommitLogReader>();
        services.AddSingleton<IRefReader, RefReader>();
        services.AddSingleton<ICommitSignatureReader, CommitSignatureReader>();
        services.AddSingleton<IDiffReader, DiffReader>();
        services.AddSingleton<IRecentRepositoryStore>(_ => new RecentRepositoryStore());

        // Settings (P08-T14). Must be read BEFORE the window opens: theme, font, and panel
        // layout come from here; reading it later would mean the app opening with the wrong
        // theme first and visibly jumping. Synchronous for the same reason as GitExecutable.
        services.AddSingleton<ISettingsStore>(_ =>
        {
            SettingsStore store = new();
            store.LoadAsync().GetAwaiter().GetResult();

            return store;
        });
        services.AddSingleton<IStatusReader, StatusReader>();
        services.AddSingleton<IObjectReader, ObjectReader>();

        // Commit message helpers (P05-T13). Singleton because the draft store caches the git
        // directory per repository; creating a new one per request would mean an extra
        // `git rev-parse` for every draft save.
        services.AddSingleton<IGitConfigReader, GitConfigReader>();
        services.AddSingleton<IGitConfigWriter, GitConfigWriter>();
        services.AddSingleton<ICommitMessageReader, CommitMessageReader>();
        services.AddSingleton<ICommitMessageStore, CommitMessageStore>();

        // File system watching (P05-T14). MUST be a SINGLETON: if a new one were created per
        // request, each would hold its own `inotify` instance and one watch per directory in
        // the repository tree (measured: 11,512 watches for an 11,512-directory tree). The
        // instance limit on this machine is 1024, and measurement hit an `IOException` on the
        // 949th watcher.
        services.AddSingleton<IRepositoryWatcher>(_ => new RepositoryWatcher());

        // Appearance service (P08-T07…T10): theme, palette, and typography are applied from a
        // single place. `Application.Current` is needed because the resource dictionary is
        // application-level.
        services.AddSingleton<IAppearanceService>(provider => new AppearanceService(
            Avalonia.Application.Current!,
            provider.GetRequiredService<ISettingsStore>()));

        // Translator (P11-T01). Singleton and early for the same reason as the theme: the
        // language must be chosen BEFORE the window opens, otherwise the app would open in
        // English and visibly jump to Turkish.
        services.AddSingleton<ITranslator>(provider =>
        {
            Translator translator = new(provider.GetRequiredService<ISettingsStore>());

            // Introduced to the XAML extension. Markup extension instances are created by the
            // XAML parser, not the DI container — there is no way to pass a dependency into
            // the constructor. This does not break the composition root's (ADR-0004) rule of
            // sole authority: the object is constructed here and given to the extension ONCE.
            TranslateExtension.Attach(translator);

            // Access from code (P11-T05): ViewModels get their text from here.
            Loc.Attach(translator);

            return translator;
        });

        // Command registry (P08-T01). The SINGLE source of shortcuts; depends on the settings
        // store since the user's re-assignments come from there.
        services.AddSingleton<ICommandRegistry, CommandRegistry>();

        services.AddSingleton<CommitListViewModel>();
        // Session tracker (P08-T16).
        services.AddSingleton(provider => new SessionTracker(provider.GetRequiredService<ISettingsStore>()));

        services.AddSingleton(provider =>
        {
            MainWindowViewModel model = ActivatorUtilities.CreateInstance<MainWindowViewModel>(provider);
            model.Session = provider.GetRequiredService<SessionTracker>();

            return model;
        });

        services.AddSingleton(provider =>
        {
            MainWindow window = new();

            // Cannot be passed to the constructor: the XAML designer requires a parameterless
            // constructor. The wiring is still done here — at the composition root (ADR-0004).
            window.AttachShortcuts(provider.GetRequiredService<ICommandRegistry>());

            // Layout is applied BEFORE the window is SHOWN (P08-T13): leaving it for after
            // would mean the app first opening at default size and then visibly re-laying out.
            window.AttachLayout(provider.GetRequiredService<ISettingsStore>());

            window.AttachSettings(
                provider.GetRequiredService<IAppearanceService>(),
                provider.GetRequiredService<IGitConfigWriter>());

            return window;
        });

        return services;
    }
}
