using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

public enum RevisionGridMenuId
{
    None,
    ResetChanges,
    Commit,
    MarkRevisionAsBad,
    MarkRevisionAsGood,
    BisectSkipRevision,
    StopBisect,
    SepBisect,
    CopyToClipboard,
    SepCopy,
    ApplyStash,
    PopStash,
    DropStash,
    SepStash,
    CheckoutBranch,
    PushBranch,
    MergeBranch,
    RebaseOn,
    ResetCurrentBranchToHere,
    SepBranch,
    SelectInLeftPanel,
    CreateNewBranch,
    ResetAnotherBranchToHere,
    RenameBranch,
    DeleteBranch,
    SepBranchModification,
    CreateTag,
    DeleteTag,
    SepCommit,
    CheckoutRevision,
    RevertCommit,
    CherryPickCommit,
    ArchiveRevision,
    ManipulateCommit,
    SepCompare,
    Compare,
    SepNavigate,
    Navigate,
    View,
    RunScript,
    OpenBuildReport,
    OpenPullRequestPage,
    OtherActions,

    RebaseSelectedCommit,
    RebaseSelectedCommitInteractively,
    SepRebase,
    RebaseSelectedCommitWithAdvancedOptions,

    EditCommit,
    RewordCommit,
    FixupCommit,
    SquashCommit,
    AmendCommit,
    GetHelpOnHowToUseTheseFeatures,

    OpenCommitsWithDiffTool,
    SepCompareDropdown,
    CompareToBranch,
    CompareWithCurrentBranch,
    SelectAsBase,
    CompareToBase,
    CompareToWorkingDirectory,
    CompareSelectedCommits,

    CopyBranchName,
    CopyTagName,
    CopyCommitHash,
    CopyMessage,
    CopyAuthor,
    CopyDate,
    CopyAuthorDate,
    CopyCommitDate,
    CopyCaptionBranches,
    CopyCaptionTags,

    ToggleBetweenArtificialAndHeadCommits,
    GoToCurrentRevision,
    GoToCommit,
    GoToChildCommit,
    GoToParentCommit,
    GoToFirstParentCommit,
    GoToLastParentCommit,
    GoToMergeBaseCommit,
    NavigateBackward,
    NavigateForward,
    QuickSearch,
    QuickSearchPrevious,
    QuickSearchNext,

    ViewGroupBranches,
    ShowAllBranches,
    ShowCurrentBranchOnly,
    ShowFilteredBranches,
    ShowReflogReferences,
    AdvancedFilter,
    DrawNonRelativesGray,
    HighlightSelectedBranch,
    ViewGroupCommits,
    ShowArtificialCommits,
    ShowStashes,
    ShowGitNotes,
    ShowSessionCheckpoints,
    ViewGroupGridLabels,
    ShowRemoteBranches,
    ShowTags,
    ShowSuperprojectTags,
    ShowSuperprojectRemoteBranches,
    ShowSuperprojectBranches,
    ViewGroupGridInfo,
    ShowBuildStatusIcon,
    ShowBuildStatusText,
    ShowCommitMessageBody,
    ShowAuthorDate,
    ShowRelativeDate,
    ViewGroupColumns,
    ShowRevisionGraphColumn,
    ShowGitNotesColumn,
    ShowAuthorAvatarColumn,
    ShowAuthorNameColumn,
    ShowDateColumn,
    ShowIdColumn,
    ViewGroupSorting,
    AuthorDateSort,
    TopoOrder,
    ViewGroupSettingsPersistence,
    SaveAsDefault,

    DynamicRef,
    DynamicCommitHash,
    Separator,
}

/// <summary>Which branches the commit list is showing.</summary>
public enum RevisionGridBranchScope
{
    All,
    CurrentOnly,
    Filtered,
}

public enum RevisionGridRefKind
{
    LocalBranch,
    RemoteBranch,
    Tag,
}

public sealed record RevisionGridRef(
    string Name,
    RevisionGridRefKind Kind,
    bool IsCurrent = false,
    bool IsTrackedByLocalBranch = false)
{
    public bool IsRemote => Kind == RevisionGridRefKind.RemoteBranch;

    public bool IsTag => Kind == RevisionGridRefKind.Tag;
}

public enum RevisionGridMenuToggle
{
    None,
    Radio,
    Check,
}

public sealed record RevisionGridMenuEntry
{
    public required RevisionGridMenuId Id { get; init; }

    public string Header { get; init; } = string.Empty;

    public string? Gesture { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsHeading { get; init; }

    /// <summary>
    /// Is the entry ticked?
    /// </summary>
    /// <remarks>
    /// GitExtensions' <c>IsCheckedFunc</c>. The branch-scope entries are a set of three where
    /// exactly one is on; without the tick the user cannot tell which scope the list is showing.
    /// </remarks>
    public bool IsChecked { get; init; }

    public RevisionGridMenuToggle Toggle { get; init; }

    public bool IsVisible { get; init; } = true;

    public bool IsEnabled { get; init; } = true;

    public string? Argument { get; init; }

    public IReadOnlyList<RevisionGridMenuEntry> Children { get; init; } = [];

    public override string ToString() => IsSeparator ? "──" : Header;
}

public sealed record RevisionGridMenuContext
{
    public int SelectedCount { get; init; } = 1;

    public string CommitHash { get; init; } = string.Empty;

    public IReadOnlyList<RevisionGridRef> Refs { get; init; } = [];

    public string? CurrentBranch { get; init; }

    public bool IsArtificial { get; init; }

    public bool IsStash { get; init; }

    public bool IsAutostash { get; init; }

    public bool IsBareRepository { get; init; }

    public bool InTheMiddleOfBisect { get; init; }

    public bool HasBuildReport { get; init; }

    public bool HasPullRequest { get; init; }

    public bool HasUserScripts { get; init; }

    public bool HasLeftPanel { get; init; } = true;

    /// <summary>
    /// Has a commit been remembered with "Select as BASE to compare"?
    /// </summary>
    /// <remarks>
    /// GitExtensions enables <i>Compare to BASE</i> from
    /// <c>RevisionGridControl._baseCommitToCompare</c> — the entry keeps its place, it is only
    /// dead until a base is picked.
    /// </remarks>
    public bool HasBaseCommitToCompare { get; init; }

    public bool RefClicked { get; init; }

    public string? ClickedRef { get; init; }

    public bool ForceFullMenu { get; init; }

    /// <summary>Which branch scope the list is showing — the ticked entry of the three.</summary>
    public RevisionGridBranchScope BranchScope { get; init; } = RevisionGridBranchScope.All;

    public RevisionGridDisplay Display { get; init; } = new();

    public RevisionSortOrder SortOrder { get; init; } = RevisionSortOrder.GitDefault;

    public bool ShowReflogReferences { get; init; }
}

public static class RevisionGridContextMenu
{
    private static readonly RevisionGridMenuId[] _advanced =
    [
        RevisionGridMenuId.ResetAnotherBranchToHere,
        RevisionGridMenuId.CreateTag,
        RevisionGridMenuId.SepCommit,
        RevisionGridMenuId.CheckoutRevision,
        RevisionGridMenuId.RevertCommit,
        RevisionGridMenuId.CherryPickCommit,
        RevisionGridMenuId.ArchiveRevision,
        RevisionGridMenuId.ManipulateCommit,
        RevisionGridMenuId.SepNavigate,
        RevisionGridMenuId.Navigate,
        RevisionGridMenuId.View,
        RevisionGridMenuId.OpenBuildReport,
        RevisionGridMenuId.OpenPullRequestPage,
    ];

    private static readonly RevisionGridMenuId[] _unimplemented =
    [
        RevisionGridMenuId.ArchiveRevision,
        RevisionGridMenuId.RunScript,
        RevisionGridMenuId.OpenBuildReport,
        RevisionGridMenuId.OpenPullRequestPage,
        RevisionGridMenuId.OpenCommitsWithDiffTool,
        RevisionGridMenuId.ToggleBetweenArtificialAndHeadCommits,
        RevisionGridMenuId.QuickSearchPrevious,
        RevisionGridMenuId.QuickSearchNext,
        RevisionGridMenuId.AdvancedFilter,
        RevisionGridMenuId.HighlightSelectedBranch,
        RevisionGridMenuId.ShowArtificialCommits,
        RevisionGridMenuId.ShowStashes,
        RevisionGridMenuId.ShowGitNotes,
        RevisionGridMenuId.ShowSessionCheckpoints,
        RevisionGridMenuId.ShowSuperprojectTags,
        RevisionGridMenuId.ShowSuperprojectRemoteBranches,
        RevisionGridMenuId.ShowSuperprojectBranches,
        RevisionGridMenuId.ShowBuildStatusIcon,
        RevisionGridMenuId.ShowBuildStatusText,
        RevisionGridMenuId.ShowGitNotesColumn,
        RevisionGridMenuId.ShowAuthorAvatarColumn,
    ];

    /// <summary>
    /// The order of the entries, from GitExtensions <c>RevisionGridControl.Designer.cs</c>
    /// (<c>mainContextMenu.Items.AddRange</c>) — <b>copied, not chosen</b>.
    /// </summary>
    public static IReadOnlyList<RevisionGridMenuId> Order { get; } =
    [
        RevisionGridMenuId.MarkRevisionAsBad,
        RevisionGridMenuId.MarkRevisionAsGood,
        RevisionGridMenuId.BisectSkipRevision,
        RevisionGridMenuId.StopBisect,
        RevisionGridMenuId.SepBisect,
        RevisionGridMenuId.CopyToClipboard,
        RevisionGridMenuId.SepCopy,
        RevisionGridMenuId.ApplyStash,
        RevisionGridMenuId.PopStash,
        RevisionGridMenuId.DropStash,
        RevisionGridMenuId.SepStash,
        RevisionGridMenuId.CheckoutBranch,
        RevisionGridMenuId.PushBranch,
        RevisionGridMenuId.MergeBranch,
        RevisionGridMenuId.RebaseOn,
        RevisionGridMenuId.ResetCurrentBranchToHere,
        RevisionGridMenuId.SepBranch,
        RevisionGridMenuId.ResetChanges,
        RevisionGridMenuId.Commit,
        RevisionGridMenuId.CreateNewBranch,
        RevisionGridMenuId.ResetAnotherBranchToHere,
        RevisionGridMenuId.RenameBranch,
        RevisionGridMenuId.DeleteBranch,
        RevisionGridMenuId.SepBranchModification,
        RevisionGridMenuId.CreateTag,
        RevisionGridMenuId.DeleteTag,
        RevisionGridMenuId.SepCommit,
        RevisionGridMenuId.CheckoutRevision,
        RevisionGridMenuId.RevertCommit,
        RevisionGridMenuId.CherryPickCommit,
        RevisionGridMenuId.ArchiveRevision,
        RevisionGridMenuId.ManipulateCommit,
        RevisionGridMenuId.SepCompare,
        RevisionGridMenuId.Compare,
        RevisionGridMenuId.SepNavigate,
        RevisionGridMenuId.Navigate,
        RevisionGridMenuId.SelectInLeftPanel,
        RevisionGridMenuId.View,
        RevisionGridMenuId.RunScript,
        RevisionGridMenuId.OpenBuildReport,
        RevisionGridMenuId.OpenPullRequestPage,
        RevisionGridMenuId.OtherActions,
    ];

    public static IReadOnlyList<RevisionGridMenuEntry> Build(RevisionGridMenuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<RevisionGridRef> refs = Filter(context);

        List<RevisionGridRef> tags = [.. refs.Where(r => r.IsTag)];
        List<RevisionGridRef> allBranches = [.. refs.Where(r => !r.IsTag)];
        List<RevisionGridRef> noIdenticalRemotes =
            [.. allBranches.Where(b => !b.IsRemote || !b.IsTrackedByLocalBranch)];

        bool currentBranchPointsToRevision = !context.IsArtificial
            && context.CurrentBranch is { Length: > 0 } current
            && allBranches.Any(b => !b.IsRemote && b.Name == current);

        List<RevisionGridMenuEntry> deleteTag = [.. tags.Select(RefItem)];

        List<RevisionGridMenuEntry> selectInLeftPanel =
            [.. tags.Select(RefItem), .. allBranches.Select(RefItem)];

        List<RevisionGridMenuEntry> merge = [.. tags.Select(RefItem)];

        foreach (RevisionGridRef head in noIdenticalRemotes)
        {
            if (!head.IsRemote && head.Name == context.CurrentBranch)
            {
                continue;
            }

            merge.Add(RefItem(head));
        }

        if (merge.Count == 0 && !currentBranchPointsToRevision)
        {
            merge.Add(new RevisionGridMenuEntry
            {
                Id = RevisionGridMenuId.DynamicCommitHash,
                Header = Escape(context.CommitHash),
                Argument = context.CommitHash,
            });
        }

        List<RevisionGridMenuEntry> checkout = [];
        List<RevisionGridMenuEntry> deleteBranch = [];
        List<RevisionGridMenuEntry> rename = [];
        List<RevisionGridMenuEntry> push = [];
        bool isHeadOfCurrentBranch = false;
        bool localSeenBeforeRemote = false;

        foreach (RevisionGridRef head in allBranches)
        {
            if (!head.IsRemote)
            {
                if (head.Name == context.CurrentBranch)
                {
                    isHeadOfCurrentBranch = true;
                }
                else
                {
                    deleteBranch.Add(RefItem(head));
                }

                rename.Add(RefItem(head));
                push.Add(RefItem(head));
            }

            if (head.Name == context.CurrentBranch && !head.IsRemote)
            {
                continue;
            }

            if (!head.IsRemote)
            {
                localSeenBeforeRemote = true;
            }
            else if (localSeenBeforeRemote)
            {
                checkout.Add(SeparatorEntry());
                localSeenBeforeRemote = false;
            }

            checkout.Add(RefItem(head));
        }

        bool firstRemoteForDelete = true;

        foreach (RevisionGridRef head in allBranches.Where(b => b.IsRemote))
        {
            if (firstRemoteForDelete)
            {
                firstRemoteForDelete = false;

                if (deleteBranch.Count > 0)
                {
                    deleteBranch.Add(SeparatorEntry());
                }
            }

            deleteBranch.Add(RefItem(head));
        }

        bool bareOrArtificial = context.IsBareRepository || context.IsArtificial;
        bool showStash = !bareOrArtificial && context.IsStash;
        bool twoSelected = context.SelectedCount == 2;

        Dictionary<RevisionGridMenuId, bool> visible = new()
        {
            [RevisionGridMenuId.ResetChanges] = context.IsArtificial,
            [RevisionGridMenuId.Commit] = context.IsArtificial,
            [RevisionGridMenuId.MarkRevisionAsBad] = context.InTheMiddleOfBisect,
            [RevisionGridMenuId.MarkRevisionAsGood] = context.InTheMiddleOfBisect,
            [RevisionGridMenuId.BisectSkipRevision] = context.InTheMiddleOfBisect,
            [RevisionGridMenuId.StopBisect] = context.InTheMiddleOfBisect,
            [RevisionGridMenuId.CopyToClipboard] = !context.IsArtificial,
            [RevisionGridMenuId.ApplyStash] = showStash || (!bareOrArtificial && context.IsAutostash),
            [RevisionGridMenuId.PopStash] = showStash,
            [RevisionGridMenuId.DropStash] = showStash,
            [RevisionGridMenuId.CheckoutBranch] = !bareOrArtificial && checkout.Any(i => !i.IsSeparator),
            [RevisionGridMenuId.PushBranch] = !bareOrArtificial && push.Count > 0,
            [RevisionGridMenuId.MergeBranch] = !bareOrArtificial && merge.Count > 0,
            [RevisionGridMenuId.RebaseOn] = !bareOrArtificial,
            [RevisionGridMenuId.ResetCurrentBranchToHere] = !bareOrArtificial,
            [RevisionGridMenuId.SelectInLeftPanel] = context.HasLeftPanel && selectInLeftPanel.Count > 0,
            [RevisionGridMenuId.CreateNewBranch] = !bareOrArtificial,
            [RevisionGridMenuId.ResetAnotherBranchToHere] = !bareOrArtificial,
            [RevisionGridMenuId.RenameBranch] = rename.Count > 0,
            [RevisionGridMenuId.DeleteBranch] =
                deleteBranch.Any(i => !i.IsSeparator) && !context.IsBareRepository,
            [RevisionGridMenuId.CreateTag] = !context.IsArtificial,
            [RevisionGridMenuId.DeleteTag] = deleteTag.Count > 0,
            [RevisionGridMenuId.CheckoutRevision] = !bareOrArtificial,
            [RevisionGridMenuId.RevertCommit] = !bareOrArtificial,
            [RevisionGridMenuId.CherryPickCommit] = !bareOrArtificial,
            [RevisionGridMenuId.ArchiveRevision] = !context.IsArtificial,
            [RevisionGridMenuId.ManipulateCommit] = !bareOrArtificial,
            [RevisionGridMenuId.Compare] = true,
            [RevisionGridMenuId.Navigate] = true,
            [RevisionGridMenuId.View] = true,
            [RevisionGridMenuId.RunScript] = true,
            [RevisionGridMenuId.OpenBuildReport] = context.HasBuildReport,
            [RevisionGridMenuId.OpenPullRequestPage] = context.HasPullRequest,
        };

        bool focused = context.RefClicked && !context.ForceFullMenu;

        List<RevisionGridMenuEntry> top = [];
        List<RevisionGridMenuEntry> other = [];

        foreach (RevisionGridMenuId id in Order)
        {
            if (id == RevisionGridMenuId.OtherActions)
            {
                continue;
            }

            bool available = visible.GetValueOrDefault(id, true);
            bool shown = available
                || (id == RevisionGridMenuId.DeleteBranch && isHeadOfCurrentBranch);

            RevisionGridMenuEntry entry = IsSeparatorId(id)
                ? SeparatorEntry(id)
                : new RevisionGridMenuEntry
                {
                    Id = id,
                    Header = Text(id),
                    Gesture = Gesture(id),
                    IsVisible = shown,
                    IsEnabled = available && !_unimplemented.Contains(id),
                    Children = ChildrenOf(id),
                };

            (focused && _advanced.Contains(id) ? other : top).Add(entry);
        }

        if (focused)
        {
            top.Add(new RevisionGridMenuEntry
            {
                Id = RevisionGridMenuId.OtherActions,
                Header = Text(RevisionGridMenuId.OtherActions),
                Children = ApplySeparatorRules(other),
                IsEnabled = other.Any(e => !e.IsSeparator && e.IsEnabled),
            });
        }

        return ApplySeparatorRules(top);

        IReadOnlyList<RevisionGridMenuEntry> ChildrenOf(RevisionGridMenuId id) => id switch
        {
            RevisionGridMenuId.CopyToClipboard => CopyChildren(context, allBranches, tags),
            RevisionGridMenuId.CheckoutBranch => checkout,
            RevisionGridMenuId.PushBranch => push,
            RevisionGridMenuId.MergeBranch => merge,
            RevisionGridMenuId.RenameBranch => rename,
            RevisionGridMenuId.DeleteBranch => deleteBranch,
            RevisionGridMenuId.DeleteTag => deleteTag,
            RevisionGridMenuId.SelectInLeftPanel =>
                selectInLeftPanel.Count > 1 ? selectInLeftPanel : [],
            RevisionGridMenuId.RebaseOn => RebaseChildren(context),
            RevisionGridMenuId.ManipulateCommit => AdvancedChildren(),
            RevisionGridMenuId.Compare => CompareChildren(context, twoSelected),
            RevisionGridMenuId.Navigate => NavigateChildren(),
            RevisionGridMenuId.View => ViewChildren(context),
            _ => [],
        };
    }

    private static IReadOnlyList<RevisionGridRef> Filter(RevisionGridMenuContext context)
    {
        if (context.ClickedRef is not { Length: > 0 } clicked)
        {
            return context.Refs;
        }

        return [.. context.Refs.Where(r => r.Name == clicked)];
    }

    private static RevisionGridMenuEntry Ticked(RevisionGridMenuId id, bool isChecked) => new()
    {
        Id = id,
        Header = Text(id),
        Gesture = Gesture(id),
        IsEnabled = !_unimplemented.Contains(id),
        IsChecked = isChecked,
        Toggle = RevisionGridMenuToggle.Radio,
    };

    private static RevisionGridMenuEntry Checked(RevisionGridMenuId id, RevisionGridDisplay display) =>
        Checked(id, display.IsChecked(id) ?? false);

    private static RevisionGridMenuEntry Checked(RevisionGridMenuId id, bool isChecked) => new()
    {
        Id = id,
        Header = Text(id),
        Gesture = Gesture(id),
        IsEnabled = !_unimplemented.Contains(id),
        IsChecked = isChecked,
        Toggle = RevisionGridMenuToggle.Check,
    };

    private static RevisionGridMenuEntry RefItem(RevisionGridRef gitRef) => new()
    {
        Id = RevisionGridMenuId.DynamicRef,
        Header = Escape(gitRef.Name),
        Argument = gitRef.Name,
    };

    private static RevisionGridMenuEntry SeparatorEntry(
        RevisionGridMenuId id = RevisionGridMenuId.Separator) => new()
    {
        Id = id,
        IsSeparator = true,
    };

    private static bool IsSeparatorId(RevisionGridMenuId id) => id is
        RevisionGridMenuId.SepBisect or
        RevisionGridMenuId.SepCopy or
        RevisionGridMenuId.SepStash or
        RevisionGridMenuId.SepBranch or
        RevisionGridMenuId.SepBranchModification or
        RevisionGridMenuId.SepCommit or
        RevisionGridMenuId.SepCompare or
        RevisionGridMenuId.SepNavigate or
        RevisionGridMenuId.SepRebase or
        RevisionGridMenuId.SepCompareDropdown or
        RevisionGridMenuId.Separator;

    private static List<RevisionGridMenuEntry> ApplySeparatorRules(
        IReadOnlyList<RevisionGridMenuEntry> entries)
    {
        List<RevisionGridMenuEntry> result = [];
        bool seenItem = false;

        foreach (RevisionGridMenuEntry entry in entries)
        {
            if (entry.IsSeparator)
            {
                result.Add(entry with { IsVisible = seenItem });
                seenItem = false;
                continue;
            }

            if (entry.IsVisible)
            {
                seenItem = true;
            }

            result.Add(entry);
        }

        return result;
    }

    private static IReadOnlyList<RevisionGridMenuEntry> CopyChildren(
        RevisionGridMenuContext context,
        IReadOnlyList<RevisionGridRef> branches,
        IReadOnlyList<RevisionGridRef> tags)
    {
        List<RevisionGridMenuEntry> items = [];

        if (branches.Count > 0)
        {
            items.Add(Heading(RevisionGridMenuId.CopyCaptionBranches));
            items.AddRange(branches.Select(b => new RevisionGridMenuEntry
            {
                Id = RevisionGridMenuId.CopyBranchName,
                Header = Escape(b.Name),
                Argument = b.Name,
            }));
            items.Add(SeparatorEntry());
        }

        if (tags.Count > 0)
        {
            items.Add(Heading(RevisionGridMenuId.CopyCaptionTags));
            items.AddRange(tags.Select(t => new RevisionGridMenuEntry
            {
                Id = RevisionGridMenuId.CopyTagName,
                Header = Escape(t.Name),
                Argument = t.Name,
            }));
            items.Add(SeparatorEntry());
        }

        items.Add(Item(RevisionGridMenuId.CopyCommitHash));
        items.Add(Item(RevisionGridMenuId.CopyMessage));
        items.Add(Item(RevisionGridMenuId.CopyAuthor));

        if (context.SelectedCount == 1)
        {
            items.Add(Item(RevisionGridMenuId.CopyDate));
        }
        else
        {
            items.Add(Item(RevisionGridMenuId.CopyAuthorDate));
            items.Add(Item(RevisionGridMenuId.CopyCommitDate));
        }

        return items;
    }

    private static IReadOnlyList<RevisionGridMenuEntry> RebaseChildren(
        RevisionGridMenuContext context)
    {
        bool one = context.SelectedCount == 1;

        return
        [
            Item(RevisionGridMenuId.RebaseSelectedCommit, one),
            Item(RevisionGridMenuId.RebaseSelectedCommitInteractively, one),
            SeparatorEntry(RevisionGridMenuId.SepRebase),
            Item(
                RevisionGridMenuId.RebaseSelectedCommitWithAdvancedOptions,
                one || (context.SelectedCount == 2 && !context.IsArtificial)),
        ];
    }

    private static IReadOnlyList<RevisionGridMenuEntry> AdvancedChildren() =>
    [
        Item(RevisionGridMenuId.EditCommit),
        Item(RevisionGridMenuId.RewordCommit),
        Item(RevisionGridMenuId.FixupCommit),
        Item(RevisionGridMenuId.SquashCommit),
        Item(RevisionGridMenuId.AmendCommit),
        Item(RevisionGridMenuId.GetHelpOnHowToUseTheseFeatures),
    ];

    private static IReadOnlyList<RevisionGridMenuEntry> CompareChildren(
        RevisionGridMenuContext context,
        bool twoSelected) =>
    [
        Item(RevisionGridMenuId.OpenCommitsWithDiffTool, twoSelected),
        SeparatorEntry(RevisionGridMenuId.SepCompareDropdown),
        Item(RevisionGridMenuId.CompareToBranch),
        Item(RevisionGridMenuId.CompareWithCurrentBranch, context.CurrentBranch is { Length: > 0 }),
        Item(RevisionGridMenuId.SelectAsBase),
        Item(RevisionGridMenuId.CompareToBase, enabled: context.HasBaseCommitToCompare),
        Item(RevisionGridMenuId.CompareToWorkingDirectory, !context.IsArtificial),
        Item(RevisionGridMenuId.CompareSelectedCommits, twoSelected),
    ];

    private static IReadOnlyList<RevisionGridMenuEntry> NavigateChildren() =>
    [
        Item(RevisionGridMenuId.ToggleBetweenArtificialAndHeadCommits),
        Item(RevisionGridMenuId.GoToCurrentRevision),
        Item(RevisionGridMenuId.GoToCommit),
        SeparatorEntry(),
        Item(RevisionGridMenuId.GoToChildCommit),
        Item(RevisionGridMenuId.GoToParentCommit),
        Item(RevisionGridMenuId.GoToFirstParentCommit),
        Item(RevisionGridMenuId.GoToLastParentCommit),
        Item(RevisionGridMenuId.GoToMergeBaseCommit),
        SeparatorEntry(),
        Item(RevisionGridMenuId.NavigateBackward),
        Item(RevisionGridMenuId.NavigateForward),
        SeparatorEntry(),
        Item(RevisionGridMenuId.QuickSearch),
        Item(RevisionGridMenuId.QuickSearchPrevious),
        Item(RevisionGridMenuId.QuickSearchNext),
    ];

    private static IReadOnlyList<RevisionGridMenuEntry> ViewChildren(RevisionGridMenuContext context) =>
    [
        Heading(RevisionGridMenuId.ViewGroupBranches),
        Ticked(
            RevisionGridMenuId.ShowAllBranches,
            context.BranchScope == RevisionGridBranchScope.All),
        Ticked(
            RevisionGridMenuId.ShowCurrentBranchOnly,
            context.BranchScope == RevisionGridBranchScope.CurrentOnly),
        Ticked(
            RevisionGridMenuId.ShowFilteredBranches,
            context.BranchScope == RevisionGridBranchScope.Filtered),
        Checked(RevisionGridMenuId.ShowReflogReferences, context.ShowReflogReferences),
        SeparatorEntry(),
        Item(RevisionGridMenuId.AdvancedFilter),
        SeparatorEntry(),
        Checked(RevisionGridMenuId.DrawNonRelativesGray, context.Display),
        Item(RevisionGridMenuId.HighlightSelectedBranch),
        SeparatorEntry(),
        Heading(RevisionGridMenuId.ViewGroupCommits),
        Item(RevisionGridMenuId.ShowArtificialCommits),
        Item(RevisionGridMenuId.ShowStashes),
        Item(RevisionGridMenuId.ShowGitNotes),
        Item(RevisionGridMenuId.ShowSessionCheckpoints),
        SeparatorEntry(),
        Heading(RevisionGridMenuId.ViewGroupGridLabels),
        Checked(RevisionGridMenuId.ShowRemoteBranches, context.Display),
        Checked(RevisionGridMenuId.ShowTags, context.Display),
        Item(RevisionGridMenuId.ShowSuperprojectTags),
        Item(RevisionGridMenuId.ShowSuperprojectRemoteBranches),
        Item(RevisionGridMenuId.ShowSuperprojectBranches),
        SeparatorEntry(),
        Heading(RevisionGridMenuId.ViewGroupGridInfo),
        Item(RevisionGridMenuId.ShowBuildStatusIcon),
        Item(RevisionGridMenuId.ShowBuildStatusText),
        Checked(RevisionGridMenuId.ShowCommitMessageBody, context.Display),
        Checked(RevisionGridMenuId.ShowAuthorDate, context.Display),
        Checked(RevisionGridMenuId.ShowRelativeDate, context.Display),
        SeparatorEntry(),
        Heading(RevisionGridMenuId.ViewGroupColumns),
        Checked(RevisionGridMenuId.ShowRevisionGraphColumn, context.Display),
        Item(RevisionGridMenuId.ShowGitNotesColumn),
        Item(RevisionGridMenuId.ShowAuthorAvatarColumn),
        Checked(RevisionGridMenuId.ShowAuthorNameColumn, context.Display),
        Checked(RevisionGridMenuId.ShowDateColumn, context.Display),
        Checked(RevisionGridMenuId.ShowIdColumn, context.Display),
        SeparatorEntry(),
        Heading(RevisionGridMenuId.ViewGroupSorting),
        Checked(RevisionGridMenuId.AuthorDateSort, context.SortOrder == RevisionSortOrder.AuthorDate),
        Checked(RevisionGridMenuId.TopoOrder, context.SortOrder == RevisionSortOrder.Topology),
        SeparatorEntry(),
        Heading(RevisionGridMenuId.ViewGroupSettingsPersistence),
        Item(RevisionGridMenuId.SaveAsDefault),
    ];

    /// <summary>A leaf entry.</summary>
    /// <remarks>
    /// <c>enabled</c> greys the entry out while leaving it in place. GitExtensions <b>disables</b>
    /// rather than removes <i>Compare to BASE</i>, so the compare submenu keeps the same shape
    /// whether or not a base has been picked.
    /// </remarks>
    private static RevisionGridMenuEntry Item(
        RevisionGridMenuId id,
        bool visible = true,
        bool? enabled = null) => new()
    {
        Id = id,
        Header = Text(id),
        Gesture = Gesture(id),
        IsVisible = visible,
        IsEnabled = visible && (enabled ?? true) && !_unimplemented.Contains(id),
    };

    private static RevisionGridMenuEntry Heading(RevisionGridMenuId id) => new()
    {
        Id = id,
        Header = Text(id),
        IsHeading = true,
        IsEnabled = false,
    };

    private static string Escape(string text) => text.Replace("_", "__", StringComparison.Ordinal);

    private static string? Gesture(RevisionGridMenuId id) => id switch
    {
        RevisionGridMenuId.CreateNewBranch => "Ctrl+B",
        RevisionGridMenuId.CreateTag => "Ctrl+T",
        RevisionGridMenuId.CompareToWorkingDirectory => "Ctrl+D",
        RevisionGridMenuId.CompareSelectedCommits => "Ctrl+Shift+D",
        RevisionGridMenuId.GoToParentCommit => "Ctrl+P",
        RevisionGridMenuId.GoToChildCommit => "Ctrl+N",
        RevisionGridMenuId.GoToCommit => "Ctrl+F",
        _ => null,
    };

    public static string Text(RevisionGridMenuId id) => Loc.T(Key(id));

    private static string Key(RevisionGridMenuId id) => id switch
    {
        RevisionGridMenuId.ResetChanges => "revision_grid.reset_changes",
        RevisionGridMenuId.Commit => "revision_grid.commit",
        RevisionGridMenuId.MarkRevisionAsBad => "revision_grid.mark_revision_as_bad",
        RevisionGridMenuId.MarkRevisionAsGood => "revision_grid.mark_revision_as_good",
        RevisionGridMenuId.BisectSkipRevision => "revision_grid.skip_revision",
        RevisionGridMenuId.StopBisect => "revision_grid.stop_bisect",
        RevisionGridMenuId.CopyToClipboard => "revision_grid.copy_to_clipboard",
        RevisionGridMenuId.ApplyStash => "revision_grid.apply_stash",
        RevisionGridMenuId.PopStash => "revision_grid.pop_stash",
        RevisionGridMenuId.DropStash => "revision_grid.drop_stash",
        RevisionGridMenuId.CheckoutBranch => "revision_grid.checkout_branch",
        RevisionGridMenuId.PushBranch => "revision_grid.push_branch",
        RevisionGridMenuId.MergeBranch => "revision_grid.merge_into_current_branch",
        RevisionGridMenuId.RebaseOn => "revision_grid.rebase_current_branch_on",
        RevisionGridMenuId.ResetCurrentBranchToHere => "revision_grid.reset_current_branch_to_here",
        RevisionGridMenuId.SelectInLeftPanel => "revision_grid.select_in_left_panel",
        RevisionGridMenuId.CreateNewBranch => "revision_grid.create_new_branch_here",
        RevisionGridMenuId.ResetAnotherBranchToHere => "revision_grid.reset_another_branch_to_here",
        RevisionGridMenuId.RenameBranch => "revision_grid.rename_branch",
        RevisionGridMenuId.DeleteBranch => "revision_grid.delete_branch",
        RevisionGridMenuId.CreateTag => "revision_grid.create_new_tag_here",
        RevisionGridMenuId.DeleteTag => "revision_grid.delete_tag",
        RevisionGridMenuId.CheckoutRevision => "revision_grid.checkout_this_commit",
        RevisionGridMenuId.RevertCommit => "revision_grid.revert_this_commit",
        RevisionGridMenuId.CherryPickCommit => "revision_grid.cherry_pick_this_commit",
        RevisionGridMenuId.ArchiveRevision => "revision_grid.archive_this_commit",
        RevisionGridMenuId.ManipulateCommit => "revision_grid.advanced",
        RevisionGridMenuId.Compare => "revision_grid.compare",
        RevisionGridMenuId.Navigate => "revision_grid.navigate",
        RevisionGridMenuId.View => "revision_grid.view",
        RevisionGridMenuId.RunScript => "revision_grid.run_script",
        RevisionGridMenuId.OpenBuildReport => "revision_grid.open_build_report",
        RevisionGridMenuId.OpenPullRequestPage => "revision_grid.open_pull_request_page",
        RevisionGridMenuId.OtherActions => "revision_grid.other_actions",

        RevisionGridMenuId.RebaseSelectedCommit => "revision_grid.rebase_selected_commit",
        RevisionGridMenuId.RebaseSelectedCommitInteractively =>
            "revision_grid.rebase_selected_commit_interactively",
        RevisionGridMenuId.RebaseSelectedCommitWithAdvancedOptions =>
            "revision_grid.rebase_selected_commit_with_advanced_options",

        RevisionGridMenuId.EditCommit => "revision_grid.edit_commit",
        RevisionGridMenuId.RewordCommit => "revision_grid.reword_commit",
        RevisionGridMenuId.FixupCommit => "revision_grid.create_fixup_commit",
        RevisionGridMenuId.SquashCommit => "revision_grid.create_squash_commit",
        RevisionGridMenuId.AmendCommit => "revision_grid.create_amend_commit",
        RevisionGridMenuId.GetHelpOnHowToUseTheseFeatures =>
            "revision_grid.get_help_on_how_to_use_these_features",

        RevisionGridMenuId.OpenCommitsWithDiffTool =>
            "revision_grid.open_selected_commits_with_difftool",
        RevisionGridMenuId.CompareToBranch => "revision_grid.compare_to_branch",
        RevisionGridMenuId.CompareWithCurrentBranch => "revision_grid.compare_with_current_branch",
        RevisionGridMenuId.SelectAsBase => "revision_grid.select_as_base_to_compare",
        RevisionGridMenuId.CompareToBase => "revision_grid.compare_to_base",
        RevisionGridMenuId.CompareToWorkingDirectory => "revision_grid.compare_to_working_directory",
        RevisionGridMenuId.CompareSelectedCommits => "revision_grid.compare_selected_commits",

        RevisionGridMenuId.CopyCaptionBranches => "revision_grid.branches",
        RevisionGridMenuId.CopyCaptionTags => "revision_grid.tags",
        RevisionGridMenuId.CopyCommitHash => "revision_grid.commit_hash",
        RevisionGridMenuId.CopyMessage => "revision_grid.message",
        RevisionGridMenuId.CopyAuthor => "revision_grid.author",
        RevisionGridMenuId.CopyDate => "revision_grid.date",
        RevisionGridMenuId.CopyAuthorDate => "revision_grid.author_date",
        RevisionGridMenuId.CopyCommitDate => "revision_grid.commit_date",

        RevisionGridMenuId.ToggleBetweenArtificialAndHeadCommits =>
            "revision_grid.toggle_between_artificial_and_head_commits",
        RevisionGridMenuId.GoToCurrentRevision => "revision_grid.go_to_current_revision",
        RevisionGridMenuId.GoToCommit => "revision_grid.go_to_commit",
        RevisionGridMenuId.GoToChildCommit => "revision_grid.go_to_child_commit",
        RevisionGridMenuId.GoToParentCommit => "revision_grid.go_to_parent_commit",
        RevisionGridMenuId.GoToFirstParentCommit => "revision_grid.go_to_first_parent_commit",
        RevisionGridMenuId.GoToLastParentCommit => "revision_grid.go_to_last_parent_commit",
        RevisionGridMenuId.GoToMergeBaseCommit => "revision_grid.go_to_merge_base_commit",
        RevisionGridMenuId.NavigateBackward => "revision_grid.navigate_backward",
        RevisionGridMenuId.NavigateForward => "revision_grid.navigate_forward",
        RevisionGridMenuId.QuickSearch => "revision_grid.quick_search",
        RevisionGridMenuId.QuickSearchPrevious => "revision_grid.quick_search_previous",
        RevisionGridMenuId.QuickSearchNext => "revision_grid.quick_search_next",

        RevisionGridMenuId.ViewGroupBranches => "revision_grid.view_group_branches",
        RevisionGridMenuId.ShowAllBranches => "revision_grid.show_all_branches",
        RevisionGridMenuId.ShowCurrentBranchOnly => "revision_grid.show_current_branch_only",
        RevisionGridMenuId.ShowFilteredBranches => "revision_grid.show_filtered_branches",
        RevisionGridMenuId.ShowReflogReferences => "revision_grid.show_reflog_references",
        RevisionGridMenuId.AdvancedFilter => "revision_grid.advanced_filter",
        RevisionGridMenuId.DrawNonRelativesGray => "revision_grid.draw_non_relatives_gray",
        RevisionGridMenuId.HighlightSelectedBranch => "revision_grid.highlight_selected_branch",
        RevisionGridMenuId.ViewGroupCommits => "revision_grid.view_group_commits",
        RevisionGridMenuId.ShowArtificialCommits => "revision_grid.show_artificial_commits",
        RevisionGridMenuId.ShowStashes => "revision_grid.show_stashes",
        RevisionGridMenuId.ShowGitNotes => "revision_grid.show_git_notes",
        RevisionGridMenuId.ShowSessionCheckpoints => "revision_grid.show_session_checkpoints",
        RevisionGridMenuId.ViewGroupGridLabels => "revision_grid.view_group_grid_labels",
        RevisionGridMenuId.ShowRemoteBranches => "revision_grid.show_remote_branches",
        RevisionGridMenuId.ShowTags => "revision_grid.show_tags",
        RevisionGridMenuId.ShowSuperprojectTags => "revision_grid.show_superproject_tags",
        RevisionGridMenuId.ShowSuperprojectRemoteBranches =>
            "revision_grid.show_superproject_remote_branches",
        RevisionGridMenuId.ShowSuperprojectBranches => "revision_grid.show_superproject_branches",
        RevisionGridMenuId.ViewGroupGridInfo => "revision_grid.view_group_grid_info",
        RevisionGridMenuId.ShowBuildStatusIcon => "revision_grid.show_build_status_icon",
        RevisionGridMenuId.ShowBuildStatusText => "revision_grid.show_build_status_text",
        RevisionGridMenuId.ShowCommitMessageBody => "revision_grid.show_commit_message_body",
        RevisionGridMenuId.ShowAuthorDate => "revision_grid.show_author_date",
        RevisionGridMenuId.ShowRelativeDate => "revision_grid.show_relative_date",
        RevisionGridMenuId.ViewGroupColumns => "revision_grid.view_group_columns",
        RevisionGridMenuId.ShowRevisionGraphColumn => "revision_grid.show_revision_graph_column",
        RevisionGridMenuId.ShowGitNotesColumn => "revision_grid.show_git_notes_column",
        RevisionGridMenuId.ShowAuthorAvatarColumn => "revision_grid.show_author_avatar_column",
        RevisionGridMenuId.ShowAuthorNameColumn => "revision_grid.show_author_name_column",
        RevisionGridMenuId.ShowDateColumn => "revision_grid.show_date_column",
        RevisionGridMenuId.ShowIdColumn => "revision_grid.show_id_column",
        RevisionGridMenuId.ViewGroupSorting => "revision_grid.view_group_sorting",
        RevisionGridMenuId.AuthorDateSort => "revision_grid.sort_commits_by_author_date",
        RevisionGridMenuId.TopoOrder => "revision_grid.topo_order",
        RevisionGridMenuId.ViewGroupSettingsPersistence =>
            "revision_grid.view_group_settings_persistence",
        RevisionGridMenuId.SaveAsDefault => "revision_grid.save_as_default",

        _ => string.Empty,
    };
}
