using System.Collections.ObjectModel;
using System.Text;
using System.Globalization;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A single row in the changed-files list (P04-T08).
/// </summary>
public sealed class FileChangeRow
{
    public FileChangeRow(FileDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        Diff = diff;
    }

    public FileDiff Diff { get; }

    public RepositoryPath Path => Diff.Path;

    /// <summary>File name — the row title in the flat list.</summary>
    public string Name => Diff.Path.Name;

    /// <summary>Folder the file lives in; empty when at the root.</summary>
    public string Directory => Diff.Path.Parent.Value;

    /// <summary>
    /// Shows the status as a single letter (<c>A</c>, <c>M</c>, <c>D</c>, <c>R</c>, <c>C</c>, <c>T</c>).
    /// </summary>
    /// <remarks>
    /// Same letters as in git's <c>--raw</c> output: the user recognises here the same
    /// notation seen on the command line.
    /// </remarks>
    public string StatusLetter => Diff.Change switch
    {
        FileChangeKind.Added => "A",
        FileChangeKind.Modified => "M",
        FileChangeKind.Deleted => "D",
        FileChangeKind.Renamed => "R",
        FileChangeKind.Copied => "C",
        FileChangeKind.TypeChanged => "T",
        FileChangeKind.Unmerged => "U",
        _ => " ",
    };

    public bool IsAdded => Diff.Change == FileChangeKind.Added;

    public bool IsDeleted => Diff.Change == FileChangeKind.Deleted;

    public bool IsRenamed => Diff.Change is FileChangeKind.Renamed or FileChangeKind.Copied;

    public bool IsBinary => Diff.IsBinary;

    public bool IsTooLarge => Diff.IsTooLarge;

    /// <summary>
    /// Line counts; not shown for a binary file.
    /// </summary>
    /// <remarks>
    /// <c>--numstat</c> gives no numbers for a binary file (measured: <c>-</c> comes back).
    /// Showing "0 / 0" would mean "nothing changed at all".
    /// </remarks>
    public bool HasLineCounts => !Diff.IsBinary && Diff.StatAdded is not null;

    public int AddedLines => Diff.AddedLines;

    public int RemovedLines => Diff.RemovedLines;

    /// <summary>Old path on a rename; empty otherwise.</summary>
    public string RenamedFrom => Diff.OldPath?.Value ?? string.Empty;

    public override string ToString() => $"{StatusLetter} {Path}";
}

/// <summary>
/// A single row in the unified diff view (P04-T09).
/// </summary>
/// <remarks>
/// Hunk headers also flow through as rows: a single flat list is required for
/// virtualisation — showing hunks as separate groups would mean a control per row.
/// </remarks>
public sealed class DiffLineRow
{
    private DiffLineRow(string text, DiffLineKind kind)
    {
        Text = text;
        Kind = kind;
    }

    /// <summary>Produces a hunk header row.</summary>
    /// <remarks>
    /// The header is handed over <b>as a segment</b> too: the view always draws a row
    /// through <see cref="Segments"/>, and if left empty the header text never appears
    /// on screen (caught exactly this way rendering a real repository).
    /// </remarks>
    public static DiffLineRow ForHunk(DiffHunk hunk, int hunkIndex = -1) =>
        new(hunk.Header, DiffLineKind.Context)
        {
            IsHunkHeader = true,
            HunkIndex = hunkIndex,
            Segments = [new DiffSegment(DiffLineKind.Context, hunk.Header)],
        };

    /// <summary>Produces a content row.</summary>
    /// <param name="line">Model line.</param>
    /// <param name="text">
    /// Display settings (tab width, whitespace rendering). The model content stays
    /// <b>unchanged</b> — only what appears on screen is transformed.
    /// </param>
    /// <param name="hunkIndex">Index of the hunk the row belongs to.</param>
    /// <param name="lineIndex">Index of the row within its hunk.</param>
    public static DiffLineRow ForLine(
        DiffLine line,
        DiffTextOptions? text = null,
        int hunkIndex = -1,
        int lineIndex = -1)
    {
        text ??= DiffTextOptions.Default;

        string raw = Display(line.Content);

        return new DiffLineRow(DiffTextFormatter.Format(raw, text), line.Kind)
        {
            RawText = raw,
            HunkIndex = hunkIndex,
            LineIndex = lineIndex,
            OldLineNumber = line.OldLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            NewLineNumber = line.NewLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            EndsWithoutNewline = line.EndsWithoutNewline,
            Segments = DiffTextFormatter.Format(BuildSegments(line), text),
        };
    }

    /// <summary>
    /// Filler row placed on the side that has no counterpart in side-by-side view (P04-T10).
    /// </summary>
    /// <remarks>
    /// A filler is <b>not an empty line</b>: it means "there is no line here" and is painted
    /// differently in the UI. Confused with an empty context line, the user would believe a
    /// line exists that does not.
    /// </remarks>
    public static DiffLineRow Filler { get; } =
        new(string.Empty, DiffLineKind.Context) { IsFiller = true };

    /// <summary>Text as shown on screen: tabs expanded, whitespace marked if requested.</summary>
    public string Text { get; }

    /// <summary>
    /// Text to be copied — the form with <b>no display transformation applied</b>.
    /// </summary>
    /// <remarks>
    /// ⚠️ The reason for keeping it separate is concrete: with whitespace rendering on,
    /// <see cref="Text"/> contains <c>·</c> and <c>»</c> characters. Had copying used it, the
    /// user would get <b>broken code</b> on the clipboard; tabs would have become spaces too.
    /// </remarks>
    public string RawText { get; private init; } = string.Empty;

    public DiffLineKind Kind { get; }

    public bool IsHunkHeader { get; private init; }

    /// <summary>
    /// Index of the hunk the row belongs to; <c>-1</c> when unknown.
    /// </summary>
    /// <remarks>
    /// Partial staging (P05-T10) uses this when turning the selected rows into a
    /// <see cref="PatchSelection"/>. The on-screen row order is <b>not enough</b>: hunk
    /// headers sit in the list too and shift the indices.
    /// </remarks>
    public int HunkIndex { get; private init; } = -1;

    /// <summary>Index of the row within its hunk; <c>-1</c> on header and filler rows.</summary>
    public int LineIndex { get; private init; } = -1;

    public bool IsFiller { get; private init; }

    public string OldLineNumber { get; private init; } = string.Empty;

    public string NewLineNumber { get; private init; } = string.Empty;

    public bool EndsWithoutNewline { get; private init; }

    /// <summary>
    /// Segments of the row; a single segment when no intra-line diff was computed.
    /// </summary>
    /// <remarks>
    /// The view always draws through segments: making the "are there segments" distinction
    /// in the UI would mean maintaining two separate templates.
    /// </remarks>
    public IReadOnlyList<DiffSegment> Segments { get; private init; } = [];

    public bool IsAdded => !IsHunkHeader && Kind == DiffLineKind.Added;

    public bool IsRemoved => !IsHunkHeader && Kind == DiffLineKind.Removed;

    /// <summary>
    /// Display text: a trailing <c>\r</c> is trimmed.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED (P04-T07):</b> in a CRLF file the line content ends with <c>\r</c> and the
    /// model <b>keeps it deliberately</b> (needed in phase 05 to hand the patch back to
    /// <c>git apply</c> byte for byte). On screen it would show up as a box character.
    /// </remarks>
    private static string Display(string content) =>
        content.EndsWith('\r') ? content[..^1] : content;

    private static IReadOnlyList<DiffSegment> BuildSegments(DiffLine line)
    {
        if (line.Segments.Count == 0)
        {
            return [new DiffSegment(DiffLineKind.Context, Display(line.Content))];
        }

        DiffSegment[] segments = [.. line.Segments];

        // A trailing `\r` can only occur in the last segment.
        int last = segments.Length - 1;

        if (segments[last].Text.EndsWith('\r'))
        {
            segments[last] = segments[last] with { Text = segments[last].Text[..^1] };
        }

        return segments;
    }

    public override string ToString() => IsHunkHeader ? Text : $"{Kind} {Text}";
}

/// <summary>
/// Which form the diff is copied to the clipboard in (P04-T12).
/// </summary>
/// <remarks>
/// All four modes map to <b>GitExtensions' four copy commands</b>: <i>Copy</i>
/// (code without prefixes), <i>Copy patch</i>, <i>Copy old version</i>, <i>Copy new version</i>.
/// </remarks>
public enum DiffCopyMode
{
    /// <summary>Code only: no <c>+</c>/<c>-</c> prefixes and no hunk headers. Default.</summary>
    Code,

    /// <summary>Patch form: with hunk headers and <c>+</c>/<c>-</c>/space prefixes.</summary>
    Patch,

    /// <summary>The old state of the file: added lines are skipped.</summary>
    OldVersion,

    /// <summary>The new state of the file: deleted lines are skipped.</summary>
    NewVersion,
}

/// <summary>
/// A single row in the side-by-side view: old on the left, new on the right (P04-T10).
/// </summary>
/// <remarks>
/// Both sides are <b>always populated</b>; the side without a counterpart gets a
/// <see cref="DiffLineRow.Filler"/>. That way the template needs no <c>null</c> check
/// and the filler can be painted separately.
/// </remarks>
public sealed class SideBySideLineRow
{
    private SideBySideLineRow(DiffLineRow left, DiffLineRow right)
    {
        Left = left;
        Right = right;
    }

    public static SideBySideLineRow ForHunk(string header, int hunkIndex = -1) =>
        new(DiffLineRow.Filler, DiffLineRow.Filler)
        {
            IsHunkHeader = true,
            Header = header,
            HunkIndex = hunkIndex,
        };

    public static SideBySideLineRow ForRow(SideBySideRow row, DiffTextOptions? text = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new SideBySideLineRow(
            row.Left is null
                ? DiffLineRow.Filler
                : DiffLineRow.ForLine(row.Left, text, row.HunkIndex, row.LeftIndex),
            row.Right is null
                ? DiffLineRow.Filler
                : DiffLineRow.ForLine(row.Right, text, row.HunkIndex, row.RightIndex));
    }

    public DiffLineRow Left { get; }

    public DiffLineRow Right { get; }

    public bool IsHunkHeader { get; private init; }

    /// <summary>
    /// Index of the hunk the row belongs to; <c>-1</c> when unknown.
    /// </summary>
    /// <remarks>
    /// Partial staging (P05-T10) uses this when turning the selected rows into a
    /// <see cref="PatchSelection"/>. The on-screen row order is <b>not enough</b>: hunk
    /// headers sit in the list too and shift the indices.
    /// </remarks>
    public int HunkIndex { get; private init; } = -1;

    /// <summary>Index of the row within its hunk; <c>-1</c> on header and filler rows.</summary>
    public int LineIndex { get; private init; } = -1;

    public string Header { get; private init; } = string.Empty;

    public override string ToString() =>
        IsHunkHeader ? Header : $"{Left.Text} │ {Right.Text}";
}

/// <summary>
/// Shows the changed files of a revision and the diff of the selected file (P04-T08, P04-T09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately INDEPENDENT of the main window.</b> The same component is used in two
/// places: the panel in the main window and the comparison window in <c>P04-T16</c>. The
/// decision was made by looking at GitExtensions — there too an embedded diff area
/// (<c>FormBrowse</c>) exists plus a separate <b>modeless</b> comparison window
/// (<c>FormDiff</c>, opened with <c>Show()</c>, several at once).
/// </para>
/// <para>
/// So this class knows neither <c>MainWindow</c> nor the commit list; <b>what to show</b>
/// is told to it from the outside.
/// </para>
/// </remarks>
public sealed partial class DiffViewModel : ViewModelBase
{
    /// <summary>
    /// Delay waited before the read starts.
    /// </summary>
    /// <remarks>
    /// The user can hold down <c>↓</c> in the commit list; we wait rather than start a
    /// <c>git</c> process per row, and cancel when the selection changes.
    /// The same solution was applied to the signature read in P03-T15.
    /// </remarks>
    private static readonly TimeSpan _loadDelay = TimeSpan.FromMilliseconds(150);

    private readonly IDiffReader _reader;

    private CancellationTokenSource? _loading;
    private IReadOnlyList<FileChangeRow> _allFiles = [];

    public DiffViewModel(IDiffReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>Files that pass the filter.</summary>
    public ObservableCollection<FileChangeRow> Files { get; } = [];

    /// <summary>Tree grouped by folder, independent of the filter.</summary>
    public ObservableCollection<FileTreeNode> Tree { get; } = [];

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    public FileChangeRow? SelectedFile =>
        SelectedIndex >= 0 && SelectedIndex < Files.Count ? Files[SelectedIndex] : null;

    partial void OnSelectedIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedFile));
        ShowSelectedFileLines();
    }

    /// <summary>Diff lines of the selected file (P04-T09).</summary>
    public AvaloniaList<DiffLineRow> Lines { get; } = [];

    /// <summary>Side-by-side rows of the selected file (P04-T10).</summary>
    public AvaloniaList<SideBySideLineRow> SideLines { get; } = [];

    /// <summary>
    /// Side-by-side view or unified view?
    /// </summary>
    /// <remarks>
    /// Both lists are produced from the <b>same</b> <see cref="FileDiff"/>, and when the mode
    /// changes only the displayed list changes; <c>git</c> is not run again.
    /// </remarks>
    [ObservableProperty]
    public partial bool ShowSideBySide { get; set; }

    partial void OnShowSideBySideChanged(bool value)
    {
        ShowSelectedFileLines();

        OnPropertyChanged(nameof(ShowUnifiedLines));
        OnPropertyChanged(nameof(ShowSideLines));

        // The focused row is reset when the mode changes; the scope of the commands changed too.
        OnPropertyChanged(nameof(CanStageSelection));
        OnPropertyChanged(nameof(CanUnstageSelection));
        OnPropertyChanged(nameof(CanDiscardSelection));
    }

    /// <summary>Is there any content to show for the selected file?</summary>
    [ObservableProperty]
    public partial bool HasLines { get; private set; }

    /// <summary>Is the unified list visible?</summary>
    /// <remarks>
    /// The two conditions (<see cref="HasLines"/> and the mode) are combined <b>here</b>, not
    /// in the UI: in Avalonia a compound conditional <c>IsVisible</c> binding can silently
    /// misbehave (measured in phase 03 — the element never showed and no test complained).
    /// </remarks>
    public bool ShowUnifiedLines => HasLines && !ShowSideBySide;

    /// <summary>Is the side-by-side list visible?</summary>
    public bool ShowSideLines => HasLines && ShowSideBySide;

    partial void OnHasLinesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUnifiedLines));
        OnPropertyChanged(nameof(ShowSideLines));
    }

    /// <summary>Why the content cannot be shown (binary, too large, mode change only…).</summary>
    [ObservableProperty]
    public partial string? ContentNotice { get; private set; }

    /// <summary>
    /// Transfers the lines of the selected file into the list.
    /// </summary>
    /// <remarks>
    /// Hunk headers and content lines flow through <b>one flat list</b>: that is how
    /// virtualisation works. A grouped structure would mean a control per row
    /// (the commit list was kept flat in phase 03 for the same reason).
    /// </remarks>
    private void ShowSelectedFileLines()
    {
        Lines.Clear();
        SideLines.Clear();
        ContentNotice = null;

        // The two lists have different indices; when the file or the mode changes the focused
        // row loses its meaning (P04-T12).
        CurrentLineIndex = -1;
        LineSearchStatus = null;

        FileDiff? diff = SelectedFile?.Diff;

        if (diff is null)
        {
            HasLines = false;
            return;
        }

        if (!diff.HasHunks)
        {
            // Diff kinds without hunks are normal (measured in P04-T02); the user must be told
            // WHY there is no content, not left with an empty area.
            ContentNotice = diff switch
            {
                { IsTooLarge: true } => Loc.T("diff.this_file_is_too_large_its_content_was_not_l"),
                { IsBinary: true } => Loc.T("diff.binary_file_the_content_cannot_be_shown"),
                { IsModeOnlyChange: true } => $"Only the file mode changed: {diff.OldMode} → {diff.NewMode}",
                { Change: FileChangeKind.Renamed } => Loc.T("diff.the_file_was_moved_the_content_did_not_chang"),
                _ => Loc.T("diff.no_changes_to_show"),
            };

            HasLines = false;
            return;
        }

        if (ShowSideBySide)
        {
            // Alignment lives in the core layer (`SideBySideDiff`) and uses the SAME mapping as
            // intra-line highlighting — had the two diverged, the user would see two
            // contradictory answers on the same screen.
            DiffTextOptions text = TextOptions;

            SideLines.AddRange(SideBySideDiff.Build(diff).Select(row =>
                row.IsHunkHeader
                    ? SideBySideLineRow.ForHunk(row.HunkHeader!, row.HunkIndex)
                    : SideBySideLineRow.ForRow(row, text)));
        }
        else
        {
            DiffTextOptions text = TextOptions;
            List<DiffLineRow> rows = [];

            for (int hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
            {
                DiffHunk hunk = diff.Hunks[hunkIndex];

                rows.Add(DiffLineRow.ForHunk(hunk, hunkIndex));

                for (int lineIndex = 0; lineIndex < hunk.Lines.Count; lineIndex++)
                {
                    rows.Add(DiffLineRow.ForLine(hunk.Lines[lineIndex], text, hunkIndex, lineIndex));
                }
            }

            Lines.AddRange(rows);
        }

        HasLines = true;
    }

    // ---- P04-T13: display settings ----

    /// <summary>
    /// Should spaces and tabs be shown with visible characters?
    /// </summary>
    /// <remarks>
    /// GitExtensions has <b>a single switch</b> too ("Show non-printing characters"): spaces
    /// and tabs are turned on together, not separately.
    /// </remarks>
    [ObservableProperty]
    public partial bool ShowWhitespace { get; set; }

    /// <summary>
    /// How many columns one tab advances.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED:</b> Avalonia draws a tab not as a tab stop but at a <b>fixed four-space</b>
    /// width, and that is not configurable; the conversion is therefore done in
    /// <see cref="DiffTextFormatter"/>.
    /// </remarks>
    [ObservableProperty]
    public partial int TabWidth { get; set; } = 4;

    /// <summary>
    /// Should long lines wrap?
    /// </summary>
    /// <remarks>
    /// <b>MEASURED:</b> variable row height does not break virtualisation (8 containers
    /// realised for 500 items) and in side-by-side view the two sides <b>stay aligned</b>
    /// thanks to the <c>Grid</c> row — the wrapping side grows the other one too.
    /// </remarks>
    [ObservableProperty]
    public partial bool WordWrap { get; set; }

    /// <summary>Font size of the diff text.</summary>
    /// <remarks>
    /// Because font size is <b>inherited</b> in Avalonia it is set once on the root element;
    /// it is not bound one by one in the row templates.
    /// </remarks>
    [ObservableProperty]
    public partial double FontSize { get; set; } = 12;

    partial void OnShowWhitespaceChanged(bool value) => ShowSelectedFileLines();

    partial void OnTabWidthChanged(int value) => ShowSelectedFileLines();

    /// <summary>Display settings used when producing rows.</summary>
    private DiffTextOptions TextOptions => new()
    {
        TabWidth = Math.Clamp(TabWidth, 1, 16),
        ShowWhitespace = ShowWhitespace,
    };

    /// <summary>Tree view or flat list?</summary>
    [ObservableProperty]
    public partial bool ShowAsTree { get; set; }

    partial void OnShowAsTreeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFlatFileList));
        OnPropertyChanged(nameof(ShowTreeFileList));
    }

    /// <summary>Filter by path; all files when empty.</summary>
    [ObservableProperty]
    public partial string? Filter { get; set; }

    partial void OnFilterChanged(string? value) => ApplyFilter();

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Message shown when the read fails.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Short description of what is shown (for the title).</summary>
    [ObservableProperty]
    public partial string? Subject { get; private set; }

    /// <summary>Are there any changes?</summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>A revision is selected but no file changed?</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    /// <summary>
    /// Shows the changes of a commit.
    /// </summary>
    /// <remarks>
    /// The previous read is cancelled: running <c>git</c> for every row while the user
    /// scrolls quickly through the list is pointless.
    /// </remarks>
    public Task ShowCommitAsync(
        string? workingDirectory,
        CommitId commit,
        string? subject = null,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // ⚠️ This section MUST stay SYNCHRONOUS. If an `await` lands between the cancellation
        // and the assignment of the new token, back-to-back calls cannot cancel each other:
        // they all see the not-yet-assigned `_loading` and pass, and EVERY ONE of them runs
        // git. A test caught this — 21 rapid selections produced 21 reads.
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;

        if (string.IsNullOrEmpty(workingDirectory) || commit.IsEmpty)
        {
            Clear();
            return Task.CompletedTask;
        }

        Subject = subject;

        _loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        return LoadAsync(workingDirectory, commit, options, _loading.Token);
    }

    /// <summary>
    /// Shows the difference between two revisions (P04-T16).
    /// </summary>
    /// <remarks>
    /// When <paramref name="toRevision"/> is <see langword="null"/> the <b>working directory</b>
    /// is compared (<c>git diff &lt;rev&gt;</c>).
    /// </remarks>
    public Task ShowRangeAsync(
        string? workingDirectory,
        string fromRevision,
        string? toRevision,
        string? subject = null,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromRevision);

        // Same rationale as ShowCommitAsync: no `await` may land between the cancellation and
        // the assignment of the new token, otherwise back-to-back calls cannot cancel each other.
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;

        if (string.IsNullOrEmpty(workingDirectory))
        {
            Clear();
            return Task.CompletedTask;
        }

        Subject = subject;

        _loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        return LoadRangeAsync(workingDirectory, fromRevision, toRevision, options, _loading.Token);
    }

    /// <summary>
    /// Shows the changes in the working tree (P05-T09).
    /// </summary>
    /// <param name="workingDirectory">Repository working directory.</param>
    /// <param name="staged">
    /// <see langword="true"/> means index ↔ <c>HEAD</c> (staged), otherwise working tree
    /// ↔ index (unstaged).
    /// </param>
    /// <param name="subject">Panel title.</param>
    /// <param name="options">Diff options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Two separate reads, because the same file can have <b>two different</b> diffs: its
    /// staged state and its unstaged state. The user must see the row of the list they clicked.
    /// </remarks>
    public Task ShowWorkingTreeAsync(
        string? workingDirectory,
        bool staged,
        string? subject = null,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Same rationale as ShowCommitAsync: no `await` may land between the cancellation and
        // the assignment of the new token, otherwise back-to-back calls cannot cancel each other.
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;

        if (string.IsNullOrEmpty(workingDirectory))
        {
            Clear();
            return Task.CompletedTask;
        }

        Subject = subject;

        _loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        CancellationToken token = _loading.Token;

        return LoadCoreAsync(
            (reader, effective, inner) => staged
                ? reader.ReadStagedAsync(workingDirectory, effective, inner)
                : reader.ReadUnstagedAsync(workingDirectory, effective, inner),
            options,
            token);
    }

    private Task LoadRangeAsync(
        string workingDirectory,
        string fromRevision,
        string? toRevision,
        DiffOptions? options,
        CancellationToken token) =>
        LoadCoreAsync(
            (reader, effective, inner) => toRevision is null
                ? reader.ReadAgainstWorkingTreeAsync(workingDirectory, fromRevision, effective, inner)
                : reader.ReadBetweenAsync(workingDirectory, fromRevision, toRevision, effective, inner),
            options,
            token);

    private Task LoadAsync(
        string workingDirectory,
        CommitId commit,
        DiffOptions? options,
        CancellationToken token) =>
        LoadCoreAsync(
            (reader, effective, inner) =>
                reader.ReadCommitAsync(workingDirectory, commit, effective, inner),
            options,
            token);

    /// <summary>
    /// Read scaffolding: delay, cancellation, error and loading state in one place.
    /// </summary>
    /// <remarks>
    /// The three entry points (commit · range · working tree) differ only in <b>which read</b>
    /// is performed; the rest is identical. Writing them separately would mean three copies of
    /// the cancellation logic — logic that already went silently wrong once in P04-T08.
    /// </remarks>
    private async Task LoadCoreAsync(
        Func<IDiffReader, DiffOptions, CancellationToken, Task<IReadOnlyList<FileDiff>>> read,
        DiffOptions? options,
        CancellationToken token)
    {
        try
        {
            await Task.Delay(_loadDelay, token).ConfigureAwait(true);

            IsLoading = true;
            ErrorMessage = null;

            // Intra-line diff is on by default: seeing EXACTLY what changed in a changed line
            // is the real benefit of reading a diff. Because it is computed locally there is
            // no extra `git` process (P04-T05).
            IReadOnlyList<FileDiff> diffs = await read(
                    _reader,
                    options ?? new DiffOptions
                    {
                        WordLevel = true,

                        // The write path uses this encoding too; if the two diverge the produced
                        // patch will not match the file's bytes and git rejects it (P05-T16).
                        ContentEncoding = ContentEncoding,
                    },
                    token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            Apply(diffs);
        }
        catch (OperationCanceledException)
        {
            // The user selected something else; not an error.
        }
        catch (Exception ex) when (ex is GitException or DiffParseException)
        {
            Clear();

            // On GitException the message is translated by Kind (P11-T06); a parse error is
            // not classified, its own message is shown.
            ErrorMessage = ex is GitException classified ? Loc.GitError(classified) : ex.Message;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    // ---- P04-T12: gezinme, arama, kopyalama ----

    /// <summary>
    /// Index of the focused row inside the diff; <c>-1</c> when there is none.
    /// </summary>
    /// <remarks>
    /// It is an index into the active list (unified or side-by-side). It is <b>reset</b> when
    /// the file or the mode changes: the two lists have different indices and the old value
    /// would point at another row.
    /// </remarks>
    [ObservableProperty]
    public partial int CurrentLineIndex { get; private set; } = -1;

    /// <summary>Text searched for inside the diff.</summary>
    [ObservableProperty]
    public partial string? LineSearchText { get; set; }

    /// <summary>Note shown to the user when the search finds nothing.</summary>
    /// <remarks>
    /// Staying silent on a search that finds nothing makes the user think the shortcut is
    /// not working (the same decision was made for the commit list in phase 03).
    /// </remarks>
    [ObservableProperty]
    public partial string? LineSearchStatus { get; private set; }

    /// <summary>Row count of the active list — unified or side-by-side per the mode.</summary>
    private int ActiveLineCount => ShowSideBySide ? SideLines.Count : Lines.Count;

    /// <summary>
    /// Is row <paramref name="index"/> a <b>change</b> row?
    /// </summary>
    /// <remarks>
    /// In side-by-side mode <b>both sides</b> of the row are examined: if the left was deleted
    /// or the right was added, that row is a change.
    /// </remarks>
    private bool IsChangeLine(int index)
    {
        if (ShowSideBySide)
        {
            SideBySideLineRow row = SideLines[index];

            return !row.IsHunkHeader
                && (IsChangeKind(row.Left) || IsChangeKind(row.Right));
        }

        DiffLineRow line = Lines[index];

        return !line.IsHunkHeader && IsChangeKind(line);
    }

    private static bool IsChangeKind(DiffLineRow row) =>
        !row.IsFiller && row.Kind is DiffLineKind.Added or DiffLineKind.Removed;

    private bool IsHunkHeaderLine(int index) =>
        ShowSideBySide ? SideLines[index].IsHunkHeader : Lines[index].IsHunkHeader;

    /// <summary>
    /// Moves to the start of the next <b>change block</b>.
    /// </summary>
    /// <remarks>
    /// Not "next hunk" but <b>"next change"</b>: GitExtensions' <c>GoToNextChange</c> works
    /// the same way. Jumping to the header inside a large hunk would be counting the same
    /// place. <b>Consecutive</b> change rows count as one block — a deleted line and the line
    /// added in its place are a single change for the user.
    /// </remarks>
    public bool GoToNextChange() => GoToChange(forward: true);

    /// <summary>Moves to the start of the previous change block.</summary>
    public bool GoToPreviousChange() => GoToChange(forward: false);

    private bool GoToChange(bool forward)
    {
        int count = ActiveLineCount;

        if (count == 0)
        {
            return false;
        }

        int start = CurrentLineIndex;

        if (forward)
        {
            // Advance to the end of the block we are in, then look for the start of the next one.
            int index = start < 0 ? 0 : start + 1;

            while (index < count && start >= 0 && IsChangeLine(index))
            {
                index++;
            }

            for (; index < count; index++)
            {
                if (IsChangeLine(index))
                {
                    CurrentLineIndex = index;
                    return true;
                }
            }

            return false;
        }

        int back = start < 0 ? count - 1 : start - 1;

        // Going backwards we must land on the START of the block; landing in its middle would
        // mean staying in the same block on another press of "previous change".
        while (back >= 0 && !IsChangeLine(back))
        {
            back--;
        }

        if (back < 0)
        {
            return false;
        }

        while (back > 0 && IsChangeLine(back - 1))
        {
            back--;
        }

        if (back == start)
        {
            return false;
        }

        CurrentLineIndex = back;
        return true;
    }

    /// <summary>Moves to the next hunk header.</summary>
    public bool GoToNextHunk() => GoToHunk(forward: true);

    /// <summary>Moves to the previous hunk header.</summary>
    public bool GoToPreviousHunk() => GoToHunk(forward: false);

    private bool GoToHunk(bool forward)
    {
        int count = ActiveLineCount;

        if (count == 0)
        {
            return false;
        }

        int step = forward ? 1 : -1;
        int index = CurrentLineIndex < 0
            ? (forward ? 0 : count - 1)
            : CurrentLineIndex + step;

        for (; index >= 0 && index < count; index += step)
        {
            if (IsHunkHeaderLine(index))
            {
                CurrentLineIndex = index;
                return true;
            }
        }

        return false;
    }

    /// <summary>Moves to the next file in the list.</summary>
    public bool GoToNextFile() => GoToFile(1);

    /// <summary>Moves to the previous file in the list.</summary>
    public bool GoToPreviousFile() => GoToFile(-1);

    private bool GoToFile(int delta)
    {
        int target = SelectedIndex + delta;

        if (target < 0 || target >= Files.Count)
        {
            return false;
        }

        SelectedIndex = target;
        return true;
    }

    /// <summary>
    /// Moves to the next match of the search term; <b>wraps to the top</b> at the end.
    /// </summary>
    /// <remarks>
    /// Wrapping is essential: if what is searched for lies above the cursor, saying "not found"
    /// would be misleading. The search runs on the <b>raw text</b> — with whitespace rendering
    /// on the text contains <c>·</c>/<c>»</c> and nobody searches a tab by its marker (P04-T13).
    /// </remarks>
    public bool FindNext() => Find(forward: true);

    /// <summary>Moves to the previous match; wraps to the end at the top.</summary>
    public bool FindPrevious() => Find(forward: false);

    private bool Find(bool forward)
    {
        string? needle = LineSearchText?.Trim();
        int count = ActiveLineCount;

        if (string.IsNullOrEmpty(needle) || count == 0)
        {
            LineSearchStatus = null;
            return false;
        }

        int step = forward ? 1 : -1;

        for (int offset = 1; offset <= count; offset++)
        {
            // `+ count` : so the modulo also works correctly in the negative direction.
            int index = (((CurrentLineIndex + (step * offset)) % count) + count) % count;

            if (LineMatches(index, needle))
            {
                CurrentLineIndex = index;
                LineSearchStatus = null;
                return true;
            }
        }

        LineSearchStatus = Loc.T("diff.not_found");
        return false;
    }

    private bool LineMatches(int index, string needle)
    {
        if (!ShowSideBySide)
        {
            return Contains(Lines[index].RawText, needle);
        }

        SideBySideLineRow row = SideLines[index];

        return Contains(row.Left.RawText, needle) || Contains(row.Right.RawText, needle);
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts the visible diff into the text to place on the clipboard.
    /// </summary>
    /// <param name="mode">Copy form; code without prefixes by default.</param>
    /// <param name="selection">
    /// Indices of the rows to copy; <see langword="null"/> or empty means <b>all of them</b>.
    /// </param>
    /// <remarks>
    /// ⚠️ The text is produced from <see cref="DiffLineRow.RawText"/>: copying the text with
    /// the display transformation applied would put <b>broken code</b> on the clipboard (tabs
    /// expanded, spaces turned into <c>·</c>).
    /// </remarks>
    public string CopyText(DiffCopyMode mode = DiffCopyMode.Code, IReadOnlyList<int>? selection = null)
    {
        int count = ActiveLineCount;

        if (count == 0)
        {
            return string.Empty;
        }

        HashSet<int>? wanted = selection is { Count: > 0 } ? [.. selection] : null;
        StringBuilder builder = new();

        for (int index = 0; index < count; index++)
        {
            if (wanted is not null && !wanted.Contains(index))
            {
                continue;
            }

            AppendCopyLine(builder, index, mode);
        }

        return builder.ToString().TrimEnd('\n');
    }

    private void AppendCopyLine(StringBuilder builder, int index, DiffCopyMode mode)
    {
        if (ShowSideBySide)
        {
            SideBySideLineRow row = SideLines[index];

            if (row.IsHunkHeader)
            {
                if (mode == DiffCopyMode.Patch)
                {
                    builder.Append(row.Header).Append('\n');
                }

                return;
            }

            // ⚠️ A context line sits on BOTH sides in side-by-side mode; written to the patch
            // twice, the produced patch would be invalid.
            if (IsChangeKind(row.Left))
            {
                Append(builder, row.Left, mode);
            }

            if (IsChangeKind(row.Right))
            {
                Append(builder, row.Right, mode);
            }

            if (!IsChangeKind(row.Left) && !IsChangeKind(row.Right) && !row.Left.IsFiller)
            {
                Append(builder, row.Left, mode);
            }

            return;
        }

        DiffLineRow line = Lines[index];

        if (line.IsHunkHeader)
        {
            if (mode == DiffCopyMode.Patch)
            {
                builder.Append(line.Text).Append('\n');
            }

            return;
        }

        Append(builder, line, mode);
    }

    private static void Append(StringBuilder builder, DiffLineRow line, DiffCopyMode mode)
    {
        if (line.IsFiller)
        {
            return;
        }

        // "Old state" and "new state" skip the opposite side: the user wants that version of
        // the file, not the diff.
        if (mode == DiffCopyMode.OldVersion && line.Kind == DiffLineKind.Added)
        {
            return;
        }

        if (mode == DiffCopyMode.NewVersion && line.Kind == DiffLineKind.Removed)
        {
            return;
        }

        if (mode == DiffCopyMode.Patch)
        {
            builder.Append(line.Kind switch
            {
                DiffLineKind.Added => '+',
                DiffLineKind.Removed => '-',
                _ => ' ',
            });
        }

        builder.Append(line.RawText).Append('\n');
    }

    /// <summary>
    /// Should the component show its own file list? (P05-T09)
    /// </summary>
    /// <remarks>
    /// In the working directory view the file list sits <b>on the left</b>, split in two
    /// as staged/unstaged. Showing the component's own list as well would list the same
    /// files in two separate places and leave the user asking "which one is the selection
    /// from?".
    /// </remarks>
    [ObservableProperty]
    public partial bool ShowFileList { get; set; } = true;

    /// <summary>Should the flat file list be visible?</summary>
    /// <remarks>
    /// The compound <c>IsVisible</c> condition lives <b>here</b>, not in XAML: in phase 03 a
    /// compound binding was measured to misbehave silently (same decision in P04-T10).
    /// </remarks>
    public bool ShowFlatFileList => ShowFileList && !ShowAsTree;

    /// <summary>Should the tree view be visible?</summary>
    public bool ShowTreeFileList => ShowFileList && ShowAsTree;

    partial void OnShowFileListChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFlatFileList));
        OnPropertyChanged(nameof(ShowTreeFileList));
    }

    /// <summary>
    /// Selects the file at the given path; the selection stays put if it is not in the list.
    /// </summary>
    /// <remarks>
    /// Used when an outside list is the selection source (P05-T09). If the path is not found
    /// it returns <see langword="false"/> — silently showing another file would present the
    /// user with content they did not select as if it were correct.
    /// </remarks>
    public bool SelectPath(RepositoryPath path)
    {
        for (int index = 0; index < Files.Count; index++)
        {
            if (Files[index].Path == path)
            {
                SelectedIndex = index;
                return true;
            }
        }

        return false;
    }

    // ---- P05-T10: partial staging ----

    /// <summary>
    /// The party that performs partial staging; the actions are disabled without one.
    /// </summary>
    /// <remarks>
    /// The component stays <b>independent</b> (the P04-T08 decision): staging is meaningless
    /// in the commit history and in the comparison window, so there this field is
    /// <see langword="null"/> and the menu items appear disabled.
    /// </remarks>
    public IPartialStagingHost? StagingHost
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(CanStageSelection));
            OnPropertyChanged(nameof(CanUnstageSelection));
        }
    }

    /// <summary>
    /// Is "stage selected lines" available?
    /// </summary>
    /// <remarks>
    /// In GitExtensions the two commands are <b>mutually exclusive</b> too:
    /// <c>stageSelectedLines</c> only appears on the working tree side, <c>unstageSelectedLines</c>
    /// only on the index side (<c>FileViewer.cs</c>). Offering both at once would show the
    /// user an action that is meaningless on the side they are on.
    /// <para>
    /// Valid in side-by-side mode too (P05-T11): because the rows carry the hunk and line
    /// index, the selection can be converted exactly there as well.
    /// </para>
    /// </remarks>
    public bool CanStageSelection => StagingHost?.CanStage == true;

    /// <summary>Is "revert selected lines" available?</summary>
    public bool CanUnstageSelection => StagingHost?.CanUnstage == true;

    /// <summary>
    /// Encoding the diff content is read with and the patch is written with (P05-T16).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Reading and writing MUST use the SAME encoding.</b> Measured in P05-T16 on a real
    /// repository: for a file with Latin-5 content, when the diff is read with the UTF-8
    /// default and the patch is written as UTF-8, <c>git apply</c> <b>rejects</b> the patch
    /// (<c>patch does not apply</c>) — because UTF-8 decoding does not bring the raw bytes back.
    /// Passing the encoding end to end makes it succeed with the correct bytes in the index.
    /// </para>
    /// <para>
    /// Wrong content is <b>not</b> written <b>silently</b>: git rejects the patch. This is the
    /// counterpart of the P05-T04 decision "do not use <c>--recount</c>" — because git's
    /// validation is left on, the error becomes visible.
    /// </para>
    /// <para>
    /// The default (<see langword="null"/>) is UTF-8. Letting the user pick an encoding depends
    /// on the settings infrastructure (<b>P08-T14</b>); the plumbing here is ready.
    /// </para>
    /// </remarks>
    public System.Text.Encoding? ContentEncoding { get; set; }

    /// <summary>
    /// Can the selected lines be discarded from the working tree (P05-T15)?
    /// </summary>
    /// <remarks>
    /// Only meaningful on the working tree side: on the index side "reset" would already mean
    /// <i>unstage</i> and the two commands would do the same thing.
    /// </remarks>
    public bool CanDiscardSelection => StagingHost?.CanStage == true;

    /// <summary>
    /// Builds a patch selection from the selected rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>If a hunk header is selected, the whole hunk is selected.</b> There is no separate
    /// "stage this hunk" command — GitExtensions has none either; there too there is a single
    /// command whose scope follows the selection. The header row is the natural way to say
    /// "this hunk".
    /// </para>
    /// <para>
    /// If no row is selected the <b>focused row</b> is used; without that the selection comes
    /// back empty and the caller does nothing — "stage with nothing selected" would silently
    /// stage the whole file.
    /// </para>
    /// </remarks>
    public PatchSelection? BuildSelection(IReadOnlyList<int>? selectedRowIndices)
    {
        if (SelectedFile?.Diff is not { } diff)
        {
            return null;
        }

        IReadOnlyList<int> indices = selectedRowIndices is { Count: > 0 }
            ? selectedRowIndices
            : CurrentLineIndex >= 0 ? [CurrentLineIndex] : [];

        if (indices.Count == 0)
        {
            return null;
        }

        HashSet<(int Hunk, int Line)> lines = [];

        // The bound follows the ACTIVE list: `Lines` is empty in side-by-side, `SideLines` in unified.
        int count = ShowSideBySide ? SideLines.Count : Lines.Count;

        foreach (int index in indices)
        {
            if (index < 0 || index >= count)
            {
                continue;
            }

            if (ShowSideBySide)
            {
                SideBySideLineRow side = SideLines[index];

                if (side.IsHunkHeader)
                {
                    AddWholeHunk(diff, side.HunkIndex, lines);
                    continue;
                }

                // ⚠️ One side-by-side row can carry TWO different unified lines (the deleted one
                // on the left, the one added in its place on the right). Both must enter the
                // selection — taking only one would stage half of the pair the user sees.
                Add(side.Left, lines);
                Add(side.Right, lines);
                continue;
            }

            DiffLineRow row = Lines[index];

            if (row.IsHunkHeader)
            {
                AddWholeHunk(diff, row.HunkIndex, lines);
                continue;
            }

            Add(row, lines);
        }

        return lines.Count == 0 ? null : PatchSelection.Lines(lines);
    }

    /// <summary>
    /// Adds the row to the selection if it is a change row.
    /// </summary>
    /// <remarks>
    /// Context lines enter the patch by themselves; counting them as "selected" would also take
    /// a change the user did not select. A filler row has no counterpart at all.
    /// </remarks>
    private static void Add(DiffLineRow row, HashSet<(int Hunk, int Line)> lines)
    {
        if (row.IsHunkHeader || row.IsFiller)
        {
            return;
        }

        if (row.Kind is DiffLineKind.Added or DiffLineKind.Removed
            && row.HunkIndex >= 0
            && row.LineIndex >= 0)
        {
            lines.Add((row.HunkIndex, row.LineIndex));
        }
    }

    private static void AddWholeHunk(
        FileDiff diff,
        int hunkIndex,
        HashSet<(int Hunk, int Line)> lines)
    {
        if (hunkIndex < 0 || hunkIndex >= diff.Hunks.Count)
        {
            return;
        }

        DiffHunk hunk = diff.Hunks[hunkIndex];

        for (int line = 0; line < hunk.Lines.Count; line++)
        {
            if (hunk.Lines[line].Kind != DiffLineKind.Context)
            {
                lines.Add((hunkIndex, line));
            }
        }
    }

    /// <summary>
    /// Announces that the availability of the partial staging commands has changed.
    /// </summary>
    /// <remarks>
    /// Availability comes from the state of <see cref="StagingHost"/> and that state changes
    /// outside (the user switched to the other list); the component cannot see it by itself.
    /// </remarks>
    public void NotifyStagingAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanStageSelection));
        OnPropertyChanged(nameof(CanUnstageSelection));
        OnPropertyChanged(nameof(CanDiscardSelection));
    }

    /// <summary>Stages the selected lines.</summary>
    public Task StageSelectionAsync(IReadOnlyList<int>? selectedRowIndices = null) =>
        ApplySelectionAsync(selectedRowIndices, stage: true);

    /// <summary>Takes the selected lines back out of the index.</summary>
    public Task UnstageSelectionAsync(IReadOnlyList<int>? selectedRowIndices = null) =>
        ApplySelectionAsync(selectedRowIndices, stage: false);

    /// <summary>
    /// Discards the selected lines' changes from the working tree (P05-T15) — <b>destructive</b>.
    /// </summary>
    public async Task DiscardSelectionAsync(IReadOnlyList<int>? selectedRowIndices = null)
    {
        if (StagingHost is not { } host
            || SelectedFile?.Diff is not { } diff
            || BuildSelection(selectedRowIndices) is not { } selection)
        {
            return;
        }

        try
        {
            ErrorMessage = null;

            await host.DiscardAsync(diff, selection).ConfigureAwait(true);
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
        }
    }

    private async Task ApplySelectionAsync(IReadOnlyList<int>? selectedRowIndices, bool stage)
    {
        if (StagingHost is not { } host
            || SelectedFile?.Diff is not { } diff
            || BuildSelection(selectedRowIndices) is not { } selection)
        {
            return;
        }

        try
        {
            ErrorMessage = null;

            await host.ApplyAsync(diff, selection, stage).ConfigureAwait(true);
        }
        catch (GitException ex)
        {
            // `git apply` REJECTS count/context errors (measured in P05-T04); the message must
            // reach the user, otherwise we get an "I clicked but nothing happened" situation.
            ErrorMessage = Loc.GitError(ex);
        }
    }

    /// <summary>Clears the view — also called from outside when the repository closes.</summary>
    public void Clear()
    {
        _allFiles = [];
        Files.Clear();
        Tree.Clear();
        Lines.Clear();
        SideLines.Clear();

        SelectedIndex = -1;
        CurrentLineIndex = -1;
        LineSearchStatus = null;
        ContentNotice = null;
        Subject = null;
        HasLines = false;
        IsEmpty = false;

        OnPropertyChanged(nameof(HasFiles));
    }

    private void Apply(IReadOnlyList<FileDiff> diffs)
    {
        _allFiles = [.. diffs.Select(d => new FileChangeRow(d))];

        ApplyFilter();

        // No changes is not an error here: an empty commit, or only ignored differences
        // (measured in P04-T04: `--ignore-blank-lines` keeps the file in the list but
        // `-w` drops a file that became identical).
        IsEmpty = _allFiles.Count == 0;
    }

    private void ApplyFilter()
    {
        Files.Clear();

        string? filter = Filter?.Trim();

        foreach (FileChangeRow row in _allFiles)
        {
            if (filter is { Length: > 0 }
                && !row.Path.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Files.Add(row);
        }

        RebuildTree();

        // If the filter eliminated the selected file the selection drops; going back to the
        // first row is better than taking the user to a file they did not expect.
        SelectedIndex = Files.Count > 0 ? 0 : -1;

        OnPropertyChanged(nameof(HasFiles));
    }

    /// <summary>
    /// Builds a folder tree from the files that pass the filter.
    /// </summary>
    /// <remarks>
    /// The tree is rebuilt <b>on every filter change</b>. The number of files a commit changes
    /// is in the hundreds; incremental updating costs more complexity here than it gains.
    /// </remarks>
    private void RebuildTree()
    {
        Tree.Clear();

        Dictionary<string, FileTreeNode> folders = [];

        foreach (FileChangeRow row in Files)
        {
            FileTreeNode? parent = EnsureFolder(row.Directory, folders);

            FileTreeNode leaf = FileTreeNode.ForFile(row);

            if (parent is null)
            {
                Tree.Add(leaf);
            }
            else
            {
                parent.Children.Add(leaf);
            }
        }
    }

    /// <summary>Creates the folder node (and its parents if needed).</summary>
    private FileTreeNode? EnsureFolder(string directory, Dictionary<string, FileTreeNode> folders)
    {
        if (directory.Length == 0)
        {
            return null;
        }

        if (folders.TryGetValue(directory, out FileTreeNode? existing))
        {
            return existing;
        }

        int separator = directory.LastIndexOf('/');
        string name = separator < 0 ? directory : directory[(separator + 1)..];
        string parentPath = separator < 0 ? string.Empty : directory[..separator];

        FileTreeNode node = FileTreeNode.ForFolder(name);
        folders[directory] = node;

        FileTreeNode? parent = EnsureFolder(parentPath, folders);

        if (parent is null)
        {
            Tree.Add(node);
        }
        else
        {
            parent.Children.Add(node);
        }

        return node;
    }

}

/// <summary>
/// A node in the tree view: folder or file (P04-T08).
/// </summary>
public sealed class FileTreeNode
{
    private FileTreeNode(string name, FileChangeRow? file)
    {
        Name = name;
        File = file;
    }

    public static FileTreeNode ForFolder(string name) => new(name, null);

    public static FileTreeNode ForFile(FileChangeRow file) => new(file.Name, file);

    public string Name { get; }

    /// <summary>File on a leaf node; <see langword="null"/> on a folder.</summary>
    public FileChangeRow? File { get; }

    public bool IsFolder => File is null;

    public ObservableCollection<FileTreeNode> Children { get; } = [];

    public override string ToString() => IsFolder ? Name + "/" : Name;
}
