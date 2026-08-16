using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Graph;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Loads a repository's commit history incrementally and places it into the graph (P03-T13).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why incremental streaming instead of paging?</b> The layout algorithm is a single-pass
/// forward scan (ADR-0007): the lane of the 500,000th row cannot be computed until all rows
/// before it have been processed. So a "fetch the requested page" model can't be applied
/// directly; commits flow in order and the layout engine preserves its state.
/// </para>
/// <para>
/// <b>Batched updates are mandatory</b> (ADR-0004): publishing one <c>CollectionChanged</c>
/// event per commit locks up the app at 100k commits. Rows are added in
/// batches via <see cref="AvaloniaList{T}.AddRange"/>.
/// </para>
/// </remarks>
public sealed partial class CommitListViewModel : ViewModelBase
{
    /// <summary>
    /// Number of rows transferred to the UI per batch.
    /// </summary>
    /// <remarks>
    /// Keeping it small makes the first screen appear sooner; keeping it large reduces the
    /// number of events. 256 is small enough to fill the first screen (~40 rows) instantly.
    /// </remarks>
    private const int BatchSize = 256;

    private readonly IRepositoryLocator _locator;
    private readonly ICommitLogReader _logReader;
    private readonly IRefReader _refReader;
    private readonly IDiffReader _diffReader;

    /// <summary>
    /// Mapping from commit id to row index — for jumping to a parent and for lookup by SHA.
    /// </summary>
    /// <remarks>
    /// Fills incrementally as rows are added. Its cost is ~20 MB at 500k rows; leaving the
    /// jump-to-parent lookup to a scan would mean walking half the repository for merge
    /// commits with a distant parent.
    /// </remarks>
    private readonly Dictionary<CommitId, int> _rowIndex = [];

    private CancellationTokenSource? _loading;

    public CommitListViewModel(
        IRepositoryLocator locator,
        ICommitLogReader logReader,
        IRefReader refReader,
        ICommitSignatureReader signatureReader,
        IDiffReader diffReader)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(logReader);
        ArgumentNullException.ThrowIfNull(refReader);
        ArgumentNullException.ThrowIfNull(signatureReader);
        ArgumentNullException.ThrowIfNull(diffReader);

        _locator = locator;
        _logReader = logReader;
        _refReader = refReader;
        _diffReader = diffReader;

        // Parent links in the details panel navigate back to the list; only this callback is
        // handed to the panel — not the whole ViewModel — so the dependency stays one-way.
        Details = new CommitDetailsViewModel(signatureReader, TryGoToCommit);

        // The diff component is INDEPENDENT: it doesn't know about this class, it only gets
        // told "show this". The same component is also used in the P04-T16 comparison window.
        Diff = new DiffViewModel(diffReader);
    }

    /// <summary>Details panel of the selected commit (P03-T15).</summary>
    public CommitDetailsViewModel Details { get; }

    /// <summary>
    /// Produces a new ViewModel for a comparison window (P04-T16).
    /// </summary>
    /// <remarks>
    /// <b>Each window has its own ViewModel</b> — windows are modeless and several can be
    /// open at once; a shared instance would let one overwrite another's content.
    /// <para>
    /// <b>Opening</b> the window is the view's job; this method only sets up what to show.
    /// </para>
    /// </remarks>
    public CompareViewModel? CreateComparison()
    {
        string? workingDirectory = Repository?.WorkingDirectory;

        return string.IsNullOrEmpty(workingDirectory)
            ? null
            : new CompareViewModel(_diffReader, workingDirectory);
    }

    /// <summary>Changed files of the selected commit (P04-T08).</summary>
    public DiffViewModel Diff { get; }

    /// <summary>Loaded rows.</summary>
    public AvaloniaList<CommitRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Index of the selected row; <c>-1</c> if there is no selection.
    /// </summary>
    /// <remarks>
    /// This is the <b>single source of truth</b> for selection; <see cref="SelectedRow"/> is
    /// derived from it. Binding both directions would mean two bindings chasing each other
    /// during programmatic navigation. The index is also needed for navigation — <c>IndexOf</c>
    /// would scan the list from scratch on every keystroke at 500k rows.
    /// </remarks>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>Last read ref state; <see langword="null"/> if it could not be read (P06-T04).</summary>
    public RepositoryRefs? Refs { get; private set; }

    /// <summary>Selected row; <see langword="null"/> if there is no selection.</summary>
    public CommitRowViewModel? SelectedRow =>
        SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex] : null;

    partial void OnSelectedIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedRow));
        EnsureLaneVisible(SelectedRow?.GraphRow.Lane);
        Details.Show(SelectedRow, Repository?.WorkingDirectory);

        // Debounce and cancellation live inside DiffViewModel; git isn't run during fast navigation.
        _ = Diff.ShowCommitAsync(
            Repository?.WorkingDirectory,
            SelectedRow?.Commit.Id ?? default,
            SelectedRow?.Subject);
    }

    /// <summary>
    /// Scrolls the graph window so the given lane becomes visible (P03-T21).
    /// </summary>
    /// <remarks>
    /// The node of the selected commit must always be visible — otherwise the user can't see
    /// where the row they selected is in the graph. The window only scrolls <b>when
    /// needed</b>; centering on every selection would make the graph jump constantly.
    /// </remarks>
    internal void EnsureLaneVisible(int? lane)
    {
        if (lane is not { } target || target < 0)
        {
            return;
        }

        int window = Math.Max(VisibleLanes, 1);

        if (target < FirstVisibleLane)
        {
            FirstVisibleLane = target;
        }
        else if (target > FirstVisibleLane + window - 1)
        {
            FirstVisibleLane = target - window + 1;
        }
    }

    /// <summary>
    /// Number of lanes shown at once in the graph window (P03-T21).
    /// </summary>
    /// <remarks>
    /// <b>MEASURED:</b> in real repositories the median lane count is ~120 (git/git 118, Linux
    /// 120) and nodes are spread across those lanes — a 16-lane cap would show only 24% of
    /// nodes on Linux. So a fixed "cut off" limit doesn't work; the column stays a fixed width
    /// and shows a <b>sliding window</b> via <see cref="FirstVisibleLane"/>.
    /// </remarks>
    public const int DefaultVisibleLanes = 12;

    /// <summary>
    /// Leftmost first lane of the graph window.
    /// </summary>
    /// <remarks>
    /// All rows use the same value; computing it per row would make lanes shift row to row and
    /// the graph unreadable. The window is scrolled to contain the selected commit as the
    /// selection changes.
    /// </remarks>
    [ObservableProperty]
    public partial int FirstVisibleLane { get; private set; }

    /// <summary>Upper bound of the graph window.</summary>
    [ObservableProperty]
    public partial int MaxVisibleLanes { get; set; } = DefaultVisibleLanes;

    /// <summary>
    /// Number of lanes the column actually uses.
    /// </summary>
    /// <remarks>
    /// The smaller of the upper bound and the repository's real width. In a narrow repository a
    /// fixed 12-lane column would waste space; in a wide one the bound kicks in. The value is
    /// <b>shared across all rows</b> so columns stay aligned.
    /// </remarks>
    [ObservableProperty]
    public partial int VisibleLanes { get; private set; } = 1;

    private int _widestRow = 1;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Number of rows loaded so far — used to show progress while loading is in flight (P03-T17).
    /// </summary>
    /// <remarks>
    /// A separate property instead of <c>Rows.Count</c>: with hundreds of thousands of rows,
    /// publishing a single notification per batch is enough, instead of binding to the
    /// collection's counter.
    /// </remarks>
    [ObservableProperty]
    public partial int LoadedCount { get; private set; }

    /// <summary>
    /// Translated text for the number of loaded commits (P11-T08).
    /// </summary>
    /// <remarks>
    /// Cannot be written with <c>StringFormat</c> in XAML: the format string itself must be
    /// translated and Avalonia's <c>StringFormat</c> isn't exposed as a bindable property. The
    /// text is composed here.
    /// </remarks>
    public string LoadedCountText => Loc.F("commit_list.commits_loaded", LoadedCount);

    partial void OnLoadedCountChanged(int value) => OnPropertyChanged(nameof(LoadedCountText));

    /// <summary>
    /// Is the repository open but has no commits at all? (Freshly <c>git init</c>'d — not an error.)
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmptyRepository { get; private set; }

    /// <summary>
    /// Cancels an in-progress load (P03-T17).
    /// </summary>
    /// <remarks>
    /// In a very large repository the user may notice they opened the wrong folder; they
    /// shouldn't be forced to wait for loading to finish. Rows that arrived up to that point
    /// stay on screen — a partial history is more useful than a blank screen.
    /// </remarks>
    public Task CancelLoadingAsync() => CancelLoadingCoreAsync();

    /// <summary>SHA prefix typed into the search box.</summary>
    [ObservableProperty]
    public partial string? SearchText { get; set; }

    /// <summary>Short note shown to the user when the search fails; empty when it succeeds.</summary>
    [ObservableProperty]
    public partial string? SearchStatus { get; set; }

    partial void OnSearchTextChanged(string? value) => SearchStatus = null;

    /// <summary>
    /// Applies the text in the search box (P03-T14).
    /// </summary>
    /// <remarks>
    /// Runs on <c>Enter</c>, not while typing. Reason: the short-prefix search scans the list;
    /// scanning 500k rows on every keystroke would make typing feel stuck.
    /// </remarks>
    public void ApplySearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchStatus = null;
            return;
        }

        SearchStatus = TryGoToCommit(SearchText) ? null : Loc.T("commit_list.not_found");
    }

    /// <summary>Message shown to the user when loading fails.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// The <b>full</b> git output of the error; only populated for <see cref="GitException"/> (P05-T07).
    /// </summary>
    /// <remarks>
    /// <see cref="ErrorMessage"/> is a classified <b>summary</b> ("Git command failed.") and
    /// isn't enough on its own to diagnose anything — what git actually said is here.
    /// </remarks>
    [ObservableProperty]
    public partial GitOutputViewModel? ErrorDetails { get; set; }

    /// <summary>Location of the open repository; <see langword="null"/> if none is open yet.</summary>
    [ObservableProperty]
    public partial RepositoryLocation? Repository { get; set; }

    /// <summary>
    /// Opens a repository and starts loading its history.
    /// </summary>
    /// <remarks>
    /// A previous load in progress is cancelled — this prevents two streams from writing into
    /// the same list when the user quickly opens another repository.
    /// </remarks>
    /// <summary>
    /// Closes the open repository and clears the list (P08-T26).
    /// </summary>
    /// <remarks>
    /// Corresponds to <i>Repository → Close (go to Dashboard)</i> in GitExtensions. If a load
    /// is in progress it is cancelled: rows arriving after closing and repopulating the list
    /// would make the user think the repository never closed.
    /// </remarks>
    public void Close()
    {
        _loading?.Cancel();

        Rows.Clear();
        _rowIndex.Clear();
        SelectedIndex = -1;
        FirstVisibleLane = 0;
        _widestRow = 1;
        VisibleLanes = 1;
        LoadedCount = 0;
        IsEmptyRepository = false;
        ErrorMessage = null;
        ErrorDetails = null;
        IsLoading = false;
        Repository = null;

        Details.Show(null, null);
        Diff.Clear();
    }

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        await CancelLoadingCoreAsync().ConfigureAwait(true);

        _loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _loading.Token;

        Rows.Clear();
        _rowIndex.Clear();
        SelectedIndex = -1;
        FirstVisibleLane = 0;
        _widestRow = 1;
        VisibleLanes = 1;
        LoadedCount = 0;
        IsEmptyRepository = false;
        ErrorMessage = null;
        ErrorDetails = null;

        // The previous repository is forgotten: if opening fails, the path of a repository with
        // no rows on screen must not linger — the user would still think it's open.
        Repository = null;
        IsLoading = true;

        try
        {
            RepositoryLocation location = await _locator
                .LocateAsync(path, token)
                .ConfigureAwait(true);

            Repository = location;

            // Refs are read BEFORE history: without the badge index, rows would come in without
            // badges and adding them later would mean regenerating every row.
            // A single call, only a few milliseconds even on a large repository (measured).
            RefBadgeIndex badges = await LoadBadgesAsync(location.WorkingDirectory, token)
                .ConfigureAwait(true);

            await LoadHistoryAsync(location.WorkingDirectory, badges, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The user opened another repository or closed the window; not an error.
        }
        catch (Exception ex) when (ex is GitException or GitNotFoundException
                                       or GitVersionTooOldException or DirectoryNotFoundException)
        {
            // For GitException the message is translated based on Kind (P11-T06); for the other
            // exceptions (git not found, version too old, directory missing) there's no
            // classification — their own message is already user-facing.
            ErrorMessage = ex is GitException classified
                ? Loc.GitError(classified)
                : ex.Message;

            // The summary alone isn't enough: git's actual output (and any hook messages) only
            // become visible here.
            ErrorDetails = ex is GitException gitException
                ? GitOutputViewModel.ForFailure(gitException)
                : null;
        }
        finally
        {
            IsLoading = false;

            // "Repository open but no commits" is not an error, it's a state that needs to be
            // communicated: the user shouldn't stare at a blank screen in a freshly
            // `git init`ed repository (P03-T17).
            IsEmptyRepository = Repository is not null && ErrorMessage is null && Rows.Count == 0;
        }
    }

    /// <summary>
    /// Moves the selection by <paramref name="delta"/> rows; stops at list boundaries (P03-T14).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why does this method exist?</b> <c>ListBox</c> handles <c>↑↓</c>, <c>Home</c> and
    /// <c>End</c> itself but <b>does not handle</b> <c>PgUp</c>/<c>PgDn</c> — measured: these
    /// keys scroll the <c>ScrollViewer</c> while the selection stays put. Page navigation is
    /// therefore implemented by hand.
    /// </para>
    /// <para>
    /// Stopping at the boundary is preferable to wrapping: pressing <c>PgDn</c> at the end of a
    /// long list and jumping back to the start would mean the user loses their place.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> if the selection actually changed.</returns>
    public bool MoveSelection(int delta)
    {
        if (Rows.Count == 0)
        {
            return false;
        }

        // With no selection, the first move starts from the end of the list; from the start if
        // moving down, from the end if moving up.
        int current = SelectedIndex >= 0 ? SelectedIndex : (delta >= 0 ? -1 : Rows.Count);

        int target = Math.Clamp(current + delta, 0, Rows.Count - 1);

        if (target == SelectedIndex)
        {
            return false;
        }

        SelectedIndex = target;
        return true;
    }

    /// <summary>
    /// Jumps to the selected commit's <b>first parent</b> (older commit, further down the list).
    /// </summary>
    /// <remarks>
    /// The first parent was chosen because it's the "main line" of the branch in merge commits.
    /// Other parents are reached by clicking from the details panel (P03-T15).
    /// </remarks>
    /// <returns><see langword="true"/> if a parent was found and selected.</returns>
    public bool GoToParent()
    {
        CommitInfo? commit = SelectedRow?.Commit;

        return commit is { Parents.Count: > 0 } && TryGoToCommit(commit.Parents[0]);
    }

    /// <summary>
    /// Jumps to the nearest child that shows the selected commit as a parent (further up the list).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reverse index isn't kept; instead we scan upward. Rationale: a child index would
    /// double the memory footprint, whereas <c>--topo-order</c> guarantees every child comes
    /// <b>before</b> its parent, so the child is always above and in practice only a few rows away.
    /// </para>
    /// <para>
    /// The worst case is the tip of a branch (no children): the list is scanned end to end. This
    /// is acceptable for a single keystroke from the user.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> if a child was found and selected.</returns>
    public bool GoToChild()
    {
        CommitRowViewModel? selected = SelectedRow;

        if (selected is null)
        {
            return false;
        }

        CommitId id = selected.Commit.Id;

        for (int i = SelectedIndex - 1; i >= 0; i--)
        {
            if (Rows[i].Commit.Parents.Contains(id))
            {
                SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Jumps to the commit with the given id.
    /// </summary>
    /// <returns><see langword="true"/> if the commit was found among the loaded rows.</returns>
    public bool TryGoToCommit(CommitId id)
    {
        if (!_rowIndex.TryGetValue(id, out int index))
        {
            return false;
        }

        SelectedIndex = index;
        return true;
    }

    /// <summary>
    /// Searches for a commit by SHA prefix and jumps to the first match (P03-T14).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a <b>SHA prefix</b> is searched. Message/author search is a separate task
    /// (P07-T21); mixing them in here would mean a user typing a 7-character SHA landing on an
    /// unrelated commit whose subject line happens to match.
    /// </para>
    /// <para>
    /// A full-length SHA is found from the dictionary in O(1). A short prefix requires scanning
    /// the list — a dictionary can't be indexed by prefix. The scan only runs when the user
    /// explicitly searches, not per keystroke.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> if it jumped to the first matching commit.</returns>
    public bool TryGoToCommit(string shaPrefix)
    {
        if (string.IsNullOrWhiteSpace(shaPrefix))
        {
            return false;
        }

        string prefix = shaPrefix.Trim().ToLowerInvariant();

        if (CommitId.TryParse(prefix, out CommitId exact) && exact.IsFull && TryGoToCommit(exact))
        {
            return true;
        }

        if (prefix.Length < CommitId.MinimumLength)
        {
            return false;
        }

        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Commit.Id.Value.StartsWith(prefix, StringComparison.Ordinal))
            {
                SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads ref badges. If it fails, continues without badges.
    /// </summary>
    /// <remarks>
    /// Badges are auxiliary information; failing to read them is not a reason to withhold the history.
    /// </remarks>
    private async Task<RefBadgeIndex> LoadBadgesAsync(string workingDirectory, CancellationToken token)
    {
        try
        {
            RepositoryRefs refs = await _refReader
                .ReadAsync(workingDirectory, token)
                .ConfigureAwait(true);

            // HEAD state is also stored separately: the detached HEAD lane (P06-T04) reads this
            // and there's no point making a second `symbolic-ref` call.
            Refs = refs;

            return RefBadgeIndex.Build(refs);
        }
        catch (GitException)
        {
            Refs = null;

            return RefBadgeIndex.Empty;
        }
    }

    private async Task LoadHistoryAsync(
        string workingDirectory,
        RefBadgeIndex badges,
        CancellationToken token)
    {
        GraphLayoutEngine engine = new();
        List<CommitRowViewModel> batch = new(BatchSize);

        // TopologicalOrder is on by default — layout depends on it (ADR-0007).
        CommitLogQuery query = new() { IncludeAllRefs = true };

        try
        {
            await foreach (CommitInfo commit in _logReader
                               .StreamAsync(workingDirectory, query, token)
                               .ConfigureAwait(false))
            {
                GraphRow row = engine.Add(ToDagCommit(commit));
                batch.Add(new CommitRowViewModel(commit, row, badges.For(commit.Id)));

                if (batch.Count >= BatchSize)
                {
                    await FlushAsync(batch, token).ConfigureAwait(false);
                }
            }

            if (batch.Count > 0)
            {
                await FlushAsync(batch, token).ConfigureAwait(false);
            }
        }
        catch (GitException ex) when (ex.Kind is GitFailureKind.UnknownRevision or GitFailureKind.Unknown)
        {
            // Unborn repository: no commits. An empty list is the correct result, not an error.
            if (Rows.Count > 0)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Adds an accumulated batch to the list on the UI thread.
    /// </summary>
    /// <remarks>
    /// <c>GitExt.Core</c> knows nothing about threads (ADR-0004); the hop to the UI thread
    /// happens here, explicitly.
    /// </remarks>
    private async Task FlushAsync(List<CommitRowViewModel> batch, CancellationToken token)
    {
        CommitRowViewModel[] items = [.. batch];
        batch.Clear();

        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                // The index is updated TOGETHER with the rows. Filling it in a separate pass
                // would mean a navigation request arriving in between could see a half-built index.
                int next = Rows.Count;

                foreach (CommitRowViewModel item in items)
                {
                    // The same commit can't appear in two rows; TryAdd is still used so that in
                    // a corrupt repository the first row wins instead of crashing.
                    _rowIndex.TryAdd(item.Commit.Id, next++);
                }

                Rows.AddRange(items);
                LoadedCount = Rows.Count;

                // Column width can grow while loading but quickly saturates at the upper bound;
                // so a notification is only published when the value actually changes.
                foreach (CommitRowViewModel item in items)
                {
                    if (item.GraphRow.LaneCount > _widestRow)
                    {
                        _widestRow = item.GraphRow.LaneCount;
                    }
                }

                VisibleLanes = Math.Clamp(_widestRow, 1, MaxVisibleLanes);

                // When the first batch arrives, the newest commit is selected: instead of facing
                // an empty details panel, the user sees something right away. Only the SELECTION
                // is set, focus is not stolen — the user may be typing in the search box.
                if (SelectedIndex < 0)
                {
                    SelectedIndex = 0;
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Converts a <see cref="CommitInfo"/> to the reduced type the layout engine needs.
    /// </summary>
    /// <remarks>
    /// The engine only knows about id and parents; date, author and message don't concern it
    /// (ADR-0003 — <c>GitExt.Graph</c> works on pure data).
    /// </remarks>
    private static DagCommit ToDagCommit(CommitInfo commit)
    {
        string[] parents = new string[commit.Parents.Count];

        for (int i = 0; i < parents.Length; i++)
        {
            parents[i] = commit.Parents[i].Value;
        }

        return new DagCommit(commit.Id.Value, parents);
    }

    private async Task CancelLoadingCoreAsync()
    {
        if (_loading is null)
        {
            return;
        }

        await _loading.CancelAsync().ConfigureAwait(true);
        _loading.Dispose();
        _loading = null;
    }
}
