using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The changed files list and the diff view (P04-T08…T12).
/// </summary>
/// <remarks>
/// A standalone component: it knows nothing about the main window and takes only a
/// <c>DiffViewModel</c> from outside. The same view will be used in the comparison window of
/// <c>P04-T16</c>.
/// </remarks>
public partial class DiffView : UserControl
{
    public DiffView()
    {
        InitializeComponent();

        // ⚠️ The TUNNEL phase — measured in Phase 03: the `ScrollViewer` inside the list takes the
        // bubbling key event first and marks it `Handled`, so a handler in the bubbling phase never
        // runs at all.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private ShortcutDispatcher? _dispatcher;

    private DiffViewModel? Model => DataContext as DiffViewModel;

    /// <summary>
    /// Binds the context shortcuts to the command registry (P08-T01).
    /// </summary>
    /// <remarks>
    /// Without a registry the view works <b>without shortcuts</b>: <see cref="DiffView"/> is a
    /// standalone component used in the comparison window too, and there may be no shortcut
    /// registry there.
    /// </remarks>
    public ShortcutDispatcher AttachShortcuts(ICommandRegistry registry)
    {
        ShortcutDispatcher dispatcher = new(registry, CommandContext.Diff);

        // 🔴 While in the search box, navigation and bare-letter shortcuts MUST NOT RUN: the `S`
        // shortcut was staging lines while the user was typing into the text box. This condition
        // sits IN FRONT OF shortcut dispatch, not inside the individual commands — forgetting it in
        // one of them would silently break typing.
        dispatcher.Bind(CommandIds.DiffNextChange, () => WhenNotSearching(m => Move(m.GoToNextChange())));
        dispatcher.Bind(CommandIds.DiffPreviousChange, () => WhenNotSearching(m => Move(m.GoToPreviousChange())));
        dispatcher.Bind(CommandIds.DiffNextHunk, () => WhenNotSearching(m => Move(m.GoToNextHunk())));
        dispatcher.Bind(CommandIds.DiffPreviousHunk, () => WhenNotSearching(m => Move(m.GoToPreviousHunk())));
        dispatcher.Bind(CommandIds.DiffNextFile, () => WhenNotSearching(m => Move(m.GoToNextFile())));
        dispatcher.Bind(CommandIds.DiffPreviousFile, () => WhenNotSearching(m => Move(m.GoToPreviousFile())));

        dispatcher.Bind(CommandIds.DiffStageLines,
            () => WhenNotSearching(m => Started(m.StageSelectionAsync(SelectedLineIndices()))));
        dispatcher.Bind(CommandIds.DiffUnstageLines,
            () => WhenNotSearching(m => Started(m.UnstageSelectionAsync(SelectedLineIndices()))));
        dispatcher.Bind(CommandIds.DiffResetLines,
            () => WhenNotSearching(m => Started(m.DiscardSelectionAsync(SelectedLineIndices()))));

        dispatcher.Bind(CommandIds.DiffCopyCode,
            () => WhenNotSearching(m => Started(CopyAsync(m, DiffCopyMode.Code))));
        dispatcher.Bind(CommandIds.DiffCopyPatch,
            () => WhenNotSearching(m => Started(CopyAsync(m, DiffCopyMode.Patch))));

        dispatcher.Bind(CommandIds.DiffFind, () =>
        {
            LineSearch.Focus();
            LineSearch.SelectAll();
        });

        // Find/previous/next work in the search box TOO — that is how you carry on searching.
        dispatcher.Bind(CommandIds.DiffFindNext, () => Find(next: true));
        dispatcher.Bind(CommandIds.DiffFindPrevious, () => Find(next: false));

        _dispatcher = dispatcher;

        return dispatcher;
    }

    /// <summary>
    /// Focuses the panel (P08-T05).
    /// </summary>
    /// <remarks>
    /// When there is no line to focus it returns <see langword="false"/> so panel navigation does
    /// not get stuck here: giving focus to an empty diff panel meant the keys went nowhere.
    /// </remarks>
    public bool FocusPanel()
    {
        ListBox list = ActiveList;

        return list.ItemCount > 0
            && list.ContainerFromIndex(Math.Max(0, list.SelectedIndex))?.Focus() == true;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        bool inSearchBox = LineSearch.IsFocused;

        // Two keys that do not go into the registry: both belong to the search box and being
        // rebindable would be meaningless for them (Enter is "confirm", Escape is "cancel" — box
        // behaviour, not shortcuts).
        switch (e.Key)
        {
            case Key.Enter when inSearchBox:
                // Focus STAYS in the box: the user must be able to carry on searching.
                Move(model.FindNext(), e, keepFocus: true);
                return;

            case Key.Escape when inSearchBox:
                FocusLines();
                e.Handled = true;
                return;

            default:
                break;
        }

        _dispatcher?.Handle(e);
    }

    /// <summary>Runs when not in the search box; does not consume the event while in the box.</summary>
    private bool WhenNotSearching(Func<DiffViewModel, bool> action) =>
        !LineSearch.IsFocused && Model is { } model && action(model);

    /// <summary>Runs the navigation, brings the line into view and gives focus back to the list.</summary>
    private bool Move(bool moved)
    {
        if (moved)
        {
            ScrollCurrentIntoView();
            FocusLines();
        }

        return moved;
    }

    private bool Find(bool next)
    {
        if (Model is not { } model)
        {
            return false;
        }

        bool inSearchBox = LineSearch.IsFocused;
        bool moved = next ? model.FindNext() : model.FindPrevious();

        if (moved)
        {
            ScrollCurrentIntoView();

            if (!inSearchBox)
            {
                FocusLines();
            }
        }

        return moved;
    }

    /// <summary>Reports a started operation as "consumed".</summary>
    /// <remarks>
    /// The operation is asynchronous; waiting for its result would block the key event. Consuming
    /// the key depends on <b>the work having been started</b>, not on it having finished.
    /// </remarks>
    private static bool Started(Task operation) => operation is not null;

    /// <summary>
    /// Consumes the event when the navigation succeeded, brings the line into view and keeps focus
    /// on the list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On failure it is <b>not consumed</b>: swallowing <c>Ctrl+↓</c> at the end of a file would be
    /// a silent wall to the user.
    /// </para>
    /// <para>
    /// ⚠️ <b>Giving focus back is essential — a test caught this.</b> When the file changes the list
    /// is rebuilt and the focused <c>ListBoxItem</c> ceases to exist; unless focus is moved, <b>the
    /// next key event never passes through the view's tree at all</b> and the keyboard dies
    /// silently. The same trap measured on the commit list in Phase 03.
    /// </para>
    /// </remarks>
    private void Move(bool moved, KeyEventArgs e, bool keepFocus = false)
    {
        if (!moved)
        {
            return;
        }

        e.Handled = true;
        ScrollCurrentIntoView();

        if (!keepFocus)
        {
            FocusLines();
        }
    }

    private ListBox ActiveList =>
        Model?.ShowSideBySide == true ? SideDiffLines : DiffLines;

    private void ScrollCurrentIntoView()
    {
        if (Model is not { CurrentLineIndex: >= 0 } model)
        {
            return;
        }

        ActiveList.SelectedIndex = model.CurrentLineIndex;
        ActiveList.ScrollIntoView(model.CurrentLineIndex);
    }

    /// <summary>
    /// Gives focus to the diff list.
    /// </summary>
    /// <remarks>
    /// ⚠️ Measured in Phase 03: <c>ListBox.Focusable</c> is <see langword="false"/>; the thing that
    /// takes focus is the <c>ListBoxItem</c>. Trying to focus the list silently does nothing.
    /// </remarks>
    private void FocusLines()
    {
        ListBox list = ActiveList;
        int index = Math.Max(0, list.SelectedIndex);

        if (list.ContainerFromIndex(index)?.Focus() == true)
        {
            return;
        }

        // ⚠️ When the file changes the list is rebuilt and the containers DO NOT EXIST YET; focus
        // silently goes nowhere and the next key never reaches the view. A test caught this (after
        // changing file with Alt+↓, Alt+↑ was dead).
        // Handing focus over was deferred for the same reason on the commit list in Phase 03.
        Dispatcher.UIThread.Post(
            () => list.ContainerFromIndex(Math.Max(0, list.SelectedIndex))?.Focus(),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Copies the selected lines to the clipboard; with no selection, the whole file.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>MEASURED:</b> in Avalonia 12 there is <b>no</b> <c>IClipboard.GetTextAsync</c>; reading
    /// goes through the <c>TryGetTextAsync()</c> extension, and <c>TryGetDataAsync()</c> takes no
    /// parameters and returns an <c>IAsyncDataTransfer</c> (the same change as in the drag-and-drop
    /// API). The write side is the <c>SetTextAsync</c> extension and <b>works headless too</b>, so
    /// copying can be verified by a test.
    /// </remarks>
    private async Task CopyAsync(DiffViewModel model, DiffCopyMode mode)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard is null)
        {
            return;
        }

        string text = model.CopyText(mode, SelectedLineIndices());

        if (text.Length > 0)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private IReadOnlyList<int> SelectedLineIndices()
    {
        ListBox list = ActiveList;

        if (list.SelectedItems is not { Count: > 0 } selected)
        {
            return [];
        }

        List<int> indices = [];

        foreach (object? item in selected)
        {
            int index = list.Items.IndexOf(item);

            if (index >= 0)
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    // ---- P05-T10: partial staging and the context menu ----

    private void OnStageLinesClick(object? sender, RoutedEventArgs e) =>
        Apply(model => model.StageSelectionAsync(SelectedLineIndices()));

    private void OnUnstageLinesClick(object? sender, RoutedEventArgs e) =>
        Apply(model => model.UnstageSelectionAsync(SelectedLineIndices()));

    // P05-T15: destructive — confirmation and backup live on the WorkingTreeViewModel side.
    private void OnResetLinesClick(object? sender, RoutedEventArgs e) =>
        Apply(model => model.DiscardSelectionAsync(SelectedLineIndices()));

    private void OnCopyCodeClick(object? sender, RoutedEventArgs e) => Copy(DiffCopyMode.Code);

    private void OnCopyPatchClick(object? sender, RoutedEventArgs e) => Copy(DiffCopyMode.Patch);

    private void OnCopyNewClick(object? sender, RoutedEventArgs e) => Copy(DiffCopyMode.NewVersion);

    private void OnCopyOldClick(object? sender, RoutedEventArgs e) => Copy(DiffCopyMode.OldVersion);

    private void Copy(DiffCopyMode mode)
    {
        if (Model is { } model)
        {
            _ = CopyAsync(model, mode);
        }
    }

    private void Apply(Func<DiffViewModel, Task> operation)
    {
        if (Model is { } model)
        {
            _ = operation(model);
        }
    }
}
