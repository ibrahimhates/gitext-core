using System.Runtime.CompilerServices;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Storage;

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
