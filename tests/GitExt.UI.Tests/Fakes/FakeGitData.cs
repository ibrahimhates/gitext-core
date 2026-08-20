using System.Runtime.CompilerServices;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Storage;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.Fakes;

/// <summary>
/// Fake repository locator for the ViewModel tests.
/// </summary>
/// <remarks>
/// The ViewModels are tested without starting a real <c>git</c> process (ADR-0004). Real <c>git</c>
/// behavior is already verified thoroughly in <c>GitExt.Core.Tests</c>; what is tested here is
/// <b>ViewModel logic</b>.
/// </remarks>
public sealed class FakeRepositoryLocator : IRepositoryLocator
{
    private readonly Exception? _failure;

    public FakeRepositoryLocator(Exception? failure = null)
    {
        _failure = failure;
    }

    public Task<RepositoryLocation> LocateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (_failure is not null)
        {
            return Task.FromException<RepositoryLocation>(_failure);
        }

        return Task.FromResult(FakeGitData.Location(path));
    }
}

/// <summary>
/// Fake commit history reader for the ViewModel tests.
/// </summary>
public sealed class FakeCommitLogReader : ICommitLogReader
{
    private readonly IReadOnlyList<CommitInfo> _commits;
    private readonly Exception? _failure;

    /// <summary>How many times it was awaited while streaming — to test batched updates.</summary>
    public int StreamCallCount { get; private set; }

    /// <summary>
    /// The query of the last read — this is what the filter tests assert on (P12-T07).
    /// </summary>
    /// <remarks>
    /// The filtering is git's job, so what has to be verified is <b>the query handed to git</b>,
    /// not which rows a fake happened to return.
    /// </remarks>
    public CommitLogQuery? LastQuery { get; private set; }

    public FakeCommitLogReader(IReadOnlyList<CommitInfo>? commits = null, Exception? failure = null)
    {
        _commits = commits ?? [];
        _failure = failure;
    }

    public Task<IReadOnlyList<CommitInfo>> ReadAsync(
        string workingDirectory,
        CommitLogQuery query,
        CancellationToken cancellationToken = default)
    {
        LastQuery = query;

        return _failure is not null
            ? Task.FromException<IReadOnlyList<CommitInfo>>(_failure)
            : Task.FromResult(_commits);
    }

    public async IAsyncEnumerable<CommitInfo> StreamAsync(
        string workingDirectory,
        CommitLogQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamCallCount++;
        LastQuery = query;

        if (_failure is not null)
        {
            throw _failure;
        }

        foreach (CommitInfo commit in _commits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return commit;
            await Task.Yield();
        }
    }
}

/// <summary>
/// Fake ref reader for the ViewModel tests.
/// </summary>
public sealed class FakeRefReader : IRefReader
{
    private readonly RepositoryRefs _refs;
    private readonly Exception? _failure;

    public int ReadCallCount { get; private set; }

    public FakeRefReader(RepositoryRefs? refs = null, Exception? failure = null)
    {
        _refs = refs ?? FakeGitData.NoRefs();
        _failure = failure;
    }

    public Task<RepositoryRefs> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ReadCallCount++;

        return _failure is not null
            ? Task.FromException<RepositoryRefs>(_failure)
            : Task.FromResult(_refs);
    }
}

/// <summary>
/// Fake signature reader for the ViewModel tests.
/// </summary>
/// <remarks>
/// By default it returns unsigned: most tests do not care about signatures, and real signature
/// behavior is verified in <c>GitExt.Core.Tests</c> with real SSH keys.
/// </remarks>
public sealed class FakeCommitSignatureReader : ICommitSignatureReader
{
    private readonly CommitSignatureInfo _signature;

    public int ReadCallCount { get; private set; }

    public FakeCommitSignatureReader(CommitSignatureInfo? signature = null)
    {
        _signature = signature ?? CommitSignatureInfo.Unsigned;
    }

    public Task<CommitSignatureInfo> ReadAsync(
        string workingDirectory,
        CommitId commit,
        CancellationToken cancellationToken = default)
    {
        ReadCallCount++;
        return Task.FromResult(_signature);
    }
}

/// <summary>
/// Fake diff reader for the ViewModel tests.
/// </summary>
public sealed class FakeDiffReader : IDiffReader
{
    private readonly IReadOnlyList<FileDiff> _diffs;
    private readonly Exception? _failure;

    public int ReadCallCount { get; private set; }

    public FakeDiffReader(IReadOnlyList<FileDiff>? diffs = null, Exception? failure = null)
    {
        _diffs = diffs ?? [];
        _failure = failure;
    }

    public Task<IReadOnlyList<FileDiff>> ReadCommitAsync(
        string workingDirectory,
        CommitId commit,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ReadCallCount++;

        return _failure is not null
            ? Task.FromException<IReadOnlyList<FileDiff>>(_failure)
            : Task.FromResult(_diffs);
    }

    public Task<IReadOnlyList<FileDiff>> ReadBetweenAsync(
        string workingDirectory,
        string fromRevision,
        string toRevision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ReadCommitAsync(workingDirectory, default, options, cancellationToken);

    public Task<IReadOnlyList<FileDiff>> ReadAgainstWorkingTreeAsync(
        string workingDirectory,
        string revision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ReadCallCount++;

        return _failure is not null
            ? Task.FromException<IReadOnlyList<FileDiff>>(_failure)
            : Task.FromResult(_diffs);
    }

    public Task<IReadOnlyList<FileDiff>> ReadUnstagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ReadCommitAsync(workingDirectory, default, options, cancellationToken);

    public Task<IReadOnlyList<FileDiff>> ReadStagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ReadCommitAsync(workingDirectory, default, options, cancellationToken);
}

/// <summary>
/// In-memory fake "recently opened" store — for ViewModel tests without touching the disk.
/// </summary>
public sealed class FakeRecentRepositoryStore : IRecentRepositoryStore
{
    private readonly List<RecentRepository> _repositories;

    public FakeRecentRepositoryStore(params string[] initial)
    {
        _repositories = [.. initial.Select(path => new RecentRepository(path))];
    }

    /// <summary>The paths in order — what most of the tests actually assert on.</summary>
    public IReadOnlyList<string> Paths => [.. _repositories.Select(r => r.Path)];

    /// <summary>Files a repository under a category up front (P12-T03).</summary>
    public FakeRecentRepositoryStore WithCategory(string workingDirectory, string category)
    {
        int index = _repositories.FindIndex(r => r.Path == workingDirectory);

        if (index >= 0)
        {
            _repositories[index] = _repositories[index] with { Category = category };
        }
        else
        {
            _repositories.Add(new RecentRepository(workingDirectory, category));
        }

        return this;
    }

    public Task<IReadOnlyList<RecentRepository>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RecentRepository>>([.. _repositories]);

    public Task AddAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        RecentRepository? existing = _repositories.FirstOrDefault(r => r.Path == workingDirectory);

        _repositories.RemoveAll(r => r.Path == workingDirectory);
        _repositories.Insert(0, new RecentRepository(workingDirectory, existing?.Category));

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        _repositories.RemoveAll(r => r.Path == workingDirectory);
        return Task.CompletedTask;
    }

    public Task SetCategoryAsync(
        string workingDirectory,
        string? category,
        CancellationToken cancellationToken = default)
    {
        int index = _repositories.FindIndex(r => r.Path == workingDirectory);

        if (index >= 0)
        {
            _repositories[index] = _repositories[index] with { Category = category };
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Test data generators.
/// </summary>
public static class FakeGitData
{
    public static RepositoryLocation Location(string path) =>
        new(
            gitDirectory: Path.Combine(path, ".git"),
            commonDirectory: Path.Combine(path, ".git"),
            workTreeRoot: path,
            superprojectWorkTree: null);

    /// <summary>
    /// Produces a linear history; newest to oldest, in topological order.
    /// </summary>
    public static IReadOnlyList<CommitInfo> LinearHistory(int count)
    {
        List<CommitInfo> commits = new(count);

        for (int i = count; i >= 1; i--)
        {
            commits.Add(Commit(
                id: Sha(i),
                parents: i > 1 ? [Sha(i - 1)] : [],
                subject: $"commit {i}"));
        }

        return commits;
    }

    public static CommitInfo Commit(
        string id,
        IReadOnlyList<string> parents,
        string subject,
        IReadOnlyList<string>? refs = null) =>
        new()
        {
            Id = CommitId.Parse(id),
            Parents = [.. parents.Select(CommitId.Parse)],
            Author = new Signature("Test Yazar", "yazar@test.invalid", DateTimeOffset.UnixEpoch),
            Committer = new Signature("Test Yazar", "yazar@test.invalid", DateTimeOffset.UnixEpoch),
            Subject = subject,
            Body = string.Empty,
            Refs = refs ?? [],
        };

    /// <summary>Produces a file diff for tests.</summary>
    public static FileDiff Diff(
        string path,
        FileChangeKind change = FileChangeKind.Modified,
        int? added = 1,
        int? removed = 1,
        bool binary = false,
        bool tooLarge = false,
        string? oldPath = null) =>
        new()
        {
            Path = RepositoryPath.Parse(path),
            OldPath = oldPath is null ? null : RepositoryPath.Parse(oldPath),
            Change = change,
            StatAdded = binary ? null : added,
            StatRemoved = binary ? null : removed,
            IsBinary = binary,
            IsTooLarge = tooLarge,
            Hunks = [],
        };

    /// <summary>Produces a deterministic, valid SHA from a sequence number.</summary>
    public static string Sha(int index) => index.ToString("x40", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The ref state of an unborn repository containing no branches/tags at all.</summary>
    public static RepositoryRefs NoRefs() =>
        new()
        {
            Head = new HeadState { IsDetached = false, IsUnborn = true },
            LocalBranches = [],
            RemoteBranches = [],
            Tags = [],
            Remotes = [],
        };

    /// <summary>Builds a <see cref="RepositoryRefs"/> from the given refs.</summary>
    public static RepositoryRefs Refs(
        IReadOnlyList<BranchInfo>? localBranches = null,
        IReadOnlyList<BranchInfo>? remoteBranches = null,
        IReadOnlyList<TagInfo>? tags = null,
        HeadState? head = null) =>
        new()
        {
            Head = head ?? new HeadState
            {
                IsDetached = false,
                IsUnborn = false,
                BranchName = localBranches?.FirstOrDefault(b => b.IsCurrent)?.Name,
            },
            LocalBranches = localBranches ?? [],
            RemoteBranches = remoteBranches ?? [],
            Tags = tags ?? [],
            Remotes = [],
        };

    public static BranchInfo LocalBranch(string name, string targetSha, bool isCurrent = false) =>
        new()
        {
            Ref = Ref($"refs/heads/{name}", name, GitRefKind.LocalBranch, targetSha),
            IsCurrent = isCurrent,
        };

    public static BranchInfo RemoteBranch(string shortName, string targetSha) =>
        new()
        {
            Ref = Ref($"refs/remotes/{shortName}", shortName, GitRefKind.RemoteBranch, targetSha),
        };

    /// <summary>
    /// <c>refs/remotes/&lt;remote&gt;/HEAD</c> — the symbolic ref present in every cloned repository.
    /// </summary>
    /// <remarks>
    /// The short name is deliberately <c>"origin"</c>: git abbreviates this ref as <c>origin</c>, not
    /// <c>origin/HEAD</c> (measured). If the fake data does not reflect reality the test protects
    /// the wrong thing.
    /// </remarks>
    public static BranchInfo SymbolicRemoteHead(string remote, string targetRef, string targetSha) =>
        new()
        {
            Ref = Ref($"refs/remotes/{remote}/HEAD", remote, GitRefKind.RemoteBranch, targetSha) with
            {
                SymbolicTarget = targetRef,
            },
        };

    /// <summary>
    /// Produces a tag.
    /// </summary>
    /// <remarks>
    /// If <paramref name="annotated"/>, <c>ObjectId</c> (the tag object) and <c>TargetCommit</c>
    /// (the resolved commit) differ — that is the situation in real <c>for-each-ref</c>
    /// output.
    /// </remarks>
    public static TagInfo Tag(string name, string targetSha, bool annotated = false) =>
        new()
        {
            Ref = new GitRef
            {
                FullName = $"refs/tags/{name}",
                ShortName = name,
                Kind = GitRefKind.Tag,
                ObjectId = annotated ? CommitId.Parse(Sha(999_999)) : CommitId.Parse(targetSha),
                TargetCommit = CommitId.Parse(targetSha),
                IsAnnotatedTag = annotated,
            },
            Subject = name,
        };

    private static GitRef Ref(string fullName, string shortName, GitRefKind kind, string targetSha) =>
        new()
        {
            FullName = fullName,
            ShortName = shortName,
            Kind = kind,
            ObjectId = CommitId.Parse(targetSha),
            TargetCommit = CommitId.Parse(targetSha),
        };
}

/// <summary>
/// In-memory fake status reader — for ViewModel tests without touching <c>git</c>.
/// </summary>
/// <remarks>
/// Stage/unstage operations change the state of this object via <see cref="FakeStagingWriter"/>;
/// that way "what happens to the list after staging" can genuinely be tested.
/// </remarks>
public sealed class FakeStatusReader : IStatusReader
{
    private readonly List<FileStatus> _entries;
    private readonly Exception? _failure;

    public FakeStatusReader(IEnumerable<FileStatus>? entries = null, Exception? failure = null)
    {
        _entries = [.. entries ?? []];
        _failure = failure;
    }

    public int ReadCallCount { get; private set; }

    /// <summary>Runs during a read; to observe the watcher suspension (P05-T14).</summary>
    public Action? OnRead { get; set; }

    public IList<FileStatus> Entries => _entries;

    public Task<WorkingTreeStatus> ReadAsync(
        string workingDirectory,
        bool includeIgnored = false,
        CancellationToken cancellationToken = default)
    {
        ReadCallCount++;
        OnRead?.Invoke();

        if (_failure is not null)
        {
            return Task.FromException<WorkingTreeStatus>(_failure);
        }

        return Task.FromResult(new WorkingTreeStatus
        {
            BranchName = "main",
            Entries = [.. _entries],
        });
    }
}

/// <summary>
/// Fake staging writer: moves the entries of <see cref="FakeStatusReader"/> in place.
/// </summary>
public sealed class FakeStagingWriter : IStagingWriter
{
    private readonly FakeStatusReader _status;

    public FakeStagingWriter(FakeStatusReader status)
    {
        _status = status;
    }

    public List<string> Calls { get; } = [];

    public Task StageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"stage:{string.Join(',', paths)}");

        Move(paths, toStaged: true);
        return Task.CompletedTask;
    }

    public Task UnstageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"unstage:{string.Join(',', paths)}");

        Move(paths, toStaged: false);
        return Task.CompletedTask;
    }

    private void Move(IReadOnlyList<RepositoryPath> paths, bool toStaged)
    {
        foreach (RepositoryPath path in paths)
        {
            for (int i = 0; i < _status.Entries.Count; i++)
            {
                if (_status.Entries[i].Path != path)
                {
                    continue;
                }

                _status.Entries[i] = toStaged
                    ? new FileStatus { Path = path, StagedChange = FileChangeKind.Modified }
                    : new FileStatus { Path = path, UnstagedChange = FileChangeKind.Modified };
            }
        }
    }

    /// <summary>The encoding passed through to partial staging (P05-T16).</summary>
    public System.Text.Encoding? LastPartialEncoding { get; private set; }

    public Task StagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default)
    {
        LastPartialEncoding = contentEncoding;
        return Task.CompletedTask;
    }

    public Task UnstagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default)
    {
        LastPartialEncoding = contentEncoding;
        return Task.CompletedTask;
    }

    public Task UntrackAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Fake commit writer — for testing the commit flow without touching <c>git</c>.
/// </summary>
public sealed class FakeCommitWriter : ICommitWriter
{
    private readonly FakeStatusReader? _status;

    public FakeCommitWriter(FakeStatusReader? status = null)
    {
        _status = status;
    }

    public List<string> Messages { get; } = [];

    public List<CommitOptions> Options { get; } = [];

    public Exception? Failure { get; set; }

    /// <summary>The output to be returned in the result (simulating hook output).</summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>Behaves as if a hook had changed the message.</summary>
    public string? RewrittenMessage { get; set; }

    public Task<CommitResult> CommitAsync(
        string workingDirectory,
        string message,
        CommitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<CommitResult>(Failure);
        }

        Messages.Add(message);
        Options.Add(options ?? CommitOptions.Default);

        // The committed files drop out of the working directory.
        _status?.Entries.Clear();

        return Task.FromResult(new CommitResult
        {
            Id = CommitId.Parse(new string('c', 40)),
            Message = RewrittenMessage ?? message,
            RequestedMessage = message,
            Output = Output,
        });
    }
}

/// <summary>
/// P05-T13 — fake commit message reader (history, HEAD message, template).
/// </summary>
public sealed class FakeCommitMessageReader : ICommitMessageReader
{
    /// <summary>Messages ordered newest to oldest.</summary>
    public List<string> Recent { get; } = [];

    /// <summary>Messages returned by the "only mine" filter; if empty, <see cref="Recent"/>.</summary>
    public List<string> Mine { get; } = [];

    public string? HeadMessage { get; set; }

    public CommitTemplate? Template { get; set; }

    public string CommentCharacter { get; set; } = "#";

    /// <summary>How many times history was read — to verify it is not read before the menu opens.</summary>
    public int RecentReadCount { get; private set; }

    public bool LastOnlyCurrentUser { get; private set; }

    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<string>> ReadRecentAsync(
        string workingDirectory,
        int count,
        bool onlyCurrentUser = false,
        CancellationToken cancellationToken = default)
    {
        RecentReadCount++;
        LastOnlyCurrentUser = onlyCurrentUser;

        if (Failure is not null)
        {
            return Task.FromException<IReadOnlyList<string>>(Failure);
        }

        List<string> source = onlyCurrentUser && Mine.Count > 0 ? Mine : Recent;

        return Task.FromResult<IReadOnlyList<string>>([.. source.Take(count)]);
    }

    public Task<string?> ReadHeadMessageAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) =>
        Failure is not null
            ? Task.FromException<string?>(Failure)
            : Task.FromResult(HeadMessage);

    public Task<CommitTemplate?> ReadTemplateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Template);

    public Task<string> ReadCommentCharacterAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CommentCharacter);
}

/// <summary>
/// P05-T13 — fake draft store.
/// </summary>
/// <remarks>
/// Kept separately per repository path: so the real store's per-worktree behavior (verified with
/// real git in <c>CommitMessageTests</c>) has its counterpart here too.
/// </remarks>
public sealed class FakeCommitMessageStore : ICommitMessageStore
{
    public Dictionary<string, string> Drafts { get; } = new(StringComparer.Ordinal);

    /// <summary>The message git prepared (merge/cherry-pick) — comes before the draft.</summary>
    public string? PendingMessage { get; set; }

    public int SaveCount { get; private set; }

    public int ClearCount { get; private set; }

    public Task<PendingCommitMessage> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (PendingMessage is { Length: > 0 } pending)
        {
            return Task.FromResult(new PendingCommitMessage(pending, CommitMessageSource.Pending));
        }

        return Task.FromResult(
            Drafts.TryGetValue(workingDirectory, out string? draft) && draft.Length > 0
                ? new PendingCommitMessage(draft, CommitMessageSource.Draft)
                : PendingCommitMessage.None);
    }

    public Task SaveDraftAsync(
        string workingDirectory,
        string message,
        CancellationToken cancellationToken = default)
    {
        SaveCount++;

        if (message.Trim().Length == 0)
        {
            Drafts.Remove(workingDirectory);
        }
        else
        {
            Drafts[workingDirectory] = message;
        }

        return Task.CompletedTask;
    }

    public Task ClearDraftAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ClearCount++;
        Drafts.Remove(workingDirectory);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Fake watcher whose events the tests can raise by hand (P05-T14).
/// </summary>
/// <remarks>
/// The real <see cref="RepositoryWatcher"/> waits on the file system and a timer; what is tested on
/// the ViewModel side is <b>how the event is reacted to</b>, not where the event came from.
/// </remarks>
public sealed class FakeRepositoryWatcher : IRepositoryWatcher
{
    public event EventHandler<RepositoryChangedEventArgs>? Changed;

    public bool IsRunning { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public string? WorkingTreeRoot { get; private set; }

    public string? GitDirectory { get; private set; }

    public string? CommonDirectory { get; private set; }

    /// <summary>Is it suspended right now? Our own reads not raising events depends on this.</summary>
    public bool IsSuspended => SuspendDepth > 0;

    public int SuspendDepth { get; private set; }

    /// <summary>How many events were dropped while suspended? Greater than zero means a loop risk.</summary>
    public int SuppressedCount { get; private set; }

    public bool Start(string workingTreeRoot, string gitDirectory, string commonDirectory)
    {
        StartCount++;
        IsRunning = true;
        WorkingTreeRoot = workingTreeRoot;
        GitDirectory = gitDirectory;
        CommonDirectory = commonDirectory;
        return true;
    }

    public void Stop()
    {
        StopCount++;
        IsRunning = false;
    }

    public IDisposable Suspend()
    {
        SuspendDepth++;
        return new Suspension(this);
    }

    /// <summary>Raises a change event; while suspended it is swallowed silently.</summary>
    public void Raise(RepositoryChangeKind kind)
    {
        if (IsSuspended)
        {
            SuppressedCount++;
            return;
        }

        Changed?.Invoke(this, new RepositoryChangedEventArgs(kind));
    }

    public void Dispose() => Stop();

    private sealed class Suspension : IDisposable
    {
        private readonly FakeRepositoryWatcher _owner;

        public Suspension(FakeRepositoryWatcher owner) => _owner = owner;

        public void Dispose() => _owner.SuspendDepth--;
    }
}

/// <summary>
/// Fake writer that records destructive operations (P05-T15).
/// </summary>
public sealed class FakeWorkingTreeWriter : IWorkingTreeWriter
{
    public Task<IReadOnlyList<DiscardBackup>> BackupPathsAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DiscardBackup>>(
            [.. paths.Select(path => new DiscardBackup { Path = path, BlobId = "0" })]);

    private readonly FakeStatusReader? _status;

    public FakeWorkingTreeWriter(FakeStatusReader? status = null) => _status = status;

    public List<string> Calls { get; } = [];

    /// <summary>The paths written back; did "undo" really work?</summary>
    public List<string> Restored { get; } = [];

    /// <summary>How many backups should count as having their objects pruned (partial recovery).</summary>
    public int PrunedBackupCount { get; set; }

    public Task<IReadOnlyList<DiscardBackup>> DiscardChangesAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        DiscardScope scope,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed)
        {
            throw new InvalidOperationException("onaysız çağrı");
        }

        Calls.Add($"discard:{scope}:{string.Join(',', paths)}");
        Remove(paths);

        return Task.FromResult(Backups(paths));
    }

    public Task<IReadOnlyList<DiscardBackup>> DeleteUntrackedAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed)
        {
            throw new InvalidOperationException("onaysız çağrı");
        }

        Calls.Add($"delete:{string.Join(',', paths)}");
        Remove(paths);

        return Task.FromResult(Backups(paths));
    }

    public Task<IReadOnlyList<DiscardBackup>> DiscardPartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        bool userConfirmed,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed)
        {
            throw new InvalidOperationException("onaysız çağrı");
        }

        Calls.Add($"discard-partial:{diff.Path}:{selection.Count}");

        return Task.FromResult(Backups([diff.Path]));
    }

    public Task CleanAsync(
        string workingDirectory,
        CleanOptions options,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("clean");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DiscardBackup>> RestoreBackupsAsync(
        string workingDirectory,
        IReadOnlyList<DiscardBackup> backups,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"restore:{backups.Count}");

        List<DiscardBackup> restored = [.. backups.Skip(PrunedBackupCount)];
        Restored.AddRange(restored.Select(backup => backup.Path.Value));

        return Task.FromResult<IReadOnlyList<DiscardBackup>>(restored);
    }

    public Task<GitIgnoreOutcome> AddToGitIgnoreAsync(
        string workingDirectory,
        RepositoryPath path,
        string pattern,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"ignore:{path}");
        return Task.FromResult(GitIgnoreOutcome.Added);
    }

    private static IReadOnlyList<DiscardBackup> Backups(IReadOnlyList<RepositoryPath> paths) =>
        [.. paths.Select(path => new DiscardBackup { Path = path, BlobId = $"blob-{path.Value}" })];

    private void Remove(IReadOnlyList<RepositoryPath> paths)
    {
        if (_status is null)
        {
            return;
        }

        foreach (RepositoryPath path in paths)
        {
            for (int i = _status.Entries.Count - 1; i >= 0; i--)
            {
                if (_status.Entries[i].Path == path)
                {
                    _status.Entries.RemoveAt(i);
                }
            }
        }
    }
}

/// <summary>
/// Fake confirmer whose answer is decided by the test (P05-T15).
/// </summary>
public sealed class FakeConfirmer : IDestructiveActionConfirmer
{
    private readonly ResetChangesDecision _decision;

    public FakeConfirmer(ResetChangesDecision? decision = null) =>
        _decision = decision ?? new ResetChangesDecision { Confirmed = true };

    public int AskCount { get; private set; }

    public ResetChangesRequest? LastRequest { get; private set; }

    public Task<ResetChangesDecision> ConfirmResetAsync(ResetChangesRequest request)
    {
        AskCount++;
        LastRequest = request;

        return Task.FromResult(_decision);
    }
}

/// <summary>
/// Fake of the branch writer (P06-T01).
/// </summary>
public sealed class FakeBranchWriter : IBranchWriter
{
    /// <summary>If set, <see cref="CreateAsync"/> throws this.</summary>
    public Exception? Failure { get; set; }

    /// <summary>The default upstream git sets up.</summary>
    public string? Upstream { get; set; }

    public List<BranchCreateOptions> Created { get; } = [];

    public Task<BranchCreateResult> CreateAsync(
        string workingDirectory,
        BranchCreateOptions options,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<BranchCreateResult>(Failure);
        }

        Created.Add(options);

        return Task.FromResult(new BranchCreateResult(options.Name, options.Checkout, Upstream));
    }

    /// <summary>If set, the switch returns this result.</summary>
    public BranchSwitchResult? SwitchResult { get; set; }

    public List<BranchSwitchOptions> Switched { get; } = [];

    public Task<BranchSwitchResult> SwitchAsync(
        string workingDirectory,
        BranchSwitchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<BranchSwitchResult>(Failure);
        }

        Switched.Add(options);

        return Task.FromResult(
            SwitchResult ?? new BranchSwitchResult { Target = options.Target });
    }

    public List<(string Old, string New)> Renamed { get; } = [];

    public Task RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Renamed.Add((oldName, newName));

        return Task.CompletedTask;
    }

    /// <summary>Thrown when deletion is attempted without force (two-round flow test).</summary>
    public BranchNotMergedException? UnmergedFailure { get; set; }

    public string DeletedCommitId { get; set; } = "abcdef1234567890";

    public List<(string Name, bool Force)> Deleted { get; } = [];

    public Task<BranchDeleteResult> DeleteAsync(
        string workingDirectory,
        string name,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<BranchDeleteResult>(Failure);
        }

        if (!force && UnmergedFailure is not null)
        {
            return Task.FromException<BranchDeleteResult>(UnmergedFailure);
        }

        Deleted.Add((name, force));

        return Task.FromResult(new BranchDeleteResult
        {
            Name = name,
            LastCommitId = DeletedCommitId,
            WasUnmerged = force,
        });
    }
}

/// <summary>
/// Fake of the create branch dialog (P06-T01).
/// </summary>
public sealed class FakeBranchPrompt : ICreateBranchPrompt
{
    private readonly CreateBranchDecision _decision;

    public FakeBranchPrompt(CreateBranchDecision? decision = null) =>
        _decision = decision ?? new CreateBranchDecision { Confirmed = true, Name = "yeni" };

    public int AskCount { get; private set; }

    public CreateBranchRequest? LastRequest { get; private set; }

    public Task<CreateBranchDecision> RequestAsync(CreateBranchRequest request)
    {
        AskCount++;
        LastRequest = request;

        return Task.FromResult(_decision);
    }
}

/// <summary>
/// Fake of the switch branch dialog (P06-T02).
/// </summary>
public sealed class FakeCheckoutPrompt : ICheckoutPrompt
{
    private readonly CheckoutDecision _decision;

    public FakeCheckoutPrompt(CheckoutDecision? decision = null) =>
        _decision = decision ?? new CheckoutDecision { Confirmed = true };

    public int AskCount { get; private set; }

    public CheckoutRequest? LastRequest { get; private set; }

    public Task<CheckoutDecision> RequestAsync(CheckoutRequest request)
    {
        AskCount++;
        LastRequest = request;

        return Task.FromResult(_decision);
    }
}

/// <summary>
/// Fake of the branch edit dialogs (P06-T03).
/// </summary>
public sealed class FakeBranchEditPrompt : IBranchEditPrompt
{
    private readonly RenameBranchDecision _rename;
    private readonly DeleteBranchDecision _delete;

    public FakeBranchEditPrompt(
        RenameBranchDecision? rename = null,
        DeleteBranchDecision? delete = null)
    {
        _rename = rename ?? new RenameBranchDecision { Confirmed = true, NewName = "yeniad" };
        _delete = delete ?? new DeleteBranchDecision { Confirmed = true };
    }

    /// <summary>The decision given in the second round (not merged); if absent the first is used.</summary>
    public DeleteBranchDecision? ForcedDecision { get; set; }

    public List<DeleteBranchRequest> DeleteRequests { get; } = [];

    public RenameBranchRequest? LastRenameRequest { get; private set; }

    public Task<RenameBranchDecision> RequestRenameAsync(RenameBranchRequest request)
    {
        LastRenameRequest = request;

        return Task.FromResult(_rename);
    }

    public Task<DeleteBranchDecision> RequestDeleteAsync(DeleteBranchRequest request)
    {
        DeleteRequests.Add(request);

        return Task.FromResult(
            request.IsUnmerged && ForcedDecision is not null ? ForcedDecision : _delete);
    }
}

/// <summary>
/// Fake of the in-progress operation reader (P06-T04).
/// </summary>
public sealed class FakeInProgressOperationReader : IInProgressOperationReader
{
    public FakeInProgressOperationReader(InProgressOperation operation = InProgressOperation.None) =>
        Operation = operation;

    /// <summary>Writable so the test can change it between refreshes (P06-T12).</summary>
    public InProgressOperation Operation { get; set; }

    public Task<InProgressOperation> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Operation);
}

/// <summary>Fake of the merge writer (P06-T11, P06-T12).</summary>
public sealed class FakeMergeWriter : IMergeWriter
{
    public List<MergeOptions> Merged { get; } = [];

    public int Aborted { get; private set; }

    public MergeResult? Result { get; set; }

    public MergePreview Preview { get; set; } = new()
    {
        HasChanges = true,
        CanFastForward = true,
        HasCommonAncestor = true,
        Ahead = 1,
    };

    public GitExt.Core.Git.GitException? Failure { get; set; }

    public Task<MergeResult> MergeAsync(
        string workingDirectory,
        MergeOptions options,
        CancellationToken cancellationToken = default)
    {
        Merged.Add(options);

        if (Failure is { } error)
        {
            throw error;
        }

        return Task.FromResult(Result ?? new MergeResult
        {
            Outcome = MergeOutcome.FastForward,
            HeadBefore = "aaaa",
            HeadAfter = "bbbb",
        });
    }

    public Task<MergePreview> PreviewAsync(
        string workingDirectory,
        string source,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Preview);

    public Task<string> AbortAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        Aborted++;
        return Task.FromResult("aaaa");
    }

    public string DescribeCommand(MergeOptions options) => MergeWriter.Describe(options);
}

/// <summary>Fake of the side that shows the merge screen (P06-T11).</summary>
public sealed class FakeMergePrompt : IMergePrompt
{
    public MergeViewModel? Shown { get; private set; }

    public Task ShowAsync(MergeViewModel model)
    {
        Shown = model;
        return Task.CompletedTask;
    }
}

/// <summary>Fake of the merge abort confirmation (P06-T12).</summary>
public sealed class FakeMergeAbortConfirmer : IMergeAbortConfirmer
{
    public bool Asked { get; private set; }

    public bool Answer { get; set; } = true;

    public IReadOnlyList<string> SeenConflicts { get; private set; } = [];

    public Task<bool> ConfirmAsync(IReadOnlyList<string> conflicted)
    {
        Asked = true;
        SeenConflicts = conflicted;
        return Task.FromResult(Answer);
    }
}

/// <summary>
/// Fake of the remote repository reader (P06-T05).
/// </summary>
public sealed class FakeRemoteReader : IRemoteReader
{
    public List<GitRemote> Remotes { get; } = [];

    public Task<IReadOnlyList<GitRemote>> ReadAllAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitRemote>>([.. Remotes]);

    public Task<GitRemote?> FindAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Remotes.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal)));
}

/// <summary>
/// Fake of the remote repository writer (P06-T05).
/// </summary>
public sealed class FakeRemoteWriter : IRemoteWriter
{
    private readonly FakeRemoteReader _reader;

    public FakeRemoteWriter(FakeRemoteReader reader) => _reader = reader;

    /// <summary>If set, the write calls throw this.</summary>
    public Exception? Failure { get; set; }

    /// <summary>The warnings the rename returns (the ones that come with exit code 0).</summary>
    public IReadOnlyList<string> RenameWarnings { get; set; } = [];

    public List<RemoteAddOptions> Added { get; } = [];

    public List<(string Old, string New)> Renamed { get; } = [];

    public List<string> Removed { get; } = [];

    public List<(string Name, RemoteUrlKind Kind, string Url, string Operation)> UrlChanges { get; } = [];

    /// <summary>The contents of the delete plan; tests set this up and look at what reaches the dialog.</summary>
    public RemoteRemovalPlan? Plan { get; set; }

    /// <summary>Was the plan requested BEFORE the deletion? (for the sabotage test)</summary>
    public bool PlanRequestedBeforeRemoval { get; private set; }

    public Task AddAsync(
        string workingDirectory,
        RemoteAddOptions options,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Added.Add(options);
        _reader.Remotes.Add(new GitRemote { Name = options.Name, FetchUrls = [options.Url] });

        return Task.CompletedTask;
    }

    public Task<RemoteRemovalPlan> PrepareRemovalAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (Removed.Count == 0)
        {
            PlanRequestedBeforeRemoval = true;
        }

        return Task.FromResult(Plan ?? new RemoteRemovalPlan
        {
            Remote = _reader.Remotes.First(r => string.Equals(r.Name, name, StringComparison.Ordinal)),
        });
    }

    public async Task<RemoteRemovalPlan> RemoveAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        RemoteRemovalPlan plan =
            await PrepareRemovalAsync(workingDirectory, name, cancellationToken);

        if (Failure is not null)
        {
            throw Failure;
        }

        Removed.Add(name);
        _reader.Remotes.RemoveAll(r => string.Equals(r.Name, name, StringComparison.Ordinal));

        return plan;
    }

    public Task<RemoteRenameResult> RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<RemoteRenameResult>(Failure);
        }

        Renamed.Add((oldName, newName));

        int index = _reader.Remotes.FindIndex(r => string.Equals(r.Name, oldName, StringComparison.Ordinal));
        if (index >= 0)
        {
            _reader.Remotes[index] = _reader.Remotes[index] with { Name = newName };
        }

        return Task.FromResult(new RemoteRenameResult(oldName, newName, RenameWarnings));
    }

    public Task SetUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        UrlChanges.Add((name, kind, url, "set"));

        int index = _reader.Remotes.FindIndex(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        if (index >= 0)
        {
            _reader.Remotes[index] = kind == RemoteUrlKind.Fetch
                ? _reader.Remotes[index] with { FetchUrls = [url] }
                : _reader.Remotes[index] with { PushUrls = [url] };
        }

        return Task.CompletedTask;
    }

    public Task AddUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default)
    {
        UrlChanges.Add((name, kind, url, "add"));
        return Task.CompletedTask;
    }

    public Task RemoveUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default)
    {
        UrlChanges.Add((name, kind, url, "delete"));

        int index = _reader.Remotes.FindIndex(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        if (index >= 0 && kind == RemoteUrlKind.Push)
        {
            _reader.Remotes[index] = _reader.Remotes[index] with { PushUrls = [] };
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Fake of the remote repository delete confirmation (P06-T05).
/// </summary>
public sealed class FakeRemoteRemovalConfirmer : IRemoteRemovalConfirmer
{
    private readonly bool _answer;

    public FakeRemoteRemovalConfirmer(bool answer = true) => _answer = answer;

    public List<RemoteRemovalRequest> Requests { get; } = [];

    public Task<bool> ConfirmAsync(RemoteRemovalRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(_answer);
    }
}

/// <summary>
/// Fake of the side that shows the remote repository management screen (P06-T05).
/// </summary>
public sealed class FakeRemotesPrompt : IRemotesPrompt
{
    public FakeRemoteRemovalConfirmer Confirmer { get; } = new();

    public IRemoteRemovalConfirmer RemovalConfirmer => Confirmer;

    /// <summary>The ViewModel shown; the tests look at its contents.</summary>
    public RemotesViewModel? Shown { get; private set; }

    public Task ShowAsync(RemotesViewModel model)
    {
        Shown = model;
        return Task.CompletedTask;
    }
}

/// <summary>Fake of the fetch writer (P06-T06).</summary>
public sealed class FakeFetchWriter : IFetchWriter
{
    public List<FetchOptions> Fetched { get; } = [];

    public FetchResult Result { get; set; } = new();

    public PrunePreview Preview { get; set; } = new();

    public Task<FetchResult> FetchAsync(
        string workingDirectory,
        FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        Fetched.Add(options);
        return Task.FromResult(Result);
    }

    public Task<PrunePreview> PreviewPruneAsync(
        string workingDirectory,
        string remote,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Preview);
}

/// <summary>Fake of the pull writer (P06-T07).</summary>
public sealed class FakePullWriter : IPullWriter
{
    public List<PullOptions> Pulled { get; } = [];

    /// <summary>The strategy resolved from the settings (decides which option the screen opens with).</summary>
    public ResolvedPullStrategy Configured { get; set; } =
        new(PullStrategy.Merge, PullStrategySource.ApplicationDefault, null);

    public PullResult? Result { get; set; }

    public Task<ResolvedPullStrategy> ResolveStrategyAsync(
        string workingDirectory,
        PullStrategy requested = PullStrategy.Default,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(requested == PullStrategy.Default
            ? Configured
            : new ResolvedPullStrategy(requested, PullStrategySource.UserChoice, null));

    public Task<PullResult> PullAsync(
        string workingDirectory,
        PullOptions options,
        CancellationToken cancellationToken = default)
    {
        Pulled.Add(options);

        return Task.FromResult(Result ?? new PullResult
        {
            Strategy = Configured,
            HeadBefore = "aaaa",
            HeadAfter = "aaaa",
        });
    }
}

/// <summary>Fake of the push writer (P06-T08).</summary>
public sealed class FakePushWriter : IPushWriter
{
    public List<PushOptions> Pushed { get; } = [];

    /// <summary>The plan the screen will build; the lease anchor comes from here too.</summary>
    public PushPlan Plan { get; set; } = new()
    {
        Remote = "origin",
        LocalBranch = "main",
        RemoteBranch = "main",
        RemoteTipObjectId = "aaaabbbbcccc",
        HasUpstream = true,
        RemoteBranches = ["main"],
    };

    public PushResult? Result { get; set; }

    public GitExt.Core.Git.GitException? Failure { get; set; }

    /// <summary>Fail until credentials are supplied (P06-T09 retry flow).</summary>
    public bool FailUntilCredentialsGiven { get; set; }

    /// <summary>The progress steps to report during the call (P06-T10).</summary>
    public IReadOnlyList<GitExt.Core.Git.GitProgress> ReportProgress { get; set; } = [];

    /// <summary>The progress channel passed to the writer — null if it was not passed.</summary>
    public IProgress<GitExt.Core.Git.GitProgress>? SeenProgress { get; private set; }

    /// <summary>The cancellation token passed to the writer.</summary>
    public CancellationToken SeenToken { get; private set; }

    /// <summary>When called, behave as if it had been cancelled (P06-T10).</summary>
    public bool CancelOnRun { get; set; }

    public Task<PushPlan> PlanAsync(
        string workingDirectory,
        string remote,
        string localBranch,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Plan with { Remote = remote, LocalBranch = localBranch });

    public Task<PushResult> PushAsync(
        string workingDirectory,
        PushOptions options,
        CancellationToken cancellationToken = default)
    {
        Pushed.Add(options);
        SeenProgress = options.Progress;
        SeenToken = cancellationToken;

        if (CancelOnRun)
        {
            throw new OperationCanceledException();
        }

        foreach (GitExt.Core.Git.GitProgress step in ReportProgress)
        {
            options.Progress?.Report(step);
        }

        if (Failure is { } error && (!FailUntilCredentialsGiven || options.Credentials is null))
        {
            throw error;
        }

        return Task.FromResult(Result ?? new PushResult
        {
            Refs = [new PushRefResult(' ', "refs/heads/main", "refs/heads/main", "aaa..bbb", null)],
        });
    }

    public string DescribeCommand(PushOptions options) => PushWriter.Describe(options);
}

/// <summary>Fake of the side that shows the push screen (P06-T08).</summary>
public sealed class FakePushPrompt : IPushPrompt
{
    public PushViewModel? Shown { get; private set; }

    public Task ShowAsync(PushViewModel model)
    {
        Shown = model;
        return Task.CompletedTask;
    }
}

/// <summary>Fake of the side that shows the pull/fetch screen (P06-T07).</summary>
public sealed class FakePullPrompt : IPullPrompt
{
    public PullViewModel? Shown { get; private set; }

    public Task ShowAsync(PullViewModel model)
    {
        Shown = model;
        return Task.CompletedTask;
    }
}
