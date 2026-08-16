using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// File-level stage / unstage operations (P05-T03).
/// </summary>
public interface IStagingWriter
{
    /// <summary>Adds the given paths to the index (<c>git add</c>).</summary>
    /// <remarks>Deleted files are included too: deletion is a change as well.</remarks>
    Task StageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the given paths from the index; doesn't touch the working tree.</summary>
    Task UnstageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the selected lines/hunks into the index — <b>partial stage</b> (P05-T04).
    /// </summary>
    /// <param name="workingDirectory">Repository working directory.</param>
    /// <param name="diff">
    /// Difference between the working tree and the index (<c>git diff</c>). The patch is
    /// generated from this.
    /// </param>
    /// <param name="selection">Lines to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="contentEncoding">
    /// Encoding of the file; defaults to UTF-8. If the diff was read with
    /// <c>DiffOptions.ContentEncoding</c>, the same one should be given here.
    /// </param>
    Task StagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverts the selected lines/hunks out of the index — <b>partial unstage</b> (P05-T04).
    /// </summary>
    /// <param name="workingDirectory">Repository working directory.</param>
    /// <param name="diff">
    /// Difference between the index and <c>HEAD</c> (<c>git diff --cached</c>). The patch is
    /// generated from this and applied <b>in reverse</b>.
    /// </param>
    /// <param name="selection">Lines to revert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="contentEncoding">Encoding of the file; defaults to UTF-8.</param>
    Task UnstagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops tracking the file but <b>leaves it on disk</b> (<c>git rm --cached</c>).
    /// </summary>
    /// <remarks>
    /// This is <b>not unstage</b>: for a tracked file the result is the file being staged as
    /// <i>deleted</i>. It stands as a separate command because it's an operation the user might
    /// deliberately want (e.g. removing a config file that was added by mistake from the
    /// repository).
    /// </remarks>
    Task UntrackAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IStagingWriter"/>
/// <remarks>
/// <para>
/// <b>MEASURED (P05-T03) — unstage cannot be done with a single command:</b>
/// </para>
/// <list type="table">
/// <item>
/// <term>No HEAD (before the first commit)</term>
/// <description><c>git restore --staged</c> <b>crashes</b>:
/// <c>fatal: could not resolve 'HEAD'</c> (exit 128). <c>git rm --cached</c> is required.</description>
/// </item>
/// <item>
/// <term>HEAD exists, file not in HEAD</term>
/// <description><c>restore --staged</c> is correct: the file goes back to untracked.</description>
/// </item>
/// <item>
/// <term>HEAD exists, file in HEAD</term>
/// <description><c>restore --staged</c> is correct. <c>rm --cached</c> would be <b>wrong</b>:
/// the file gets staged as <i>deleted</i> — the user asks for unstage and sees a deletion instead.</description>
/// </item>
/// </list>
/// </remarks>
public sealed class StagingWriter : IStagingWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public StagingWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public Task StageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default)
    {
        if (IsEmpty(paths))
        {
            return Task.CompletedTask;
        }

        // `-A`: also pick up a deletion if the file was removed. Without it, deleted files
        // would be silently skipped.
        return _writer.RunAsync(
            workingDirectory,
            ["add", "-A", "--", .. Values(paths)],
            cancellationToken);
    }

    public async Task UnstageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default)
    {
        if (IsEmpty(paths))
        {
            return;
        }

        bool hasHead = await HasCommitsAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        // If there's no HEAD, `restore --staged` crashes (measured); the only fix is
        // `rm --cached`. In that case the file isn't in HEAD to begin with, so there's also no
        // risk of "staging as deleted".
        IReadOnlyList<string> arguments = hasHead
            ? ["restore", "--staged", "--", .. Values(paths)]
            : ["rm", "--cached", "--quiet", "--", .. Values(paths)];

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default) =>
        ApplyPatchAsync(
            workingDirectory, diff, selection, PatchDirection.Stage, contentEncoding, cancellationToken);

    public Task UnstagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default) =>
        ApplyPatchAsync(
            workingDirectory, diff, selection, PatchDirection.Unstage, contentEncoding, cancellationToken);

    /// <summary>
    /// Builds the patch and applies it with <c>git apply --cached</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>--cached</c>: the patch is applied <b>only to the index</b>; the working tree is not
    /// touched — that's the definition of partial stage.
    /// </para>
    /// <para>
    /// The patch is passed via <b>stdin</b>; no temp file, no shell interpretation.
    /// </para>
    /// <para>
    /// ⚠️ <c>--recount</c> is <b>not used</b>: it corrects wrong counts and makes the patch get
    /// accepted anyway, closing off the one verification git offers us (measured).
    /// </para>
    /// </remarks>
    private async Task ApplyPatchAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        PatchDirection direction,
        System.Text.Encoding? contentEncoding,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string? patch = PatchBuilder.Build(diff, selection, direction);

        if (patch is null)
        {
            // Nothing was selected: silently do nothing.
            return;
        }

        List<string> arguments = ["apply", "--cached"];

        if (direction == PatchDirection.Unstage)
        {
            arguments.Add("--reverse");
        }

        arguments.Add("-");

        // The patch must be byte-encoded with the same encoding the diff was read with: git
        // compares it against the bytes in the working tree (the write side of the encoding
        // architecture from P04-T07).
        await _writer
            .RunAsync(workingDirectory, arguments, patch, contentEncoding, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task UntrackAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default)
    {
        if (IsEmpty(paths))
        {
            return Task.CompletedTask;
        }

        return _writer.RunAsync(
            workingDirectory,
            ["rm", "--cached", "--quiet", "--", .. Values(paths)],
            cancellationToken);
    }

    /// <summary>
    /// Does the repository have at least one commit?
    /// </summary>
    /// <remarks>
    /// Asked up front rather than deciding by inspecting an error message: the message text can
    /// vary by git version, whereas <c>rev-parse</c> costs about ~1 ms.
    /// </remarks>
    private async Task<bool> HasCommitsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", "HEAD"],

                // Unborn HEAD isn't an error, it's information.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    private static bool IsEmpty(IReadOnlyList<RepositoryPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // Running `git add -A --` with no path would stage the ENTIRE repository; an empty
        // list means "do nothing".
        return paths.Count == 0;
    }

    private static IEnumerable<string> Values(IReadOnlyList<RepositoryPath> paths) =>
        paths.Select(path => path.Value);
}
