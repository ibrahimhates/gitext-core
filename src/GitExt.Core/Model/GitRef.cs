namespace GitExt.Core.Model;

/// <summary>The kind of a ref.</summary>
public enum GitRefKind
{
    /// <summary>An unrecognised ref namespace (<c>refs/stash</c>, <c>refs/notes/…</c> and so on).</summary>
    Other,

    /// <summary><c>refs/heads/…</c></summary>
    LocalBranch,

    /// <summary><c>refs/remotes/…</c></summary>
    RemoteBranch,

    /// <summary><c>refs/tags/…</c></summary>
    Tag,
}

/// <summary>
/// A Git reference (branch, tag, remote branch).
/// </summary>
public sealed record GitRef
{
    /// <summary>The full name, e.g. <c>refs/heads/main</c>.</summary>
    public required string FullName { get; init; }

    /// <summary>The short name, e.g. <c>main</c> or <c>origin/main</c>.</summary>
    public required string ShortName { get; init; }

    public required GitRefKind Kind { get; init; }

    /// <summary>
    /// The object the ref points at directly.
    /// </summary>
    /// <remarks>
    /// On an annotated tag this is the <b>tag object</b>, not the commit. For the commit, use
    /// <see cref="TargetCommit"/>.
    /// </remarks>
    public required CommitId ObjectId { get; init; }

    /// <summary>
    /// The commit the ref ultimately points at.
    /// </summary>
    /// <remarks>
    /// For annotated tags it is resolved with <c>%(*objectname)</c>; for other refs it is the same as
    /// <see cref="ObjectId"/>.
    /// </remarks>
    public required CommitId TargetCommit { get; init; }

    /// <summary>
    /// Is it an annotated tag (one with its own object)? Lightweight tags point straight at a commit.
    /// </summary>
    public bool IsAnnotatedTag { get; init; }

    /// <summary>
    /// When this ref is symbolic, the full name of the ref it points at; otherwise
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only common example in practice is <c>refs/remotes/&lt;remote&gt;/HEAD</c>: it exists in
    /// every cloned repository and points at the remote's default branch
    /// (<c>refs/remotes/origin/main</c>). Shown as if it were a separate branch, the user sees two
    /// identical badges on the same commit.
    /// </para>
    /// <para>
    /// Read from the <c>%(symref)</c> field; it comes back <b>empty</b> for non-symbolic refs
    /// (measured).
    /// </para>
    /// </remarks>
    public string? SymbolicTarget { get; init; }

    /// <summary>Is it a symbolic ref pointing at another ref?</summary>
    public bool IsSymbolic => SymbolicTarget is not null;

    public override string ToString() => ShortName;

    /// <summary>Determines the kind from the full name.</summary>
    internal static GitRefKind ClassifyKind(string fullName) => fullName switch
    {
        _ when fullName.StartsWith("refs/heads/", StringComparison.Ordinal) => GitRefKind.LocalBranch,
        _ when fullName.StartsWith("refs/remotes/", StringComparison.Ordinal) => GitRefKind.RemoteBranch,
        _ when fullName.StartsWith("refs/tags/", StringComparison.Ordinal) => GitRefKind.Tag,
        _ => GitRefKind.Other,
    };
}

/// <summary>
/// A branch's position relative to its upstream.
/// </summary>
/// <remarks>
/// Parsed from the <c>%(upstream:track)</c> field. The measured forms:
/// <c>[ahead 3, behind 2]</c>, <c>[ahead 1]</c>, <c>[behind 4]</c>, <c>[gone]</c>,
/// and <b>empty</b> when in sync.
/// </remarks>
public readonly record struct UpstreamTracking(int Ahead, int Behind, bool IsGone)
{
    /// <summary>There is no upstream, or the tracking information could not be read.</summary>
    public static UpstreamTracking None { get; } = new(0, 0, false);

    /// <summary>Is it at the same point as the upstream?</summary>
    public bool IsUpToDate => !IsGone && Ahead == 0 && Behind == 0;

    /// <summary>Both ahead and behind — diverged.</summary>
    public bool IsDiverged => Ahead > 0 && Behind > 0;
}

/// <summary>
/// Yerel veya uzak bir dal.
/// </summary>
public sealed record BranchInfo
{
    public required GitRef Ref { get; init; }

    /// <summary>Is this branch currently checked out?</summary>
    /// <remarks>
    /// In a detached HEAD state <b>no branch</b> is current — measured: <c>%(HEAD)</c> returns a
    /// space for every branch.
    /// </remarks>
    public bool IsCurrent { get; init; }

    /// <summary>Takip edilen uzak dal (<c>origin/main</c>); yoksa <see langword="null"/>.</summary>
    public string? Upstream { get; init; }

    public UpstreamTracking Tracking { get; init; } = UpstreamTracking.None;

    /// <summary>Is it a branch on a remote?</summary>
    public bool IsRemote => Ref.Kind == GitRefKind.RemoteBranch;

    public string Name => Ref.ShortName;

    public override string ToString() => Name;
}

/// <summary>Bir tag.</summary>
public sealed record TagInfo
{
    public required GitRef Ref { get; init; }

    /// <summary>The subject of an annotated tag's message; for a lightweight tag, the commit subject.</summary>
    public string Subject { get; init; } = string.Empty;

    public string Name => Ref.ShortName;

    /// <summary>Is it an annotated tag (with its own object, message and author)?</summary>
    public bool IsAnnotated => Ref.IsAnnotatedTag;

    public override string ToString() => Name;
}

/// <summary>A configured remote.</summary>
public sealed record RemoteInfo
{
    public required string Name { get; init; }

    /// <summary>The URL data is fetched from.</summary>
    public required string FetchUrl { get; init; }

    /// <summary>
    /// The URL data is pushed to.
    /// </summary>
    /// <remarks>
    /// Usually the same as <see cref="FetchUrl"/>, but it can differ via
    /// <c>remote.&lt;name&gt;.pushurl</c>.
    /// </remarks>
    public required string PushUrl { get; init; }

    public override string ToString() => $"{Name} → {FetchUrl}";
}

/// <summary>
/// <c>HEAD</c>'in durumu.
/// </summary>
public sealed record HeadState
{
    /// <summary>
    /// Does it point straight at a commit rather than at a branch?
    /// </summary>
    public required bool IsDetached { get; init; }

    /// <summary>
    /// Are there no commits at all yet (a fresh <c>git init</c>)?
    /// </summary>
    /// <remarks>
    /// In this state <c>HEAD</c> points at a branch that does not exist and <c>rev-parse HEAD</c>
    /// fails. This may be the first repository the user opens; it must not crash.
    /// </remarks>
    public required bool IsUnborn { get; init; }

    /// <summary>The checked-out branch; <see langword="null"/> when detached.</summary>
    public string? BranchName { get; init; }

    /// <summary>The commit pointed at; empty in an unborn repository.</summary>
    public CommitId Commit { get; init; }

    public override string ToString() =>
        IsUnborn ? "(unborn)" : IsDetached ? $"(detached) {Commit.ToShortString()}" : BranchName!;
}

/// <summary>
/// All the ref information for a repository — in a single read.
/// </summary>
public sealed record RepositoryRefs
{
    public required HeadState Head { get; init; }

    public required IReadOnlyList<BranchInfo> LocalBranches { get; init; }

    public required IReadOnlyList<BranchInfo> RemoteBranches { get; init; }

    public required IReadOnlyList<TagInfo> Tags { get; init; }

    public required IReadOnlyList<RemoteInfo> Remotes { get; init; }

    /// <summary>The currently checked-out branch; <see langword="null"/> when detached or unborn.</summary>
    public BranchInfo? CurrentBranch => LocalBranches.FirstOrDefault(b => b.IsCurrent);
}
