namespace GitExt.Core;

/// <summary>
/// Which refresh a file system event calls for (P05-T14).
/// </summary>
public enum RepositoryChangeKind
{
    /// <summary>
    /// The working tree or the index changed → <c>git status</c> must be re-read.
    /// </summary>
    WorkingTree,

    /// <summary>
    /// Refs, <c>HEAD</c> or the repository state changed → the commit list must be re-read too.
    /// </summary>
    Repository,
}

/// <summary>
/// Decides whether a file system event is meaningful (P05-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>This class was kept pure</b> because all of the watcher's intelligence lives here; tied to the
/// file system and a timer, none of these rules could be tested quickly and deterministically.
/// </para>
/// <para>
/// <b>⚠️ MEASURED — the plan was corrected at this point.</b> The plan said "filter out changes in
/// the <c>.git</c> directory". Applied literally it <b>does not work</b>: a <c>git commit</c> made in
/// another terminal produced <b>64 events</b> in the measurement and <b>every one of them was under
/// <c>.git</c></b>, with zero events in the working tree. Filter <c>.git</c> out entirely and a
/// commit, branch switch or ref update made from outside goes <b>completely unnoticed</b>.
/// </para>
/// <para>
/// The right distinction is not <c>.git</c> but the <b>lock files</b>: in the measurement even a
/// read-only <c>git status</c> creates and deletes <c>.git/index.lock</c> (2 events). What closes the
/// endless refresh loop is the <c>*.lock</c> filter. Because a ref update arrives as a <b>rename</b>
/// from <c>refs/heads/x.lock → refs/heads/x</c> and the event's path is the <b>new name</b>, this
/// filter does not eat the real signal.
/// </para>
/// </remarks>
public static class RepositoryChangeClassifier
{
    /// <summary>
    /// The git directory's name inside the working tree.
    /// </summary>
    public const string GitDirectoryName = ".git";

    /// <summary>
    /// Classifies a path relative to the working tree root.
    /// </summary>
    /// <param name="relativePath">
    /// The path relative to the root directory. Either <c>/</c> or the platform separator is accepted.
    /// </param>
    /// <returns>The refresh required, or <see langword="null"/> when the event is to be ignored.</returns>
    public static RepositoryChangeKind? ClassifyWorkingTreePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        string[] segments = Split(relativePath);

        if (segments.Length == 0)
        {
            return null;
        }

        // Everything inside `.git`, nested repositories included, goes to the git directory rule.
        // In submodules `.git` may be a FILE; that too is a repository state change.
        for (int i = 0; i < segments.Length; i++)
        {
            if (!segments[i].Equals(GitDirectoryName, StringComparison.Ordinal))
            {
                continue;
            }

            // The parts are not rejoined and re-split: that would mean an extra join plus an array
            // allocation for every `.git` event, and this path is hot — a single branch switch
            // produces 2102 events (measured).
            return i == segments.Length - 1
                ? RepositoryChangeKind.Repository
                : ClassifySegments(segments.AsSpan(i + 1));
        }

        return IsLockFile(segments[^1]) ? null : RepositoryChangeKind.WorkingTree;
    }

    /// <summary>
    /// Classifies a path relative to the git directory (<c>.git</c>, or a linked working tree's own
    /// directory).
    /// </summary>
    /// <remarks>
    /// What is ignored is deliberate: <c>objects/</c> and <c>logs/</c> produce dozens of events on
    /// every write but say nothing on their own — an object having been written is not a visible
    /// change for the user unless a ref is updated as well. <c>GITEXT_COMMITMESSAGE</c> is
    /// <b>our own draft</b> (P05-T13): it is written after every keystroke, and unless it were
    /// filtered out it would trigger a refresh continuously while the user types.
    /// </remarks>
    public static RepositoryChangeKind? ClassifyGitDirectoryPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        return ClassifySegments(Split(relativePath));
    }

    private static RepositoryChangeKind? ClassifySegments(ReadOnlySpan<string> segments)
    {
        if (segments.Length == 0)
        {
            return null;
        }

        // Lock files are the source of the endless loop: every one of our own `git` calls creates them.
        if (IsLockFile(segments[^1]))
        {
            return null;
        }

        return segments[0] switch
        {
            // Refs: creating a branch/tag, committing, fetching, resetting.
            "refs" or "packed-refs" => RepositoryChangeKind.Repository,

            // Operations in progress: the rebase/merge/cherry-pick state files.
            "rebase-merge" or "rebase-apply" => RepositoryChangeKind.Repository,

            "HEAD" or "MERGE_HEAD" or "CHERRY_PICK_HEAD" or "REVERT_HEAD" or "BISECT_LOG"
                => RepositoryChangeKind.Repository,

            // The index only changes the staging state; the commit list stays the same.
            "index" => RepositoryChangeKind.WorkingTree,

            _ => null,
        };
    }

    private static bool IsLockFile(string name) =>
        name.EndsWith(".lock", StringComparison.Ordinal);

    private static string[] Split(string path) =>
        path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
}
