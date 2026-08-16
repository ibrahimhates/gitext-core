using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A single row in the working directory lists (P05-T09).
/// </summary>
public sealed class WorkingTreeFileRow
{
    public WorkingTreeFileRow(FileStatus status, bool staged)
    {
        ArgumentNullException.ThrowIfNull(status);

        Status = status;
        IsStagedSide = staged;
    }

    public FileStatus Status { get; }

    /// <summary>Does the row belong to the <b>staged</b> list?</summary>
    /// <remarks>
    /// The same file can be in both lists at once: part of it staged, the rest not.
    /// This determines which side is shown when reading the diff.
    /// </remarks>
    public bool IsStagedSide { get; }

    public RepositoryPath Path => Status.Path;

    public string Name => Status.Path.Name;

    public string Directory => Status.Path.Parent.Value;

    /// <summary>
    /// git's own status letter.
    /// </summary>
    /// <remarks>
    /// The letters are the same as in <c>git status</c>: the user already knows them, so
    /// teaching a new alphabet is unnecessary (same decision was made for the changed files
    /// list in P04-T08).
    /// </remarks>
    public string StatusLetter => Status switch
    {
        { IsConflicted: true } => "U",
        { IsUntracked: true } => "?",
        _ => Kind switch
        {
            FileChangeKind.Added => "A",
            FileChangeKind.Deleted => "D",
            FileChangeKind.Renamed => "R",
            FileChangeKind.Copied => "C",
            FileChangeKind.TypeChanged => "T",
            _ => "M",
        },
    };

    private FileChangeKind Kind =>
        IsStagedSide ? Status.StagedChange : Status.UnstagedChange;

    public bool IsUntracked => Status.IsUntracked;

    public bool IsConflicted => Status.IsConflicted;

    public bool IsDeleted => Kind == FileChangeKind.Deleted;

    public bool IsAdded => Kind == FileChangeKind.Added || Status.IsUntracked;

    public override string ToString() => $"{StatusLetter} {Path}";
}

/// <summary>
/// Working directory view: unstaged and staged files (P05-T09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout taken from GitExtensions' <c>FormCommit</c></b> (CLAUDE.md § 9): <i>Unstaged</i>
/// top-left, <i>Staged</i> below it, with stage/unstage buttons on the toolbar between them;
/// the selected file's diff on the right.
/// </para>
/// <para>
/// 🔴 <b>Deviation from the plan:</b> the plan said "staged / unstaged / <b>untracked</b>
/// sections". GitExtensions has <b>no separate untracked section</b> — untracked files sit
/// in the <i>Unstaged</i> list (with a hide option, <c>tsmiShowUntrackedFiles</c>). A third
/// list would establish a distinction between "my changes" and "my new files" that the user
/// never made, and would require looking in two separate places to stage. Layout won per the
/// § 9 rule.
/// </para>
/// </remarks>
public sealed partial class WorkingTreeViewModel : ViewModelBase, IPartialStagingHost, IDisposable
{
    private readonly IStatusReader _statusReader;
    private readonly IStagingWriter _staging;
    private readonly ICommitWriter _commitWriter;
    private readonly IRepositoryWatcher? _watcher;
    private readonly IWorkingTreeWriter? _workingTreeWriter;

    /// <summary>
    /// Did the user say "don't ask again"?
    /// </summary>
    /// <remarks>
    /// 🔑 <b>Deliberately IN-SESSION</b> — not written to disk. If it were persistent, the
    /// user could lose data months later without any warning, having long forgotten they
    /// checked that box. The actual complaint — "I'm resetting ten files in a row and it
    /// asks every time" — is already solved by in-session suppression.
    /// </remarks>
    private bool _suppressResetPrompt;

    /// <summary>
    /// Backups of the last reset; "undo" uses these.
    /// </summary>
    private readonly List<DiscardBackup> _lastResetBackups = [];

    private CancellationTokenSource? _refreshing;

    /// <summary>
    /// The selection changes <b>programmatically</b> during refresh; the active list must not change.
    /// </summary>
    /// <remarks>
    /// 🔴 A test caught this: when a staged file left the list, the selection shifted, which
    /// also changed the other list's index, and the active list <b>silently</b> jumped to the
    /// other side — while the user was working in the unstaged list, the diff suddenly started
    /// showing the staged side. Only the <b>user's</b> selection may change the active list.
    /// </remarks>
    private bool _refreshingSelection;

    public WorkingTreeViewModel(
        IStatusReader statusReader,
        IStagingWriter staging,
        ICommitWriter commitWriter,
        DiffViewModel diff,
        ICommitMessageReader? messageReader = null,
        ICommitMessageStore? messageStore = null,
        IRepositoryWatcher? watcher = null,
        IWorkingTreeWriter? workingTreeWriter = null)
    {
        ArgumentNullException.ThrowIfNull(statusReader);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(commitWriter);
        ArgumentNullException.ThrowIfNull(diff);

        _statusReader = statusReader;
        _staging = staging;
        _commitWriter = commitWriter;
        _watcher = watcher;
        _workingTreeWriter = workingTreeWriter;

        if (_watcher is not null)
        {
            _watcher.Changed += OnRepositoryChanged;
        }

        Message = new CommitMessageViewModel(messageReader, messageStore);

        Diff = diff;

        // The file list is on the LEFT in this view; the diff component's own list is hidden.
        // Showing the same list twice would make the user wonder which one is the selection
        // source.
        Diff.ShowFileList = false;

        // Partial staging only makes sense here; the diff component receives it from outside.
        Diff.StagingHost = this;

        // The commit button's state changes as the message becomes empty or non-empty.
        Message.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanCommit));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The two commands are mutually exclusive — same as in GitExtensions: <c>stageSelectedLines</c>
    /// only appears on the working tree side, <c>unstageSelectedLines</c> only on the index side.
    /// </remarks>
    bool IPartialStagingHost.CanStage => SelectedRow is { IsStagedSide: false };

    /// <inheritdoc />
    bool IPartialStagingHost.CanUnstage => SelectedRow is { IsStagedSide: true };

    /// <inheritdoc />
    async Task IPartialStagingHost.ApplyAsync(FileDiff diff, PatchSelection selection, bool stage)
    {
        if (WorkingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        // The encoding must be the one the diff was read with: the patch is compared by git
        // against the bytes in the working tree (two rounds of mistakes were made in P05-T04).
        if (stage)
        {
            await _staging
                .StagePartialAsync(directory, diff, selection, Diff.ContentEncoding)
                .ConfigureAwait(true);
        }
        else
        {
            await _staging
                .UnstagePartialAsync(directory, diff, selection, Diff.ContentEncoding)
                .ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Should hook verification be skipped for the commit (P05-T15)?
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>MEASURED (P05-T07):</b> this flag does NOT mean "skip hooks" — only
    /// <c>pre-commit</c> and <c>commit-msg</c> are skipped; <c>prepare-commit-msg</c> and
    /// <c>post-commit</c> still run, so the message can still change.
    /// <para>
    /// There is <b>no</b> confirmation dialog: a skipped hook doesn't lose data, the resulting
    /// commit stays in the reflog and can be undone (measured in P05-T15). The dialog exists
    /// only for operations that cannot be recovered.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    public partial bool SkipHooks { get; set; }

    /// <summary>
    /// The party that asks for confirmation on destructive operations (P05-T15).
    /// </summary>
    /// <remarks>
    /// A <b>property</b> instead of a ctor parameter: the confirmation dialog needs an owner
    /// window, but the ViewModel is constructed before the window. The same pattern was used
    /// in <see cref="DiffViewModel.StagingHost"/> (P05-T10).
    /// <para>
    /// If unset, the reset commands <b>do nothing</b>: running a destructive operation without
    /// confirmation is worse than not running it at all.
    /// </para>
    /// </remarks>
    public IDestructiveActionConfirmer? Confirmer { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// A <b>destructive</b> request coming from the diff panel: it goes through the same
    /// safety net as the reset in the file list — confirmation, backup, undo strip. Only the
    /// scope differs.
    /// </remarks>
    async Task IPartialStagingHost.DiscardAsync(FileDiff diff, PatchSelection selection)
    {
        if (WorkingDirectory is not { Length: > 0 } directory
            || _workingTreeWriter is null
            || Confirmer is null)
        {
            return;
        }

        ResetChangesDecision? decision = await RequestResetAsync(new ResetChangesRequest
        {
            ModifiedPaths = [diff.Path],
            UntrackedPaths = [],

            // The patch is applied only to the working tree; the index is untouched (measured).
            IncludesStaged = false,
            CanSuppress = true,
        }).ConfigureAwait(true);

        if (decision is null)
        {
            return;
        }

        IReadOnlyList<DiscardBackup> backups = await _workingTreeWriter
            .DiscardPartialAsync(
                directory,
                diff,
                selection,
                userConfirmed: true,
                Diff.ContentEncoding)
            .ConfigureAwait(true);

        RecordReset(
            backups,
            backups.Count == 0
                ? Loc.T("working_tree.the_selected_lines_were_reverted")
                : $"The selected lines were reverted. {diff.Path.Name} was backed up.");

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>The selected file's diff — the component in the right panel.</summary>
    public DiffViewModel Diff { get; }

    /// <summary>Unstaged changes; <b>includes untracked files</b>.</summary>
    public AvaloniaList<WorkingTreeFileRow> Unstaged { get; } = [];

    /// <summary>Staged changes.</summary>
    public AvaloniaList<WorkingTreeFileRow> Staged { get; } = [];

    /// <summary>The open repository's working directory; <see langword="null"/> if none is open.</summary>
    [ObservableProperty]
    public partial string? WorkingDirectory { get; private set; }

    [ObservableProperty]
    public partial int SelectedUnstagedIndex { get; set; } = -1;

    [ObservableProperty]
    public partial int SelectedStagedIndex { get; set; } = -1;

    /// <summary>
    /// Is the focused list the <b>staged</b> one?
    /// </summary>
    /// <remarks>
    /// Both lists can have a selection at the same time; the diff needs to know which one to
    /// show. The list the user last touched wins.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsStagedListActive { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanCommit));

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>The error's full git output (P05-T07).</summary>
    [ObservableProperty]
    public partial GitOutputViewModel? ErrorDetails { get; private set; }

    /// <summary>Is there anything to commit?</summary>
    public bool HasStagedChanges => Staged.Count > 0;

    /// <summary>Are there no changes at all?</summary>
    [ObservableProperty]
    public partial bool IsClean { get; private set; }

    /// <summary>Commit message box (P05-T12) and its helpers (P05-T13).</summary>
    public CommitMessageViewModel Message { get; }

    /// <summary>Overwrite the last commit (<c>--amend</c>).</summary>
    /// <remarks>
    /// ⚠️ Rewrites history on a published commit; a warning will come in P05-T15.
    /// </remarks>
    [ObservableProperty]
    public partial bool Amend { get; set; }

    /// <summary>
    /// Can a commit be created?
    /// </summary>
    /// <remarks>
    /// The compound condition lives <b>here</b>, not in XAML (P04-T10 decision). Leaving the
    /// commit button enabled while the message is empty would offer an operation git would
    /// reject — an empty message exits with 1 (measured in P05-T06).
    /// </remarks>
    public bool CanCommit => !IsBusy && !Message.IsEmpty && (HasStagedChanges || Amend);

    /// <summary>Output to show after commit; <see langword="null"/> if none (P05-T07).</summary>
    [ObservableProperty]
    public partial GitOutputViewModel? CommitOutput { get; set; }

    partial void OnAmendChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCommit));

        // When amend is checked, the message to fix needs to be visible: HEAD's message is
        // loaded when the box is empty (same condition as in GitExtensions). A non-empty box
        // is left untouched — if the user already started typing a new message, checking
        // amend must not erase it.
        if (value)
        {
            _ = Message.LoadHeadMessageAsync();
        }
    }

    /// <summary>
    /// Creates a commit from the staged changes.
    /// </summary>
    /// <remarks>
    /// On success the message is <b>cleared</b>: committing a second time with the same text
    /// is almost always an accident. If there's hook output or a message change,
    /// <see cref="CommitOutput"/> is populated (P05-T07).
    /// </remarks>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (WorkingDirectory is not { Length: > 0 } directory || !CanCommit)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ErrorDetails = null;
            CommitOutput = null;

            CommitResult result = await _commitWriter
                .CommitAsync(directory, Message.Text, new CommitOptions { Amend = Amend, SkipHooks = SkipHooks }, cancellationToken)
                .ConfigureAwait(true);

            // The box and the DRAFT are cleared together: if the committed text stayed on
            // disk, it would come back the next time the screen opens and invite a second
            // commit (P05-T13).
            await Message.OnCommittedAsync(cancellationToken).ConfigureAwait(true);
            Amend = false;

            // If there's something to show: the hook spoke, or the message changed.
            CommitOutput = result.NeedsReporting ? GitOutputViewModel.ForCommit(result) : null;
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
            ErrorDetails = GitOutputViewModel.ForFailure(ex);
            IsBusy = false;
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>The selected row in the active list.</summary>
    public WorkingTreeFileRow? SelectedRow =>
        IsStagedListActive
            ? Get(Staged, SelectedStagedIndex)
            : Get(Unstaged, SelectedUnstagedIndex);

    private static WorkingTreeFileRow? Get(AvaloniaList<WorkingTreeFileRow> rows, int index) =>
        index >= 0 && index < rows.Count ? rows[index] : null;

    partial void OnSelectedUnstagedIndexChanged(int value)
    {
        if (value >= 0 && !_refreshingSelection)
        {
            IsStagedListActive = false;
        }

        OnSelectionChanged();
    }

    partial void OnSelectedStagedIndexChanged(int value)
    {
        if (value >= 0 && !_refreshingSelection)
        {
            IsStagedListActive = true;
        }

        OnSelectionChanged();
    }

    partial void OnIsStagedListActiveChanged(bool value) => OnSelectionChanged();

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedRow));

        // If the selection changed sides, which partial staging command is available also changed.
        Diff.NotifyStagingAvailabilityChanged();

        _ = ShowSelectedDiffAsync();
    }

    /// <summary>
    /// Loads the selected file's diff into the right panel.
    /// </summary>
    /// <remarks>
    /// The diff is read for <b>the whole</b> side in one call, and the selection only
    /// determines which file to show. Running a separate <c>git</c> per file would mean one
    /// process per row while the user arrows through the list (the same mistake was measured
    /// in P04-T08).
    /// </remarks>
    private async Task ShowSelectedDiffAsync()
    {
        WorkingTreeFileRow? row = SelectedRow;

        if (WorkingDirectory is not { Length: > 0 } directory || row is null)
        {
            Diff.Clear();
            return;
        }

        await Diff.ShowWorkingTreeAsync(directory, row.IsStagedSide, row.Path.Value)
            .ConfigureAwait(true);

        Diff.SelectPath(row.Path);
    }

    /// <summary>
    /// Attaches the repository and reads the status.
    /// </summary>
    public async Task OpenAsync(string? workingDirectory, CancellationToken cancellationToken = default)
    {
        WorkingDirectory = workingDirectory;

        // The draft, git-prepared message (merge/cherry-pick), and template status are loaded
        // here; none of them overwrite a non-empty box (P05-T13).
        await Message.OpenAsync(workingDirectory, cancellationToken).ConfigureAwait(true);

        if (string.IsNullOrEmpty(workingDirectory))
        {
            Unstaged.Clear();
            Staged.Clear();
            Diff.Clear();
            IsClean = true;
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs when the watcher reports a change (P05-T14).
    /// </summary>
    /// <remarks>
    /// The event arrives on a <b>timer thread</b>; collections may only be touched from the
    /// UI thread.
    /// </remarks>
    private void OnRepositoryChanged(object? sender, RepositoryChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => _ = AutoRefreshAsync());

    /// <summary>
    /// Refreshes automatically after an external change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Does <b>not touch</b> the typed commit message: refreshing only updates the file lists
    /// and the diff. A background event erasing the user's typed text would be unacceptable
    /// (invariant of P05-T13).
    /// </para>
    /// <para>
    /// <b>Suspension is required:</b> measured that <c>git status</c> rewrites the index the
    /// first time it runs in a repository — meaning the refresh itself can trigger another
    /// refresh event. The suspension breaks that chain.
    /// </para>
    /// </remarks>
    private async Task AutoRefreshAsync()
    {
        if (IsBusy || WorkingDirectory is not { Length: > 0 })
        {
            return;
        }

        using IDisposable? suspension = _watcher?.Suspend();

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Re-reads the working directory status.
    /// </summary>
    /// <remarks>
    /// The selection is <b>preserved where possible</b>: jumping to the top of the list after
    /// the user stages a file would mean manually going to the next file every time. If the
    /// file left the list, the selection stays <b>at the same position</b> (i.e. shifts to the
    /// next file).
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (WorkingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        _refreshing?.Cancel();
        _refreshing?.Dispose();
        _refreshing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        CancellationToken token = _refreshing.Token;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ErrorDetails = null;

            WorkingTreeStatus status = await _statusReader
                .ReadAsync(directory, includeIgnored: false, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            Replace(Unstaged, [.. status.Unstaged.Concat(status.Untracked)
                .OrderBy(entry => entry.Path)
                .Select(entry => new WorkingTreeFileRow(entry, staged: false))]);

            Replace(Staged, [.. status.Staged
                .OrderBy(entry => entry.Path)
                .Select(entry => new WorkingTreeFileRow(entry, staged: true))]);

            _refreshingSelection = true;

            try
            {
                SelectedUnstagedIndex = Clamp(SelectedUnstagedIndex, Unstaged.Count);
                SelectedStagedIndex = Clamp(SelectedStagedIndex, Staged.Count);
            }
            finally
            {
                _refreshingSelection = false;
            }

            // ⚠️ Even if the active list becomes empty, it does NOT jump to the OTHER list.
            // Jumping seems tempting ("leave something to show") but is dangerous: after the
            // user stages their last file, the active list would become staged, and the
            // `Space` key would then UNDO the file they just staged. An empty list stays empty.

            IsClean = Unstaged.Count == 0 && Staged.Count == 0;

            OnPropertyChanged(nameof(HasStagedChanges));
            OnPropertyChanged(nameof(CanCommit));

            // Even if the selection stays at the same index, the file BEHIND it may have
            // changed; if the diff isn't refreshed the user looks at another file's content.
            await ShowSelectedDiffAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
            ErrorDetails = GitOutputViewModel.ForFailure(ex);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    private static void Replace(
        AvaloniaList<WorkingTreeFileRow> target,
        IReadOnlyList<WorkingTreeFileRow> rows)
    {
        target.Clear();
        target.AddRange(rows);
    }

    /// <summary>
    /// Keeps the selection within the list bounds.
    /// </summary>
    /// <remarks>
    /// When a file is staged it leaves the list; <b>preserving</b> the index automatically
    /// moves the selection to the next file. If it was the last row, it's pulled up by one.
    /// </remarks>
    private static int Clamp(int index, int count)
    {
        if (count == 0)
        {
            return -1;
        }

        return index < 0 ? 0 : Math.Min(index, count - 1);
    }

    /// <summary>
    /// Resets changes — <b>destructive</b> (P05-T15).
    /// </summary>
    /// <param name="scope">
    /// <see cref="DiscardScope.UnstagedOnly"/> discards only the working tree,
    /// <see cref="DiscardScope.All"/> also discards staged content.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// The GitExtensions equivalent is the *Reset all changes* / *Reset unstaged changes*
    /// buttons in <c>FormCommit</c>; those also ask via <c>FormResetChanges</c> and offer the
    /// "delete new files too" option in the same dialog (§ 9).
    /// </para>
    /// <para>
    /// 🔑 <b>Confirmation can be skipped, the safety net cannot.</b> Even if the user says
    /// "don't ask again", content is always backed up and undo is offered after the operation.
    /// </para>
    /// </remarks>
    public async Task ResetChangesAsync(
        DiscardScope scope,
        CancellationToken cancellationToken = default)
    {
        if (WorkingDirectory is not { Length: > 0 } directory
            || _workingTreeWriter is null
            || Confirmer is null
            || IsBusy)
        {
            return;
        }

        List<RepositoryPath> modified = [];
        List<RepositoryPath> untracked = [];

        foreach (WorkingTreeFileRow row in Unstaged)
        {
            (row.IsUntracked ? untracked : modified).Add(row.Path);
        }

        if (scope == DiscardScope.All)
        {
            // Staged files will also revert to HEAD; the user must be told the count.
            foreach (WorkingTreeFileRow row in Staged)
            {
                if (!modified.Contains(row.Path))
                {
                    modified.Add(row.Path);
                }
            }
        }

        if (modified.Count == 0 && untracked.Count == 0)
        {
            return;
        }

        ResetChangesDecision? decision = await RequestResetAsync(new ResetChangesRequest
        {
            ModifiedPaths = modified,
            UntrackedPaths = untracked,
            IncludesStaged = scope == DiscardScope.All,

            // Both paths are backed up (P05-T15), so suppression is safe.
            CanSuppress = true,
        }).ConfigureAwait(true);

        if (decision is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ErrorDetails = null;

            List<DiscardBackup> backups = [];

            if (modified.Count > 0)
            {
                backups.AddRange(await _workingTreeWriter
                    .DiscardChangesAsync(directory, modified, scope, userConfirmed: true, cancellationToken)
                    .ConfigureAwait(true));
            }

            if (decision.DeleteUntracked && untracked.Count > 0)
            {
                backups.AddRange(await _workingTreeWriter
                    .DeleteUntrackedAsync(directory, untracked, userConfirmed: true, cancellationToken)
                    .ConfigureAwait(true));
            }

            IsBusy = false;
            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            RecordReset(
                backups,
                backups.Count == 0
                    ? Loc.T("working_tree.the_changes_were_reset")
                    : $"The changes were reset. The content of {backups.Count} files was backed up.");
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
            ErrorDetails = GitOutputViewModel.ForFailure(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Asks for confirmation on a destructive operation and applies the "don't ask again" choice (P05-T15).
    /// </summary>
    /// <returns>
    /// The user's decision; <see langword="null"/> if cancelled.
    /// </returns>
    /// <remarks>
    /// Both destructive paths — from the file list and from the diff panel — go through here:
    /// if the suppression rule didn't live in a single place, the second path forgetting to
    /// apply it would be a silent behavioral difference.
    /// </remarks>
    private async Task<ResetChangesDecision?> RequestResetAsync(ResetChangesRequest request)
    {
        if (_suppressResetPrompt)
        {
            return new ResetChangesDecision { Confirmed = true };
        }

        ResetChangesDecision decision =
            await Confirmer!.ConfirmResetAsync(request).ConfigureAwait(true);

        if (!decision.Confirmed)
        {
            return null;
        }

        if (decision.DoNotAskAgain)
        {
            _suppressResetPrompt = true;
        }

        return decision;
    }

    /// <summary>
    /// Records a destructive operation's backups and summary; the banner is fed from these.
    /// </summary>
    private void RecordReset(IReadOnlyList<DiscardBackup> backups, string notice)
    {
        _lastResetBackups.Clear();
        _lastResetBackups.AddRange(backups);

        ResetNotice = notice;

        OnPropertyChanged(nameof(CanUndoReset));
    }

    /// <summary>
    /// Summary of the last reset; shown as a banner. <see langword="null"/> if none.
    /// </summary>
    [ObservableProperty]
    public partial string? ResetNotice { get; private set; }

    /// <summary>Can the last reset be undone?</summary>
    public bool CanUndoReset => _lastResetBackups.Count > 0;

    /// <summary>
    /// Undoes the last reset (P05-T15).
    /// </summary>
    /// <remarks>
    /// 🔑 This is where the safety net actually earns its keep: giving the user a blob id and
    /// expecting them to type <c>git cat-file</c> is useless in a moment of panic. This
    /// command doesn't appear at all for an operation with no backup.
    /// </remarks>
    public async Task UndoResetAsync(CancellationToken cancellationToken = default)
    {
        if (WorkingDirectory is not { Length: > 0 } directory
            || _workingTreeWriter is null
            || _lastResetBackups.Count == 0)
        {
            return;
        }

        IReadOnlyList<DiscardBackup> backups = [.. _lastResetBackups];

        IReadOnlyList<DiscardBackup> restored = await _workingTreeWriter
            .RestoreBackupsAsync(directory, backups, cancellationToken)
            .ConfigureAwait(true);

        // The backups are consumed: offering the same undo a second time makes no sense. A
        // partial recovery must not be silently shown as "success" either — files whose
        // backup was pruned (`gc --prune=now`) don't come back, and the user must know that.
        RecordReset(
            [],
            restored.Count == backups.Count
                ? $"{restored.Count} files were restored."
                : $"{restored.Count}/{backups.Count} files were restored; "
                  + Loc.T("working_tree.the_rest_have_no_backup_in_the_object_databa"));

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Closes the banner.</summary>
    public void ClearResetNotice()
    {
        _lastResetBackups.Clear();
        ResetNotice = null;
        OnPropertyChanged(nameof(CanUndoReset));
    }

    /// <summary>Stages the selected unstaged file.</summary>
    public Task StageSelectedAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            paths => _staging.StageAsync(WorkingDirectory!, paths, cancellationToken),
            Get(Unstaged, SelectedUnstagedIndex),
            cancellationToken);

    /// <summary>Stages all unstaged files.</summary>
    public Task StageAllAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            paths => _staging.StageAsync(WorkingDirectory!, paths, cancellationToken),
            [.. Unstaged],
            cancellationToken);

    /// <summary>Unstages the selected staged file.</summary>
    public Task UnstageSelectedAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            paths => _staging.UnstageAsync(WorkingDirectory!, paths, cancellationToken),
            Get(Staged, SelectedStagedIndex),
            cancellationToken);

    /// <summary>Unstages all staged files.</summary>
    public Task UnstageAllAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            paths => _staging.UnstageAsync(WorkingDirectory!, paths, cancellationToken),
            [.. Staged],
            cancellationToken);

    private Task RunAsync(
        Func<IReadOnlyList<RepositoryPath>, Task> operation,
        WorkingTreeFileRow? row,
        CancellationToken cancellationToken) =>
        RunAsync(operation, row is null ? [] : [row], cancellationToken);

    private async Task RunAsync(
        Func<IReadOnlyList<RepositoryPath>, Task> operation,
        IReadOnlyList<WorkingTreeFileRow> rows,
        CancellationToken cancellationToken)
    {
        if (WorkingDirectory is not { Length: > 0 } || rows.Count == 0)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ErrorDetails = null;

            await operation([.. rows.Select(row => row.Path)]).ConfigureAwait(true);
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
            ErrorDetails = GitOutputViewModel.ForFailure(ex);
            IsBusy = false;
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Unsubscribes from the watcher (P05-T14).
    /// </summary>
    /// <remarks>
    /// The watcher is a single object that <b>lives for the application's lifetime</b>, while
    /// the commit window opens and closes. If the subscription isn't released, every closed
    /// window keeps receiving events and <c>git status</c> gets run for a closed screen.
    /// </remarks>
    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.Changed -= OnRepositoryChanged;
        }

        _refreshing?.Cancel();
        _refreshing?.Dispose();
        _refreshing = null;
    }
}
