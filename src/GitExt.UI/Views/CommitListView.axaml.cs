using System.Globalization;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core.Model;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Commit list view. Keyboard navigation is wired up here (P03-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why code-behind?</b> The work here is pure view concern: which key does what, how
/// many rows a page is, and where focus should go. Page size comes from
/// <c>ScrollViewer</c>'s visible area — something the ViewModel neither knows nor should
/// know. The navigation <b>decision</b> itself lives in the ViewModel
/// (<see cref="CommitListViewModel.MoveSelection"/> etc.) and is tested there separately.
/// </para>
/// <para>
/// <b>THREE MEASURED BEHAVIORS</b> (all of which shaped this code):
/// </para>
/// <list type="number">
/// <item><c>ListBox</c> moves selection itself with <c>↑↓</c>, <c>Home</c> and <c>End</c>;
/// with <c>PgUp</c>/<c>PgDn</c> only the <c>ScrollViewer</c> scrolls, <b>selection stays
/// put</b>. Page navigation is therefore applied manually.</item>
/// <item>Because <c>ScrollViewer</c> is <b>inside</b> <c>ListBox</c>, it grabs the bubbling
/// event first and marks it <c>Handled</c>. That's why keys are caught in the
/// <b>tunneling</b> (<see cref="RoutingStrategies.Tunnel"/>) phase — otherwise
/// <c>PgUp</c>/<c>PgDn</c> would never be seen.</item>
/// <item><c>ListBox.Focusable</c> is <see langword="false"/>; the thing that can be
/// focused is <c>ListBoxItem</c>, and arrow-key navigation is based on the <b>focused
/// container</b>, not <c>SelectedIndex</c>.</item>
/// </list>
/// </remarks>
public partial class CommitListView : UserControl
{
    /// <summary>
    /// Fallback page size used when the visible area can't be computed.
    /// </summary>
    /// <remarks>
    /// Only kicks in while the list hasn't been measured yet (the first frame). Moving by a
    /// reasonable page is preferable to doing nothing.
    /// </remarks>
    private const int FallbackPageSize = 20;

    /// <summary>Are we waiting to hand focus to the list once rows arrive?</summary>
    private bool _awaitingFirstRows;

    private CommitListViewModel? _watched;

    private ShortcutDispatcher? _dispatcher;

    public CommitListView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        // Tunneling is required: in the bubble phase ScrollViewer swallows page keys (measured).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private CommitListViewModel? ViewModel => DataContext as CommitListViewModel;

    /// <summary>
    /// Makes the keyboard usable once the view has loaded.
    /// </summary>
    /// <remarks>
    /// If nothing has focus, key events <b>never pass through</b> this view's tree at all
    /// and shortcuts silently do nothing. Focus is given on open so the user doesn't have
    /// to click with the mouse first.
    /// </remarks>
    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (FocusSelectedRow())
        {
            return;
        }

        // Rows haven't arrived yet — repository loading finishes after the view does. Focus
        // is temporarily given to the search box so keys go somewhere, but once rows arrive
        // focus will be handed to the list (otherwise arrow keys on open would type into the
        // search box).
        _awaitingFirstRows = true;
        ShaSearchBox.Focus();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _watched = ViewModel;

        if (_watched is not null)
        {
            _watched.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_awaitingFirstRows || e.PropertyName != nameof(CommitListViewModel.SelectedIndex))
        {
            return;
        }

        // We don't steal focus away if the user has meanwhile started typing in the search box.
        if (!string.IsNullOrEmpty(ShaSearchBox.Text))
        {
            _awaitingFirstRows = false;
            return;
        }

        // Must be tried AFTER layout: since rows were just added, the ListBoxItem container
        // doesn't exist yet and there's nothing to focus.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_awaitingFirstRows && FocusSelectedRow())
                {
                    _awaitingFirstRows = false;
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Binds context shortcuts to the command registry (P08-T01).</summary>
    public ShortcutDispatcher AttachShortcuts(ICommandRegistry registry)
    {
        ShortcutDispatcher dispatcher = new(registry, CommandContext.CommitList);

        // Ones that need to work while the search box is focused.
        dispatcher.Bind(CommandIds.CommitListFind, () =>
        {
            FocusSearch();
            ShaSearchBox.SelectAll();
        });

        dispatcher.Bind(CommandIds.CommitListCompareToWorkingDirectory,
            () => Compare(againstSelected: false));
        dispatcher.Bind(CommandIds.CommitListCompareSelected,
            () => Compare(againstSelected: true));

        // 🔴 Navigation shortcuts do NOT WORK while the search box is focused: a user
        // navigating within the text with PgDn shouldn't have the list shift underneath them.
        dispatcher.Bind(CommandIds.CommitListPageDown, () => Navigate(m => m.MoveSelection(PageSize())));
        dispatcher.Bind(CommandIds.CommitListPageUp, () => Navigate(m => m.MoveSelection(-PageSize())));
        dispatcher.Bind(CommandIds.CommitListGoToParent, () => Navigate(m => m.GoToParent()));
        dispatcher.Bind(CommandIds.CommitListGoToChild, () => Navigate(m => m.GoToChild()));

        _dispatcher = dispatcher;

        return dispatcher;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        CommitListViewModel? viewModel = ViewModel;

        if (viewModel is null)
        {
            return;
        }

        // The search box's own Enter/Escape behavior isn't a shortcut; making them
        // rebindable would be pointless.
        if (ShaSearchBox.IsFocused && e.Key is Key.Enter or Key.Escape)
        {
            HandleSearchKey(viewModel, e);
            return;
        }

        _dispatcher?.Handle(e);
    }

    private bool Compare(bool againstSelected)
    {
        if (ViewModel is not { } viewModel)
        {
            return false;
        }

        OpenComparison(viewModel, againstSelected);

        return true;
    }

    /// <summary>
    /// Runs a navigation shortcut and moves focus to the selected row.
    /// </summary>
    /// <remarks>
    /// The key is <b>consumed</b> even if the target isn't found: otherwise
    /// <c>ScrollViewer</c> would scroll the view away from the selection, or
    /// <c>ListBox</c> would nudge the selection by one row — the user asking to "go to
    /// parent" would silently land on a different commit.
    /// </remarks>
    private bool Navigate(Func<CommitListViewModel, bool> move)
    {
        if (ShaSearchBox.IsFocused || ViewModel is not { } viewModel)
        {
            return false;
        }

        if (move(viewModel))
        {
            FocusSelectedRow();
        }

        return true;
    }

    private void HandleSearchKey(CommitListViewModel viewModel, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                viewModel.ApplySearch();

                // Focus stays on the search box — lets the user keep searching.
                ScrollSelectionIntoView();
                e.Handled = true;
                break;

            case Key.Escape:
                // Return from search to the list: lets the user keep navigating.
                FocusSelectedRow();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Focuses the panel (P08-T05).
    /// </summary>
    /// <remarks>
    /// Goes to the selected row if there is one; otherwise the search box. If neither can be
    /// focused, we return <see langword="false"/> so navigation moves on to the <b>next
    /// panel</b> — staying on a panel that can't be focused would mean keys go nowhere.
    /// </remarks>
    public bool FocusPanel() => FocusSelectedRow() || ShaSearchBox.Focus();

    /// <summary>Focuses the search box (<c>Ctrl+F</c>).</summary>
    public void FocusSearch() => ShaSearchBox.Focus();

    /// <summary>
    /// Computes how many rows a page is from the visible area.
    /// </summary>
    /// <remarks>
    /// A fixed row height is not assumed; the list item's actual height is measured. Row
    /// height can change with the theme or font.
    /// </remarks>
    private int PageSize()
    {
        ScrollViewer? scroll = CommitList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        double rowHeight = CommitList.GetRealizedContainers().FirstOrDefault()?.Bounds.Height ?? 0;

        if (scroll is null || rowHeight <= 0)
        {
            return FallbackPageSize;
        }

        // Scrolling one row short is intentional: the last row the user was looking at
        // stays at the edge of the screen after the page changes, so context isn't lost.
        int rows = (int)(scroll.Viewport.Height / rowHeight) - 1;

        return Math.Max(1, rows);
    }

    private void ScrollSelectionIntoView()
    {
        int index = ViewModel?.SelectedIndex ?? -1;

        if (index >= 0)
        {
            CommitList.ScrollIntoView(index);
        }
    }

    // ---- Context menu (P08-T27) ----

    /// <summary>
    /// Copies text from the selected commit.
    /// </summary>
    /// <remarks>
    /// Corresponds to the fields in GitExtensions' "Copy to clipboard" submenu:
    /// hash · message · author · date · branch name.
    /// </remarks>
    private async void CopyAsync(Func<CommitRowViewModel, string?> select)
    {
        if (ViewModel?.SelectedRow is not { } row)
        {
            return;
        }

        string? text = select(row);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnCopyHashClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => row.Commit.Id.Value);

    private void OnCopyMessageClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => string.IsNullOrEmpty(row.Commit.Body)
            ? row.Commit.Subject
            : row.Commit.Subject + "\n\n" + row.Commit.Body);

    private void OnCopyAuthorClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => $"{row.Commit.Author.Name} <{row.Commit.Author.Email}>");

    private void OnCopyDateClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => row.Commit.Author.When.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>Copies the names of the branch badges on the row.</summary>
    private void OnCopyBranchClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => string.Join('\n', row.Badges
            .Where(badge => badge.Kind is RefBadgeKind.LocalBranch or RefBadgeKind.RemoteBranch)
            .Select(badge => badge.Text)));

    /// <summary>
    /// "Create new branch here…" (P06-T01).
    /// </summary>
    /// <remarks>
    /// The command lives on the main window's ViewModel; this view only knows about the
    /// commit list. It's read from the top-level window instead of via binding — the
    /// context menu sits in a separate name scope in the visual tree and its bindings are
    /// never evaluated while it's closed (measured in P05-T13). The selected commit is
    /// read on the ViewModel side as the starting point.
    /// </remarks>
    private async void OnCreateBranchClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.CreateBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnRenameBranchClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.RenameBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnDeleteBranchClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.DeleteBranchCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// "Checkout branch" / "Checkout this commit" (P06-T02).
    /// </summary>
    /// <remarks>
    /// Both menu items call the same command: which one applies is decided by whether the
    /// selected commit has a local branch, and the result is <b>spelled out in the
    /// dialog</b>. Both remain in place because GitExtensions has two separate items (§ 9).
    /// </remarks>
    // P06-T14: these two items were in place in P08-T27 but disabled; their commands
    // arrived in T07/T08 and T11. The menu item doesn't write its own flow, it calls the
    // command IN THE MENU — a second path would mean one of them silently ends up unprotected.
    private async void OnPushClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.PushCommand.ExecuteAsync(null);
        }
    }

    private async void OnMergeClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.MergeCommand.ExecuteAsync(null);
        }
    }

    /// <remarks>
    /// The context menu calls <b>the same command</b> as the main menu. Opening a second
    /// path would mean one of them ends up unprotected — the lesson from P06-T13.
    /// </remarks>
    private async void OnRebaseClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.RebaseCommand.ExecuteAsync(null);
        }
    }

    private async void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.ResetCommand.ExecuteAsync(null);
        }
    }

    private async void OnCherryPickClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.CherryPickCommand.ExecuteAsync(null);
        }
    }

    private async void OnRevertClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.RevertCommand.ExecuteAsync(null);
        }
    }

    private async void OnCheckoutClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.CheckoutCommand.ExecuteAsync(null);
        }
    }

    private void OnCompareSelectedClick(object? sender, RoutedEventArgs e) => RequestComparison(againstHead: false);

    private void OnCompareHeadClick(object? sender, RoutedEventArgs e) => RequestComparison(againstHead: true);

    /// <summary>Comparison against the working tree: selection is reduced to a single row.</summary>
    private void OnCompareWorkingTreeClick(object? sender, RoutedEventArgs e)
    {
        CommitList.SelectedItems?.Clear();
        RequestComparison(againstHead: false);
    }

    private void RequestComparison(bool againstHead)
    {
        if (ViewModel is { } viewModel)
        {
            OpenComparison(viewModel, againstHead);
        }
    }

    private void OnGoToParentClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.GoToParent() == true)
        {
            FocusSelectedRow();
        }
    }

    private void OnGoToChildClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.GoToChild() == true)
        {
            FocusSelectedRow();
        }
    }

    /// <summary>
    /// Opens the comparison window (P04-T16).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Source selection: if <b>two rows are selected</b>, those two are compared (the first
    /// consumer of the multi-select opened in P03-T14); with a single row selected, it's the
    /// commit against the <b>working tree</b>. If <c>Shift</c> is held, a single selection
    /// compares the commit against <c>HEAD</c> instead.
    /// </para>
    /// <para>
    /// The window is <b>modeless</b>: opened with <c>Show()</c>, and there can be several
    /// at once. That was exactly the user's objection — a single embedded panel made it
    /// impossible to put two diffs side by side.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Fires right before the window opens, when a comparison is requested.
    /// </summary>
    /// <remarks>
    /// For tests: counting on the window actually opening in a headless environment isn't
    /// reliable, but <b>the key binding to the right command with the right revisions</b>
    /// can be verified from here.
    /// </remarks>
    internal event EventHandler<CompareViewModel>? ComparisonRequested;

    private void OpenComparison(CommitListViewModel viewModel, bool againstHead)
    {
        CompareViewModel? compare = viewModel.CreateComparison();

        if (compare is null)
        {
            return;
        }

        List<CommitId> selected = [.. CommitList.SelectedItems?
            .OfType<CommitRowViewModel>()
            .Select(row => row.Commit.Id) ?? []];

        Task loading;

        if (selected.Count >= 2)
        {
            // The list is sorted newest to oldest; the user expects an "old to new" diff.
            loading = compare.CompareAsync(selected[^1], selected[0]);
        }
        else if (viewModel.SelectedRow is { } row)
        {
            loading = againstHead
                ? compare.CompareAsync(row.Commit.Id.Value, "HEAD")
                : compare.CompareWithWorkingTreeAsync(row.Commit.Id.Value);
        }
        else
        {
            return;
        }

        _ = loading;

        ComparisonRequested?.Invoke(this, compare);

        new CompareWindow { DataContext = compare }
            .ShowOwnedBy(TopLevel.GetTopLevel(this) as Window);
    }

    /// <summary>
    /// Makes the selected row visible and <b>moves focus to it</b>.
    /// </summary>
    /// <remarks>
    /// Moving focus is required: since <c>ListBox</c>'s arrow-key navigation is based on the
    /// focused container (measured), pressing <c>↓</c> after advancing 40 rows with
    /// <c>PgDn</c> would make the selection <b>jump back next to the old row</b>.
    /// </remarks>
    /// <returns><see langword="true"/> if a row to focus was found.</returns>
    private bool FocusSelectedRow()
    {
        int index = ViewModel?.SelectedIndex ?? -1;

        if (index < 0)
        {
            return false;
        }

        CommitList.ScrollIntoView(index);

        // Because of virtualization the container only exists once it enters the visible
        // area; requesting it before ScrollIntoView would return null.
        return CommitList.ContainerFromIndex(index)?.Focus() == true;
    }
}
