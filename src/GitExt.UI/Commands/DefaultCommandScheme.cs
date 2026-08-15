using Avalonia.Input;
using GitExt.UI.Localization;

namespace GitExt.UI.Commands;

/// <summary>
/// The default command and shortcut scheme (P08-T02).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This scheme FREEZES at <c>v1.0.0</c></b> (ADR-0006): after that, changing a shortcut
/// requires a MAJOR release. That is why every assignment either comes from GitExtensions or
/// is written down as a deviation <b>with its rationale</b>.
/// </para>
/// <para>
/// <b>Source:</b> GitExtensions <c>src/app/GitUI/Hotkey/HotkeySettingsManager.cs</c> →
/// <c>CreateDefaultSettings()</c>. There the shortcuts are grouped by form/control
/// (<c>FormBrowse</c>, <c>RevisionGridControl</c>, <c>FileViewer</c>,
/// <c>RevisionDiffControl</c>, <c>RepoObjectsTree</c>) — that is the counterpart of our
/// <see cref="CommandContext"/>. The same gesture doing different work in two groups is
/// routine there too (<c>Ctrl+D</c>, <c>S</c>, <c>F5</c>), so the rule
/// "a different context is not a conflict" holds in GitExtensions as well.
/// </para>
/// <para>
/// <b>Deliberate deviations</b> (nothing was copied blindly):
/// </para>
/// <list type="bullet">
///   <item>
/// <b>Three levels of navigation in the diff.</b> GitExtensions' <c>FileViewer</c> only
///     has "next change". We navigate change / hunk / file separately:
///     <c>Alt+↓↑</c> change (same as GitExtensions), <c>Ctrl+PgDn/PgUp</c> hunk,
///     <c>Alt+→←</c> file. We are a superset; GitExtensions' <c>Alt+→←</c>
///     (<c>NextOccurrence</c>) does not exist here, so it is free.
///   </item>
///   <item>
/// <b>Search in the commit list is <c>Ctrl+F</c>.</b> In GitExtensions jumping to a SHA is
///     <c>Ctrl+Shift+G</c>. <c>Ctrl+F</c> is universal for "find", and the plan explicitly
///     put it on the "keep what is familiar" list.
///   </item>
///   <item>
/// <b>Stage/unstage all is <c>Ctrl+Shift+S</c> / <c>Ctrl+Shift+U</c>.</b> GitExtensions
///     uses <c>Ctrl+Space</c> in <c>FormBrowse</c> for <i>open the commit screen</i>; that is
///     the more frequent operation, so <c>Ctrl+Space</c> was given to it.
///   </item>
///   <item>
/// <b><c>Ctrl+Q</c> quit.</b> Not in GitExtensions; standard on Linux desktops.
///   </item>
///   <item>
/// <b><c>Ctrl+Shift+P</c> command palette</b> and <b><c>F1</c> shortcut list</b>: no
///     counterpart in GitExtensions, both are discoverability tools this project adds.
///     So is <c>F6</c> panel navigation — MEASURED in P08-T00/M09, <c>F6</c> is free in Avalonia.
///   </item>
/// </list>
/// <para>
/// <b>Deliberately left without a default</b> (reachable from the menu and the palette):
/// destructive or rare operations — cherry-pick, revert, reset, branch deletion, remote
/// management. Binding a destructive operation to a key that can be pressed by accident
/// would mean testing the undo path every single time.
/// </para>
/// </remarks>
public static class DefaultCommandScheme
{
    private static readonly CommandDefinition[] All =
    [
        // ------------------------------------------------------------------ Global
        Define(CommandIds.RepositoryOpen, Loc.T("default_command_scheme.open_repository"), CommandCategory.Repository,
            CommandContext.Global, Key.O, KeyModifiers.Control),
        Define(CommandIds.RepositoryClose, "Depoyu kapat", CommandCategory.Repository,
            CommandContext.Global, Key.W, KeyModifiers.Control),
        Define(CommandIds.RepositoryRefresh, "Yenile", CommandCategory.Repository,
            CommandContext.Global, Key.F5),
        Define(CommandIds.RepositoryRemotes, "Uzak depolar…", CommandCategory.Remote,
            CommandContext.Global, gesture: null),

        Define(CommandIds.CommitShow, "Commit…", CommandCategory.Commit,
            CommandContext.Global, Key.Space, KeyModifiers.Control),

        Define(CommandIds.RemotePull, "Pull/Fetch…", CommandCategory.Remote,
            CommandContext.Global, Key.Down, KeyModifiers.Control),
        Define(CommandIds.RemotePush, "Push…", CommandCategory.Remote,
            CommandContext.Global, Key.Up, KeyModifiers.Control),

        Define(CommandIds.BranchCreate, Loc.T("default_command_scheme.create_branch"), CommandCategory.Branch,
            CommandContext.Global, Key.B, KeyModifiers.Control),
        Define(CommandIds.BranchDelete, "Dal sil…", CommandCategory.Branch,
            CommandContext.Global, gesture: null),
        Define(CommandIds.BranchCheckout, Loc.T("default_command_scheme.check_out_branch"), CommandCategory.Branch,
            CommandContext.Global, Key.OemPeriod, KeyModifiers.Control),
        Define(CommandIds.BranchMerge, Loc.T("default_command_scheme.merge_branches"), CommandCategory.Branch,
            CommandContext.Global, Key.M, KeyModifiers.Control),

        Define(CommandIds.HistoryRebase, "Rebase…", CommandCategory.History,
            CommandContext.Global, Key.E, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.HistoryCherryPick, "Cherry pick…", CommandCategory.History,
            CommandContext.Global, gesture: null),
        Define(CommandIds.HistoryRevert, "Commit'i geri al (revert)…", CommandCategory.History,
            CommandContext.Global, gesture: null),
        Define(CommandIds.HistoryReset, Loc.T("default_command_scheme.reset_to_this_commit"), CommandCategory.History,
            CommandContext.Global, gesture: null),
        Define(CommandIds.HistoryReflog, Loc.T("default_command_scheme.show_reflog"), CommandCategory.History,
            CommandContext.Global, Key.L, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.TagCreate, Loc.T("default_command_scheme.create_tag"), CommandCategory.History,
            CommandContext.Global, Key.T, KeyModifiers.Control),
        Define(CommandIds.StashManage, Loc.T("default_command_scheme.manage_stashes"), CommandCategory.History,
            CommandContext.Global, Key.S, KeyModifiers.Control | KeyModifiers.Alt),

        Define(CommandIds.ViewToggleLeftPanel, Loc.T("default_command_scheme.toggle_the_left_panel"), CommandCategory.View,
            CommandContext.Global, Key.C, KeyModifiers.Control | KeyModifiers.Alt),
        Define(CommandIds.ViewToggleBottomPanel, Loc.T("default_command_scheme.toggle_the_bottom_panel"), CommandCategory.View,
            CommandContext.Global, Key.D, KeyModifiers.Control | KeyModifiers.Alt),
        Define(CommandIds.ViewFocusLeftPanel, "Sol panele odaklan", CommandCategory.Navigation,
            CommandContext.Global, Key.D0, KeyModifiers.Control),
        Define(CommandIds.ViewFocusCommitList, "Commit listesine odaklan", CommandCategory.Navigation,
            CommandContext.Global, Key.D1, KeyModifiers.Control),
        Define(CommandIds.ViewFocusCommitDetails, Loc.T("default_command_scheme.focus_the_commit_details"), CommandCategory.Navigation,
            CommandContext.Global, Key.D2, KeyModifiers.Control),
        Define(CommandIds.ViewFocusDiff, "Diff'e odaklan", CommandCategory.Navigation,
            CommandContext.Global, Key.D3, KeyModifiers.Control),
        Define(CommandIds.ViewNextPanel, "Sonraki panel", CommandCategory.Navigation,
            CommandContext.Global, Key.F6),
        Define(CommandIds.ViewPreviousPanel, Loc.T("default_command_scheme.previous_panel"), CommandCategory.Navigation,
            CommandContext.Global, Key.F6, KeyModifiers.Shift),

        Define(CommandIds.ToolsCommandLog, Loc.T("default_command_scheme.git_command_log"), CommandCategory.Tools,
            CommandContext.Global, Key.D9, KeyModifiers.Control),
        // Ctrl+Shift+F12: diagnostics panel (P09-T03). Not in the menu, on purpose — the shortcut
        // and the command palette are access enough; staying invisible in daily use was preferred.
        //
        // 🔴 The first pick, Ctrl+Shift+D, collided with "Compare the two selected commits";
        // the conflict test caught it. Left silent, one of the two commands would be a dead key.
        Define(CommandIds.ToolsDiagnostics, Loc.T("default_command_scheme.performance_diagnostics"), CommandCategory.Tools,
            CommandContext.Global, Key.F12, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.ToolsSettings, "Ayarlar…", CommandCategory.Tools,
            CommandContext.Global, Key.OemComma, KeyModifiers.Control),
        Define(CommandIds.ToolsCommandPalette, "Command palette", CommandCategory.Tools,
            CommandContext.Global, Key.P, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.HelpShortcuts, Loc.T("default_command_scheme.keyboard_shortcuts"), CommandCategory.Help,
            CommandContext.Global, Key.F1),
        Define(CommandIds.HelpAbout, Loc.T("default_command_scheme.about"), CommandCategory.Help,
            CommandContext.Global, gesture: null),
        Define(CommandIds.AppExit, Loc.T("default_command_scheme.exit"), CommandCategory.Repository,
            CommandContext.Global, Key.Q, KeyModifiers.Control),

        // ------------------------------------------------------------- Commit listesi
        Define(CommandIds.CommitListFind, "Commit ara (SHA veya mesaj)", CommandCategory.Navigation,
            CommandContext.CommitList, Key.F, KeyModifiers.Control),
        Define(CommandIds.CommitListGoToParent, "Ebeveyne git", CommandCategory.Navigation,
            CommandContext.CommitList, Key.P, KeyModifiers.Control),
        Define(CommandIds.CommitListGoToChild, Loc.T("default_command_scheme.go_to_child"), CommandCategory.Navigation,
            CommandContext.CommitList, Key.N, KeyModifiers.Control),
        Define(CommandIds.CommitListPageDown, Loc.T("default_command_scheme.one_page_down"), CommandCategory.Navigation,
            CommandContext.CommitList, Key.PageDown),
        Define(CommandIds.CommitListPageUp, Loc.T("default_command_scheme.one_page_up"), CommandCategory.Navigation,
            CommandContext.CommitList, Key.PageUp),
        Define(CommandIds.CommitListCompareToWorkingDirectory, Loc.T("default_command_scheme.compare_with_the_working_directory"),
            CommandCategory.History, CommandContext.CommitList, Key.D, KeyModifiers.Control),
        Define(CommandIds.CommitListCompareSelected, Loc.T("default_command_scheme.compare_the_two_selected_commits"),
            CommandCategory.History, CommandContext.CommitList,
            Key.D, KeyModifiers.Control | KeyModifiers.Shift),

        // ---------------------------------------------------------------------- Diff
        Define(CommandIds.DiffFind, "Diff'te bul", CommandCategory.Navigation,
            CommandContext.Diff, Key.F, KeyModifiers.Control),
        Define(CommandIds.DiffFindNext, "Sonrakini bul", CommandCategory.Navigation,
            CommandContext.Diff, Key.F3),
        Define(CommandIds.DiffFindPrevious, Loc.T("default_command_scheme.find_previous"), CommandCategory.Navigation,
            CommandContext.Diff, Key.F3, KeyModifiers.Shift),
        Define(CommandIds.DiffNextChange, Loc.T("default_command_scheme.next_change"), CommandCategory.Navigation,
            CommandContext.Diff, Key.Down, KeyModifiers.Alt),
        Define(CommandIds.DiffPreviousChange, Loc.T("default_command_scheme.previous_change"), CommandCategory.Navigation,
            CommandContext.Diff, Key.Up, KeyModifiers.Alt),
        Define(CommandIds.DiffNextHunk, "Sonraki hunk", CommandCategory.Navigation,
            CommandContext.Diff, Key.PageDown, KeyModifiers.Control),
        Define(CommandIds.DiffPreviousHunk, Loc.T("default_command_scheme.previous_hunk"), CommandCategory.Navigation,
            CommandContext.Diff, Key.PageUp, KeyModifiers.Control),
        Define(CommandIds.DiffNextFile, "Sonraki dosya", CommandCategory.Navigation,
            CommandContext.Diff, Key.Right, KeyModifiers.Alt),
        Define(CommandIds.DiffPreviousFile, Loc.T("default_command_scheme.previous_file"), CommandCategory.Navigation,
            CommandContext.Diff, Key.Left, KeyModifiers.Alt),
        Define(CommandIds.DiffStageLines, Loc.T("default_command_scheme.stage_the_selected_lines"), CommandCategory.Commit,
            CommandContext.Diff, Key.S),
        Define(CommandIds.DiffUnstageLines, Loc.T("default_command_scheme.unstage_the_selected_lines"), CommandCategory.Commit,
            CommandContext.Diff, Key.U),
        Define(CommandIds.DiffResetLines, Loc.T("default_command_scheme.revert_the_selected_lines"), CommandCategory.Commit,
            CommandContext.Diff, Key.R),
        Define(CommandIds.DiffCopyCode, "Kodu kopyala", CommandCategory.Tools,
            CommandContext.Diff, Key.C, KeyModifiers.Control),
        Define(CommandIds.DiffCopyPatch, Loc.T("default_command_scheme.copy_the_patch"), CommandCategory.Tools,
            CommandContext.Diff, Key.C, KeyModifiers.Control | KeyModifiers.Shift),

        // ------------------------------------------------------------- Working tree
        Define(CommandIds.WorkingTreeToggleStage, Loc.T("default_command_scheme.stage_unstage_the_selection"),
            CommandCategory.Commit, CommandContext.WorkingTree, Key.Space),
        Define(CommandIds.WorkingTreeStageAll, Loc.T("default_command_scheme.stage_all"), CommandCategory.Commit,
            CommandContext.WorkingTree, Key.S, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.WorkingTreeUnstageAll, Loc.T("default_command_scheme.unstage_all"), CommandCategory.Commit,
            CommandContext.WorkingTree, Key.U, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.WorkingTreeCommit, "Commit et", CommandCategory.Commit,
            CommandContext.WorkingTree, Key.Enter, KeyModifiers.Control),

        // --------------------------------------------------------------- Dal paneli
        Define(CommandIds.RefTreeDelete, Loc.T("default_command_scheme.delete_the_selected_branch"), CommandCategory.Branch,
            CommandContext.RefTree, Key.Delete),
        Define(CommandIds.RefTreeRename, Loc.T("default_command_scheme.rename_the_selected_branch"), CommandCategory.Branch,
            CommandContext.RefTree, Key.F2),
    ];

    /// <summary>The whole scheme, in definition order.</summary>
    public static IReadOnlyList<CommandDefinition> Definitions => All;

    private static CommandDefinition Define(
        string id,
        string title,
        CommandCategory category,
        CommandContext context,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None) =>
        new(id, title, category, context, new KeyGesture(key, modifiers));

    private static CommandDefinition Define(
        string id,
        string title,
        CommandCategory category,
        CommandContext context,
        KeyGesture? gesture) =>
        new(id, title, category, context, gesture);
}
