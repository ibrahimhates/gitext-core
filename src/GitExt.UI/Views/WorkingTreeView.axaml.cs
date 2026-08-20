using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The working directory view (P05-T09).
/// </summary>
/// <remarks>
/// <para>
/// The keyboard shortcuts now come from the command registry (P08-T01/T02): <c>Space</c> moves the
/// selected file to the other side, <c>Ctrl+Shift+S</c>/<c>Ctrl+Shift+U</c> move all of them, and
/// <c>Ctrl+Enter</c> commits. <c>F5</c> is now a <b>global</b> command.
/// </para>
/// <para>
/// ⚠️ <b>Focus is the most fragile part of this screen.</b> After every stage/unstage both lists are
/// rebuilt; the focused <c>ListBoxItem</c> ceases to exist and <b>the next key event never passes
/// through the view's tree</b> — the keyboard dies silently (measured in P04-T12). Giving focus back
/// is not enough on its own: the container <b>does not exist yet</b> at that moment and
/// <c>ContainerFromIndex</c> returns <see langword="null"/>, which is why it is deferred with
/// <see cref="DispatcherPriority.Loaded"/>.
/// </para>
/// </remarks>
public partial class WorkingTreeView : UserControl
{
    public WorkingTreeView()
    {
        InitializeComponent();

        // Tunnelling: the inner `ScrollViewer` swallows the bubbling key event (measured in Phase 03).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        TrackCaret();
    }

    private ShortcutDispatcher? _dispatcher;

    private ICommandRegistry? _registry;

    private WorkingTreeViewModel? ViewModel => DataContext as WorkingTreeViewModel;

    /// <summary>The list the user touched last.</summary>
    private ListBox ActiveList =>
        ViewModel?.IsStagedListActive == true ? StagedList : UnstagedList;

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        // With nothing focused, key events NEVER pass through this tree and the shortcuts silently
        // do not work (measured in Phase 03).
        FocusActiveList();
    }

    /// <summary>
    /// The focused list becomes the "active" one; the diff follows it.
    /// </summary>
    /// <remarks>
    /// Both lists can hold a selection at the same time. Tying the active list to focus keeps the
    /// place the user is looking at and the diff being shown in step — otherwise you would be
    /// looking at the unstaged diff while moving through the staged list.
    /// </remarks>
    private void OnUnstagedFocused(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } model)
        {
            model.IsStagedListActive = false;
        }
    }

    private void OnStagedFocused(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } model)
        {
            model.IsStagedListActive = true;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } model)
        {
            return;
        }

        // 🔴 While in a text box the list shortcuts MUST NOT RUN. Because we catch them in the
        // tunnelling phase, the `Space` key was staging a file instead of typing a space into the
        // commit message — a user writing a message would change the index without noticing.
        // The commit shortcut is a deliberate exception: a commit is given from the message box anyway.
        bool isCommit = _registry?.GetGesture(CommandIds.WorkingTreeCommit) is { } commit
            && commit.Matches(e);

        if (e.Source is TextBox && !isCommit)
        {
            return;
        }

        // F5 is GLOBAL now (P08-T02): catching it here as well would register the same gesture in two
        // contexts, and conflict detection would rightly report it.
        _dispatcher?.Handle(e);
    }

    /// <summary>Binds the context shortcuts to the command registry (P08-T01).</summary>
    public ShortcutDispatcher AttachShortcuts(ICommandRegistry registry)
    {
        _registry = registry;

        ShortcutDispatcher dispatcher = new(registry, CommandContext.WorkingTree);

        dispatcher.Bind(CommandIds.WorkingTreeToggleStage, () => WithModel(m =>
            m.IsStagedListActive ? m.UnstageSelectedAsync() : m.StageSelectedAsync()));

        dispatcher.Bind(CommandIds.WorkingTreeStageAll, () => WithModel(m => m.StageAllAsync()));
        dispatcher.Bind(CommandIds.WorkingTreeUnstageAll, () => WithModel(m => m.UnstageAllAsync()));

        // 🔴 MEASURED (P05-T12): a `TextBox` with `AcceptsReturn` on handles Ctrl+Enter like a plain
        // Enter and ADDS A LINE BREAK. Unless it is caught in the tunnelling phase and marked
        // `Handled`, committing would also put a blank line into the message.
        dispatcher.Bind(CommandIds.WorkingTreeCommit, () => WithModel(m => m.CommitAsync()));

        _dispatcher = dispatcher;

        return dispatcher;
    }

    private bool WithModel(Func<WorkingTreeViewModel, Task> operation)
    {
        if (ViewModel is not { } model)
        {
            return false;
        }

        Run(operation(model));

        return true;
    }

    /// <summary>
    /// Keeps the status bar's Ln/Col in step with the message box's caret (P12-T16).
    /// </summary>
    /// <remarks>
    /// <para>
    /// GitExtensions writes the caret position on the commit screen's status strip, and it is not
    /// decoration there either: the commit message convention (a subject line of at most ~50
    /// characters, a blank second line) is about lines and columns, and the box itself shows
    /// neither.
    /// </para>
    /// <para>
    /// ⚠️ The position is computed from <c>CaretIndex</c>, not from a "caret moved" event — there
    /// is none. `CaretIndex` is a property, so a property listener sees every move, including the
    /// ones made with the mouse.
    /// </para>
    /// </remarks>
    private void TrackCaret()
    {
        MessageBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.CaretIndexProperty && e.Property != TextBox.TextProperty)
            {
                return;
            }

            if (DataContext is not WorkingTreeViewModel model)
            {
                return;
            }

            string text = MessageBox.Text ?? string.Empty;
            int caret = Math.Clamp(MessageBox.CaretIndex, 0, text.Length);

            int line = 1;
            int lineStart = 0;

            for (int index = 0; index < caret; index++)
            {
                if (text[index] == '\n')
                {
                    line++;
                    lineStart = index + 1;
                }
            }

            model.CursorLine = line;
            model.CursorColumn = caret - lineStart + 1;
        };
    }

    /// <summary>
    /// Opens the conflict resolver (P12-T16).
    /// </summary>
    /// <remarks>
    /// The same screen the main window's banner opens — the staging view does not start a second
    /// flow of its own.
    /// </remarks>
    private void OnSolveConflictsClick(object? sender, RoutedEventArgs e) => SolveConflicts?.Invoke();

    /// <summary>Shows the conflict resolver; supplied by the window that hosts this view.</summary>
    public Action? SolveConflicts { get; set; }

    private void OnStageClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.StageSelectedAsync());

    private void OnStageAllClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.StageAllAsync());

    private void OnUnstageClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.UnstageSelectedAsync());

    private void OnUnstageAllClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.UnstageAllAsync());

    /// <summary>
    /// Runs the operation and gives focus back to the list <b>after it finishes</b>.
    /// </summary>
    private void Run(Task? operation)
    {
        if (operation is null)
        {
            return;
        }

        _ = RunCoreAsync(operation);
    }

    private async Task RunCoreAsync(Task operation)
    {
        await operation.ConfigureAwait(true);

        FocusActiveList();
    }

    /// <summary>
    /// Focuses the selected row in the active list.
    /// </summary>
    /// <remarks>
    /// The container may not exist yet (the list was just rebuilt); in that case it is deferred with
    /// <see cref="DispatcherPriority.Loaded"/>. The first attempt was failing silently.
    /// </remarks>
    private void FocusActiveList()
    {
        if (TryFocusActiveList())
        {
            return;
        }

        Dispatcher.UIThread.Post(() => TryFocusActiveList(), DispatcherPriority.Loaded);
    }

    private bool TryFocusActiveList()
    {
        ListBox list = ActiveList;

        if (list.SelectedIndex < 0)
        {
            // With no selection the list box itself cannot take focus (`ListBox.Focusable` is false);
            // focusing the view itself still keeps the shortcuts working.
            return Focus();
        }

        if (list.ContainerFromIndex(list.SelectedIndex) is not Control container)
        {
            return false;
        }

        list.ScrollIntoView(list.SelectedIndex);

        return container.Focus();
    }

    // ---- P05-T12: commit paneli ----

    private void OnCommitClick(object? sender, RoutedEventArgs e) => Run(ViewModel?.CommitAsync());

    // ---- P05-T15: confirmation and the safety net ----

    private void OnResetAllClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.ResetChangesAsync(DiscardScope.All));

    private void OnResetUnstagedClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.ResetChangesAsync(DiscardScope.UnstagedOnly));

    private void OnUndoResetClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.UndoResetAsync());

    private void OnDismissResetNoticeClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.ClearResetNotice();

    // ---- P05-T13: message helpers ----

    /// <summary>
    /// Reads the messages and builds the items as the history menu opens.
    /// </summary>
    /// <remarks>
    /// The items are not bound with <c>ItemsSource</c>: inside the <see cref="MenuFlyout"/>, fixed
    /// items (<i>only my messages</i> plus a separator) sit alongside data-bound ones, and a single
    /// <c>ItemsSource</c> cannot supply both. The fixed items are <b>preserved</b> and the history
    /// rows are rebuilt on every open.
    /// </remarks>
    private async void OnMessageHistoryOpening(object? sender, EventArgs e)
    {
        if (ViewModel is not { } model)
        {
            return;
        }

        // Names inside a flyout are NOT GENERATED AS FIELDS in the code-behind (a separate name
        // scope); the menu is reached through the button itself.
        if (MessageHistoryButton.Flyout is not MenuFlyout flyout)
        {
            return;
        }

        await model.Message.LoadRecentAsync().ConfigureAwait(true);

        RebuildHistoryMenu(flyout, model.Message);
    }

    private static void RebuildHistoryMenu(MenuFlyout flyout, CommitMessageViewModel message)
    {
        List<object> items = [.. flyout.Items.OfType<object>()];

        // Everything after the separator is history; it is rebuilt from scratch on every open.
        int separator = items.FindIndex(item => item is Separator);

        if (separator >= 0 && items.Count > separator + 1)
        {
            items.RemoveRange(separator + 1, items.Count - separator - 1);
        }

        foreach (CommitMessageHistoryItem entry in message.RecentMessages)
        {
            items.Add(new MenuItem
            {
                Header = entry.Label,
                Command = entry.ApplyCommand,
                CommandParameter = entry,

                // The full message goes in the tooltip: only the first line shows in the menu, and the
                // user must be able to see what they are getting before picking a message with a body.
                [ToolTip.TipProperty] = entry.Message,
            });
        }

        flyout.Items.Clear();

        foreach (object item in items)
        {
            flyout.Items.Add(item);
        }
    }

    private void OnApplyTemplateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } model)
        {
            _ = model.Message.ApplyTemplateAsync();
        }
    }
}
