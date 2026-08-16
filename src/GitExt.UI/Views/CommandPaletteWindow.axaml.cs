using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Komut paleti penceresi (P08-T04).
/// </summary>
public partial class CommandPaletteWindow : Window
{
    public CommandPaletteWindow()
    {
        InitializeComponent();

        // Tunnelling: the arrow keys and Enter have to drive the list even while in the search box.
        // In the bubbling phase the `TextBox` would take them for its own purposes and the user could
        // not move through the list while typing — which is the palette's only mode of use.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        Opened += OnOpened;
    }

    private CommandPaletteViewModel? Model => DataContext as CommandPaletteViewModel;

    /// <summary>Opens the palette modally above its owner.</summary>
    internal static Task ShowAsync(CommandPaletteViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        CommandPaletteWindow window = new() { DataContext = model };

        return window.ShowDialog(owner);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Focus goes to the search box: typing must be possible the moment the palette opens.
        QueryBox.Focus();

        Dispatcher.UIThread.Post(() => QueryBox.SelectAll(), DispatcherPriority.Loaded);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                model.MoveSelection(1);
                ScrollSelectionIntoView();
                e.Handled = true;
                break;

            case Key.Up:
                model.MoveSelection(-1);
                ScrollSelectionIntoView();
                e.Handled = true;
                break;

            case Key.Enter:
                // The window closes only when the command ACTUALLY ran. Closing on a command that
                // cannot run would give the user the impression it had happened.
                if (model.RunSelected())
                {
                    Close();
                }

                e.Handled = true;
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void ScrollSelectionIntoView()
    {
        if (Model is { SelectedIndex: >= 0 } model)
        {
            ResultList.ScrollIntoView(model.SelectedIndex);
        }
    }
}
