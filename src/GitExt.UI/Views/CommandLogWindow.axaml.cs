using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The git command log window (P06-T16).
/// </summary>
/// <remarks>
/// NOT modal: the user has to be able to keep the log open and carry on using the application — the
/// panel's whole point is watching what happens live.
/// </remarks>
public partial class CommandLogWindow : Window
{
    public CommandLogWindow()
    {
        InitializeComponent();
    }

    internal static Task ShowAsync(CommandLogViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        CommandLogWindow window = new() { DataContext = model };

        // The subscription is released when the window closes; otherwise the log would keep the
        // ViewModel alive forever.
        window.Closed += (_, _) => model.Dispose();

        window.Show(owner);

        return Task.CompletedTask;
    }
}
