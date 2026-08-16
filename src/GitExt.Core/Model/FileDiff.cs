namespace GitExt.Core.Model;

/// <summary>
/// Kind of a single line within a diff.
/// </summary>
/// <remarks>
/// <b>Deviation from the plan:</b> <c>P04-T01</c> listed the <c>\ No newline at end of file</c>
/// marker as a separate line <i>kind</i>. Measured, and found not to be so: this marker isn't a
/// line on its own, it's <b>an attribute of the preceding line</b> — and it can even appear
/// twice within the same hunk, after both a <c>-</c> and a <c>+</c> line. So it was modeled not
/// as a kind but as the <see cref="DiffLine.EndsWithoutNewline"/> flag; reproducing the patch
/// exactly is only possible this way (Phase 05).
/// </remarks>
public enum DiffLineKind
{
    /// <summary>Unchanged context line (leading space).</summary>
    Context,

    /// <summary>Added line (<c>+</c>).</summary>
    Added,

    /// <summary>Removed line (<c>-</c>).</summary>
    Removed,
}

/// <summary>
/// A changed/unchanged segment within a line (P04-T05).
/// </summary>
/// <param name="Kind">
/// A <see cref="DiffLineKind.Context"/> segment is the same on both sides; <see cref="DiffLineKind.Added"/>
/// and <see cref="DiffLineKind.Removed"/> exist only on their own side.
/// </param>
/// <param name="Text">Raw text of the segment.</param>
public sealed record DiffSegment(DiffLineKind Kind, string Text)
{
    public bool IsAdded => Kind == DiffLineKind.Added;

    public bool IsRemoved => Kind == DiffLineKind.Removed;

    public override string ToString() => Text;
}

/// <summary>
/// A single line within a diff.
/// </summary>
/// <param name="Kind">The line's kind.</param>
/// <param name="Content">Line content — the leading <c>+</c>/<c>-</c>/space is <b>not included</b>.</param>
public sealed record DiffLine(DiffLineKind Kind, string Content)
{
    /// <summary>Line number in the old file; <see langword="null"/> for added lines.</summary>
    public int? OldLineNumber { get; init; }

    /// <summary>Line number in the new file; <see langword="null"/> for removed lines.</summary>
    public int? NewLineNumber { get; init; }

    /// <summary>
    /// Does the <c>\ No newline at end of file</c> marker follow this line?
    /// </summary>
    /// <remarks>
    /// Means the file has no trailing newline character. When reproducing the patch, this
    /// marker must be written <b>immediately after this exact line</b>, otherwise
    /// <c>git apply</c> refuses it.
    /// </remarks>
    public bool EndsWithoutNewline { get; init; }

    /// <summary>
    /// Inline change segments (P04-T05); empty if word-level diffing wasn't requested.
    /// </summary>
    /// <remarks>
    /// Concatenating the segments reproduces <see cref="Content"/> <b>exactly</b> — this was
    /// confirmed by measurement and drove the design: with git's <b>default</b> word separator
    /// a fake space was being appended to the end of the old line, whereas with the
    /// character-level separator (<c>--word-diff-regex=.</c>) reconstruction is exactly correct.
    /// </remarks>
    public IReadOnlyList<DiffSegment> Segments { get; init; } = [];

    /// <summary>Were inline segments computed?</summary>
    public bool HasSegments => Segments.Count > 0;

    public override string ToString() => Kind switch
    {
        DiffLineKind.Added => "+" + Content,
        DiffLineKind.Removed => "-" + Content,
        _ => " " + Content,
    };
}

/// <summary>
/// A single change block (hunk) within a file's diff.
/// </summary>
public sealed record DiffHunk
{
    /// <summary>
    /// <b>Raw text</b> of the hunk header — the entire <c>@@ -1,3 +1,3 @@ context</c> line.
    /// </summary>
    /// <remarks>
    /// The parsed fields already exist; the raw text is kept separately because in Phase 05
    /// we'll need to <b>hand a modified patch back to <c>git apply</c></b>, and that's only
    /// safe if the original format is preserved. Trying to regenerate it would mean imitating
    /// every fine detail of git's format (such as omitting the length on a single-line hunk).
    /// </remarks>
    public required string Header { get; init; }

    /// <summary>Starting line in the old file (1-based).</summary>
    public required int OldStart { get; init; }

    /// <summary>How many lines from the old file are covered.</summary>
    public required int OldLength { get; init; }

    /// <summary>Starting line in the new file (1-based).</summary>
    public required int NewStart { get; init; }

    /// <summary>How many lines from the new file are covered.</summary>
    public required int NewLength { get; init; }

    /// <summary>
    /// The context text after the header's second <c>@@</c> (usually the enclosing function name).
    /// </summary>
    /// <remarks>May be empty; git doesn't always fill this field in.</remarks>
    public string Section { get; init; } = string.Empty;

    public required IReadOnlyList<DiffLine> Lines { get; init; }

    public int AddedCount => Lines.Count(l => l.Kind == DiffLineKind.Added);

    public int RemovedCount => Lines.Count(l => l.Kind == DiffLineKind.Removed);

    public override string ToString() => Header;
}

/// <summary>
/// A single file's diff (P04-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>⚠️ Paths are NOT TAKEN from the <c>diff --git</c> header.</b> Measured: that line
/// cannot be parsed in general — for paths containing spaces there's no safe way to split the
/// two paths (<c>a/sub dir/b -&gt; c.txt b/sub dir/b -&gt; c.txt</c>), and non-ASCII names get
/// quoted with C-style octal escapes. Paths, the change kind, and mode/blob information come
/// from <c>git diff --raw -z</c> output (ADR-0003: the machine-readable channel).
/// </para>
/// <para>
/// <b>⚠️ <see cref="Hunks"/> CAN be EMPTY and that's normal.</b> In four measured cases git
/// produces no hunks at all: a 100%-similarity rename, a mode-only change, an empty new file,
/// and a binary file. Code that assumes every file has hunks breaks on these repositories.
/// </para>
/// </remarks>
public sealed record FileDiff
{
    /// <summary>
    /// Path of the file — the <b>new</b> path except for a deletion.
    /// </summary>
    /// <remarks>
    /// A deleted file has no new path, so this field carries the old path instead. For a
    /// rename, the new path is here and the old one is in <see cref="OldPath"/>.
    /// </remarks>
    public required RepositoryPath Path { get; init; }

    /// <summary>
    /// Previous path; only set for renames and copies.
    /// </summary>
    public RepositoryPath? OldPath { get; init; }

    public required FileChangeKind Change { get; init; }

    /// <summary>
    /// Rename/copy similarity percentage (<c>R100</c> → 100).
    /// </summary>
    public int? SimilarityScore { get; init; }

    /// <summary>Old file mode (<c>100644</c>, <c>100755</c>, <c>120000</c>, <c>160000</c>).</summary>
    public string OldMode { get; init; } = string.Empty;

    /// <summary>New file mode.</summary>
    public string NewMode { get; init; } = string.Empty;

    /// <summary>Blob id of the old content.</summary>
    /// <remarks>
    /// <see cref="CommitId"/> is used as the type: all object ids in a repository share the
    /// same format, and this type is already used the same way in <c>TreeEntry</c>/<c>BlobContent</c>.
    /// </remarks>
    public CommitId OldBlob { get; init; }

    /// <summary>Blob id of the new content.</summary>
    public CommitId NewBlob { get; init; }

    /// <summary>
    /// Did git report this file as binary?
    /// </summary>
    /// <remarks>
    /// For binary files, the line <c>Binary files a/… and b/… differ</c> comes instead of
    /// content; <see cref="Hunks"/> is empty.
    /// </remarks>
    public bool IsBinary { get; init; }

    public required IReadOnlyList<DiffHunk> Hunks { get; init; }

    public bool HasHunks => Hunks.Count > 0;

    /// <summary>
    /// Did only the file mode change (content identical)?
    /// </summary>
    /// <remarks>
    /// Measured: in this case <b>both blob ids are identical</b> in <c>--raw</c> output and the
    /// status letter is <c>M</c>; the unified diff output has only <c>old mode</c>/<c>new mode</c>
    /// lines and no hunk. This needs its own display — saying "changed" and showing an empty
    /// diff looks like a bug to the user.
    /// </remarks>
    public bool IsModeOnlyChange =>
        !OldBlob.IsEmpty
        && OldBlob == NewBlob
        && OldMode.Length > 0
        && NewMode.Length > 0
        && OldMode != NewMode;

    /// <summary>Is the file a submodule (mode <c>160000</c>)?</summary>
    public bool IsSubmodule => OldMode == "160000" || NewMode == "160000";

    /// <summary>Is it a symbolic link (mode <c>120000</c>)?</summary>
    public bool IsSymlink => OldMode == "120000" || NewMode == "120000";

    /// <summary>Did the executable flag change?</summary>
    public bool IsExecutableChanged =>
        OldMode.Length > 0 && NewMode.Length > 0
        && (OldMode == "100755") != (NewMode == "100755");

    /// <summary>
    /// The file's content was <b>not read</b> because it exceeds the configured change limit (P04-T06).
    /// </summary>
    /// <remarks>
    /// Hunks are empty, but <see cref="AddedLines"/>/<see cref="RemovedLines"/> are still
    /// correct: the counts come from <c>--numstat</c> and are obtained without producing
    /// content. In this case the UI offers a "too large, show anyway" option.
    /// </remarks>
    public bool IsTooLarge { get; init; }

    /// <summary>Added line count from <c>--numstat</c>; <see langword="null"/> if unavailable.</summary>
    public int? StatAdded { get; init; }

    /// <summary>Removed line count from <c>--numstat</c>; <see langword="null"/> if unavailable.</summary>
    public int? StatRemoved { get; init; }

    /// <summary>
    /// Number of added lines.
    /// </summary>
    /// <remarks>
    /// <c>--numstat</c> takes priority: the count stays correct even when content wasn't read
    /// (a too-large file). Otherwise it's computed from the hunks.
    /// </remarks>
    public int AddedLines => StatAdded ?? Hunks.Sum(h => h.AddedCount);

    public int RemovedLines => StatRemoved ?? Hunks.Sum(h => h.RemovedCount);

    /// <summary>Total number of changed lines — the size limit is applied against this.</summary>
    public int ChangedLines => AddedLines + RemovedLines;

    public override string ToString() =>
        OldPath is { } previous ? $"{Change}: {previous} → {Path}" : $"{Change}: {Path}";
}
