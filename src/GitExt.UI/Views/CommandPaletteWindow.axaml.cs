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

        // Tünelleme: ok tuşları ve Enter arama kutusundayken de listeyi sürmeli. Kabarma
        // fazında `TextBox` onları kendi işine alırdı ve kullanıcı yazarken listede
        // gezinemezdi — paletin tek kullanım biçimi tam da bu.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        Opened += OnOpened;
    }

    private CommandPaletteViewModel? Model => DataContext as CommandPaletteViewModel;

    /// <summary>Paleti sahibinin üstünde modal açar.</summary>
    internal static Task ShowAsync(CommandPaletteViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        CommandPaletteWindow window = new() { DataContext = model };

        return window.ShowDialog(owner);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Odak arama kutusuna: palet açıldığı anda yazmaya başlanabilmeli.
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
                // Pencere yalnızca komut GERÇEKTEN çalıştıysa kapanıyor. Çalıştırılamayan
                // bir komutta kapanmak, kullanıcıya "oldu" izlenimi verirdi.
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
