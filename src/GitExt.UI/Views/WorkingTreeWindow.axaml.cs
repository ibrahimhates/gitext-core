using Avalonia.Controls;
using GitExt.UI.Commands;
using GitExt.UI.Localization;
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
        viewModel.Prompt = new DialogCommitPrompt(window);

        void OnCloseRequested(object? sender, EventArgs e) => window.Close();

        viewModel.CloseRequested += OnCloseRequested;
        window.Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;

        return window.ShowDialog(owner);
    }

    /// <summary>Asks the commit screen's questions in real dialogs (P14-T03).</summary>
    private sealed class DialogCommitPrompt : ICommitPrompt
    {
        private readonly Window _owner;

        public DialogCommitPrompt(Window owner) => _owner = owner;

        public async Task<AmendConfirmation> ConfirmAmendAsync()
        {
            ConfirmResult result = await ConfirmDialog.AskAsync(
                Loc.T("commit.commit"),
                Loc.T("commit.are_you_sure_you_want_to_amend_the_last_com"),
                _owner,
                suppressible: true).ConfigureAwait(true);

            return new AmendConfirmation
            {
                Confirmed = result.Confirmed,
                DoNotAskAgain = result.DoNotAskAgain,
            };
        }

        public Task<bool> ConfirmEmptyMergeCommitAsync() =>
            ConfirmDialog.ShowAsync(
                Loc.T("commit.commit"),
                Loc.T("commit.it_is_a_bad_practice_to_commit_an_empty_merg"),
                _owner);

        public async Task<NoStagedChangesChoice> AskNoStagedChangesAsync(bool canStageAll)
        {
            if (!canStageAll)
            {
                await ConfirmDialog
                    .ShowAsync(
                        Loc.T("commit.commit"),
                        Loc.T("commit.there_are_no_files_staged_for_this_commit"),
                        _owner)
                    .ConfigureAwait(true);

                return NoStagedChangesChoice.Cancel;
            }

            bool stage = await ConfirmDialog.ShowAsync(
                Loc.T("commit.commit"),
                Loc.T("commit.there_are_no_files_staged_for_this_commit_st"),
                _owner).ConfigureAwait(true);

            return stage ? NoStagedChangesChoice.StageAllAndCommit : NoStagedChangesChoice.Cancel;
        }

        public async Task ReportMergeConflictsAsync() =>
            await ConfirmDialog
                .ShowAsync(
                    Loc.T("commit.commit"),
                    Loc.T("commit.there_are_unresolved_merge_conflicts_resolve"),
                    _owner)
                .ConfigureAwait(true);

        public async Task ReportMissingCommitMessageAsync() =>
            await ConfirmDialog
                .ShowAsync(
                    Loc.T("commit.commit"),
                    Loc.T("commit.you_need_to_enter_a_commit_message"),
                    _owner)
                .ConfigureAwait(true);

        public Task<bool> ConfirmCommitValidationAsync(string question) =>
            ConfirmDialog.ShowAsync(Loc.T("commit.commit_validation"), question, _owner);

        public async Task<NotOnBranchChoice> AskNotOnBranchAsync()
        {
            bool go = await ConfirmDialog.ShowAsync(
                Loc.T("commit.not_on_a_branch"),
                Loc.T("commit.you_are_not_on_a_branch_this_commit_will_not"),
                _owner).ConfigureAwait(true);

            return go ? NotOnBranchChoice.Continue : NotOnBranchChoice.Cancel;
        }
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
