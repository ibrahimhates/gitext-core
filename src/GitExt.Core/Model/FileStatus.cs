namespace GitExt.Core.Model;

/// <summary>
/// The kind of change to a file in a single area (the index or the working tree).
/// </summary>
/// <remarks>
/// Corresponds to one character of the <c>XY</c> field in <c>--porcelain=v2</c> output.
/// Unchanged is shown with <b><c>.</c></b> rather than a space — that is the difference from v1.
/// </remarks>
public enum FileChangeKind
{
    /// <summary><c>.</c> — no change in this area.</summary>
    Unmodified,

    /// <summary><c>M</c></summary>
    Modified,

    /// <summary><c>A</c></summary>
    Added,

    /// <summary><c>D</c></summary>
    Deleted,

    /// <summary><c>R</c></summary>
    Renamed,

    /// <summary><c>C</c></summary>
    Copied,

    /// <summary><c>T</c> — the file type changed (a regular file becoming a symlink, say).</summary>
    TypeChanged,

    /// <summary><c>U</c> — unmerged because of a conflict.</summary>
    Unmerged,
}

/// <summary>
/// The conflict kind of an unmerged file.
/// </summary>
/// <remarks>
/// The meaning of the <c>XY</c> pair; "us" is the current branch (<c>HEAD</c>), "them" the branch
/// being merged. This distinction will be shown to the user directly in the conflict resolution UI
/// (Phase 07).
/// </remarks>
public enum ConflictKind
{
    None,

    /// <summary><c>UU</c> — both sides changed it.</summary>
    BothModified,

    /// <summary><c>AA</c> — her iki taraf da ekledi.</summary>
    BothAdded,

    /// <summary><c>DD</c> — her iki taraf da sildi.</summary>
    BothDeleted,

    /// <summary><c>AU</c> — we added it, they did not touch it.</summary>
    AddedByUs,

    /// <summary><c>UA</c> — they added it, we did not touch it.</summary>
    AddedByThem,

    /// <summary><c>DU</c> — we deleted it, they changed it.</summary>
    DeletedByUs,

    /// <summary><c>UD</c> — they deleted it, we changed it.</summary>
    DeletedByThem,
}

/// <summary>
/// The state of a submodule entry (the <c>S&lt;c&gt;&lt;m&gt;&lt;u&gt;</c> field).
/// </summary>
public readonly record struct SubmoduleState(
    bool CommitChanged,
    bool HasTrackedChanges,
    bool HasUntrackedChanges)
{
    public bool HasAnyChange => CommitChanged || HasTrackedChanges || HasUntrackedChanges;
}

/// <summary>
/// The state of a single file in the working directory.
/// </summary>
public sealed record FileStatus
{
    public required RepositoryPath Path { get; init; }

    /// <summary>
    /// The index's state relative to <c>HEAD</c> — that is, the <b>staged</b> change.
    /// </summary>
    public FileChangeKind StagedChange { get; init; } = FileChangeKind.Unmodified;

    /// <summary>
    /// The working tree's state relative to the index — that is, the <b>unstaged</b> change.
    /// </summary>
    public FileChangeKind UnstagedChange { get; init; } = FileChangeKind.Unmodified;

    /// <summary>Is it an untracked file?</summary>
    public bool IsUntracked { get; init; }

    /// <summary>Is it ignored by <c>.gitignore</c>?</summary>
    public bool IsIgnored { get; init; }

    /// <summary>The conflict kind; <see cref="ConflictKind.None"/> when there is no conflict.</summary>
    public ConflictKind Conflict { get; init; } = ConflictKind.None;

    /// <summary>
    /// The source path on a rename or copy.
    /// </summary>
    /// <remarks>
    /// In <c>-z</c> mode this value arrives in <b>a separate NUL record</b> (measured); the parser has
    /// to consume the next record after a <c>2</c> line.
    /// </remarks>
    public RepositoryPath? OriginalPath { get; init; }

    /// <summary>The rename/copy similarity percentage (<c>R100</c> → 100).</summary>
    public int? SimilarityScore { get; init; }

    /// <summary>Girdi bir submodule ise durumu.</summary>
    public SubmoduleState? Submodule { get; init; }

    public bool IsConflicted => Conflict != ConflictKind.None;

    /// <summary>Is there a staged change?</summary>
    public bool IsStaged =>
        StagedChange is not (FileChangeKind.Unmodified or FileChangeKind.Unmerged);

    /// <summary>Is there an unstaged change?</summary>
    public bool IsUnstaged =>
        IsUntracked
        || UnstagedChange is not (FileChangeKind.Unmodified or FileChangeKind.Unmerged);

    public override string ToString() => Path.Value;
}

/// <summary>
/// The complete state of the working directory.
/// </summary>
public sealed record WorkingTreeStatus
{
    /// <summary>The current commit; empty in an unborn repository.</summary>
    public CommitId Head { get; init; }

    /// <summary>Mevcut dal; detached ise <see langword="null"/>.</summary>
    public string? BranchName { get; init; }

    public bool IsDetached { get; init; }

    /// <summary>Are there no commits yet (<c># branch.oid (initial)</c>)?</summary>
    public bool IsUnborn { get; init; }

    public string? Upstream { get; init; }

    /// <summary>The position relative to the upstream; from the <c># branch.ab</c> header.</summary>
    public UpstreamTracking Tracking { get; init; } = UpstreamTracking.None;

    public required IReadOnlyList<FileStatus> Entries { get; init; }

    public IEnumerable<FileStatus> Staged => Entries.Where(e => e.IsStaged);

    public IEnumerable<FileStatus> Unstaged => Entries.Where(e => e.IsUnstaged && !e.IsUntracked);

    public IEnumerable<FileStatus> Untracked => Entries.Where(e => e.IsUntracked);

    public IEnumerable<FileStatus> Conflicted => Entries.Where(e => e.IsConflicted);

    public IEnumerable<FileStatus> Ignored => Entries.Where(e => e.IsIgnored);

    /// <summary>Are there no uncommitted changes at all?</summary>
    public bool IsClean => !Entries.Any(e => !e.IsIgnored);
}
