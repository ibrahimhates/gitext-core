using Avalonia.Controls;
using Avalonia.Threading;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The process window — GitExtensions' <c>FormStatus</c> / <c>FormProcess</c>.
/// </summary>
/// <remarks>
/// <para>
/// Modal, as it is there: while a write operation runs, the repository underneath the rest of the
/// UI is changing, and a second operation started on top of it collides over <c>index.lock</c>.
/// </para>
/// <para>
/// The window will not close while the command is running. <c>FormStatus</c> strips the close
/// button off the title bar for the same reason (<c>CP_NOCLOSE_BUTTON</c>): a git process left
/// running with nothing watching it finishes into a window that is gone, and leaves its lock file
/// behind.
/// </para>
/// </remarks>
public partial class ProcessDialogWindow : Window
{
    private ProcessDialogViewModel? _model;

    public ProcessDialogWindow()
    {
        InitializeComponent();

        Closing += OnClosing;
    }

    /// <summary>Runs the operation in a modal window and reports whether it succeeded.</summary>
    internal static async Task<bool> ShowAsync(ProcessDialogViewModel model, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(model);

        ProcessDialogWindow window = new() { DataContext = model };
        window.Attach(model);

        if (owner is null)
        {
            window.Show();
            await model.StartAsync().ConfigureAwait(true);
            window.Close();
        }
        else
        {
            Dispatcher.UIThread.Post(() => _ = model.StartCommand.ExecuteAsync(null));
            await window.ShowDialog(owner).ConfigureAwait(true);
        }

        return model.State is ProcessDialogState.Success;
    }

    private void Attach(ProcessDialogViewModel model)
    {
        _model = model;
        model.CloseRequested += OnCloseRequested;
        Closed += (_, _) => model.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_model is { CanClose: false })
        {
            e.Cancel = true;
        }
    }
}
