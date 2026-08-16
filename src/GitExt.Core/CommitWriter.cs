using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Commit creation options (P05-T06).
/// </summary>
public sealed record CommitOptions
{
    public static CommitOptions Default { get; } = new();

    /// <summary>Overwrite the last commit (<c>--amend</c>).</summary>
    /// <remarks>
    /// ⚠️ Rewrites history on a published commit. The UI should inform the user of this
    /// (P05-T15).
    /// </remarks>
    public bool Amend { get; init; }

    /// <summary>Append a <c>Signed-off-by</c> line to the message.</summary>
    public bool SignOff { get; init; }

    /// <summary>Create a commit even with no changes (<c>--allow-empty</c>).</summary>
    public bool AllowEmpty { get; init; }

    /// <summary>Allow an empty message (<c>--allow-empty-message</c>).</summary>
    /// <remarks>
    /// <b>MEASURED:</b> <c>git commit</c> with an empty message exits with <b>1</b>
    /// (<i>Aborting commit due to empty commit message</i>). Without this flag, a commit
    /// without a message cannot be created.
    /// </remarks>
    public bool AllowEmptyMessage { get; init; }

    /// <summary>
    /// Skip validation hooks (<c>--no-verify</c>). <b>Off by default.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MEASURED:</b> a failing <c>pre-commit</c> hook stops the commit with exit code 1 and
    /// writes its output to <c>stderr</c>. This means disabling validation the user set up —
    /// the UI should show a visible warning while it's on (per the plan).
    /// </para>
    /// <para>
    /// ⚠️ <b>NOT "skip all hooks".</b> Measured (P05-T07): <c>--no-verify</c> only skips
    /// <c>pre-commit</c> and <c>commit-msg</c>; <c>prepare-commit-msg</c> and
    /// <c>post-commit</c> <b>still run</b>. So even with this flag on, the message can still
    /// change (<see cref="CommitResult.MessageChanged"/>) and output can still appear.
    /// </para>
    /// </remarks>
    public bool SkipHooks { get; init; }

    /// <summary>Override the author; format is <c>Name Surname &lt;email&gt;</c>.</summary>
    /// <remarks>
    /// <b>MEASURED:</b> only changes the <i>author</i> field; the <i>committer</i> stays the
    /// user's own identity — this is git's correct behavior.
    /// </remarks>
    public string? Author { get; init; }

    /// <summary>Sign the commit with GPG/SSH (<c>-S</c>).</summary>
    public bool Sign { get; init; }

    /// <summary>Key to use for signing; if empty, git's configuration applies.</summary>
    public string? SigningKey { get; init; }
}

/// <summary>
/// Result of a completed commit (P05-T07).
/// </summary>
/// <remarks>
/// Returning only a <see cref="CommitId"/> was <b>silently</b> swallowing two pieces of
/// information: what the hooks wrote, and what the hooks changed in the message.
/// </remarks>
public sealed record CommitResult
{
    /// <summary>Id of the resulting commit.</summary>
    public required CommitId Id { get; init; }

    /// <summary>The message that actually went into the commit (read back via <c>%B</c>).</summary>
    public required string Message { get; init; }

    /// <summary>The message the caller supplied.</summary>
    public required string RequestedMessage { get; init; }

    /// <summary>
    /// Diagnostic output of <c>git commit</c> — <b>including hook output</b>.
    /// </summary>
    /// <remarks>
    /// Raw text. Should be passed through <see cref="GitOutputText.CleanForDisplay"/> before
    /// showing (ANSI codes and <c>\r</c> progress lines come through).
    /// <para>
    /// <b>MEASURED:</b> even when the commit <b>succeeds</b>, hooks write here — warnings from
    /// a successful <c>pre-commit</c>, output from <c>post-commit</c>. Previously this result
    /// was never returned, so all of it was lost.
    /// </para>
    /// </remarks>
    public required string Output { get; init; }

    /// <summary>Is there any output to show?</summary>
    public bool HasOutput => Output.Length > 0;

    /// <summary>
    /// Is there anything worth telling the user? (output, or a changed message)
    /// </summary>
    /// <remarks>
    /// Popping up an empty window after every commit in a hook-free repository would be noise
    /// the user learns to dismiss, and then dismisses the truly important case along with it.
    /// Measured: in a hook-free successful commit, output is <b>completely empty</b>, so this
    /// distinction works in practice.
    /// </remarks>
    public bool NeedsReporting => HasOutput || MessageChanged;

    /// <summary>
    /// Does the message that went into the commit differ from the requested message?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MEASURED:</b> <c>prepare-commit-msg</c> and <c>commit-msg</c> hooks can edit the
    /// message file in place (adding a <c>Change-Id</c> is the most common example), and the
    /// result goes straight into the commit. The user <b>should see</b> that a message
    /// different from what they wrote was recorded.
    /// </para>
    /// <para>
    /// ⚠️ The difference doesn't necessarily come from hooks: <c>--signoff</c> also appends a
    /// line to the message. That's why this is named "message changed", not "hook changed it"
    /// — we're not asserting the cause. Only <c>--cleanup=whitespace</c>'s own normalization
    /// (trailing line whitespace, leading/trailing blank lines) does not count as a difference;
    /// that's the behavior we want.
    /// </para>
    /// </remarks>
    public bool MessageChanged =>
        !string.Equals(Normalize(Message), Normalize(RequestedMessage), StringComparison.Ordinal);

    /// <summary>
    /// The same normalization <c>--cleanup=whitespace</c> performs: trailing whitespace on each
    /// line and leading/trailing blank lines are dropped.
    /// </summary>
    private static string Normalize(string message) =>
        string.Join(
                '\n',
                message
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Split('\n')
                    .Select(line => line.TrimEnd()))
            .Trim('\n');
}

/// <summary>
/// Creates commits (P05-T06).
/// </summary>
public interface ICommitWriter
{
    /// <summary>
    /// Creates a commit from the changes in the index and returns the new commit's id.
    /// </summary>
    /// <param name="workingDirectory">Repository working directory.</param>
    /// <param name="message">Commit message; passed via <b>stdin</b>.</param>
    /// <param name="options">Options; <see langword="null"/> means defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CommitResult> CommitAsync(
        string workingDirectory,
        string message,
        CommitOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICommitWriter"/>
public sealed class CommitWriter : ICommitWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public CommitWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<CommitResult> CommitAsync(
        string workingDirectory,
        string message,
        CommitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(message);

        options ??= CommitOptions.Default;

        // `-F -`: message from stdin. Passing it as an argument would hit length limits and
        // expose the user's text to shell interpretation (ADR-0002).
        List<string> arguments = ["commit", "-F", "-"];

        // ⚠️ `--cleanup=whitespace` is given EXPLICITLY: the user's `commit.cleanup` setting
        // could change behavior and trim the message unexpectedly. Measured: in this mode,
        // lines starting with `#` are PRESERVED (issue references aren't lost), only excess
        // leading/trailing whitespace is cleaned up.
        arguments.Add("--cleanup=whitespace");

        if (options.Amend)
        {
            arguments.Add("--amend");
        }

        if (options.SignOff)
        {
            arguments.Add("--signoff");
        }

        if (options.AllowEmpty)
        {
            arguments.Add("--allow-empty");
        }

        if (options.AllowEmptyMessage)
        {
            arguments.Add("--allow-empty-message");
        }

        if (options.SkipHooks)
        {
            arguments.Add("--no-verify");
        }


        if (options.Author is { Length: > 0 } author)
        {
            arguments.Add($"--author={author}");
        }

        if (options.Sign)
        {
            arguments.Add(options.SigningKey is { Length: > 0 } key ? $"-S{key}" : "-S");
        }

        // No process timeout is given here: hooks being able to run arbitrarily long is a
        // property of the WRITE PATH, not of a single command (see GitWriter.DefaultWriteTimeout).
        GitResult result = await _writer.RunAsync(
                workingDirectory, arguments, message, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        (CommitId id, string storedMessage) =
            await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        return new CommitResult
        {
            Id = id,
            Message = storedMessage,
            RequestedMessage = message,

            // ⚠️ Output is carried even when the command SUCCEEDS: hooks also write on the
            // success path (warnings, `post-commit`). Discarding this would mean swallowing
            // what the user's validation is saying — this was ADR-0002's rationale for
            // choosing the CLI.
            Output = result.StandardError,
        };
    }

    /// <summary>
    /// Reads the new commit's id <b>and</b> its stored message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>git commit</c> output is <b>human-readable</b> (<c>[main ec6c0d6] subject</c>) and
    /// unparsed (project rule). The id is obtained with a separate read.
    /// </para>
    /// <para>
    /// The message is also <b>read back</b>: <c>prepare-commit-msg</c> and <c>commit-msg</c>
    /// hooks can change the message, so the text we sent might not match what went into the
    /// commit. Both are obtained in one call — separator <c>%x00</c>, because a commit message
    /// <b>cannot contain</b> a NUL byte (measured in P02-T04, git rejects it).
    /// </para>
    /// </remarks>
    private async Task<(CommitId Id, string Message)> ReadHeadAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["log", "-1", "--format=%H%x00%B"],
            },
            cancellationToken).ConfigureAwait(false);

        string[] fields = result.SplitStandardOutputAtNulPreservingEmpty();

        if (fields.Length < 2)
        {
            throw new GitException(
                GitFailureKind.Unknown,
                "The commit was created but its id could not be read.",
                "git log -1 --format=%H%x00%B",
                result.ExitCode,
                result.StandardError);
        }

        // `git log` appends its own line ending after the format; the message has its own end too.
        return (CommitId.Parse(fields[0].Trim()), fields[1].TrimEnd('\n'));
    }
}
