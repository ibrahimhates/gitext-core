using System.Collections.ObjectModel;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>The kind of a node in the tree (P06-T13).</summary>
public enum RefNodeKind
{
    /// <summary>A top-level heading: <i>Branches</i>, <i>Remotes</i>, <i>Tags</i>.</summary>
    Section,

    /// <summary>A folder arising from the <c>/</c>s in a name (<c>feature/</c>).</summary>
    Folder,

    /// <summary>Yerel dal.</summary>
    LocalBranch,

    /// <summary>Uzak dal.</summary>
    RemoteBranch,

    /// <summary>A remote (above the remote branches).</summary>
    Remote,

    /// <summary>A tag entry shown as text rather than an arrow (P12-T13).</summary>
    Tag,

    /// <summary>A linked working tree (P12-T13).</summary>
    WorkTree,

    /// <summary>A submodule (P12-T13).</summary>
    Submodule,

    /// <summary>A stash entry (P12-T13).</summary>
    Stash,
}

/// <summary>
/// Which sections the panel shows (P12-T13).
/// </summary>
/// <remarks>
/// GitExtensions puts a toggle for each tree on the panel's own toolbar
/// (<c>tsbShowBranches</c> … <c>tsbShowStashes</c>) and remembers them. A user who never uses
/// submodules should not have to look at the heading for the rest of their life.
/// </remarks>
public sealed record RefTreeSections
{
    public bool Branches { get; init; } = true;

    public bool Remotes { get; init; } = true;

    public bool WorkTrees { get; init; } = true;

    public bool Tags { get; init; } = true;

    public bool Submodules { get; init; } = true;

    public bool Stashes { get; init; } = true;
}

/// <summary>
/// Everything the panel draws, in one package (P12-T13).
/// </summary>
/// <remarks>
/// 🔑 The panel does not read anything itself — the same rule as the refs since P06-T13. Asking
/// the same question through a second code path has already produced silently different answers
/// twice in this project.
/// </remarks>
public sealed record RefTreeData
{
    public RepositoryRefs? Refs { get; init; }

    /// <summary>The repository's working directory — a submodule's path is relative to it.</summary>
    public string RootPath { get; init; } = string.Empty;

    public IReadOnlyList<WorkTree> WorkTrees { get; init; } = [];

    public IReadOnlyList<Submodule> Submodules { get; init; } = [];

    public IReadOnlyList<StashEntry> Stashes { get; init; } = [];
}

/// <summary>
/// A node in the branch panel (P06-T13).
/// </summary>
/// <remarks>
/// As in GitExtensions' <c>RepoObjectsTree</c>, names are split on <c>/</c> and grouped into
/// folders: <c>feature/login</c> → <i>feature</i> ▸ <i>login</i>. Showing dozens of branches as a
/// flat list would bring back exactly the problem the panel exists to solve.
/// </remarks>
public sealed class RefNodeViewModel : ViewModelBase
{
    private bool _isExpanded = true;

    public required string Name { get; init; }

    /// <summary>The full ref name (<c>main</c>, <c>origin/main</c>, <c>v1.0</c>); empty on a folder.</summary>
    public string FullName { get; init; } = string.Empty;

    public required RefNodeKind Kind { get; init; }

    /// <summary>Is this branch currently checked out?</summary>
    public bool IsCurrent { get; init; }

    /// <summary>The position relative to the upstream; empty when there is none.</summary>
    public string AheadBehind { get; init; } = string.Empty;

    public bool HasAheadBehind => AheadBehind.Length > 0;

    /// <summary>Can it be double-clicked (checkout)?</summary>
    public bool IsCheckoutable => Kind is RefNodeKind.LocalBranch or RefNodeKind.RemoteBranch;

    /// <summary>
    /// The path behind the node — a worktree's or a submodule's directory.
    /// </summary>
    /// <remarks>
    /// Both can be <b>opened as a repository</b>, which is what GitExtensions' "Open" does on
    /// them. The path is kept separately from <see cref="FullName"/> because a submodule's name is
    /// relative to the repository while the thing to open is an absolute path.
    /// </remarks>
    public string Path { get; init; } = string.Empty;

    /// <summary>Can this node be opened as a repository of its own?</summary>
    public bool IsOpenable => Kind is RefNodeKind.WorkTree or RefNodeKind.Submodule && Path.Length > 0;

    public ObservableCollection<RefNodeViewModel> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public override string ToString() => FullName.Length > 0 ? FullName : Name;
}

/// <summary>
/// The branch panel (P06-T13).
/// </summary>
/// <remarks>
/// <para>
/// The layout comes from GitExtensions' left <c>RepoObjectsTree</c> (§ 9): <i>Branches</i> at the
/// top, <i>Remotes</i> below it (each remote its own node), and <i>Tags</i> at the bottom.
/// </para>
/// <para>
/// 🔑 <b>The data is not read here, it is handed in.</b> The panel takes <c>RepositoryRefs</c> as it
/// is — asking the same question through a second code path has already produced silently different
/// answers twice in this project (<c>RefReader</c> in P06-T05, the ref snapshot in P06-T06).
/// </para>
/// </remarks>
public sealed class RefTreeViewModel : ViewModelBase
{
    private RefTreeData _data = new();
    private string _rootPath = string.Empty;
    private string _filter = string.Empty;
    private RefNodeViewModel? _selected;
    private RefTreeSections _sections = new();

    /// <summary>The roots of the tree.</summary>
    public ObservableCollection<RefNodeViewModel> Roots { get; } = [];

    /// <summary>The search text; when empty, everything is visible.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                Rebuild();
            }
        }
    }

    public RefNodeViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(CanCheckoutSelected));
                OnPropertyChanged(nameof(CanMergeSelected));
                OnPropertyChanged(nameof(CanRenameSelected));
                OnPropertyChanged(nameof(CanDeleteSelected));
                OnPropertyChanged(nameof(CanPushSelected));
                OnPropertyChanged(nameof(CanCopySelectedName));
                OnPropertyChanged(nameof(CanBranchFromSelected));
                OnPropertyChanged(nameof(IsStashSelected));
                OnPropertyChanged(nameof(IsSubmoduleSelected));
                OnPropertyChanged(nameof(IsWorkTreeSelected));
                OnPropertyChanged(nameof(CanOpenSelected));
                OnPropertyChanged(nameof(IsRemotesSectionSelected));
                OnPropertyChanged(nameof(IsBranchesSectionSelected));
                OnPropertyChanged(nameof(IsStashesSectionSelected));
            }
        }
    }

    // ---- The context menu's decisions (P06-T14).
    //
    // 🔑 The decisions are HERE. The first implementation set `IsEnabled` by hand in the menu's
    // `Opening` event; measured — that event never fires headless, so the menu silently showed
    // everything as enabled and the test only caught it when it tried to open the menu.
    // The same lesson as Phase 03: a decision hidden on the view side cannot be verified.

    /// <summary>Can the selected node be checked out?</summary>
    public bool CanCheckoutSelected => Selected?.IsCheckoutable == true;

    /// <summary>Can the selected branch be merged into the current one?</summary>
    /// <remarks>Merging something into itself is something git refuses as well.</remarks>
    public bool CanMergeSelected => Selected is { IsCheckoutable: true, IsCurrent: false };

    /// <summary>Can it be renamed?</summary>
    /// <remarks><c>git branch -m</c> does not change a remote branch; offering it would be a false promise.</remarks>
    public bool CanRenameSelected => Selected?.Kind == RefNodeKind.LocalBranch;

    /// <summary>Silinebilir mi?</summary>
    public bool CanDeleteSelected =>
        Selected is { Kind: RefNodeKind.LocalBranch, IsCurrent: false };

    /// <summary>Can it be pushed?</summary>
    public bool CanPushSelected => Selected?.Kind == RefNodeKind.LocalBranch;

    /// <summary>Can its name be copied? (Tags included; folders and headings excluded.)</summary>
    public bool CanCopySelectedName => Selected is { FullName.Length: > 0 };

    /// <summary>Can a new branch be created from here?</summary>
    public bool CanBranchFromSelected => Selected is { FullName.Length: > 0 };

    // ---- P12-T14: the menu items of the new node kinds.
    //
    // The decisions stay HERE for the same reason as the ones above: an `Opening` handler never
    // fires headless, so an enabled state decided on the view side cannot be tested at all.

    /// <summary>Is a stash entry selected? (Apply · Pop · Drop · Show)</summary>
    public bool IsStashSelected => Selected?.Kind == RefNodeKind.Stash;

    /// <summary>Is a submodule selected?</summary>
    public bool IsSubmoduleSelected => Selected?.Kind == RefNodeKind.Submodule;

    /// <summary>Is a linked working tree selected?</summary>
    public bool IsWorkTreeSelected => Selected?.Kind == RefNodeKind.WorkTree;

    /// <summary>Can the selection be opened as a repository of its own?</summary>
    public bool CanOpenSelected => Selected?.IsOpenable == true;

    /// <summary>
    /// Is the <i>Remotes</i> heading selected? (Manage · Fetch all · Fetch and prune all)
    /// </summary>
    /// <remarks>
    /// GitExtensions hangs these off the root node (<c>mnuBtnManageRemotesFromRootNode</c>,
    /// <c>mnuBtnFetchAllRemotes</c>, <c>mnuBtnPruneAllRemotes</c>), and that is where someone
    /// looking for "fetch everything" right-clicks.
    /// </remarks>
    public bool IsRemotesSectionSelected =>
        Selected is { Kind: RefNodeKind.Section } section
        && string.Equals(section.Name, Loc.T("ref_tree.section_remotes"), StringComparison.Ordinal);

    /// <summary>Is the <i>Branches</i> heading selected? (Create branch)</summary>
    public bool IsBranchesSectionSelected =>
        Selected is { Kind: RefNodeKind.Section } section
        && string.Equals(section.Name, Loc.T("ref_tree.section_branches"), StringComparison.Ordinal);

    /// <summary>Is the <i>Stashes</i> heading selected? (Stash all · Manage stashes)</summary>
    public bool IsStashesSectionSelected =>
        Selected is { Kind: RefNodeKind.Section } section
        && string.Equals(section.Name, Loc.T("ref_tree.section_stashes"), StringComparison.Ordinal);

    /// <summary>Did filtering leave nothing at all?</summary>
    public bool IsEmpty => Roots.Count == 0;

    /// <summary>Has the panel been populated?</summary>
    public bool HasRefs => _data.Refs is not null;

    /// <summary>Takes the refs and builds the tree.</summary>
    public void Load(RepositoryRefs? refs) => Load(new RefTreeData { Refs = refs });

    /// <summary>Takes everything the panel shows and builds the tree (P12-T13).</summary>
    public void Load(RefTreeData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        _data = data;
        _rootPath = data.RootPath;
        Rebuild();
        OnPropertyChanged(nameof(HasRefs));
    }

    /// <summary>
    /// Which sections are shown — the panel's own toolbar toggles (P12-T13).
    /// </summary>
    /// <remarks>
    /// Changing this rebuilds the tree. The setting is stored by the window, not here: the panel
    /// does not know where settings live (ADR-0004).
    /// </remarks>
    public RefTreeSections Sections
    {
        get => _sections;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_sections == value)
            {
                return;
            }

            _sections = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowBranches));
            OnPropertyChanged(nameof(ShowRemotes));
            OnPropertyChanged(nameof(ShowWorkTrees));
            OnPropertyChanged(nameof(ShowTags));
            OnPropertyChanged(nameof(ShowSubmodules));
            OnPropertyChanged(nameof(ShowStashes));

            Rebuild();
            SectionsChanged?.Invoke(this, value);
        }
    }

    /// <summary>Raised when a section is toggled, so the choice can be saved.</summary>
    public event EventHandler<RefTreeSections>? SectionsChanged;

    // The toolbar's toggles bind to these; each one writes the whole record back through
    // `Sections`, so there is a single place that rebuilds and reports the change.

    public bool ShowBranches
    {
        get => _sections.Branches;
        set => Sections = _sections with { Branches = value };
    }

    public bool ShowRemotes
    {
        get => _sections.Remotes;
        set => Sections = _sections with { Remotes = value };
    }

    public bool ShowWorkTrees
    {
        get => _sections.WorkTrees;
        set => Sections = _sections with { WorkTrees = value };
    }

    public bool ShowTags
    {
        get => _sections.Tags;
        set => Sections = _sections with { Tags = value };
    }

    public bool ShowSubmodules
    {
        get => _sections.Submodules;
        set => Sections = _sections with { Submodules = value };
    }

    public bool ShowStashes
    {
        get => _sections.Stashes;
        set => Sections = _sections with { Stashes = value };
    }

    /// <summary>Collapses every node (GitExtensions' <c>tsbCollapseAll</c>).</summary>
    public void CollapseAll()
    {
        foreach (RefNodeViewModel root in Roots)
        {
            Collapse(root);
        }

        static void Collapse(RefNodeViewModel node)
        {
            node.IsExpanded = false;

            foreach (RefNodeViewModel child in node.Children)
            {
                Collapse(child);
            }
        }
    }

    private void Rebuild()
    {
        Roots.Clear();

        RepositoryRefs? refs = _data.Refs;

        if (refs is null)
        {
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        // The order is GitExtensions' own (`RepoObjectsTree`, the order the trees are created):
        // Branches · Remotes · Worktrees · Tags · Submodules · Stashes.
        RefNodeViewModel branches = BuildBranches(refs);
        RefNodeViewModel remotes = BuildRemotes(refs);
        RefNodeViewModel worktrees = BuildWorkTrees();
        RefNodeViewModel tags = BuildTags(refs);
        RefNodeViewModel submodules = BuildSubmodules();
        RefNodeViewModel stashes = BuildStashes();

        // An empty section is not shown: while filtering, nothing under a "Tags" heading tells the
        // user anything. A section switched off on the toolbar is not shown either.
        AddSection(branches, _sections.Branches);
        AddSection(remotes, _sections.Remotes);
        AddSection(worktrees, _sections.WorkTrees);
        AddSection(tags, _sections.Tags);
        AddSection(submodules, _sections.Submodules);
        AddSection(stashes, _sections.Stashes);

        OnPropertyChanged(nameof(IsEmpty));

        void AddSection(RefNodeViewModel section, bool visible)
        {
            if (visible && section.Children.Count > 0)
            {
                Roots.Add(section);
            }
        }
    }

    private RefNodeViewModel BuildBranches(RepositoryRefs refs)
    {
        RefNodeViewModel branches = new()
        {
            Name = Loc.T("ref_tree.section_branches"),
            Kind = RefNodeKind.Section,
        };

        foreach (BranchInfo branch in refs.LocalBranches)
        {
            if (!Matches(branch.Name))
            {
                continue;
            }

            Add(
                branches,
                branch.Name,
                new RefNodeViewModel
                {
                    Name = LastSegment(branch.Name),
                    FullName = branch.Name,
                    Kind = RefNodeKind.LocalBranch,
                    IsCurrent = branch.IsCurrent,
                    AheadBehind = Describe(branch.Tracking, branch.Upstream),
                });
        }

        return branches;
    }

    private RefNodeViewModel BuildRemotes(RepositoryRefs refs)
    {
        RefNodeViewModel remotes = new()
        {
            Name = Loc.T("ref_tree.section_remotes"),
            Kind = RefNodeKind.Section,
        };

        foreach (BranchInfo branch in refs.RemoteBranches)
        {
            // The symbolic `origin/HEAD` is skipped: it would look like a second "branch" on the
            // same commit. That same ref sets a trap for the fifth time in this project.
            if (branch.Ref.IsSymbolic || !Matches(branch.Name))
            {
                continue;
            }

            int slash = branch.Name.IndexOf('/', StringComparison.Ordinal);

            if (slash <= 0)
            {
                continue;
            }

            string remoteName = branch.Name[..slash];
            string rest = branch.Name[(slash + 1)..];

            RefNodeViewModel remote = Section(remotes, remoteName, RefNodeKind.Remote);

            Add(
                remote,
                rest,
                new RefNodeViewModel
                {
                    Name = LastSegment(rest),
                    FullName = branch.Name,
                    Kind = RefNodeKind.RemoteBranch,
                });
        }

        return remotes;
    }

    private RefNodeViewModel BuildTags(RepositoryRefs refs)
    {
        RefNodeViewModel tags = new()
        {
            Name = Loc.T("ref_tree.section_tags"),
            Kind = RefNodeKind.Section,
        };

        foreach (TagInfo tag in refs.Tags)
        {
            if (Matches(tag.Name))
            {
                Add(
                    tags,
                    tag.Name,
                    new RefNodeViewModel
                    {
                        Name = LastSegment(tag.Name),
                        FullName = tag.Name,
                        Kind = RefNodeKind.Tag,
                    });
            }
        }

        return tags;
    }

    /// <summary>
    /// The linked working trees (P12-T13).
    /// </summary>
    /// <remarks>
    /// The main working tree is in the list too and is marked, because "which one am I looking at"
    /// is the first question the list raises. A worktree whose directory is gone is marked as
    /// prunable rather than hidden — hiding it would leave the user wondering where it went.
    /// </remarks>
    private RefNodeViewModel BuildWorkTrees()
    {
        RefNodeViewModel worktrees = new()
        {
            Name = Loc.T("ref_tree.section_worktrees"),
            Kind = RefNodeKind.Section,
        };

        foreach (WorkTree worktree in _data.WorkTrees)
        {
            string name = LastPathSegment(worktree.Path);

            if (!Matches(name) && !Matches(worktree.BranchName ?? string.Empty))
            {
                continue;
            }

            worktrees.Children.Add(new RefNodeViewModel
            {
                Name = name,
                FullName = worktree.Path,
                Path = worktree.Path,
                Kind = RefNodeKind.WorkTree,
                IsCurrent = worktree.IsMain,
                AheadBehind = worktree.IsPrunable
                    ? Loc.T("ref_tree.worktree_missing")
                    : worktree.BranchName ?? Loc.T("dashboard.detached_head"),
            });
        }

        return worktrees;
    }

    private RefNodeViewModel BuildSubmodules()
    {
        RefNodeViewModel submodules = new()
        {
            Name = Loc.T("ref_tree.section_submodules"),
            Kind = RefNodeKind.Section,
        };

        string root = _data.Refs is not null ? _rootPath : string.Empty;

        foreach (Submodule submodule in _data.Submodules)
        {
            string name = submodule.Path.Value;

            if (!Matches(name))
            {
                continue;
            }

            submodules.Children.Add(new RefNodeViewModel
            {
                Name = LastPathSegment(name),
                FullName = name,
                Path = root.Length > 0 ? submodule.ResolvePath(root) : name,
                Kind = RefNodeKind.Submodule,
                AheadBehind = DescribeSubmodule(submodule.Status),
            });
        }

        return submodules;
    }

    private RefNodeViewModel BuildStashes()
    {
        RefNodeViewModel stashes = new()
        {
            Name = Loc.T("ref_tree.section_stashes"),
            Kind = RefNodeKind.Section,
        };

        foreach (StashEntry stash in _data.Stashes)
        {
            if (!Matches(stash.Message) && !Matches(stash.Selector))
            {
                continue;
            }

            stashes.Children.Add(new RefNodeViewModel
            {
                // GitExtensions shows the message; the selector is what every command needs.
                Name = stash.Message.Length > 0 ? stash.Message : stash.Selector,
                FullName = stash.Selector,
                Kind = RefNodeKind.Stash,
                AheadBehind = stash.ShortSelector,
            });
        }

        return stashes;
    }

    /// <summary>The submodule's state, in words.</summary>
    private static string DescribeSubmodule(SubmoduleStatusKind status) => status switch
    {
        SubmoduleStatusKind.NotInitialized => Loc.T("ref_tree.submodule_not_initialized"),
        SubmoduleStatusKind.Modified => Loc.T("ref_tree.submodule_modified"),
        SubmoduleStatusKind.Conflicted => Loc.T("ref_tree.submodule_conflicted"),
        _ => string.Empty,
    };

    private static string LastPathSegment(string path)
    {
        string trimmed = path.TrimEnd('/', '\\');
        int slash = trimmed.LastIndexOfAny(['/', '\\']);

        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private bool Matches(string name) =>
        Filter.Length == 0 || name.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    private static string LastSegment(string name)
    {
        int slash = name.LastIndexOf('/');

        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    /// <summary>Splits the name on <c>/</c> into folders and places the leaf.</summary>
    private static void Add(RefNodeViewModel parent, string path, RefNodeViewModel leaf)
    {
        string[] segments = path.Split('/');
        RefNodeViewModel current = parent;

        for (int index = 0; index < segments.Length - 1; index++)
        {
            current = Section(current, segments[index], RefNodeKind.Folder);
        }

        current.Children.Add(leaf);
    }

    private static RefNodeViewModel Section(RefNodeViewModel parent, string name, RefNodeKind kind)
    {
        RefNodeViewModel? existing = parent.Children
            .FirstOrDefault(child => child.Kind == kind
                && string.Equals(child.Name, name, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing;
        }

        RefNodeViewModel created = new() { Name = name, Kind = kind };
        parent.Children.Add(created);

        return created;
    }

    /// <summary>
    /// The ahead/behind badge.
    /// </summary>
    /// <remarks>
    /// <c>[gone]</c> is written separately: showing "0/0" for a branch whose upstream was deleted
    /// would suggest the branch is up to date.
    /// </remarks>
    internal static string Describe(UpstreamTracking tracking, string? upstream)
    {
        if (upstream is not { Length: > 0 })
        {
            return string.Empty;
        }

        if (tracking.IsGone)
        {
            return Loc.T("ref_tree.upstream_gone");
        }

        return tracking.IsUpToDate ? string.Empty : $"↑{tracking.Ahead} ↓{tracking.Behind}";
    }
}
