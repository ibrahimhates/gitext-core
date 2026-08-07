using System.Text;
using GitExt.Core;
using GitExt.Core.Model;

namespace GitExt.UI.Tests.Fakes;

/// <summary>P07-T01/T02 — çakışma okuyucusu sahtesi.</summary>
public sealed class FakeConflictReader : IConflictReader
{
    public FakeConflictReader(IReadOnlyList<ConflictedFile> files) => Files = files;

    public IReadOnlyList<ConflictedFile> Files { get; set; }

    /// <summary>
    /// Hangi aşamalar için içerik istendi?
    /// </summary>
    /// <remarks>
    /// Var olmayan bir aşama için <c>git show</c> çalıştırmanın fatal verdiğini
    /// hatırlatan test bunu okuyor.
    /// </remarks>
    public List<ConflictStage> RequestedStages { get; } = [];

    public Task<IReadOnlyList<ConflictedFile>> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) => Task.FromResult(Files);

    public Task<byte[]?> ReadStageAsync(
        string workingDirectory,
        RepositoryPath path,
        ConflictStage stage,
        CancellationToken cancellationToken = default)
    {
        RequestedStages.Add(stage);

        string text = stage switch
        {
            ConflictStage.Base => "ATA",
            ConflictStage.Ours => "BIZ",
            _ => "ONLAR",
        };

        return Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>P07-T05 — çakışma çözücü sahtesi.</summary>
public sealed class FakeConflictResolver : IConflictResolver
{
    public FakeConflictResolver(InProgressOperation operation, IReadOnlyList<string> remaining)
    {
        Operation = operation;
        Remaining = remaining;
    }

    public InProgressOperation Operation { get; set; }

    public IReadOnlyList<string> Remaining { get; set; }

    public ResolutionSide? TakenSide { get; private set; }

    public bool Continued { get; private set; }

    public bool Aborted { get; private set; }

    public Task<ConflictProgress> GetProgressAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConflictProgress
        {
            Operation = Operation,
            Remaining = [.. Remaining.Select(RepositoryPath.Parse)],
        });

    public Task MarkResolvedAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TakeSideAsync(
        string workingDirectory,
        RepositoryPath path,
        ResolutionSide side,
        CancellationToken cancellationToken = default)
    {
        TakenSide = side;
        return Task.CompletedTask;
    }

    public Task WriteResolvedAsync(
        string workingDirectory,
        RepositoryPath path,
        byte[] content,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ContinueAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        Continued = true;
        return Task.CompletedTask;
    }

    public Task AbortAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        Aborted = true;
        return Task.CompletedTask;
    }
}

/// <summary>P07-T06 — reset yazıcısı sahtesi.</summary>
public sealed class FakeResetWriter : IResetWriter
{
    private readonly ResetPreview _preview;
    private readonly SafetyPoint _point;

    public FakeResetWriter(ResetPreview preview, SafetyPoint? point = null)
    {
        _preview = preview;
        _point = point ?? new SafetyPoint
        {
            ObjectId = "0000000111122223333",
            BranchName = "main",
            Operation = "reset",
        };
    }

    public ResetOptions? LastOptions { get; private set; }

    public Task<SafetyPoint> ResetAsync(
        string workingDirectory,
        ResetOptions options,
        CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        return Task.FromResult(_point);
    }

    public Task<ResetPreview> PreviewAsync(
        string workingDirectory,
        string target,
        CancellationToken cancellationToken = default) => Task.FromResult(_preview);

    public string DescribeCommand(ResetOptions options) => ResetWriter.Describe(options);
}

/// <summary>P07-T07/T08 — sequencer sahtesi.</summary>
public sealed class FakeSequencerWriter : ISequencerWriter
{
    private readonly int _parentCount;
    private readonly IReadOnlyList<string> _conflicts;
    private readonly int _commitsCreated;
    private readonly bool _requiresCommit;

    public FakeSequencerWriter(
        int parentCount = 1,
        IReadOnlyList<string>? conflicts = null,
        int commitsCreated = 0,
        bool requiresCommit = false)
    {
        _parentCount = parentCount;
        _conflicts = conflicts ?? [];
        _commitsCreated = commitsCreated;
        _requiresCommit = requiresCommit;
    }

    public SequencerOptions? LastOptions { get; private set; }

    public Task<SequencerResult> RunAsync(
        string workingDirectory,
        SequencerOptions options,
        CancellationToken cancellationToken = default)
    {
        LastOptions = options;

        return Task.FromResult(new SequencerResult
        {
            Operation = options.Operation,
            SafetyPoint = new SafetyPoint
            {
                ObjectId = "0000000111122223333",
                BranchName = "main",
                Operation = "cherry-pick",
            },
            ConflictedPaths = [.. _conflicts.Select(RepositoryPath.Parse)],
            CommitsCreated = _commitsCreated,
            RequiresCommit = _requiresCommit,
        });
    }

    public Task<int> CountParentsAsync(
        string workingDirectory,
        string commit,
        CancellationToken cancellationToken = default) => Task.FromResult(_parentCount);

    public string DescribeCommand(SequencerOptions options) => SequencerWriter.Describe(options);
}

/// <summary>P07-T09/T10 — rebase sahtesi.</summary>
public sealed class FakeRebaseWriter : IRebaseWriter
{
    private readonly IReadOnlyList<RebaseStep> _steps;
    private readonly RebaseOutcome _outcome;

    public FakeRebaseWriter(
        IReadOnlyList<RebaseStep> steps,
        RebaseOutcome outcome = RebaseOutcome.Completed)
    {
        _steps = steps;
        _outcome = outcome;
    }

    public RebaseOptions? LastOptions { get; private set; }

    public Task<RebaseResult> RebaseAsync(
        string workingDirectory,
        RebaseOptions options,
        CancellationToken cancellationToken = default)
    {
        LastOptions = options;

        return Task.FromResult(new RebaseResult
        {
            Outcome = _outcome,
            SafetyPoint = new SafetyPoint
            {
                ObjectId = "0000000111122223333",
                BranchName = "main",
                Operation = "rebase",
            },
            CurrentStep = 1,
            TotalSteps = Math.Max(1, _steps.Count),
        });
    }

    public Task<IReadOnlyList<RebaseStep>> ReadStepsAsync(
        string workingDirectory,
        string upstream,
        string? branch = null,
        CancellationToken cancellationToken = default) => Task.FromResult(_steps);

    public Task SkipAsync(string workingDirectory, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public string DescribeCommand(RebaseOptions options) => RebaseWriter.Describe(options);
}

/// <summary>P07-T12/T13 — stash sahtesi.</summary>
public sealed class FakeStashWriter : IStashWriter
{
    public FakeStashWriter(IReadOnlyList<StashEntry> entries) => Entries = entries;

    public IReadOnlyList<StashEntry> Entries { get; set; }

    public bool PushSucceeds { get; set; } = true;

    public StashApplyResult ApplyResult { get; set; } = new()
    {
        HasConflicts = false,
        EntryKept = false,
        IndexRestored = true,
    };

    public Task<IReadOnlyList<StashEntry>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) => Task.FromResult(Entries);

    public Task<bool> PushAsync(
        string workingDirectory,
        StashPushOptions options,
        CancellationToken cancellationToken = default) => Task.FromResult(PushSucceeds);

    public Task<StashApplyResult> ApplyAsync(
        string workingDirectory,
        string selector,
        bool drop,
        CancellationToken cancellationToken = default) => Task.FromResult(ApplyResult);

    public Task DropAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task BranchAsync(
        string workingDirectory,
        string selector,
        string branchName,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string> ShowAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default) =>
        Task.FromResult("diff --git a/f.txt b/f.txt\n+degisti\n");
}

/// <summary>P07-T14 — reflog sahtesi.</summary>
public sealed class FakeReflogReader : IReflogReader
{
    public FakeReflogReader(IReadOnlyList<ReflogEntry> entries) => Entries = entries;

    public IReadOnlyList<ReflogEntry> Entries { get; set; }

    public Task<IReadOnlyList<ReflogEntry>> ReadAsync(
        string workingDirectory,
        string? reference = null,
        int limit = 200,
        CancellationToken cancellationToken = default) => Task.FromResult(Entries);
}
