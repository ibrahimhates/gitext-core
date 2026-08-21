using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// How whitespace differences are handled (P04-T04).
/// </summary>
public enum WhitespaceMode
{
    /// <summary>Whitespace differences count as normal diffs.</summary>
    Include,

    /// <summary>
    /// Whitespace differences at the <b>end of a line</b> are ignored
    /// (<c>--ignore-space-at-eol</c>).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — this is genuinely a different level from <see cref="IgnoreChange"/>,</b>
    /// not a milder wording of it. A line where the whitespace <i>inside</i> it changed
    /// (<c>beta      gamma</c> → <c>beta gamma</c>) still appears in the diff under this mode,
    /// while <c>-b</c> drops it entirely. That is the point of offering all three: this one
    /// hides the "trailing whitespace was stripped" noise without hiding a reformat.
    /// </remarks>
    IgnoreEol,

    /// <summary>
    /// Changes in whitespace <b>amount</b> are ignored (<c>-b</c>).
    /// </summary>
    /// <remarks>
    /// MEASURED: this mode does not ignore whitespace added where there was none — such a
    /// file still stays in the diff.
    /// </remarks>
    IgnoreChange,

    /// <summary>
    /// All whitespace differences are ignored (<c>-w</c>).
    /// </summary>
    /// <remarks>
    /// MEASURED: in this mode, if the file becomes <b>identical</b> it drops out of both the
    /// raw and the patch section (the counts stay aligned). If it does not become identical
    /// (e.g. a blank line was added) it stays in both sections — <c>-w</c> ignores whitespace
    /// <i>inside</i> a line, not an added blank line.
    /// </remarks>
    IgnoreAll,
}

/// <summary>
/// Diff reading options (P04-T03, P04-T04).
/// </summary>
/// <remarks>
/// <b>The plan had an item that git does not have:</b> "case insensitivity".
/// Measured — there is <b>no</b> <c>git diff --ignore-case</c> option (it produces a usage
/// error). So there is no equivalent for it either.
/// </remarks>
public sealed record DiffOptions
{
    public static DiffOptions Default { get; } = new();

    /// <summary>
    /// Which parent to compare against for a merge commit (1-based).
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <see langword="null"/>, the <b>first parent</b> is used — the "what did this merge
    /// bring to the main line" view. This default was chosen by measurement: a plain
    /// <c>git show &lt;merge&gt;</c> produces <b>no output at all</b> on a clean merge, and the
    /// user would mistake that for a bug. (<c>--cc</c> is also empty on a clean merge; it only
    /// shows conflict resolutions.)
    /// </para>
    /// <para>
    /// ⚠️ <c>-m</c> is deliberately <b>not used</b>: it produces a separate section per parent,
    /// which breaks the single-file-list assumption. When a specific parent is requested, the
    /// <c>&lt;merge&gt;^N</c> syntax is used instead.
    /// </para>
    /// </remarks>
    public int? MergeParent { get; init; }

    /// <summary>
    /// Number of context lines to show around a hunk (<c>-U</c>); <see langword="null"/>
    /// means git's default (3).
    /// </summary>
    /// <remarks>
    /// MEASURED: <c>-U0</c> reduces the hunk header to the single-number form
    /// (<c>@@ -4 +4 @@</c>), i.e. the length is not written. The parser counts this as 1.
    /// </remarks>
    public int? ContextLines { get; init; }

    /// <summary>How whitespace differences are handled.</summary>
    public WhitespaceMode Whitespace { get; init; } = WhitespaceMode.Include;

    /// <summary>
    /// Should only blank-line insertions/deletions be ignored (<c>--ignore-blank-lines</c>)?
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>MEASURED — this option behaves differently from the others.</b> For a file where
    /// only blank lines changed, git <b>leaves it in the raw section but does not produce a
    /// patch block for it</b>. The parser therefore matches by blob id when the counts don't
    /// line up; the unmatched file appears <b>without a hunk</b>.
    /// </remarks>
    public bool IgnoreBlankLines { get; init; }

    /// <summary>
    /// Rename similarity threshold (1–100); <see langword="null"/> means git's default (50%).
    /// </summary>
    /// <remarks>
    /// MEASURED: for a file with 69% similarity, <c>-M50%</c> finds the rename,
    /// <c>-M90%</c> does not (it shows up as an add + delete).
    /// </remarks>
    public int? RenameThreshold { get; init; }

    /// <summary>
    /// Should copied files also be detected?
    /// </summary>
    /// <remarks>
    /// <b>MEASURED — <c>-C</c> alone is not enough.</b> A copy made from an unmodified file is
    /// not found by <c>-C</c> alone (the status stays <c>A</c>); finding it requires
    /// <see cref="FindCopiesHarder"/> (then <c>C100</c>).
    /// </remarks>
    public bool DetectCopies { get; init; }

    /// <summary>
    /// Should unmodified files also be examined when searching for copies
    /// (<c>--find-copies-harder</c>)?
    /// </summary>
    /// <remarks>
    /// git documents this as <b>expensive</b>; default off. Should not be turned on unless the
    /// user explicitly asks for it.
    /// </remarks>
    public bool FindCopiesHarder { get; init; }

    /// <summary>Copy similarity threshold (1–100); <see langword="null"/> means git's default.</summary>
    public int? CopyThreshold { get; init; }

    /// <summary>
    /// Should inline (word/character-level) changes also be computed (P04-T05)?
    /// </summary>
    /// <remarks>
    /// <b>git's <c>--word-diff</c> is NOT USED</b> — measured and found to not faithfully
    /// preserve line structure (which side an added blank line belongs to is not in the
    /// output). Segments are computed <b>locally</b> with <see cref="InlineDiff"/> on the exact
    /// line text the parser produced: no extra <c>git</c> invocation, no fidelity risk.
    /// </remarks>
    public bool WordLevel { get; init; }

    /// <summary>
    /// A file with more changed lines than this has its <b>content not read</b> (P04-T06).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0 or negative means no limit — the UI's "show anyway" uses this.
    /// </para>
    /// <para>
    /// <b>MEASURED:</b> a fully-changed 12.7 MB text file produces a <b>23 MB</b> patch (git
    /// does this in 0.12s — the problem is not in git but in us). Creating 800k
    /// <c>DiffLine</c> objects for such a file locks up the app, due to the per-object
    /// overhead measured in Phase 03.
    /// </para>
    /// <para>
    /// The counts come from <c>--numstat</c>: since it's learned <b>without generating
    /// content</b>, line counts still appear correctly in the file list.
    /// </para>
    /// <para>
    /// <b>The limit was raised by measurement in P04-T14 (20,000 → 50,000).</b> The initial
    /// value was set cautiously, before there was a viewer. Real measurement: in
    /// <c>git/git</c>, the 43,671-line diff of <c>po/zh_CN.po</c> converts to lines in
    /// <b>202 ms</b>, takes 45 MB, and scrolling frame time is <b>0.7 ms</b>. The old limit was
    /// needlessly filtering out <b>real</b> files at this scale as "too large". The actual
    /// danger case of 800k lines is still blocked.
    /// </para>
    /// </remarks>
    public int MaximumChangedLines { get; init; } = 50_000;

    /// <summary>
    /// Upper bound on <c>git</c> output; reading stops if exceeded (P04-T06).
    /// </summary>
    /// <remarks>
    /// Last line of defense: <see cref="MaximumChangedLines"/> guards per file, but the sum of
    /// thousands of medium-sized files can still be large. If the limit is exceeded, the
    /// result is <b>not parsed</b>; parsing half of the output would silently produce
    /// incomplete data.
    /// </remarks>
    public long MaximumOutputBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// Encoding of the diff content; <see langword="null"/> means UTF-8 (P04-T07).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MEASURED:</b> <c>git diff</c> output is <b>not in a single encoding</b> — headers and
    /// markers are ASCII, but line content is <b>the file's own bytes</b>. Unlike git commit
    /// messages, it does not translate (Phase 02 had <c>i18n.logOutputEncoding</c>; diff has
    /// <b>no</b> equivalent). On a Latin-5 file, a <c>0xFC</c> byte comes through raw and gets
    /// <b>silently corrupted</b> if assumed to be UTF-8.
    /// </para>
    /// <para>
    /// The solution was taken from GitExtensions' <c>PatchProcessor</c>: the output is read
    /// <b>losslessly</b> (each byte as one character), parsing is unaffected since the
    /// structure is ASCII, and line content is later re-decoded with this encoding. They also
    /// noted the ideal would be per-file from <c>.gitattributes</c>; for now, a single encoding
    /// per repository.
    /// </para>
    /// </remarks>
    public Encoding? ContentEncoding { get; init; }

    /// <summary>Is rename detection on?</summary>
    /// <remarks>
    /// <b>MEASURED:</b> detection is <b>on by default</b> in modern git (<c>diff.renames</c>).
    /// So not writing <c>-M</c> does <b>not</b> turn it off; turning it off requires
    /// <c>--no-renames</c>. Both flags are passed explicitly, so behavior is independent of the
    /// user's <c>.gitconfig</c> (the same decision made in Phase 02 for
    /// <c>i18n.logOutputEncoding</c>).
    /// </remarks>
    public bool DetectRenames { get; init; } = true;
}

/// <summary>
/// Reads diffs from the repository (P04-T03).
/// </summary>
public interface IDiffReader
{
    /// <summary>
    /// Reads the changes a commit itself introduced.
    /// </summary>
    Task<IReadOnlyList<FileDiff>> ReadCommitAsync(
        string workingDirectory,
        CommitId commit,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the diff between two arbitrary revisions.</summary>
    Task<IReadOnlyList<FileDiff>> ReadBetweenAsync(
        string workingDirectory,
        string fromRevision,
        string toRevision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the diff between a revision and the <b>working tree</b> (P04-T16).
    /// </summary>
    /// <remarks>
    /// <c>git diff &lt;rev&gt;</c>. Different from <see cref="ReadUnstagedAsync"/>: that only
    /// compares the index against the working tree, whereas this also includes staged changes.
    /// </remarks>
    Task<IReadOnlyList<FileDiff>> ReadAgainstWorkingTreeAsync(
        string workingDirectory,
        string revision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the diff between the working directory and the index (unstaged changes).</summary>
    Task<IReadOnlyList<FileDiff>> ReadUnstagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the diff between the index and <c>HEAD</c> (staged changes).</summary>
    Task<IReadOnlyList<FileDiff>> ReadStagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IDiffReader"/>
public sealed class DiffReader : IDiffReader
{
    private readonly IGitProcessRunner _runner;

    public DiffReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public Task<IReadOnlyList<FileDiff>> ReadCommitAsync(
        string workingDirectory,
        CommitId commit,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (commit.IsEmpty)
        {
            throw new ArgumentException("A commit id cannot be empty.", nameof(commit));
        }

        options ??= DiffOptions.Default;

        // If a specific parent was requested, compare with `<commit>^N <commit>`.
        // No need for this path for the first parent; the single command below already does it.
        if (options.MergeParent is > 1 and int parent)
        {
            return ReadBetweenAsync(
                workingDirectory,
                $"{commit.Value}^{parent}",
                commit.Value,
                options,
                cancellationToken);
        }

        // A SINGLE COMMAND covers three cases at once (measured):
        //   --root         → avoids `<sha>^` crashing on the root commit
        //   --first-parent → produces a single, meaningful diff for a merge (plain `git show` returns EMPTY)
        //   normal commit  → both are harmless
        List<string> arguments =
        [
            "show",
            "--root",
            "--first-parent",

            // Commit subject/message is suppressed: the parser expects output to start with `:`.
            "--format=",
        ];

        AddFormatArguments(arguments, options);
        arguments.Add(commit.Value);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    public Task<IReadOnlyList<FileDiff>> ReadBetweenAsync(
        string workingDirectory,
        string fromRevision,
        string toRevision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(toRevision);

        options ??= DiffOptions.Default;

        List<string> arguments = ["diff"];

        AddFormatArguments(arguments, options);
        arguments.Add(fromRevision);
        arguments.Add(toRevision);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    public Task<IReadOnlyList<FileDiff>> ReadAgainstWorkingTreeAsync(
        string workingDirectory,
        string revision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        options ??= DiffOptions.Default;

        List<string> arguments = ["diff"];

        AddFormatArguments(arguments, options);
        arguments.Add(revision);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    public Task<IReadOnlyList<FileDiff>> ReadUnstagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        options ??= DiffOptions.Default;

        List<string> arguments = ["diff"];

        AddFormatArguments(arguments, options);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    public Task<IReadOnlyList<FileDiff>> ReadStagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        options ??= DiffOptions.Default;

        List<string> arguments = ["diff", "--cached"];

        AddFormatArguments(arguments, options);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    /// <summary>
    /// Common arguments that set up the format the parser expects.
    /// </summary>
    /// <remarks>
    /// <c>--raw -z</c> gives paths raw (unquoted), <c>--patch</c> adds hunks.
    /// Both are obtained in a single call; separate calls would mean two separate processes and
    /// two separate snapshots (the working directory could change in between).
    /// </remarks>
    private static void AddFormatArguments(List<string> arguments, DiffOptions options)
    {
        arguments.Add("--raw");

        // Gives the number of changed lines per file without generating content; the size
        // guard relies on this. Measured: adding it to the same call adds no extra cost
        // (20.5 vs 21.4 ms).
        arguments.Add("--numstat");

        arguments.Add("-z");
        arguments.Add("--patch");

        // Both are passed EXPLICITLY: omitting `-M` does not turn detection off (on by
        // default), and the user's `diff.renames` setting must not change our behavior.
        arguments.Add(options.DetectRenames
            ? options.RenameThreshold is { } threshold
                ? $"-M{Clamp(threshold)}%"
                : "-M"
            : "--no-renames");

        if (options.DetectCopies)
        {
            arguments.Add(options.CopyThreshold is { } copyThreshold
                ? $"-C{Clamp(copyThreshold)}%"
                : "-C");

            // MEASURED: `-C` alone cannot find a copy made from an UNMODIFIED file. If the
            // user wanted copy detection but didn't enable this, most copies remain invisible
            // — hence a separate, explicit option.
            if (options.FindCopiesHarder)
            {
                arguments.Add("--find-copies-harder");
            }
        }

        if (options.ContextLines is { } context)
        {
            arguments.Add($"-U{Math.Max(context, 0)}");
        }

        switch (options.Whitespace)
        {
            case WhitespaceMode.IgnoreEol:
                arguments.Add("--ignore-space-at-eol");
                break;

            case WhitespaceMode.IgnoreChange:
                arguments.Add("-b");
                break;

            case WhitespaceMode.IgnoreAll:
                arguments.Add("-w");
                break;

            default:
                break;
        }

        if (options.IgnoreBlankLines)
        {
            arguments.Add("--ignore-blank-lines");
        }

    }

    /// <summary>Clamps a threshold percentage to the range git accepts.</summary>
    private static int Clamp(int percent) => Math.Clamp(percent, 1, 100);

    private async Task<IReadOnlyList<FileDiff>> RunAsync(
        string workingDirectory,
        List<string> arguments,
        DiffOptions options,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = arguments,
                MaximumOutputBytes = options.MaximumOutputBytes > 0 ? options.MaximumOutputBytes : null,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.OutputTruncated)
        {
            // Parsing truncated output would mean silently showing an incomplete diff. A
            // second, content-free read is done instead so the file list is still shown.
            return await ReadMetadataOnlyAsync(workingDirectory, arguments, cancellationToken)
                .ConfigureAwait(false);
        }

        IReadOnlyList<FileDiff> parsed = DiffParser.Parse(
            result.GetStandardOutputLossless(),
            options.WordLevel,
            options.MaximumChangedLines,
            options.ContentEncoding);

        if (options.Whitespace == WhitespaceMode.IgnoreAll)
        {
            // On some git versions `-w` keeps a whitespace-only modified file in metadata with
            // no hunks and zero line stats. Keep behavior stable across versions by hiding that row.
            parsed =
            [
                .. parsed.Where(diff =>
                    diff.Change != FileChangeKind.Modified
                    || diff.HasHunks
                    || diff.IsBinary
                    || diff.IsModeOnlyChange
                    || diff.AddedLines != 0
                    || diff.RemovedLines != 0),
            ];
        }

        if (options.Whitespace == WhitespaceMode.IgnoreEol)
        {
            // MEASURED: `git --ignore-space-at-eol` has a long-standing bug in git ≤ 2.34 where it
            // does NOT filter out files whose only changes are trailing-whitespace additions/removals.
            // The flag was introduced in git 1.6, but the filtering logic was buggy until at least
            // 2.35 (some reports go further). Since we must support git 2.30, we do client-side
            // post-processing: remove diffs whose hunks contain ONLY trailing-whitespace changes.
            parsed =
            [
                .. parsed.Where(diff => HasNonEolChange(diff)),
            ];
        }

        return parsed;
    }

    /// <summary>
    /// Returns true when the diff contains at least one change that is NOT a trailing-whitespace-
    /// only modification. This is used for client-side post-processing of `--ignore-space-at-eol`
    /// on git versions where the flag does not filter these changes correctly (≤ 2.34).
    /// </summary>
    private static bool HasNonEolChange(FileDiff diff)
    {
        if (diff.IsBinary || diff.IsModeOnlyChange)
        {
            // Binary or mode-only changes — keep them.
            return true;
        }

        // MEASURED on git ≤ 2.34 with `--ignore-space-at-eol`: trailing-whitespace-only files
        // can appear in the raw section WITHOUT any patch content (no hunks, no changed lines).
        // We must treat these as "should be hidden" to compensate for the broken server-side filter.
        if (!diff.HasHunks && diff.AddedLines == 0 && diff.RemovedLines == 0)
        {
            return false;
        }

        foreach (DiffHunk hunk in diff.Hunks)
        {
            // Collect all Removed and Added lines for pairing.
            var removedLines = new List<string>();
            var addedLines = new List<string>();

            for (int i = 0; i < hunk.Lines.Count; i++)
            {
                DiffLine line = hunk.Lines[i];
                if (line.Kind == DiffLineKind.Removed)
                {
                    removedLines.Add(line.Content);
                }
                else if (line.Kind == DiffLineKind.Added)
                {
                    addedLines.Add(line.Content);
                }
            }

            // Pair Removed/Added lines in order: each pair represents a modification.
            // Unpaired Removed or Added lines indicate content was deleted or inserted —
            // those are beyond trailing-whitespace-only changes.
            int minPairs = Math.Min(removedLines.Count, addedLines.Count);

            for (int i = 0; i < removedLines.Count || i < addedLines.Count; i++)
            {
                bool isPaired = i < minPairs;

                if (!isPaired)
                {
                    // Unpaired addition or removal — not a trailing-whitespace-only change.
                    return true;
                }

                string oldContent = removedLines[i];
                string newContent = addedLines[i];

                // Strip trailing whitespace and compare.
                if (StripTrailing(oldContent) != StripTrailing(newContent))
                {
                    // Inline change (or actual content difference).
                    return true;
                }
            }
        }

        return false;
    }

    private static string StripTrailing(string s)
    {
        int end = s.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(s[end]))
        {
            end--;
        }
        return s[..(end + 1)];
    }

    /// <summary>
    /// Only the file list and line counts — no patch is requested.
    /// </summary>
    /// <remarks>
    /// Used when the output limit was exceeded. The user still sees which files changed;
    /// content is marked as "too large".
    /// </remarks>
    private async Task<IReadOnlyList<FileDiff>> ReadMetadataOnlyAsync(
        string workingDirectory,
        List<string> arguments,
        CancellationToken cancellationToken)
    {
        List<string> metadata = [.. arguments.Where(a => a != "--patch")];

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand { WorkingDirectory = workingDirectory, Arguments = metadata },
            cancellationToken).ConfigureAwait(false);

        // Limit 1: every file is considered "too large", since we don't know which one overflowed.
        return DiffParser.Parse(
            result.GetStandardOutputLossless(), inlineSegments: false, maximumChangedLines: 1);
    }
}
