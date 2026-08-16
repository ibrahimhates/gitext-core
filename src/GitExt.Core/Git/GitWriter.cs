using System.Collections.Concurrent;

namespace GitExt.Core.Git;

/// <summary>
/// Runs the git commands that <b>modify</b> the repository (P05-T03).
/// </summary>
/// <remarks>
/// This is the single entrance to the write path: serialisation (P05-T01) and the retry on a lock
/// collision (P05-T02) are combined here. Callers do not have to set the two up by hand — forgetting
/// one of them would be a silent class of bug.
/// </remarks>
public interface IGitWriter
{
    /// <summary>
    /// Runs a write command; it joins the queue and retries on a lock collision.
    /// </summary>
    /// <param name="workingDirectory">The working directory to run the command in.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a write command with environment variables specific to that call (P06-T09).
    /// </summary>
    /// <remarks>
    /// The single use is authentication: <c>GIT_ASKPASS</c> and the secret value it will read. The
    /// password cannot be passed as an argument — the command line is visible to everyone through
    /// <c>ps</c> (see <c>AskPassSession</c>).
    /// </remarks>
    Task<GitResult> RunWithEnvironmentAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        IProgress<GitProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a write command feeding data to <b>stdin</b>.
    /// </summary>
    /// <remarks>
    /// Patches and commit messages <b>cannot</b> be passed as arguments: there is a length limit and
    /// a risk of shell interpretation (ADR-0002).
    /// </remarks>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="standardInput">The text to write to stdin.</param>
    /// <param name="encoding">
    /// Which encoding the text is turned into bytes with; UTF-8 by default. For patches this must be
    /// <b>the file's encoding</b> — git compares the patch against the bytes in the working tree.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string standardInput,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IGitWriter"/>
public sealed class GitWriter : IGitWriter
{
    /// <summary>
    /// The process limit for a write command. The <see cref="GitCommand"/> default is 2 minutes.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED (P05-T07):</b> 2 minutes is not enough. This is a property of the <b>write
    /// path</b>, not of any single command: write commands run the user's hooks, and a hook can do
    /// arbitrary work — a <c>pre-commit</c> test suite, a <c>pre-push</c> build. Putting the limit on
    /// individual command options would mean repeating the same number separately for <c>commit</c>,
    /// <c>push</c>, <c>rebase</c> and <c>merge</c>.
    /// <para>
    /// When the limit is hit the process is killed; measured — the commit <b>is not created</b> and
    /// no <c>index.lock</c> <b>is left behind</b> (git takes the lock after the hook, the same finding
    /// as P05-T02). So a timeout loses no data, it just does not finish the job.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultWriteTimeout = TimeSpan.FromMinutes(10);

    private readonly IGitProcessRunner _runner;
    private readonly IGitWriteQueue _queue;
    private readonly GitLockRetryOptions _retryOptions;
    private readonly TimeSpan _writeTimeout;

    /// <summary>
    /// The working directory → git directory mapping.
    /// </summary>
    /// <remarks>
    /// The queue key is the git directory (worktrees have their own index). Running <c>rev-parse</c>
    /// on every write costs about 1 ms; it is cached anyway, because the call count will rise with
    /// line-level staging (P05-T04).
    /// </remarks>
    private readonly ConcurrentDictionary<string, string> _gitDirectories =
        new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    public GitWriter(
        IGitProcessRunner runner,
        IGitWriteQueue queue,
        GitLockRetryOptions? retryOptions = null,
        TimeSpan? writeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(queue);

        _runner = runner;
        _queue = queue;
        _retryOptions = retryOptions ?? GitLockRetryOptions.Default;
        _writeTimeout = writeTimeout ?? DefaultWriteTimeout;
    }

    public Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(workingDirectory, arguments, null, null, null, cancellationToken);

    public Task<GitResult> RunWithEnvironmentAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        IProgress<GitProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(workingDirectory, arguments, null, environment, progress, cancellationToken);

    public Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string standardInput,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(standardInput);

        return RunCoreAsync(
            workingDirectory,
            arguments,
            (encoding ?? System.Text.Encoding.UTF8).GetBytes(standardInput),
            null,
            null,
            cancellationToken);
    }

    private async Task<GitResult> RunCoreAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte>? standardInput,
        IReadOnlyDictionary<string, string>? environment,
        IProgress<GitProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        string gitDirectory = await ResolveGitDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return await _queue.RunAsync(
            gitDirectory,
            token => GitLockRetry.RunAsync(
                // ⚠️ `RunCheckedAsync` — `RunAsync` DOES NOT THROW on a failing exit, it only logs.
                // On the write path that meant a failed commit counting as a success; a test caught it
                // (a commit with an empty message was rejected but the flow carried on and tried to
                // parse `rev-parse HEAD`).
                // The lock retry depends on this too: with no exception thrown, the retry never fires.
                inner => _runner.RunCheckedAsync(
                    BuildCommand(workingDirectory, arguments, standardInput, environment, progress),
                    inner),
                _retryOptions,
                token),
            cancellationToken).ConfigureAwait(false);
    }

    private GitCommand BuildCommand(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte>? standardInput,
        IReadOnlyDictionary<string, string>? environment,
        IProgress<GitProgress>? progress) =>
        new()
        {
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            StandardInput = standardInput,
            Environment = environment,
            Progress = progress,

            // ⚠️ A write command: `GIT_OPTIONAL_LOCKS=0` is not applied. That flag only turns off
            // "optional" locks; a write's real lock has to be taken anyway.
            IsReadOnly = false,

            Timeout = _writeTimeout,
        };

    private async Task<string> ResolveGitDirectoryAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (_gitDirectories.TryGetValue(workingDirectory, out string? cached))
        {
            return cached;
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--absolute-git-dir"],
            },
            cancellationToken).ConfigureAwait(false);

        string gitDirectory = result.GetStandardOutputText().Trim();

        if (gitDirectory.Length == 0)
        {
            // The queue must not be left without a key: the working directory serves as the fallback
            // key. The serialisation is a little broad, but that beats having NONE at all.
            gitDirectory = workingDirectory;
        }

        _gitDirectories[workingDirectory] = gitDirectory;

        return gitDirectory;
    }
}
