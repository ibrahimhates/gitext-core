using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The branch panel (P06-T13) and its context menu (P06-T14).
/// </summary>
/// <remarks>
/// Double-clicks and menu items are handled here because what the selected node is has to be taken
/// into account: nothing should happen on a folder or a heading, otherwise the user would change
/// branch by accident while moving through the tree.
/// </remarks>
public partial class RefTreeView : UserControl
{
    /// <summary>The side that takes on the checkout request.</summary>
    internal Func<string, Task>? Checkout { get; set; }

    /// <summary>The side that invokes the main window's commands.</summary>
    internal MainWindowViewModel? Commands { get; set; }

    /// <summary>The side that takes on a drag-and-drop merge (P06-T15).</summary>
    internal Func<string, string, Task>? MergeDropped { get; set; }

    /// <summary>The name of the branch being dragged; empty when there is no drag.</summary>
    private string _dragged = string.Empty;

    private ShortcutDispatcher? _dispatcher;

    /// <summary>
    /// Binds the context shortcuts to the command registry (P08-T01).
    /// </summary>
    /// <remarks>
    /// <c>Delete</c> and <c>F2</c> are the same as in GitExtensions' <c>RepoObjectsTree</c>
    /// (<c>Command.Delete</c>, <c>Command.Rename</c>). Both are <b>bare keys</b>, so they apply only in
    /// this panel's context — were they global they would seize the delete key in every text box
    /// (P08-T00/M11).
    /// </remarks>
    public ShortcutDispatcher AttachShortcuts(ICommandRegistry registry)
    {
        ShortcutDispatcher dispatcher = new(registry, CommandContext.RefTree);

        dispatcher.Bind(CommandIds.RefTreeDelete, () => Invoke(m => m.DeleteBranchCommand));
        dispatcher.Bind(CommandIds.RefTreeRename, () => Invoke(m => m.RenameBranchCommand));

        _dispatcher = dispatcher;

        // Tunnelling: the tree can swallow a bubbling key for its own navigation.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        return dispatcher;
    }

    /// <summary>Focuses the panel (P08-T05).</summary>
    public bool FocusPanel() => RefTree.Focus();

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // While the rename box is open in the tree the shortcuts are disabled: `Delete` deletes text
        // there, not a branch.
        if (e.Source is TextBox)
        {
            return;
        }

        _dispatcher?.Handle(e);
    }

    private bool Invoke(Func<MainWindowViewModel, IAsyncRelayCommand> select)
    {
        if (Commands is not { } model)
        {
            return false;
        }

        IAsyncRelayCommand command = select(model);

        if (!command.CanExecute(null))
        {
            // Swallowing the key for a command that cannot run would be a silent wall to the user.
            return false;
        }

        _ = command.ExecuteAsync(null);

        return true;
    }

    /// <summary>The smallest movement required for a drag to start.</summary>
    /// <remarks>
    /// 🔑 Without a threshold, even the slightest tremor during a single click would count as a drag —
    /// the plan's "an accidental drag is a real risk" item. The confirmation dialog is the second line
    /// of defence; this is the first.
    /// </remarks>
    private const double DragThreshold = 6;

    private Point _pressedAt;

    /// <remarks>
    /// ⚠️ Avalonia's <c>DoDragDropAsync</c> wants the argument of the <b>press</b> event (measured: it
    /// does not accept a <c>PointerEventArgs</c>). That is why the threshold is measured on the move
    /// event while the drag is started with the stored press argument.
    /// </remarks>
    private PointerPressedEventArgs? _pressed;

    public RefTreeView()
    {
        InitializeComponent();

        RefTree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        RefTree.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        RefTree.AddHandler(PointerReleasedEvent, (_, _) => _pressed = null, RoutingStrategies.Tunnel);
        RefTree.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        RefTree.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>The drag data's format — only data produced by this view is accepted.</summary>
    internal static DataFormat<string> BranchFormat { get; } = DataFormat.CreateStringApplicationFormat("gitext-branch");

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = e;
        _pressedAt = e.GetPosition(this);
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } pressed
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || Selected is not { IsCheckoutable: true } node)
        {
            return;
        }

        Point position = e.GetPosition(this);

        if (Math.Abs(position.X - _pressedAt.X) < DragThreshold
            && Math.Abs(position.Y - _pressedAt.Y) < DragThreshold)
        {
            return;
        }

        _pressed = null;
        _dragged = node.FullName;

        DataTransfer data = new();
        data.Add(DataTransferItem.Create(BranchFormat, node.FullName));

        await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Only branch data coming from this view is accepted; dragging files is the main window's job
        // (opening a repository) and must not get mixed up here.
        e.DragEffects = e.DataTransfer.Contains(BranchFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(BranchFormat)
            || MergeDropped is not { } merge
            || Selected is not { Kind: RefNodeKind.LocalBranch } target)
        {
            return;
        }

        string source = e.DataTransfer.TryGetValue(BranchFormat) ?? _dragged;

        if (source.Length > 0 && !string.Equals(source, target.FullName, StringComparison.Ordinal))
        {
            await merge(source, target.FullName);
        }
    }

    private RefNodeViewModel? Selected =>
        (DataContext as RefTreeViewModel)?.Selected;

    private async void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Selected is { IsCheckoutable: true } node && Checkout is { } checkout)
        {
            await checkout(node.FullName);
        }
    }

    private async void OnCheckoutClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { IsCheckoutable: true } node && Checkout is { } checkout)
        {
            await checkout(node.FullName);
        }
    }

    private async void OnCreateBranchClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.CreateBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnMergeClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.MergeCommand.ExecuteAsync(null);
        }
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.RenameBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.DeleteBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnPushClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.PushCommand.ExecuteAsync(null);
        }
    }

    private async void OnCopyNameClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { FullName.Length: > 0 } node
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(node.FullName);
        }
    }
}
