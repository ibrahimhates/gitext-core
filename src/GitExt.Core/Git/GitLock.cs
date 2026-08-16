namespace GitExt.Core.Git;

/// <summary>
/// Information about a lock file sitting in the repository (P05-T02).
/// </summary>
/// <param name="Path">Full path of the lock file.</param>
/// <param name="Age">Time elapsed since the file was created.</param>
public sealed record GitLockInfo(string Path, TimeSpan Age)
{
    /// <summary>
    /// Has the lock been sitting there longer than can be expected for a legitimate operation?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a guess, not proof</b> — and it was deliberately named that way.
    /// Measured: the lock file is <b>empty</b> (no process id), git does not behave differently
    /// for an old lock, therefore there is no reliable answer to the question "did its owner
    /// die".
    /// </para>
    /// <para>
    /// The threshold is based on measurement: a legitimate lock lasts on the order of
    /// <b>milliseconds</b> (a <c>git add</c> of 300 files = 12 ms) and even a slow
    /// <c>pre-commit</c> hook does not extend the lock — the hook runs <b>before</b> the lock is
    /// taken (measured: <c>index.lock</c> was never seen during a hook that slept for 5 seconds).
    /// </para>
    /// </remarks>
    public bool LooksStale => Age > TimeSpan.FromMinutes(5);

    public override string ToString() => $"{Path} ({Age.TotalSeconds:F0} sn)";
}

/// <summary>
/// Inspects lock files and — <b>only on an explicit request</b> — deletes them (P05-T02).
/// </summary>
/// <remarks>
/// <para>
/// <b>A lock is never deleted on its own.</b> Another git process may genuinely be running;
/// deleting the lock while that process is writing the index corrupts the repository's index.
/// </para>
/// <para>
/// <b>GitExtensions was consulted:</b> it has <b>no</b> stale detection either —
/// <c>IndexLockManager</c> only checks "does the file exist" and deletion is tied to the
/// <i>Delete index.lock</i> command the user picks from the menu. That is, the user makes the
/// decision. We do the same, and on top of that we show <b>the age of the lock</b> so the user
/// can decide.
/// </para>
/// </remarks>
public static class GitLock
{
    /// <summary>File name of the index lock.</summary>
    public const string IndexLockName = "index.lock";

    /// <summary>
    /// Inspects the repository's index lock; <see langword="null"/> if there is none.
    /// </summary>
    /// <param name="gitDirectory">
    /// The repository's git directory. Since each worktree has its own index, the directory of
    /// that worktree must be given rather than the common directory.
    /// </param>
    public static GitLockInfo? Inspect(string gitDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);

        string path = Path.Combine(gitDirectory, IndexLockName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            DateTime createdUtc = File.GetLastWriteTimeUtc(path);
            TimeSpan age = DateTime.UtcNow - createdUtc;

            // Clock skew can produce a negative age; showing zero is more honest than telling
            // the user "locked for -3 seconds".
            return new GitLockInfo(path, age > TimeSpan.Zero ? age : TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file may have disappeared between inspection and deletion; the lock is treated
            // as absent.
            return null;
        }
    }

    /// <summary>
    /// Deletes the lock file.
    /// </summary>
    /// <param name="lockInfo">The lock to delete — must first be obtained via <see cref="Inspect"/>.</param>
    /// <param name="userConfirmed">
    /// That the user <b>explicitly</b> confirmed the deletion. If <see langword="false"/>,
    /// <see cref="InvalidOperationException"/> is thrown.
    /// </param>
    /// <remarks>
    /// The confirmation is <b>made mandatory</b> as a parameter: leaving the "never delete
    /// silently" rule in a comment does not stop somebody from calling this method without
    /// consent later on.
    /// </remarks>
    public static void Remove(GitLockInfo lockInfo, bool userConfirmed)
    {
        ArgumentNullException.ThrowIfNull(lockInfo);

        if (!userConfirmed)
        {
            throw new InvalidOperationException(
                "The lock file can only be deleted with the user's explicit consent. "
                + "Another git process may be running.");
        }

        File.Delete(lockInfo.Path);
    }
}

/// <summary>
/// Retry policy on a lock collision (P05-T02).
/// </summary>
/// <remarks>
/// <b>MEASURED:</b> the repository queue (P05-T01) only serializes <i>our</i> writes; the user's
/// terminal or IDE can write to the same repository too. With an external process writing
/// continuously, <b>9</b> out of 30 <c>git add</c> calls needed a retry (at most 6 attempts) and
/// with increasing back-off the failures dropped to <b>zero</b>.
/// </remarks>
public sealed record GitLockRetryOptions
{
    public static GitLockRetryOptions Default { get; } = new();

    /// <summary>Total number of attempts (including the first one).</summary>
    public int MaximumAttempts { get; init; } = 8;

    /// <summary>
    /// The first delay; subsequent attempts wait multiples of it.
    /// </summary>
    /// <remarks>
    /// In the measurement the lock was held for ~10 ms, which is why the first delay is short.
    /// With the default values the total wait is ~0.5 seconds — the user notices no delay.
    /// </remarks>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(15);
}

/// <summary>
/// Helper that retries an operation on a lock collision (P05-T02).
/// </summary>
public static class GitLockRetry
{
    /// <summary>
    /// Runs the operation; on a lock collision it waits and retries.
    /// </summary>
    /// <remarks>
    /// Only <see cref="GitFailureKind.IndexLocked"/> is retried. Other failures propagate as they
    /// are — repeating an authentication failure eight times does nothing but keep the user
    /// waiting.
    /// </remarks>
    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        GitLockRetryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        options ??= GitLockRetryOptions.Default;

        int attempts = Math.Max(1, options.MaximumAttempts);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (GitException exception)
                when (exception.Kind == GitFailureKind.IndexLocked && attempt < attempts)
            {
                await Task.Delay(options.InitialDelay * attempt, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc cref="RunAsync{T}"/>
    public static Task RunAsync(
        Func<CancellationToken, Task> operation,
        GitLockRetryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunAsync<object?>(
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return null;
            },
            options,
            cancellationToken);
    }
}
