using System.Text.RegularExpressions;
using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// The result of the <c>commit.template</c> setting (P05-T13).
/// </summary>
/// <remarks>
/// The case of "setting present but file missing" is <b>not swallowed silently</b>: git itself
/// gives <b>exit 128</b> in that situation with
/// <c>fatal: could not read '…': No such file or directory</c> (measured), which means the user's
/// commit in the terminal does not work either. Showing "empty template" on screen would be hiding
/// a broken configuration.
/// </remarks>
public sealed record CommitTemplate
{
    /// <summary>The resolved full path.</summary>
    public required string Path { get; init; }

    /// <summary>The template text; <see langword="null"/> when the file could not be read.</summary>
    public string? Text { get; init; }

    /// <summary>The file was not found or could not be read.</summary>
    public bool IsMissing => Text is null;
}

/// <summary>
/// Reads the commit message sources: history, the <c>HEAD</c> message, the template (P05-T13).
/// </summary>
public interface ICommitMessageReader
{
    /// <summary>
    /// Returns the recent commit messages, newest first.
    /// </summary>
    /// <param name="workingDirectory">The repository working directory.</param>
    /// <param name="count">How many messages at most.</param>
    /// <param name="onlyCurrentUser">
    /// Only the configured user's commits (<c>user.name</c>/<c>user.email</c>).
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<string>> ReadRecentAsync(
        string workingDirectory,
        int count,
        bool onlyCurrentUser = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>HEAD</c>'s message; <see langword="null"/> in a repository with no commits.
    /// </summary>
    /// <remarks>
    /// This is the text loaded when the <c>--amend</c> box is ticked.
    /// </remarks>
    Task<string?> ReadHeadMessageAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The template configured with <c>commit.template</c>; <see langword="null"/> when the setting
    /// is absent.
    /// </summary>
    Task<CommitTemplate?> ReadTemplateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The comment prefix in force in this repository (<c>core.commentChar</c>).
    /// </summary>
    Task<string> ReadCommentCharacterAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICommitMessageReader"/>
public sealed class CommitMessageReader : ICommitMessageReader
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitConfigReader _config;

    public CommitMessageReader(IGitProcessRunner runner, IGitConfigReader config)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(config);

        _runner = runner;
        _config = config;
    }

    public async Task<IReadOnlyList<string>> ReadRecentAsync(
        string workingDirectory,
        int count,
        bool onlyCurrentUser = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        // In a repository with no commits `git log` gives exit 128 (measured). "No commits yet" is
        // not an error; it would mean showing an exception to a user making their first commit.
        if (!await HasHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        // ⚠️ `-z` is REQUIRED: `%B` is multi-line, and had a line ending been used as the separator
        // between messages there would be no way to tell where a message ends (measured: without
        // `-z` the output is a flat pile of lines). `-z` puts a NUL at the end of every record, and
        // a commit message cannot contain a NUL (P02-T04).
        List<string> arguments = ["log", "-z", "-n", count.ToString(), "--format=%B"];

        if (onlyCurrentUser)
        {
            string? pattern = await BuildAuthorPatternAsync(workingDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (pattern is not null)
            {
                arguments.Add($"--author={pattern}");
            }
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand { WorkingDirectory = workingDirectory, Arguments = arguments },
            cancellationToken).ConfigureAwait(false);

        // NUL is a TERMINATOR: for n records n NULs arrive and the last field is left empty. A
        // split that keeps empty entries is used (a project rule) — commits with an empty message
        // are real (measured in P02-T04), and a split that drops empties swallows them silently.
        string[] records = result.SplitStandardOutputAtNulPreservingEmpty();

        return
        [
            .. records
                .Select(record => record.TrimEnd('\n', '\r'))

                // Empty messages are not shown in the list: they are nothing to pick.
                // But that is a DISPLAY decision; dropping them at the split stage would break the
                // ordering.
                .Where(message => message.Trim().Length > 0),
        ];
    }

    public async Task<string?> ReadHeadMessageAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (!await HasHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["log", "-1", "--format=%B"],
            },
            cancellationToken).ConfigureAwait(false);

        return result.GetStandardOutputText().TrimEnd('\n', '\r');
    }

    public async Task<CommitTemplate?> ReadTemplateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        // Without `--path`, `~/…` comes back raw and the file was never found (measured).
        string? configured = await _config
            .GetPathAsync(workingDirectory, "commit.template", cancellationToken)
            .ConfigureAwait(false);

        if (configured is null)
        {
            return null;
        }

        string path = await ResolveTemplatePathAsync(workingDirectory, configured, cancellationToken)
            .ConfigureAwait(false);

        string? text = null;

        try
        {
            if (File.Exists(path))
            {
                // The template file is the user's own file; its encoding is unknown. UTF-8 is
                // assumed and invalid bytes fall back to the replacement character — no guessing
                // here, unlike what is done for patches (P04-T07), but the template sits in the box
                // IN FRONT OF THE USER before it goes into the commit.
                text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // A template that cannot be read = a template that cannot be found: in both cases the
            // user is pointed at a way forward.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new CommitTemplate { Path = path, Text = text };
    }

    public async Task<string> ReadCommentCharacterAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        string? configured = await _config
            .GetAsync(workingDirectory, "core.commentChar", cancellationToken)
            .ConfigureAwait(false);

        return CommitMessageText.ResolveCommentCharacter(configured);
    }

    /// <summary>
    /// Resolves a relative template path.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED:</b> git resolves a relative <c>commit.template</c> path against the <b>root of
    /// the working tree</b>, not against the directory the command runs in — even with a file of
    /// the same name in a subdirectory, the one at the root was read, and a file that existed only
    /// in the subdirectory was not found, with <c>could not read</c>. That is why the root is asked
    /// for separately: opening a different file from git's when the caller passes a subdirectory
    /// would mean showing the user a different template from the one they see in the terminal.
    /// </remarks>
    private async Task<string> ResolveTemplatePathAsync(
        string workingDirectory,
        string configured,
        CancellationToken cancellationToken)
    {
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--show-toplevel"],

                // It gives 128 in a bare repository (measured in P02-T06). There is no commit
                // screen there anyway; the caller's directory is used as the fallback.
                SuccessExitCodes = [0, 128],
            },
            cancellationToken).ConfigureAwait(false);

        string root = result.ExitCode == 0
            ? result.GetStandardOutputText().Trim('\n', '\r')
            : string.Empty;

        return Path.GetFullPath(
            Path.Combine(root.Length > 0 ? root : workingDirectory, configured));
    }

    /// <summary>
    /// The <c>--author</c> pattern for "only my messages".
    /// </summary>
    /// <remarks>
    /// <b>MEASURED:</b> <c>--author</c> matches as a <b>regular expression</b>, not as a plain
    /// substring (the pattern <c>lcum</c> finds <c>Ölçüm</c>, and the <c>^…$</c> anchors work). The
    /// name and the e-mail are therefore both escaped and anchored: otherwise every commit by
    /// anyone whose name contains this name as a substring would count as "mine".
    /// </remarks>
    private async Task<string?> BuildAuthorPatternAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string? name = await _config.GetAsync(workingDirectory, "user.name", cancellationToken)
            .ConfigureAwait(false);

        string? email = await _config.GetAsync(workingDirectory, "user.email", cancellationToken)
            .ConfigureAwait(false);

        if (name is null && email is null)
        {
            return null;
        }

        return $"^{Regex.Escape(name ?? string.Empty)} <{Regex.Escape(email ?? string.Empty)}>$";
    }

    /// <summary>
    /// Does the repository have any commits?
    /// </summary>
    /// <remarks>
    /// <c>rev-parse --verify --quiet</c> is consulted rather than the message (the P05-T03
    /// decision): git's error text can be localised and can change between versions.
    /// </remarks>
    private async Task<bool> HasHeadAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", "HEAD"],
                SuccessExitCodes = [0, 1, 128],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }
}
