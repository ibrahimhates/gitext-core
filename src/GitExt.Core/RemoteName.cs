namespace GitExt.Core;

/// <summary>
/// Why a remote name cannot be accepted (P06-T05).
/// </summary>
public enum RemoteNameProblem
{
    /// <summary>The name is empty or only whitespace.</summary>
    Empty,

    /// <summary>
    /// The name starts with <c>refs/</c>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED:</b> git does not treat this as an error — <c>git remote add refs/remotes/x …</c>
    /// gives exit code <b>0</b> and creates a remote writing under
    /// <c>refs/remotes/refs/remotes/x/*</c>. When the user copies a name out of <c>branch -a</c>
    /// output they silently end up with a nested name. This rejection is put in place by <b>us</b>,
    /// not by git (the same as the <c>refs/heads/</c> decision in P06-T01).
    /// </remarks>
    NestedRefsPrefix,

    /// <summary>
    /// The name starts with <c>-</c>.
    /// </summary>
    /// <remarks>
    /// MEASURED: git <b>accepts</b> such a name (with the <c>--</c> separator, exit code 0), but
    /// everywhere <c>--</c> is forgotten it is taken for a flag (<c>unknown switch</c>, rc=129). Our
    /// own commands always use <c>--</c>; even so, we do not let the user create a name that will
    /// cause them trouble <b>in other tools</b>.
    /// </remarks>
    LeadingDash,

    /// <summary>A forbidden character: space, a control character, <c>~ ^ : ? * [ \</c>.</summary>
    ForbiddenCharacter,

    /// <summary>A component starts with <c>.</c> or ends with <c>.lock</c>.</summary>
    InvalidSegment,

    /// <summary>An empty component: a leading/trailing <c>/</c> or a consecutive <c>//</c>.</summary>
    EmptySegment,

    /// <summary>
    /// Two consecutive dots (<c>..</c>).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A trailing dot is NOT here</b> — one more point where this differs from branch names.
    /// MEASURED: <c>git remote add -- "a." …</c> works and the <c>a.</c> remote is <b>fully
    /// functional</b>: <c>fetch</c> goes through, <c>refs/remotes/a./main</c> is created, and
    /// <c>rename</c> works. The reason is <c>check-ref-format</c>'s rule: <b>the ref as a whole</b>
    /// cannot end with a dot, but because a remote name is always followed by <c>/…</c>,
    /// <c>refs/remotes/a./HEAD</c> is valid. Had <c>BranchName</c>'s rule been copied, a name git
    /// accepts would be rejected for no reason (a differential test caught it).
    /// </remarks>
    InvalidDot,
}

/// <summary>
/// Remote name validation (P06-T05).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Why is <see cref="BranchName"/> NOT REUSED?</b> The rules are not the same.
/// MEASURED:
/// </para>
/// <list type="table">
///   <listheader><term>Name</term><description><c>git remote add</c> · <c>git branch</c></description></listheader>
///   <item><term><c>HEAD</c></term><description><b>accepted</b> · rejected</description></item>
///   <item><term><c>@{-1}</c></term><description>rejected · "accepted" (but translated to another name)</description></item>
/// </list>
/// <para>
/// Had <c>BranchName</c> been used here, a remote called <c>HEAD</c> — a name git permits and one
/// that turns up in GitHub workflows — would be rejected for no reason.
/// </para>
/// <para>
/// The validation is <b>pure</b>: we do not start a process on every keystroke while the user types.
/// Drift would be silent, so a differential test feeds the same names both to this code and to the
/// <b>real</b> <c>git remote add</c> (deliberate divergences are listed by name in the test).
/// </para>
/// </remarks>
public static class RemoteName
{
    /// <summary>The ref prefix of remote tracking branches.</summary>
    public const string RemotesPrefix = "refs/remotes/";

    /// <summary>
    /// Validates the name.
    /// </summary>
    /// <param name="name">The name the user typed.</param>
    /// <returns><see langword="null"/> when there is no problem.</returns>
    public static RemoteNameProblem? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RemoteNameProblem.Empty;
        }

        // 🔴 git does not treat this as an error, it silently creates a nested name (measured).
        if (name.StartsWith("refs/", StringComparison.Ordinal))
        {
            return RemoteNameProblem.NestedRefsPrefix;
        }

        if (name[0] == '-')
        {
            return RemoteNameProblem.LeadingDash;
        }

        // Only `..`; a trailing dot IS VALID in git (see the note above).
        if (name.Contains("..", StringComparison.Ordinal))
        {
            return RemoteNameProblem.InvalidDot;
        }

        foreach (char c in name)
        {
            if (IsForbidden(c))
            {
                return RemoteNameProblem.ForbiddenCharacter;
            }
        }

        // ⚠️ `Split` keeps empty components: that is how a leading/trailing `/` and `//` are caught.
        foreach (string segment in name.Split('/'))
        {
            if (segment.Length == 0)
            {
                return RemoteNameProblem.EmptySegment;
            }

            if (segment[0] == '.' || segment.EndsWith(".lock", StringComparison.Ordinal))
            {
                return RemoteNameProblem.InvalidSegment;
            }
        }

        return null;
    }

    /// <summary>Is the name valid?</summary>
    public static bool IsValid(string? name) => Validate(name) is null;

    /// <summary>
    /// The explanation of the problem to show the user.
    /// </summary>
    public static string Describe(RemoteNameProblem problem) => problem switch
    {
        RemoteNameProblem.Empty => "A name cannot be empty.",
        RemoteNameProblem.NestedRefsPrefix =>
            "A name cannot start with \"refs/\". Git does not reject it but it creates a nested name "
            + "(\"refs/remotes/refs/remotes/…\"); that is not what you want.",
        RemoteNameProblem.LeadingDash =>
            "A name cannot start with \"-\"; git commands would read it as an option.",
        RemoteNameProblem.ForbiddenCharacter =>
            "A name cannot contain spaces or these characters: ~ ^ : ? * [ \\",
        RemoteNameProblem.InvalidSegment =>
            "Components cannot start with \".\" or end with \".lock\".",
        RemoteNameProblem.EmptySegment => "A name cannot start or end with \"/\", or contain \"//\".",
        RemoteNameProblem.InvalidDot => "A name cannot contain \"..\".",
        _ => "The name is invalid.",
    };

    private static bool IsForbidden(char c) =>
        c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\' || char.IsControl(c);
}
