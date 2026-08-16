namespace GitExt.Core.Git;

/// <summary>
/// The reason a <c>git</c> call failed.
/// </summary>
/// <remarks>
/// Showing the raw <c>stderr</c> text to the user as the primary message is not acceptable;
/// this classification exists so that the UI can offer a meaningful message and action.
/// The raw text always remains reachable via <see cref="GitException.StandardError"/>.
/// </remarks>
public enum GitFailureKind
{
    /// <summary>Could not be classified. The raw <c>stderr</c> should be shown.</summary>
    Unknown,

    /// <summary>The given path is not a Git repository.</summary>
    NotARepository,

    /// <summary>Authentication is required (SSH key, token, credential helper).</summary>
    AuthenticationRequired,

    /// <summary>Network failure: DNS, connection refused, timeout.</summary>
    NetworkFailure,

    /// <summary><c>index.lock</c> exists — another git process may be running.</summary>
    IndexLocked,

    /// <summary>The merge/rebase/cherry-pick stopped with a conflict.</summary>
    Conflict,

    /// <summary>The given revision, branch or ref could not be resolved.</summary>
    UnknownRevision,

    /// <summary>The working tree is dirty; the operation could not continue.</summary>
    DirtyWorkingTree,

    /// <summary>The process timed out and was killed.</summary>
    Timeout,

    /// <summary>A branch with the same name already exists (P06-T01).</summary>
    BranchAlreadyExists,

    /// <summary>
    /// The ref name creates a directory/file conflict with an existing ref (P06-T01).
    /// </summary>
    /// <remarks>
    /// <b>MEASURED — in both directions:</b> while a <c>feature</c> branch exists,
    /// <c>feature/x</c> cannot be created (<c>refs/heads/feature</c> is a <b>file</b>, no
    /// directory can be created under it) and symmetrically, while <c>container/sub</c> exists,
    /// <c>container</c> cannot be created. Because it is a name that <b>fully complies</b> with
    /// the naming rules it passes validation; only git can tell.
    /// </remarks>
    RefNameConflict,

    /// <summary>The repository has no commits; the operation cannot find a starting point (P06-T01).</summary>
    UnbornHead,

    /// <summary>A remote with the same name already exists (P06-T05, exit code 3).</summary>
    RemoteAlreadyExists,

    /// <summary>The given remote does not exist (P06-T05, exit code 2).</summary>
    RemoteNotFound,

    /// <summary>
    /// The remote name is nested with an existing name (P06-T05).
    /// </summary>
    /// <remarks>
    /// <b>MEASURED — in both directions:</b> while <c>ic</c> exists, <c>ic/main</c> cannot be
    /// added (<i>"is a subset of existing remote"</i>) and symmetrically, while <c>ic/main</c>
    /// exists, <c>ic</c> cannot be added (<i>"is a superset"</i>). The same cause as
    /// <see cref="RefNameConflict"/> on branches — names are stored like directories under
    /// <c>refs/remotes/</c> as well — but its message is separate, because the fix suggested to
    /// the user is different.
    /// </remarks>
    RemoteNameConflict,

    /// <summary>The remote could not be reached: wrong address, missing repository or access denied (P06-T06).</summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — without this kind the message was WRONG.</b> For an unreachable remote
    /// git writes <c>fatal: '…' does not appear to be a git repository</c>; that text matches the
    /// <see cref="NotARepository"/> pattern and the user would be told <i>"This folder is not a
    /// Git repository"</i> — while the folder is perfectly fine, the problem is <b>at the remote
    /// address</b>. The distinction comes from git's second line:
    /// <c>Could not read from remote repository.</c>
    /// </remarks>
    RemoteUnreachable,
}

/// <summary>
/// Thrown when a <c>git</c> command returns a non-zero exit code.
/// </summary>
public class GitException : Exception
{
    public GitException(
        GitFailureKind kind,
        string message,
        string commandLine,
        int exitCode,
        string standardError,
        string standardOutput = "")
        : base(message)
    {
        Kind = kind;
        CommandLine = commandLine;
        ExitCode = exitCode;
        StandardError = standardError;
        StandardOutput = standardOutput;
    }

    /// <summary>The classified kind of the failure.</summary>
    public GitFailureKind Kind { get; }

    /// <summary>The command that was run — for diagnosis and the "show the command" principle.</summary>
    public string CommandLine { get; }

    /// <summary>The process's exit code.</summary>
    public int ExitCode { get; }

    /// <summary>Raw <c>stderr</c> output. Can be shown to the user in the details panel.</summary>
    public string StandardError { get; }

    /// <summary>
    /// Raw <c>stdout</c> output.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Added in P06-T08 — its absence was a silent loss of information.</b> Because stdout
    /// is empty for most failing commands, the field was never asked for. But
    /// <c>git push --porcelain</c> writes the <b>rejected</b> refs to <b>stdout</b> in
    /// machine-readable form as well and returns exit code 1 (measured); if that output is thrown
    /// away while raising the exception, all that is left are the human-readable <c>hint:</c>
    /// lines — that is, the channel ADR-0002 forbids. On top of that, on partial success (one
    /// branch went through, one was rejected) <b>what actually happened</b> is written exactly
    /// here.
    /// </remarks>
    public string StandardOutput { get; }
}

/// <summary>
/// Thrown when no runnable <c>git</c> could be found on the system.
/// </summary>
/// <remarks>
/// This is also thrown when the host cannot be reached in a Flatpak sandbox (ADR-0009):
/// from the user's point of view the outcome is the same — there is no usable git.
/// </remarks>
public sealed class GitNotFoundException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Thrown when the <c>git</c> version found is lower than <see cref="GitVersion.Minimum"/>.
/// </summary>
public sealed class GitVersionTooOldException(GitVersion found, string executablePath)
    : Exception(
        $"The git version found, {found}, is too old. At least {GitVersion.Minimum} is required "
        + $"(executable: {executablePath}).")
{
    public GitVersion FoundVersion { get; } = found;

    public GitVersion RequiredVersion { get; } = GitVersion.Minimum;

    public string ExecutablePath { get; } = executablePath;
}
