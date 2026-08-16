using System.Collections.Concurrent;
using System.Text;
using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Where the message loaded into the box came from (P05-T13).</summary>
public enum CommitMessageSource
{
    /// <summary>There is nothing to load.</summary>
    None,

    /// <summary>A draft the user left half-finished.</summary>
    Draft,

    /// <summary>
    /// The message git prepared (<c>.git/MERGE_MSG</c>): merge, cherry-pick, revert.
    /// </summary>
    Pending,
}

/// <summary>The message to load into the box, and where it came from.</summary>
public sealed record PendingCommitMessage(string Text, CommitMessageSource Source)
{
    public static PendingCommitMessage None { get; } = new(string.Empty, CommitMessageSource.None);

    public bool HasText => Text.Length > 0;
}

/// <summary>
/// Keeps a half-finished commit message even if the application is closed (P05-T13).
/// </summary>
/// <remarks>
/// <para>
/// The draft is kept <b>in the repository directory</b> (<c>.git/GITEXT_COMMITMESSAGE</c>), not in
/// the application's settings file: the message belongs to the work being done in that repository at
/// that moment. Keeping it in the settings would leave an orphaned text behind when the repository
/// is deleted, and would mix up two worktrees.
/// </para>
/// <para>
/// ⚠️ <b>The git directory, NOT the common directory</b> (the P02-T06 distinction):
/// <c>MERGE_MSG</c> and the index are per worktree, while refs and config are shared. Putting the
/// draft in the common directory would mix up the messages of a user working in two worktrees at
/// once.
/// </para>
/// <para>
/// <c>COMMIT_EDITMSG</c> is <b>not used</b>: git overwrites it on every commit (measured), so a
/// draft written there would silently be lost. GitExtensions uses its own file
/// (<c>COMMITMESSAGE</c>) for the same reason.
/// </para>
/// </remarks>
public interface ICommitMessageStore
{
    /// <summary>
    /// Reads the message to load into the box: first git's prepared one, then the draft.
    /// </summary>
    Task<PendingCommitMessage> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Writes the draft to disk; when the text is empty the draft is deleted.</summary>
    Task SaveDraftAsync(
        string workingDirectory,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the draft (after a successful commit).</summary>
    Task ClearDraftAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICommitMessageStore"/>
public sealed class CommitMessageStore : ICommitMessageStore
{
    /// <summary>The name of the draft file.</summary>
    /// <remarks>
    /// The prefix is deliberate: files under <c>.git</c> are git's namespace, and whose file ours is
    /// should be readable from its name. Measured — a foreign file under <c>.git</c> does not affect
    /// the output of <c>git status</c> or <c>git fsck</c>.
    /// </remarks>
    public const string DraftFileName = "GITEXT_COMMITMESSAGE";

    /// <summary>The message file git prepares for merge/cherry-pick/revert.</summary>
    public const string PendingFileName = "MERGE_MSG";

    private readonly IGitProcessRunner _runner;
    private readonly IGitConfigReader _config;

    private readonly ConcurrentDictionary<string, string> _gitDirectories = new(StringComparer.Ordinal);

    public CommitMessageStore(IGitProcessRunner runner, IGitConfigReader config)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(config);

        _runner = runner;
        _config = config;
    }

    public async Task<PendingCommitMessage> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string? gitDirectory = await ResolveGitDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (gitDirectory is null)
        {
            return PendingCommitMessage.None;
        }

        // The order matters: when git is in the middle of a merge, the message it prepared wins.
        // MEASURED: MERGE_MSG is written not only on a conflict but also on a conflict-free `--no-ff`
        // merge, and it appears on a cherry-pick conflict too (with the commit's own message); once
        // git commit succeeds, git deletes it ITSELF.
        string pendingPath = Path.Combine(gitDirectory, PendingFileName);

        if (File.Exists(pendingPath))
        {
            string? pending = await ReadPendingAsync(workingDirectory, pendingPath, cancellationToken)
                .ConfigureAwait(false);

            if (pending is { Length: > 0 })
            {
                return new PendingCommitMessage(pending, CommitMessageSource.Pending);
            }
        }

        string draftPath = Path.Combine(gitDirectory, DraftFileName);

        if (!File.Exists(draftPath))
        {
            return PendingCommitMessage.None;
        }

        try
        {
            // The draft is OUR file and is always written as UTF-8; there is no guessing here.
            // Comment lines are not stripped either: a `#123` line the user wrote themselves is their
            // text (the same reasoning as choosing `--cleanup=whitespace` in P05-T06).
            string draft = await File.ReadAllTextAsync(draftPath, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            return draft.Trim().Length == 0
                ? PendingCommitMessage.None
                : new PendingCommitMessage(draft, CommitMessageSource.Draft);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A draft that cannot be read must not make the commit screen fail to open.
            return PendingCommitMessage.None;
        }
    }

    public async Task SaveDraftAsync(
        string workingDirectory,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(message);

        if (message.Trim().Length == 0)
        {
            await ClearDraftAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            return;
        }

        string? gitDirectory = await ResolveGitDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (gitDirectory is null || !Directory.Exists(gitDirectory))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(
                    Path.Combine(gitDirectory, DraftFileName),
                    message,
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // In a read-only repository the draft cannot be saved; this is not an error to show the
            // user — the message box is on screen and a commit can still be made.
        }
    }

    public async Task ClearDraftAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string? gitDirectory = await ResolveGitDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (gitDirectory is null)
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(gitDirectory, DraftFileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Reads the message git prepared and strips the comment lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Comments are stripped here</b> (see <see cref="CommitMessageText"/>): git's editor path
    /// keeps <c># Conflicts:</c> lines out of the commit, whereas our
    /// <c>--cleanup=whitespace</c> path would let them in.
    /// </para>
    /// <para>
    /// <b>MEASURED — encoding:</b> git writes this file with <b>raw bytes</b>; in a repository with
    /// <c>i18n.commitEncoding=ISO-8859-9</c>, the message of a cherry-picked commit landed in the
    /// file as Latin-5 bytes. Had UTF-8 been assumed, Turkish messages would have turned into
    /// replacement characters (the same as the diff encoding bug in P04-T07).
    /// </para>
    /// </remarks>
    private async Task<string?> ReadPendingAsync(
        string workingDirectory,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

            string? commitEncoding = await _config
                .GetAsync(workingDirectory, "i18n.commitEncoding", cancellationToken)
                .ConfigureAwait(false);

            Encoding encoding = TextEncodings.TryGet(commitEncoding) ?? TextEncodings.Default;

            string? commentCharacter = await _config
                .GetAsync(workingDirectory, "core.commentChar", cancellationToken)
                .ConfigureAwait(false);

            return CommitMessageText.PrepareForEditing(encoding.GetString(bytes), commentCharacter);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the repository's git directory and caches it per repository.
    /// </summary>
    /// <remarks>
    /// <c>rev-parse --git-path</c> is <b>not used</b>: measured, in a normal repository it returns a
    /// <b>relative</b> path such as <c>.git/MERGE_MSG</c>, and that path is resolved against the
    /// directory the command runs in (the same trap as <c>--git-common-dir</c> in P02-T06).
    /// <c>--absolute-git-dir</c> is absolute in every case; in a worktree it also gives the right
    /// thing, namely that worktree's own directory.
    /// </remarks>
    private async Task<string?> ResolveGitDirectoryAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (_gitDirectories.TryGetValue(workingDirectory, out string? cached))
        {
            return cached;
        }

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--absolute-git-dir"],
                SuccessExitCodes = [0, 128],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string directory = result.GetStandardOutputText().Trim('\n', '\r');

        if (directory.Length == 0)
        {
            return null;
        }

        _gitDirectories[workingDirectory] = directory;

        return directory;
    }
}
