using GitExt.Core;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Phase 07's services (P07-T03 … P07-T21).
/// </summary>
/// <remarks>
/// <para>
/// Why a bundle? By the end of Phase 06, <see cref="MainWindowViewModel"/>'s constructor had reached
/// 20 optional parameters; adding Phase 07's eight services one by one would have made it unreadable.
/// These are a set that is meaningful <b>together</b>: they all feed the "advanced operations" screens.
/// </para>
/// <para>
/// All of them are optional: a test supplies only the service it cares about, the rest stay
/// <see langword="null"/> and the corresponding command appears disabled.
/// </para>
/// </remarks>
public sealed record AdvancedOperationServices
{
    public IConflictReader? Conflicts { get; init; }

    public IConflictResolver? Resolver { get; init; }

    public IMergeToolRunner? MergeTools { get; init; }

    public IResetWriter? Reset { get; init; }

    public ISequencerWriter? Sequencer { get; init; }

    public IRebaseWriter? Rebase { get; init; }

    public IStashWriter? Stash { get; init; }

    public IReflogReader? Reflog { get; init; }

    public IBlameReader? Blame { get; init; }

    public IFileHistoryReader? FileHistory { get; init; }

    public ITagWriter? Tags { get; init; }

    public IWorkTreeReader? WorkTrees { get; init; }

    public ISubmoduleReader? Submodules { get; init; }

    public ISearchReader? Search { get; init; }
}
