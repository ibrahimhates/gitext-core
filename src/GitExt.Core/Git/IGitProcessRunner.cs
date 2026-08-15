namespace GitExt.Core.Git;

/// <summary>
/// The single gateway that runs <c>git</c> processes.
/// </summary>
/// <remarks>
/// <b>ADR-0002 rule:</b> <c>Process.Start</c> is never called anywhere else in the application.
/// A deterministic environment, logging, timeout and cancellation behaviour all depend on
/// being collected in one place.
/// </remarks>
public interface IGitProcessRunner
{
    /// <summary>
    /// Runs the command and waits for it to finish.
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="GitResult"/> whatever the exit code is; it does not throw.
    /// To turn a failure into an exception, <see cref="GitProcessRunnerExtensions.RunCheckedAsync"/>
    /// is used.
    /// </remarks>
    /// <exception cref="OperationCanceledException">When cancelled or on timeout.</exception>
    Task<GitResult> RunAsync(GitCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the command and yields stdout as NUL-separated chunks, <b>before the process ends</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In a repository with 500k commits, waiting for <c>git log</c> to finish delays the UI's
    /// first screen by seconds. This method yields the first records instantly (P02-T04).
    /// </para>
    /// <para>
    /// Empty chunks are <b>kept</b>: in fixed-field records, dropping an empty field shifts every
    /// following field and the data becomes silently wrong.
    /// </para>
    /// <para>
    /// If the exit code is non-zero, a <see cref="GitException"/> is thrown at the <b>end</b> of
    /// the stream — the chunks produced up to that point are valid.
    /// </para>
    /// </remarks>
    IAsyncEnumerable<string> StreamNulSeparatedAsync(
        GitCommand command,
        CancellationToken cancellationToken = default);
}
