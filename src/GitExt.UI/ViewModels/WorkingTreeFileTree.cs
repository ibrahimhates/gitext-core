namespace GitExt.UI.ViewModels;

/// <summary>
/// How the working directory file lists are laid out (GitExtensions' <c>DiffListSortType</c>).
/// </summary>
public enum WorkingTreeListMode
{
    /// <summary>Folder tree — the default, as in GitExtensions (<c>DiffListSortType.FilePath</c>).</summary>
    Tree,

    /// <summary>One row per file carrying the whole path (<c>DiffListSortType.FilePathFlat</c>).</summary>
    Flat,
}

/// <summary>
/// A row of the working directory lists: a folder or a file.
/// </summary>
/// <remarks>
/// The tree is rendered as a <b>flattened</b> row list rather than a nested control: a
/// <c>ListBox</c> virtualises its rows, and a repository with ten thousand changed files —
/// expanded, the way GitExtensions opens this screen — is exactly the case that decides whether the
/// screen stays usable (measured in P14).
/// </remarks>
public abstract class WorkingTreeRow
{
    protected WorkingTreeRow(bool staged) => IsStagedSide = staged;

    /// <summary>Does the row belong to the <b>staged</b> list?</summary>
    public bool IsStagedSide { get; }

    /// <summary>How deep the row sits; 0 at the top level.</summary>
    public int Depth { get; internal set; }

    /// <summary>The text shown on the row.</summary>
    /// <remarks>
    /// In tree mode this is the part of the path the parent row does not already carry — the same
    /// trimming GitExtensions does in <c>StatusSorter.RemoveParentPath</c>. In flat mode it is the
    /// whole path.
    /// </remarks>
    public string DisplayName { get; internal set; } = string.Empty;

    /// <summary>Is this a folder row?</summary>
    public bool IsFolder => this is WorkingTreeFolderRow;

    /// <summary>
    /// The files the row stands for: a file row is itself, a folder row its whole subtree.
    /// </summary>
    /// <remarks>
    /// This is what makes "stage the folder" work, and it is GitExtensions' own rule:
    /// <c>FileStatusList.SelectedItems</c> collects the tags of the selected node <b>and every
    /// descendant</b>, so the ordinary stage command there already applies to a whole subtree.
    /// </remarks>
    public abstract IEnumerable<WorkingTreeFileRow> EnumerateFiles();
}

/// <summary>
/// A folder row in the working directory lists.
/// </summary>
public sealed class WorkingTreeFolderRow : WorkingTreeRow
{
    internal WorkingTreeFolderRow(string path, bool staged)
        : base(staged) =>
        Path = path;

    /// <summary>The folder's path relative to the repository root, with <c>/</c> separators.</summary>
    public string Path { get; }

    /// <summary>The rows directly under the folder: subfolders first, then files.</summary>
    public IReadOnlyList<WorkingTreeRow> Children { get; internal set; } = [];

    /// <summary>Is the folder open?</summary>
    public bool IsExpanded { get; internal set; } = true;

    public override IEnumerable<WorkingTreeFileRow> EnumerateFiles()
    {
        foreach (WorkingTreeRow child in Children)
        {
            foreach (WorkingTreeFileRow file in child.EnumerateFiles())
            {
                yield return file;
            }
        }
    }

    public override string ToString() => $"{Path}/";
}

/// <summary>
/// Builds the folder tree of the working directory lists.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors GitExtensions' <c>FileStatusList.StatusSorter.CreateTreeSortedByPath</c>. That one walks a
/// sorted list and splits at the common prefix of consecutive paths; the result is the same as a path
/// trie whose <b>single-child folder chains are collapsed</b>, which is what is built here. Both were
/// checked against the snapshots in <c>FileStatusListSorterTests.*.verified.txt</c>.
/// </para>
/// <para>
/// Three rules come out of those snapshots and are load-bearing: folders come before files at every
/// level; a folder holding files of its own is never merged into its parent; and a chain like
/// <c>a/b/c</c> holding nothing but one subfolder becomes a <b>single</b> row.
/// </para>
/// </remarks>
internal static class WorkingTreeFileTree
{
    /// <summary>
    /// Builds the top-level rows of the tree.
    /// </summary>
    /// <param name="files">One side's files; the order does not matter.</param>
    /// <param name="staged">Which side the rows belong to.</param>
    /// <param name="collapsed">Paths of the folders the user has closed.</param>
    public static List<WorkingTreeRow> Build(
        IReadOnlyList<WorkingTreeFileRow> files,
        bool staged,
        IReadOnlySet<string>? collapsed = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        Node root = new();

        foreach (WorkingTreeFileRow file in files)
        {
            Insert(root, file);
        }

        List<WorkingTreeRow> rows = [];

        AddChildren(root, rows, parentPathLength: 0, depth: 0, staged, collapsed);

        return rows;
    }

    /// <summary>
    /// Lays the tree out as the flat row list the list box is bound to; a closed folder hides its
    /// contents.
    /// </summary>
    public static List<WorkingTreeRow> Flatten(IReadOnlyList<WorkingTreeRow> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        List<WorkingTreeRow> rows = [];

        Append(roots, rows);

        return rows;

        static void Append(IReadOnlyList<WorkingTreeRow> source, List<WorkingTreeRow> target)
        {
            foreach (WorkingTreeRow row in source)
            {
                target.Add(row);

                if (row is WorkingTreeFolderRow { IsExpanded: true } folder)
                {
                    Append(folder.Children, target);
                }
            }
        }
    }

    /// <summary>Every folder row of the tree, parents before children.</summary>
    public static IEnumerable<WorkingTreeFolderRow> Folders(IReadOnlyList<WorkingTreeRow> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        foreach (WorkingTreeRow row in roots)
        {
            if (row is not WorkingTreeFolderRow folder)
            {
                continue;
            }

            yield return folder;

            foreach (WorkingTreeFolderRow nested in Folders(folder.Children))
            {
                yield return nested;
            }
        }
    }

    private static void Insert(Node root, WorkingTreeFileRow file)
    {
        string value = file.Path.Value;

        Node node = root;
        int start = 0;

        while (true)
        {
            int separator = value.IndexOf('/', start);

            if (separator < 0)
            {
                (node.Files ??= []).Add(file);
                return;
            }

            // An empty segment could only come from a path like `a//b`; git never emits one, and a
            // nameless folder row would be worse than skipping it.
            if (separator > start)
            {
                string segment = value[start..separator];

                node.Directories ??= [];

                if (!node.Directories.TryGetValue(segment, out Node? child))
                {
                    child = new Node(segment, node);
                    node.Directories.Add(segment, child);
                }

                node = child;
            }

            start = separator + 1;
        }
    }

    private static void AddChildren(
        Node node,
        List<WorkingTreeRow> target,
        int parentPathLength,
        int depth,
        bool staged,
        IReadOnlySet<string>? collapsed)
    {
        if (node.Directories is { Count: > 0 } directories)
        {
            foreach (Node child in directories.Values.OrderBy(d => d.Name, StringComparer.InvariantCulture))
            {
                target.Add(CreateFolder(child, parentPathLength, depth, staged, collapsed));
            }
        }

        if (node.Files is { Count: > 0 } files)
        {
            files.Sort(static (l, r) => StringComparer.InvariantCulture.Compare(l.Path.Name, r.Path.Name));

            foreach (WorkingTreeFileRow file in files)
            {
                file.Depth = depth;
                file.DisplayName = file.Path.Name;
                target.Add(file);
            }
        }
    }

    private static WorkingTreeFolderRow CreateFolder(
        Node node,
        int parentPathLength,
        int depth,
        bool staged,
        IReadOnlySet<string>? collapsed)
    {
        // A folder holding no files of its own and exactly one subfolder does not earn a row:
        // `a` → `b` → `c` becomes the single row `a/b/c`. GitExtensions arrives at the same shape by
        // only ever creating a node for a path that actually holds files or splits.
        while (node.Files is null or { Count: 0 } && node.Directories is { Count: 1 })
        {
            node = node.Directories.Values.First();
        }

        string path = node.Path;

        WorkingTreeFolderRow folder = new(path, staged)
        {
            Depth = depth,
            DisplayName = path[parentPathLength..],
            IsExpanded = collapsed?.Contains(path) != true,
        };

        List<WorkingTreeRow> children = [];

        AddChildren(node, children, path.Length + 1, depth + 1, staged, collapsed);

        folder.Children = children;

        return folder;
    }

    /// <summary>A folder while the tree is being built.</summary>
    private sealed class Node
    {
        private string? _path;

        public Node()
        {
        }

        public Node(string name, Node parent)
        {
            Name = name;
            Parent = parent;
        }

        public string Name { get; } = string.Empty;

        public Node? Parent { get; }

        public Dictionary<string, Node>? Directories;

        public List<WorkingTreeFileRow>? Files;

        /// <summary>The path from the repository root; empty at the root.</summary>
        public string Path =>
            _path ??= Parent is { Name.Length: > 0 } parent ? $"{parent.Path}/{Name}" : Name;
    }
}
