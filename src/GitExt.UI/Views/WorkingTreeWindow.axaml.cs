using Avalonia.Controls;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The commit screen (P05-T09).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormCommit</c> and it opens <b>modally</b>
/// (<c>GitUICommands.StartCommitDialog</c> → <c>form.ShowDialog(owner)</c>). How it opens is followed
/// just like the layout (CLAUDE.md § 9) — and modal is right anyway: the user changes the index on
/// this screen, and the commit list behind it would be showing the old state.
/// </remarks>
public partial class WorkingTreeWindow : Window
{
    public WorkingTreeWindow()
    {
        InitializeComponent();

        // 🔑 The draft is written DEFINITIVELY as the window closes (P05-T13). The delayed save (750 ms)
        // may not have run yet, and the last line the user typed — the very place they left off — would
        // be lost.
        Closing += (_, _) =>
        {
            if (DataContext is WorkingTreeViewModel model)
            {
                _ = model.Message.FlushDraftAsync();

                // The watcher lives for the lifetime of the application while this window closes:
                // unless the subscription is released, `git status` would carry on running for a closed
                // screen (P05-T14).
                model.Dispose();
            }
        };
    }

    /// <summary>Opens the commit screen <b>modally</b> above its owner.</summary>
    internal static Task Open(
        WorkingTreeViewModel viewModel,
        Window owner,
        ICommandRegistry? registry = null,
        Action? solveConflicts = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(owner);

        WorkingTreeWindow window = new() { DataContext = viewModel };

        WorkingTreeView view = window.GetControl<WorkingTreeView>("Files");

        if (registry is not null)
        {
            view.AttachShortcuts(registry);
        }

        // The conflict strip opens the SAME screen the main window's banner opens (P12-T16); the
        // commit screen does not start a second flow of its own.
        view.SolveConflicts = solveConflicts;

        // The confirmation dialog will open above this window; the owner window is only known here
        // (P05-T15).
        viewModel.Confirmer = new DialogConfirmer(window);

        return window.ShowDialog(owner);
    }

    /// <summary>
    /// The implementation that asks for confirmation with a real dialog (P05-T15).
    /// </summary>
    private sealed class DialogConfirmer : IDestructiveActionConfirmer
    {
        private readonly Window _owner;

        public DialogConfirmer(Window owner) => _owner = owner;

        public Task<ResetChangesDecision> ConfirmResetAsync(ResetChangesRequest request) =>
            ResetChangesDialog.ShowAsync(request, _owner);
    }
}
