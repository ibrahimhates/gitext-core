namespace GitExt.UI.Commands;

/// <summary>
/// Command ids (P08-T01).
/// </summary>
/// <remarks>
/// 🔴 <b>These strings never change.</b> Shortcuts the user has reassigned are stored in the
/// settings file under these ids; renaming an id <b>silently</b> resets the shortcut assigned
/// to that command back to the default — the user sees no error, the shortcut simply stops
/// working one day. If a command is removed its id is retired too, and is <b>never reused</b>
/// (the same rule as for task ids, ROADMAP § 2).
/// </remarks>
public static class CommandIds
{
    // ---- Depo
    public const string RepositoryOpen = "repository.open";
    public const string RepositoryClose = "repository.close";
    public const string RepositoryRefresh = "repository.refresh";
    public const string RepositoryRemotes = "repository.remotes";

    // ---- Commit
    public const string CommitShow = "commit.show";

    // ---- Uzak
    public const string RemotePull = "remote.pull";
    public const string RemotePush = "remote.push";

    // ---- Dal
    public const string BranchCreate = "branch.create";
    public const string BranchDelete = "branch.delete";
    public const string BranchCheckout = "branch.checkout";
    public const string BranchMerge = "branch.merge";

    // ---- History
    public const string HistoryRebase = "history.rebase";
    public const string HistoryCherryPick = "history.cherryPick";
    public const string HistoryRevert = "history.revert";
    public const string HistoryReset = "history.reset";
    public const string HistoryReflog = "history.reflog";
    public const string TagCreate = "tag.create";
    public const string StashManage = "stash.manage";

    // ---- View
    public const string ViewToggleLeftPanel = "view.toggleLeftPanel";
    public const string ViewToggleBottomPanel = "view.toggleBottomPanel";
    public const string ViewFocusLeftPanel = "view.focusLeftPanel";
    public const string ViewFocusCommitList = "view.focusCommitList";
    public const string ViewFocusCommitDetails = "view.focusCommitDetails";
    public const string ViewFocusDiff = "view.focusDiff";
    public const string ViewNextPanel = "view.nextPanel";
    public const string ViewPreviousPanel = "view.previousPanel";

    // ---- Tools / help
    public const string ToolsCommandLog = "tools.commandLog";

    /// <summary>
    /// Performance diagnostics panel (P09-T03).
    /// </summary>
    /// <remarks>
    /// <b>Not</b> in the menu: noise in daily use. It opens only via the shortcut and the
    /// command palette — reachable enough to be talked through with a user who reports "slow",
    /// hidden enough not to be seen otherwise.
    /// </remarks>
    public const string ToolsDiagnostics = "tools.diagnostics";

    public const string ToolsSettings = "tools.settings";
    public const string ToolsCommandPalette = "tools.commandPalette";
    public const string HelpShortcuts = "help.shortcuts";
    public const string HelpAbout = "help.about";
    public const string AppExit = "app.exit";

    // ---- Commit listesi
    public const string CommitListFind = "commitList.find";
    public const string CommitListGoToParent = "commitList.goToParent";
    public const string CommitListGoToChild = "commitList.goToChild";
    public const string CommitListPageDown = "commitList.pageDown";
    public const string CommitListPageUp = "commitList.pageUp";
    public const string CommitListCompareToWorkingDirectory = "commitList.compareToWorkingDirectory";
    public const string CommitListCompareSelected = "commitList.compareSelected";

    // ---- Diff
    public const string DiffFind = "diff.find";
    public const string DiffFindNext = "diff.findNext";
    public const string DiffFindPrevious = "diff.findPrevious";
    public const string DiffNextChange = "diff.nextChange";
    public const string DiffPreviousChange = "diff.previousChange";
    public const string DiffNextHunk = "diff.nextHunk";
    public const string DiffPreviousHunk = "diff.previousHunk";
    public const string DiffNextFile = "diff.nextFile";
    public const string DiffPreviousFile = "diff.previousFile";
    public const string DiffStageLines = "diff.stageLines";
    public const string DiffUnstageLines = "diff.unstageLines";
    public const string DiffResetLines = "diff.resetLines";
    public const string DiffCopyCode = "diff.copyCode";
    public const string DiffCopyPatch = "diff.copyPatch";

    // ---- Working tree
    public const string WorkingTreeToggleStage = "workingTree.toggleStage";
    public const string WorkingTreeStageAll = "workingTree.stageAll";
    public const string WorkingTreeUnstageAll = "workingTree.unstageAll";
    public const string WorkingTreeCommit = "workingTree.commit";

    // ---- Dal paneli
    public const string RefTreeDelete = "refTree.delete";
    public const string RefTreeRename = "refTree.rename";
}
