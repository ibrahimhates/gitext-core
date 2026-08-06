using System.Runtime.CompilerServices;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Storage;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.Fakes;

/// <summary>
/// ViewModel testleri için sahte depo konumlandırıcı.
/// </summary>
/// <remarks>
/// ViewModel'lar gerçek <c>git</c> süreci başlatmadan test edilir (ADR-0004). Gerçek
/// <c>git</c> davranışı <c>GitExt.Core.Tests</c>'te zaten kapsamlı biçimde doğrulanıyor;
/// burada test edilen şey <b>ViewModel mantığı</b>.
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
/// ViewModel testleri için sahte commit geçmişi okuyucusu.
/// </summary>
public sealed class FakeCommitLogReader : ICommitLogReader
{
    private readonly IReadOnlyList<CommitInfo> _commits;
    private readonly Exception? _failure;

    /// <summary>Akış sırasında kaç kez beklendi — toplu güncellemeyi test etmek için.</summary>
    public int StreamCallCount { get; private set; }

    public FakeCommitLogReader(IReadOnlyList<CommitInfo>? commits = null, Exception? failure = null)
    {
        _commits = commits ?? [];
        _failure = failure;
    }

    public Task<IReadOnlyList<CommitInfo>> ReadAsync(
        string workingDirectory,
        CommitLogQuery query,
        CancellationToken cancellationToken = default) =>
        _failure is not null
            ? Task.FromException<IReadOnlyList<CommitInfo>>(_failure)
            : Task.FromResult(_commits);

    public async IAsyncEnumerable<CommitInfo> StreamAsync(
        string workingDirectory,
        CommitLogQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamCallCount++;

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
/// ViewModel testleri için sahte ref okuyucusu.
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
/// ViewModel testleri için sahte imza okuyucusu.
/// </summary>
/// <remarks>
/// Varsayılan olarak imzasız döner: testlerin çoğu imzayla ilgilenmiyor ve gerçek imza
/// davranışı <c>GitExt.Core.Tests</c>'te gerçek SSH anahtarlarıyla doğrulanıyor.
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
/// ViewModel testleri için sahte diff okuyucusu.
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
/// Bellekte tutan sahte "son açılanlar" deposu — disk dokunmadan ViewModel testi için.
/// </summary>
public sealed class FakeRecentRepositoryStore : IRecentRepositoryStore
{
    private readonly List<string> _repositories;

    public FakeRecentRepositoryStore(params string[] initial)
    {
        _repositories = [.. initial];
    }

    public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([.. _repositories]);

    public Task AddAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        _repositories.Remove(workingDirectory);
        _repositories.Insert(0, workingDirectory);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        _repositories.Remove(workingDirectory);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test verisi üreteçleri.
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
    /// Doğrusal bir geçmiş üretir; en yeniden en eskiye, topolojik sırada.
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

    /// <summary>Test için dosya diff'i üretir.</summary>
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

    /// <summary>Sıra numarasından deterministik, geçerli bir SHA üretir.</summary>
    public static string Sha(int index) => index.ToString("x40", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Hiç dal/tag içermeyen, doğmamış bir depo ref durumu.</summary>
    public static RepositoryRefs NoRefs() =>
        new()
        {
            Head = new HeadState { IsDetached = false, IsUnborn = true },
            LocalBranches = [],
            RemoteBranches = [],
            Tags = [],
            Remotes = [],
        };

    /// <summary>Verilen ref'lerden bir <see cref="RepositoryRefs"/> kurar.</summary>
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
    /// <c>refs/remotes/&lt;uzak&gt;/HEAD</c> — klonlanan her depoda bulunan sembolik ref.
    /// </summary>
    /// <remarks>
    /// Kısa ad bilerek <c>"origin"</c>: git bu ref'i <c>origin/HEAD</c> değil <c>origin</c>
    /// olarak kısaltıyor (ölçüldü). Sahte veri gerçeği yansıtmazsa test yanlış şeyi korur.
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
    /// Bir tag üretir.
    /// </summary>
    /// <remarks>
    /// <paramref name="annotated"/> ise <c>ObjectId</c> (tag nesnesi) ile
    /// <c>TargetCommit</c> (çözülmüş commit) ayrışır — gerçek <c>for-each-ref</c>
    /// çıktısındaki durum budur.
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
/// Bellekte çalışan sahte durum okuyucu — <c>git</c>'e dokunmadan ViewModel testi için.
/// </summary>
/// <remarks>
/// Stage/unstage işlemleri <see cref="FakeStagingWriter"/> ile bu nesnenin durumunu
/// değiştiriyor; böylece "stage'ledikten sonra liste ne oluyor" gerçekten test edilebiliyor.
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

    /// <summary>Okuma sırasında çalışır; izleme askısını gözlemlemek için (P05-T14).</summary>
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
/// Sahte staging yazıcısı: <see cref="FakeStatusReader"/>'ın girdilerini yerinde taşır.
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

    /// <summary>Kısmi staging'e geçirilen kodlama (P05-T16).</summary>
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
/// Sahte commit yazıcısı — <c>git</c>'e dokunmadan commit akışını test etmek için.
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

    /// <summary>Sonuçta döndürülecek çıktı (hook çıktısı benzetimi).</summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>Hook mesajı değiştirmiş gibi davranır.</summary>
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

        // Commit edilen dosyalar çalışma dizininden düşer.
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
/// P05-T13 — sahte commit mesajı okuyucusu (geçmiş, HEAD mesajı, şablon).
/// </summary>
public sealed class FakeCommitMessageReader : ICommitMessageReader
{
    /// <summary>En yeniden eskiye sıralı mesajlar.</summary>
    public List<string> Recent { get; } = [];

    /// <summary>"Yalnızca benim" filtresiyle dönecek mesajlar; boşsa <see cref="Recent"/>.</summary>
    public List<string> Mine { get; } = [];

    public string? HeadMessage { get; set; }

    public CommitTemplate? Template { get; set; }

    public string CommentCharacter { get; set; } = "#";

    /// <summary>Kaç kez geçmiş okundu — menü açılmadan okunmadığını doğrulamak için.</summary>
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
/// P05-T13 — sahte taslak deposu.
/// </summary>
/// <remarks>
/// Depo yolu başına ayrı tutuluyor: gerçek deponun worktree başına ayrı davranışı
/// (<c>CommitMessageTests</c>'te gerçek git ile doğrulandı) burada da karşılığını bulsun.
/// </remarks>
public sealed class FakeCommitMessageStore : ICommitMessageStore
{
    public Dictionary<string, string> Drafts { get; } = new(StringComparer.Ordinal);

    /// <summary>git'in hazırladığı mesaj (merge/cherry-pick) — taslaktan önce gelir.</summary>
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
/// Testlerin olayları elle tetikleyebildiği sahte izleyici (P05-T14).
/// </summary>
/// <remarks>
/// Gerçek <see cref="RepositoryWatcher"/> dosya sistemi ve zamanlayıcı bekliyor; ViewModel
/// tarafında test edilen şey <b>olaya nasıl tepki verildiği</b>, olayın nereden geldiği değil.
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

    /// <summary>Şu an askıda mı? Kendi okumalarımızın olay üretmemesi buna bağlı.</summary>
    public bool IsSuspended => SuspendDepth > 0;

    public int SuspendDepth { get; private set; }

    /// <summary>Askıdayken kaç olay atıldı? Sıfırdan büyükse döngü riski var demektir.</summary>
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

    /// <summary>Bir değişiklik olayını tetikler; askıdayken sessizce yutulur.</summary>
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
/// Yıkıcı işlemleri kaydeden sahte yazar (P05-T15).
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

    /// <summary>Geri yazılan yollar; "geri al" gerçekten çalıştı mı?</summary>
    public List<string> Restored { get; } = [];

    /// <summary>Kaç yedeğin nesnesi budanmış sayılsın (kısmi kurtarma senaryosu).</summary>
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
/// Onayı testin belirlediği sahte onaylayıcı (P05-T15).
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
/// Dal yazıcısının sahtesi (P06-T01).
/// </summary>
public sealed class FakeBranchWriter : IBranchWriter
{
    /// <summary>Ayarlanırsa <see cref="CreateAsync"/> bunu fırlatır.</summary>
    public Exception? Failure { get; set; }

    /// <summary>git'in kurduğu varsayılan upstream.</summary>
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

    /// <summary>Ayarlanırsa geçiş bu sonucu döndürür.</summary>
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

    /// <summary>Zorlanmadan silme denendiğinde bunu fırlatır (iki turlu akış testi).</summary>
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
/// Dal oluşturma diyaloğunun sahtesi (P06-T01).
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
/// Dala geçme diyaloğunun sahtesi (P06-T02).
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
/// Dal düzenleme diyaloglarının sahtesi (P06-T03).
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

    /// <summary>İkinci turda (birleştirilmemiş) verilecek karar; yoksa ilki kullanılır.</summary>
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
/// Süren işlem okuyucusunun sahtesi (P06-T04).
/// </summary>
public sealed class FakeInProgressOperationReader : IInProgressOperationReader
{
    private readonly InProgressOperation _operation;

    public FakeInProgressOperationReader(InProgressOperation operation = InProgressOperation.None) =>
        _operation = operation;

    public Task<InProgressOperation> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_operation);
}

/// <summary>
/// Uzak depo okuyucusunun sahtesi (P06-T05).
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
/// Uzak depo yazıcısının sahtesi (P06-T05).
/// </summary>
public sealed class FakeRemoteWriter : IRemoteWriter
{
    private readonly FakeRemoteReader _reader;

    public FakeRemoteWriter(FakeRemoteReader reader) => _reader = reader;

    /// <summary>Ayarlanırsa yazma çağrıları bunu fırlatır.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Yeniden adlandırmanın döndüreceği uyarılar (çıkış kodu 0 ile gelen).</summary>
    public IReadOnlyList<string> RenameWarnings { get; set; } = [];

    public List<RemoteAddOptions> Added { get; } = [];

    public List<(string Old, string New)> Renamed { get; } = [];

    public List<string> Removed { get; } = [];

    public List<(string Name, RemoteUrlKind Kind, string Url, string Operation)> UrlChanges { get; } = [];

    /// <summary>Silme planının içeriği; testler bunu kurup diyaloğa ne gittiğine bakıyor.</summary>
    public RemoteRemovalPlan? Plan { get; set; }

    /// <summary>Plan silmeden ÖNCE mi istendi? (sabotaj testi için)</summary>
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
/// Uzak depo silme onayının sahtesi (P06-T05).
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
/// Uzak depo yönetimi ekranını gösteren tarafın sahtesi (P06-T05).
/// </summary>
public sealed class FakeRemotesPrompt : IRemotesPrompt
{
    public FakeRemoteRemovalConfirmer Confirmer { get; } = new();

    public IRemoteRemovalConfirmer RemovalConfirmer => Confirmer;

    /// <summary>Gösterilen ViewModel; testler içeriğine bakıyor.</summary>
    public RemotesViewModel? Shown { get; private set; }

    public Task ShowAsync(RemotesViewModel model)
    {
        Shown = model;
        return Task.CompletedTask;
    }
}

/// <summary>Fetch yazıcısının sahtesi (P06-T06).</summary>
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

/// <summary>Pull yazıcısının sahtesi (P06-T07).</summary>
public sealed class FakePullWriter : IPullWriter
{
    public List<PullOptions> Pulled { get; } = [];

    /// <summary>Ayarlardan çözülen strateji (ekranın hangi seçenekle açılacağını belirler).</summary>
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

/// <summary>Push yazıcısının sahtesi (P06-T08).</summary>
public sealed class FakePushWriter : IPushWriter
{
    public List<PushOptions> Pushed { get; } = [];

    /// <summary>Ekranın kuracağı plan; kira çıpası da buradan geliyor.</summary>
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

    /// <summary>Kimlik verilene kadar başarısız ol (P06-T09 tekrar-deneme akışı).</summary>
    public bool FailUntilCredentialsGiven { get; set; }

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

/// <summary>Push ekranını gösteren tarafın sahtesi (P06-T08).</summary>
public sealed class FakePushPrompt : IPushPrompt
{
    public PushViewModel? Shown { get; private set; }

    public Task ShowAsync(PushViewModel model)
    {
        Shown = model;
        return Task.CompletedTask;
    }
}

/// <summary>Pull/Fetch ekranını gösteren tarafın sahtesi (P06-T07).</summary>
public sealed class FakePullPrompt : IPullPrompt
{
    public PullViewModel? Shown { get; private set; }

    public Task ShowAsync(PullViewModel model)
    {
        Shown = model;
        return Task.CompletedTask;
    }
}
