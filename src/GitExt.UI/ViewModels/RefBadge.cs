using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The kind of ref badge shown on a commit row (P03-T12).
/// </summary>
public enum RefBadgeKind
{
    /// <summary>Yerel dal.</summary>
    LocalBranch,

    /// <summary>A remote tracking branch (<c>origin/main</c>).</summary>
    RemoteBranch,

    /// <summary>Tag (hafif veya annotated).</summary>
    Tag,

    /// <summary><c>HEAD</c> — shown on its own in a detached state.</summary>
    Head,
}

/// <summary>
/// A ref of a known kind pointing at a commit.
/// </summary>
/// <param name="Text">The short name to display.</param>
/// <param name="Kind">The badge kind — the visual style is chosen from it.</param>
/// <param name="IsCurrent">Is it the checked-out branch?</param>
public sealed record RefBadge(string Text, RefBadgeKind Kind, bool IsCurrent)
{
    public bool IsLocalBranch => Kind == RefBadgeKind.LocalBranch;

    public bool IsRemoteBranch => Kind == RefBadgeKind.RemoteBranch;

    public bool IsTag => Kind == RefBadgeKind.Tag;

    public bool IsHead => Kind == RefBadgeKind.Head;

    public override string ToString() => Text;
}

/// <summary>
/// The mapping from commit id to badges.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why is <c>%D</c> not parsed?</b> <c>git log</c>'s <c>%D</c> field was measured and it gives no
/// kind information: a local branch arrives as <c>ikinci</c> and a remote branch as <c>origin/main</c>
/// — both bare names. Telling them apart would mean knowing the remote names and matching prefixes,
/// which is guesswork. (There is a <c>tag:</c> prefix, while a stash arrives by its full name as
/// <c>refs/stash</c> — the format is inconsistent.)
/// </para>
/// <para>
/// <c>for-each-ref</c>, on the other hand, gives the <b>authoritative full name</b>
/// (<c>refs/heads/…</c>, <c>refs/remotes/…</c>, <c>refs/tags/…</c>) and resolves the target commit on
/// annotated tags. The badges are produced from there.
/// </para>
/// </remarks>
public sealed class RefBadgeIndex
{
    private static readonly IReadOnlyList<RefBadge> _noBadges = [];

    private readonly Dictionary<CommitId, List<RefBadge>> _byCommit = [];

    /// <summary>An empty index — used before a repository is open, or when the ref read fails.</summary>
    public static RefBadgeIndex Empty { get; } = new();

    /// <summary>
    /// Produces the badge index from a ref read.
    /// </summary>
    public static RefBadgeIndex Build(RepositoryRefs refs)
    {
        ArgumentNullException.ThrowIfNull(refs);

        RefBadgeIndex index = new();

        // Detached HEAD: no branch is current, and HEAD gets its own badge.
        if (refs.Head is { IsDetached: true, Commit.IsEmpty: false })
        {
            index.Add(refs.Head.Commit, new RefBadge("HEAD", RefBadgeKind.Head, IsCurrent: true));
        }

        foreach (BranchInfo branch in refs.LocalBranches)
        {
            index.Add(
                branch.Ref.TargetCommit,
                new RefBadge(branch.Name, RefBadgeKind.LocalBranch, branch.IsCurrent));
        }

        foreach (BranchInfo branch in refs.RemoteBranches)
        {
            // Symbolic refs such as origin/HEAD are skipped: they exist in EVERY cloned repository,
            // point at the same commit as the branch they reference, and produce two identical badges
            // side by side. They are told apart by git's %(symref) field, not by guessing at the name.
            if (branch.Ref.IsSymbolic)
            {
                continue;
            }

            index.Add(
                branch.Ref.TargetCommit,
                new RefBadge(branch.Name, RefBadgeKind.RemoteBranch, IsCurrent: false));
        }

        foreach (TagInfo tag in refs.Tags)
        {
            // On an annotated tag, TargetCommit is the resolved commit and not the tag object —
            // otherwise the badge would land on the wrong row in the graph.
            index.Add(tag.Ref.TargetCommit, new RefBadge(tag.Name, RefBadgeKind.Tag, IsCurrent: false));
        }

        return index;
    }

    /// <summary>The badges pointing at this commit; an empty list when there are none.</summary>
    public IReadOnlyList<RefBadge> For(CommitId commit) =>
        _byCommit.TryGetValue(commit, out List<RefBadge>? badges) ? badges : _noBadges;

    public int Count => _byCommit.Count;

    private void Add(CommitId commit, RefBadge badge)
    {
        if (commit.IsEmpty)
        {
            return;
        }

        if (!_byCommit.TryGetValue(commit, out List<RefBadge>? badges))
        {
            badges = [];
            _byCommit[commit] = badges;
        }

        // The order: the current branch first, then HEAD, branch, remote branch, tag.
        // So the information the user cares about most sits on the left.
        badges.Add(badge);
        badges.Sort(static (a, b) => Rank(a).CompareTo(Rank(b)));
    }

    private static int Rank(RefBadge badge) => badge switch
    {
        { IsCurrent: true } => 0,
        { Kind: RefBadgeKind.Head } => 1,
        { Kind: RefBadgeKind.LocalBranch } => 2,
        { Kind: RefBadgeKind.RemoteBranch } => 3,
        _ => 4,
    };
}
