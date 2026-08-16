using System.Collections.ObjectModel;
using GitExt.Core.Model;

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

    /// <summary>Etiket.</summary>
    Tag,
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
    private RepositoryRefs? _refs;
    private string _filter = string.Empty;
    private RefNodeViewModel? _selected;

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

    /// <summary>Did filtering leave nothing at all?</summary>
    public bool IsEmpty => Roots.Count == 0;

    /// <summary>Has the panel been populated?</summary>
    public bool HasRefs => _refs is not null;

    /// <summary>Takes the refs and builds the tree.</summary>
    public void Load(RepositoryRefs? refs)
    {
        _refs = refs;
        Rebuild();
        OnPropertyChanged(nameof(HasRefs));
    }

    private void Rebuild()
    {
        Roots.Clear();

        if (_refs is not { } refs)
        {
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        RefNodeViewModel branches = new() { Name = "Branches", Kind = RefNodeKind.Section };

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

        RefNodeViewModel remotes = new() { Name = "Remotes", Kind = RefNodeKind.Section };

        foreach (BranchInfo branch in refs.RemoteBranches)
        {
            // The symbolic `origin/HEAD` is skipped: it would look like a second "branch" on the same
            // commit. That same ref sets a trap for the fifth time in this project.
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

        RefNodeViewModel tags = new() { Name = "Tags", Kind = RefNodeKind.Section };

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

        // An empty section is not shown: while filtering, nothing under a "Tags" heading tells the
        // user anything.
        foreach (RefNodeViewModel section in new[] { branches, remotes, tags })
        {
            if (section.Children.Count > 0)
            {
                Roots.Add(section);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
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
            return "upstream yok";
        }

        return tracking.IsUpToDate ? string.Empty : $"↑{tracking.Ahead} ↓{tracking.Behind}";
    }
}
