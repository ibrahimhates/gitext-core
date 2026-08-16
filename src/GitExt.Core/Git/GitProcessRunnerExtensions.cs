namespace GitExt.Core.Git;

/// <summary>
/// Convenience extensions for <see cref="IGitProcessRunner"/>.
/// </summary>
public static class GitProcessRunnerExtensions
{
    /// <summary>
    /// Runs the command; if it fails, throws a classified <see cref="GitException"/>.
    /// </summary>
    public static async Task<GitResult> RunCheckedAsync(
        this IGitProcessRunner runner,
        GitCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);

        GitResult result = await runner.RunAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return result;
        }

        GitFailureKind kind = GitFailureClassifier.Classify(result.StandardError);

        throw new GitException(
            kind,
            GitFailureClassifier.Describe(kind),
            command.ToDisplayString(),
            result.ExitCode,
            result.StandardError,
            result.GetStandardOutputText());
    }

    /// <summary>
    /// Runs the command and returns stdout as trimmed text.
    /// </summary>
    /// <remarks>
    /// For commands that produce a single line of output (such as <c>rev-parse</c>,
    /// <c>config --get</c>).
    /// </remarks>
    public static async Task<string> RunForTextAsync(
        this IGitProcessRunner runner,
        GitCommand command,
        CancellationToken cancellationToken = default)
    {
        GitResult result = await runner.RunCheckedAsync(command, cancellationToken).ConfigureAwait(false);
        return result.GetStandardOutputText().TrimEnd('\n', '\r');
    }
}

/// <summary>
/// Maps <c>git</c> error output to a meaningful kind (P02-T12).
/// </summary>
/// <remarks>
/// The mapping looks at the <c>stderr</c> text. This is inevitably brittle — that is why, if no
/// match is found, <see cref="GitFailureKind.Unknown"/> is returned and the raw text is shown to
/// the user. Not classifying is preferable to classifying wrongly.
/// <para>
/// Because <c>LC_ALL=C</c> is set on every call (ADR-0002) these texts do not change with the
/// user's language; otherwise this mapping would never work.
/// </para>
/// </remarks>
internal static class GitFailureClassifier
{
    internal static GitFailureKind Classify(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return GitFailureKind.Unknown;
        }

        ReadOnlySpan<char> text = standardError.AsSpan();

        // 🔴 ORDER MATTERS — fixed in P06-T09. On the SSH side git appends the line
        // "Could not read from remote repository." to ALL authentication and network failures
        // (measured):
        //   git@github.com: Permission denied (publickey).      -> AUTHENTICATION
        //   ssh: Could not resolve hostname …                   -> NETWORK
        // If that check came first (and it did until P06-T09) both would be shown as
        // "Remote repository not found": the user fiddles with the address, while the address is
        // correct — what is missing is the SSH key.
        if (ContainsAny(
                text,
                "Authentication failed",
                "could not read Username",
                "could not read Password",
                "Permission denied (publickey",
                "Permission denied, please try again",
                "terminal prompts disabled",
                "Invalid username or token",
                "Support for password authentication was removed"))
        {
            return GitFailureKind.AuthenticationRequired;
        }

        if (ContainsAny(
                text,
                "Could not resolve host",
                "Connection refused",
                "Connection timed out",
                "Network is unreachable",
                "Connection closed by",
                "kex_exchange_identification"))
        {
            return GitFailureKind.NetworkFailure;
        }

        // ⚠️ ORDER MATTERS: this check must come BEFORE the "does not appear to be a git
        // repository" pattern below. For an unreachable remote git writes both, and if it fell
        // into the generic pattern we would tell the user "This folder is not a Git repository" —
        // while the folder is fine (measured in P06-T06).
        if (ContainsAny(text, "Could not read from remote repository"))
        {
            return GitFailureKind.RemoteUnreachable;
        }

        if (ContainsAny(text, "not a git repository", "does not appear to be a git repository"))
        {
            return GitFailureKind.NotARepository;
        }

        // MEASURED (P05-T02): a lock collision has two different message shapes —
        //   index:  fatal: Unable to create '…/index.lock': File exists.
        //   ref:    fatal: cannot lock ref 'HEAD': Unable to create '…/main.lock': File exists.
        // The second one does NOT contain "index.lock", but git appends the line
        // "Another git process seems to be running…" to both; that is why the two patterns below
        // are enough. (A separate "cannot lock ref" pattern was also tried for the ref lock and
        // found to be UNNECESSARY — the test passes without it.)
        if (ContainsAny(
                text,
                "index.lock",
                "Another git process seems to be running"))
        {
            return GitFailureKind.IndexLocked;
        }

        // `unable to access` is the generic network failure on the HTTPS side; it is checked
        // AFTER the authentication patterns above, because an authentication failure can contain
        // this line too.
        if (ContainsAny(text, "unable to access"))
        {
            return GitFailureKind.NetworkFailure;
        }

        if (ContainsAny(text, "CONFLICT", "Automatic merge failed", "needs merge"))
        {
            return GitFailureKind.Conflict;
        }

        // ⚠️ ORDER MATTERS: "error: remote origin already exists." also matches the generic
        // "already exists" pattern below — the remote check must come FIRST, otherwise a remote
        // name collision would tell the user "A branch with this name already exists." (P06-T05).
        if (ContainsAny(text, "remote ") && ContainsAny(text, "already exists"))
        {
            return GitFailureKind.RemoteAlreadyExists;
        }

        // MEASURED (P06-T05): both spellings occur — `remove`/`rename` with a colon
        // ("No such remote: 'x'"), `get-url`/`set-url` without it ("No such remote 'x'").
        if (ContainsAny(text, "No such remote"))
        {
            return GitFailureKind.RemoteNotFound;
        }

        // MEASURED (P06-T05): "fatal: remote name 'ic/main' is a subset of existing remote 'ic'"
        if (ContainsAny(text, "is a subset of existing remote", "is a superset of existing remote"))
        {
            return GitFailureKind.RemoteNameConflict;
        }

        // ⚠️ Order matters: the two patterns below can contain "cannot lock ref", but the lock
        // check above only looks for "index.lock" / "Another git process…", so they do not
        // swallow each other (measured in P06-T01).
        if (ContainsAny(text, "already exists"))
        {
            return GitFailureKind.BranchAlreadyExists;
        }

        // MEASURED: "cannot lock ref 'refs/heads/feature/x': 'refs/heads/feature' exists;
        //           cannot create 'refs/heads/feature/x'"
        if (ContainsAny(text, "cannot create", "is not a valid ref name"))
        {
            return GitFailureKind.RefNameConflict;
        }

        if (ContainsAny(
                text,
                "unknown revision or path not in the working tree",
                "bad revision",
                "ambiguous argument",
                "not a valid object name",

                // MEASURED (P06-T02): for an unresolvable target `git switch` uses THIS TEXT,
                // none of the ones above.
                "invalid reference"))
        {
            return GitFailureKind.UnknownRevision;
        }

        if (ContainsAny(
                text,
                "Your local changes to the following files would be overwritten",
                "cannot pull with rebase: You have unstaged changes",
                "Please commit your changes or stash them"))
        {
            return GitFailureKind.DirtyWorkingTree;
        }

        return GitFailureKind.Unknown;
    }

    /// <summary>
    /// Produces a description of the kind that can be shown to the user.
    /// </summary>
    internal static string Describe(GitFailureKind kind) => kind switch
    {
        GitFailureKind.NotARepository => "This folder is not a Git repository.",
        GitFailureKind.AuthenticationRequired =>
            "The remote asked for authentication. Check your SSH key, your credential helper "
            + "or your access token.",
        GitFailureKind.NetworkFailure => "The remote could not be reached. Check your network connection.",
        GitFailureKind.IndexLocked =>
            "The repository is locked. Another Git process may be running; try again in a few "
            + "seconds. If the lock has been there for a long time it may be left over from a crashed process "
            + "olabilir.",
        GitFailureKind.Conflict => "The operation stopped because of a conflict.",
        GitFailureKind.UnknownRevision => "No such revision or branch.",
        GitFailureKind.DirtyWorkingTree =>
            "There are uncommitted changes in the working directory; the operation could not continue.",
        GitFailureKind.Timeout => "The command timed out.",
        GitFailureKind.BranchAlreadyExists => "Bu adda bir dal zaten var.",
        GitFailureKind.RefNameConflict =>
            "This name conflicts with an existing branch. Git stores branches like files: "
            + "with a \"feature\" branch present you cannot create \"feature/x\" (and vice versa).",
        GitFailureKind.RemoteAlreadyExists => "Bu adda bir uzak depo zaten var.",
        GitFailureKind.RemoteNotFound => "No such remote.",
        GitFailureKind.RemoteUnreachable =>
            "The remote could not be reached. Check the address, your network connection and your access rights.",
        GitFailureKind.RemoteNameConflict =>
            "This name nests with an existing remote: with \"ic\" present, \"ic/main\" "
            + "cannot be added (and vice versa). Choose a different name.",
        GitFailureKind.UnbornHead =>
            "There are no commits in the repository yet, so there is no starting point for a branch. "
            + "Make the first commit first.",
        _ => "The git command failed.",
    };

    private static bool ContainsAny(ReadOnlySpan<char> text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
