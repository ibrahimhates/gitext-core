namespace GitExt.Core;

/// <summary>
/// Why a branch name cannot be accepted (P06-T01).
/// </summary>
public enum BranchNameProblem
{
    /// <summary>The name is empty or only whitespace.</summary>
    Empty,

    /// <summary>
    /// The name starts with <c>refs/heads/</c>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED:</b> git does not treat this as an error — <c>git branch refs/heads/x</c>
    /// creates <c>refs/heads/refs/heads/x</c>. When the user pastes the full ref name they silently
    /// end up with a nested branch.
    /// </remarks>
    NestedRefsPrefix,

    /// <summary>
    /// The name contains revision syntax (such as <c>@{-1}</c> or <c>@{u}</c>).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED:</b> <c>git branch</c> <b>translates</b> these; <c>@{-1}</c> means "the previous
    /// branch" and the name typed differs from the name created.
    /// </remarks>
    RevisionSyntax,

    /// <summary>The name starts with <c>-</c> (git takes it for an option).</summary>
    LeadingDash,

    /// <summary>The name is <c>HEAD</c> (git rejects it specially).</summary>
    ReservedHead,

    /// <summary>A forbidden character: space, a control character, <c>~ ^ : ? * [ \</c>.</summary>
    ForbiddenCharacter,

    /// <summary>A component starts with <c>.</c> or ends with <c>.lock</c>.</summary>
    InvalidSegment,

    /// <summary>An empty component: a leading/trailing <c>/</c> or a consecutive <c>//</c>.</summary>
    EmptySegment,

    /// <summary>Art arda iki nokta (<c>..</c>) veya sonda nokta.</summary>
    InvalidDot,
}

/// <summary>
/// Branch name validation (P06-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a pure implementation?</b> The validation runs while the user types; starting a
/// <c>git check-ref-format</c> process on every keystroke is both slow and unnecessary. That the
/// rules here stay the same as git's is pinned down by a <b>differential test</b> (the same names are
/// fed both to this code and to the real <c>git check-ref-format --branch</c>) — any drift turns the
/// test red.
/// </para>
/// <para>
/// <b>⚠️ MEASURED — a single <c>check-ref-format</c> call DOES NOT do this job correctly.</b>
/// The two forms give opposite answers:
/// </para>
/// <list type="table">
///   <listheader><term>Name</term><description><c>--branch</c> · <c>--allow-onelevel refs/heads/…</c></description></listheader>
///   <item><term><c>@{-1}</c></term><description>passes (and <b>translates it to another name</b>) · rejects</description></item>
///   <item><term><c>HEAD</c></term><description>rejects · <b>passes</b></description></item>
///   <item><term><c>-x</c></term><description>rejects · <b>passes</b></description></item>
/// </list>
/// <para>
/// The right reference is <c>--branch</c>: <c>git branch</c> itself applies the same rules
/// (<c>HEAD</c> and <c>-x</c> are rejected even after the <c>--</c> separator). But <c>--branch</c>
/// does not validate, it <b>translates</b>: for <c>@{-1}</c> its output is not the name typed but the
/// name of "the previous branch". That is why revision syntax is filtered out <b>separately</b> here.
/// </para>
/// </remarks>
public static class BranchName
{
    /// <summary>Git's full branch ref prefix.</summary>
    public const string HeadsPrefix = "refs/heads/";

    /// <summary>
    /// Validates the name.
    /// </summary>
    /// <param name="name">The name the user typed.</param>
    /// <returns><see langword="null"/> when there is no problem.</returns>
    public static BranchNameProblem? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BranchNameProblem.Empty;
        }

        // 🔴 git does not treat this as an error, it silently creates a nested branch (measured).
        if (name.StartsWith(HeadsPrefix, StringComparison.Ordinal))
        {
            return BranchNameProblem.NestedRefsPrefix;
        }

        // `@{` is revision syntax to git. `--branch` TRANSLATES it; the name typed and the name
        // created would differ.
        if (name.Contains("@{", StringComparison.Ordinal))
        {
            return BranchNameProblem.RevisionSyntax;
        }

        if (name[0] == '-')
        {
            return BranchNameProblem.LeadingDash;
        }

        if (name.Equals("HEAD", StringComparison.Ordinal))
        {
            return BranchNameProblem.ReservedHead;
        }

        if (name.Contains("..", StringComparison.Ordinal) || name[^1] == '.')
        {
            return BranchNameProblem.InvalidDot;
        }

        foreach (char c in name)
        {
            if (IsForbidden(c))
            {
                return BranchNameProblem.ForbiddenCharacter;
            }
        }

        // ⚠️ `Split` keeps empty components: that is how a leading/trailing `/` and `//` are caught.
        foreach (string segment in name.Split('/'))
        {
            if (segment.Length == 0)
            {
                return BranchNameProblem.EmptySegment;
            }

            if (segment[0] == '.' || segment.EndsWith(".lock", StringComparison.Ordinal))
            {
                return BranchNameProblem.InvalidSegment;
            }
        }

        return null;
    }

    /// <summary>Is the name valid?</summary>
    public static bool IsValid(string? name) => Validate(name) is null;

    private static bool IsForbidden(char c) =>
        c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\' || char.IsControl(c);
}
