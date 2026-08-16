using System.Collections.Concurrent;

namespace GitExt.Core.Git;

/// <summary>
/// <b>Serialises</b> write operations against the same repository (P05-T01).
/// </summary>
public interface IGitWriteQueue
{
    /// <summary>
    /// Runs the operation, making it wait until the other operations writing to the same repository
    /// have finished.
    /// </summary>
    /// <param name="gitDirectory">
    /// The repository's <b>git directory</b> — the output of <c>rev-parse --absolute-git-dir</c>.
    /// <b>Not</b> the common directory (<c>--git-common-dir</c>), because worktrees have their own
    /// index.
    /// </param>
    /// <param name="operation">The write operation to run when its turn comes.</param>
    /// <param name="cancellationToken">The cancellation token, which applies while waiting too.</param>
    Task<T> RunAsync<T>(
        string gitDirectory,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="RunAsync{T}"/>
    Task RunAsync(
        string gitDirectory,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The queue that enforces the one-writer-per-repository rule (P05-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED — why it is needed:</b> git <b>does not wait</b> on a concurrent write, it fails
/// immediately. Running 8 parallel <c>git add</c> calls in the same repository, <b>7 of them</b>
/// failed with <c>fatal: Unable to create '…/index.lock': File exists</c>.
/// </para>
/// <para>
/// <b>The lock's scope is the git directory, not the common directory.</b> Measured: running
/// <c>git add</c> concurrently in two worktrees produced <b>no collision at all</b> — each worktree
/// has its own index (<c>.git/worktrees/&lt;name&gt;/index</c>). Keying on the common directory would
/// needlessly stop the user working in two worktrees in parallel.
/// </para>
/// <para>
/// ⚠️ <b>Ref writes have a different scope:</b> branches and tags live in the common directory
/// (measured in Phase 02). When the ref-writing commands arrive in Phase 06 those operations will need
/// the common directory as their key — which requires no change here, since this class takes the key
/// from outside.
/// </para>
/// <para>
/// <b>Reads DO NOT join the queue.</b> Measured: with an <c>index.lock</c> file present,
/// <c>status</c>, <c>log</c>, <c>diff</c>, <c>diff --cached</c>, <c>show</c> and <c>for-each-ref</c>
/// all work fine (when git cannot take an optional lock it silently gives up on it).
/// </para>
/// </remarks>
public sealed class GitWriteQueue : IGitWriteQueue, IDisposable
{
    /// <summary>
    /// Path comparison: case-sensitive on Linux, not on Windows and macOS.
    /// </summary>
    private static readonly StringComparer _pathComparer =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _queues = new(_pathComparer);

    private bool _disposed;

    /// <summary>The number of repositories currently holding a queue (for diagnostics).</summary>
    public int TrackedRepositories => _queues.Count;

    public async Task<T> RunAsync<T>(
        string gitDirectory,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        SemaphoreSlim queue = _queues.GetOrAdd(Normalize(gitDirectory), _ => new SemaphoreSlim(1, 1));

        await queue.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            queue.Release();
        }
    }

    public Task RunAsync(
        string gitDirectory,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunAsync<object?>(
            gitDirectory,
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
    }

    /// <summary>
    /// Brings the path into a form usable as a key.
    /// </summary>
    /// <remarks>
    /// Calls arriving for the same repository in two different spellings (<c>/repo/.git</c> and
    /// <c>/repo/.git/</c>) must land in the same queue; otherwise the serialisation would be
    /// <b>silently</b> disabled.
    /// </remarks>
    private static string Normalize(string gitDirectory)
    {
        string full = Path.GetFullPath(gitDirectory);

        return full.Length > 1
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (SemaphoreSlim queue in _queues.Values)
        {
            queue.Dispose();
        }

        _queues.Clear();
    }
}
