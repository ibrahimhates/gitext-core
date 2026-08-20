namespace GitExt.Core;

/// <summary>
/// What a repository's <c>HEAD</c> says, read straight from the file system (P12-T01).
/// </summary>
/// <param name="IsRepository">Whether the path really is a git working directory.</param>
/// <param name="BranchName">
/// The checked-out branch, or <see langword="null"/> when <c>HEAD</c> is detached (or unreadable).
/// </param>
public readonly record struct RepositoryHeadInfo(bool IsRepository, string? BranchName)
{
    /// <summary>A repository whose <c>HEAD</c> points at a commit rather than a branch.</summary>
    public bool IsDetached => IsRepository && BranchName is null;

    /// <summary>Not a repository at all.</summary>
    public static RepositoryHeadInfo NotARepository => new(IsRepository: false, BranchName: null);
}

/// <summary>
/// Reads the current branch of a repository <b>without starting git</b> (P12-T01, the dashboard).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This is a deliberate exception to ADR-0002 ("git is driven as a subprocess"), and it is
/// confined to <b>one</b> job: printing the branch name on the dashboard's repository tiles.
/// Everything that reads or changes repository state still goes through git.
/// </para>
/// <para>
/// <b>MEASURED</b> (12 entries, the dashboard's list length): <c>git symbolic-ref</c> per
/// repository costs <b>10.0 ms</b>, reading <c>HEAD</c> costs <b>0.73 ms</b> — 14× cheaper, and it
/// happens while the window is coming up. The answers were compared against real git on seven
/// repositories (normal · detached · a branch with slashes · a worktree host · a <b>linked
/// worktree</b> · a superproject · a submodule) and agree in all of them.
/// </para>
/// <para>
/// 🔴 That comparison is the reason this class handles the <c>gitdir:</c> file: the first version
/// resolved it as a path <b>relative</b> to the repository and a linked worktree reported
/// "detached" while git said <c>wtbranch</c> — git writes an <b>absolute</b> path there. Reading
/// only <c>&lt;path&gt;/.git/HEAD</c> would have shown the wrong branch on every worktree, and
/// nothing would have failed.
/// </para>
/// </remarks>
public static class RepositoryHead
{
    private const string RefPrefix = "ref:";
    private const string HeadsPrefix = "refs/heads/";

    /// <summary>
    /// Reads the state of the repository at <paramref name="workingDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Never throws: a missing folder, an unmounted drive or a permission error all come back as
    /// "not a repository". The dashboard shows entries it cannot reach dimmed rather than
    /// dropping them, so a failure here must not be an exception.
    /// </remarks>
    public static RepositoryHeadInfo Read(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return RepositoryHeadInfo.NotARepository;
        }

        try
        {
            string? gitDirectory = ResolveGitDirectory(workingDirectory);

            if (gitDirectory is null)
            {
                return RepositoryHeadInfo.NotARepository;
            }

            string headFile = Path.Combine(gitDirectory, "HEAD");

            if (!File.Exists(headFile))
            {
                // The `.git` entry is there but HEAD is not: a half-created repository. It is not
                // usable, so it is not a repository as far as the dashboard is concerned.
                return RepositoryHeadInfo.NotARepository;
            }

            string head = File.ReadAllText(headFile).Trim();

            return new RepositoryHeadInfo(IsRepository: true, BranchName: ParseBranchName(head));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return RepositoryHeadInfo.NotARepository;
        }
    }

    /// <summary>Is this path usable as a repository?</summary>
    public static bool IsRepository(string? workingDirectory) => Read(workingDirectory).IsRepository;

    /// <summary>
    /// Finds the git directory belonging to a working directory.
    /// </summary>
    /// <remarks>
    /// Three shapes: <c>.git</c> as a directory (an ordinary repository), <c>.git</c> as a
    /// <b>file</b> containing <c>gitdir: …</c> (a linked worktree or a submodule), or the path
    /// itself being a bare repository.
    /// </remarks>
    private static string? ResolveGitDirectory(string workingDirectory)
    {
        string dotGit = Path.Combine(workingDirectory, ".git");

        if (Directory.Exists(dotGit))
        {
            return dotGit;
        }

        if (File.Exists(dotGit))
        {
            string content = File.ReadAllText(dotGit).Trim();

            if (!content.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                return null;
            }

            string target = content["gitdir:".Length..].Trim();

            if (target.Length == 0)
            {
                return null;
            }

            // 🔴 git writes an ABSOLUTE path here for worktrees and a RELATIVE one for submodules;
            // both shapes occur in the wild, so both are handled.
            string resolved = Path.IsPathRooted(target)
                ? target
                : Path.GetFullPath(Path.Combine(workingDirectory, target));

            return Directory.Exists(resolved) ? resolved : null;
        }

        // A bare repository has no work tree and no `.git`; HEAD and objects sit at the root.
        if (File.Exists(Path.Combine(workingDirectory, "HEAD"))
            && Directory.Exists(Path.Combine(workingDirectory, "objects")))
        {
            return workingDirectory;
        }

        return null;
    }

    /// <summary>
    /// Pulls the branch name out of the contents of <c>HEAD</c>.
    /// </summary>
    /// <remarks>
    /// <c>ref: refs/heads/&lt;name&gt;</c> on a branch, a raw SHA when detached. The name may
    /// contain slashes (<c>feature/deep/name</c>) — only the prefix is stripped, nothing is split.
    /// </remarks>
    private static string? ParseBranchName(string head)
    {
        if (!head.StartsWith(RefPrefix, StringComparison.Ordinal))
        {
            // A SHA: detached HEAD.
            return null;
        }

        string reference = head[RefPrefix.Length..].Trim();

        return reference.StartsWith(HeadsPrefix, StringComparison.Ordinal)
            ? reference[HeadsPrefix.Length..]
            : null;
    }
}
