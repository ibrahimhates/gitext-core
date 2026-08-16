namespace GitExt.Core.Model;

/// <summary>The kind of a Git object.</summary>
public enum GitObjectType
{
    /// <summary>The object was not found.</summary>
    Missing,

    /// <summary>File content.</summary>
    Blob,

    /// <summary>Dizin.</summary>
    Tree,

    Commit,

    /// <summary>Annotated tag nesnesi.</summary>
    Tag,
}

/// <summary>
/// The information about an object obtainable without reading its content.
/// </summary>
/// <remarks>
/// Obtained with <c>cat-file --batch-check</c> — for finding out a large file's size without pulling
/// it into memory.
/// </remarks>
public sealed record GitObjectInfo
{
    public required CommitId Id { get; init; }

    public required GitObjectType Type { get; init; }

    /// <summary>Bayt cinsinden boyut; nesne yoksa 0.</summary>
    public required long Size { get; init; }

    public bool Exists => Type != GitObjectType.Missing;
}

/// <summary>
/// A tree (directory) entry.
/// </summary>
public sealed record TreeEntry
{
    public required RepositoryPath Path { get; init; }

    /// <summary>The octal file mode, e.g. <c>100644</c>.</summary>
    public required string Mode { get; init; }

    public required GitObjectType Type { get; init; }

    public required CommitId Id { get; init; }

    /// <summary>The size in bytes; filled in only when requested with <c>--long</c>.</summary>
    public long? Size { get; init; }

    public bool IsDirectory => Type == GitObjectType.Tree;

    /// <summary><c>120000</c> — a symbolic link.</summary>
    public bool IsSymlink => Mode == "120000";

    /// <summary><c>160000</c> — gitlink, yani submodule.</summary>
    public bool IsSubmodule => Mode == "160000";

    /// <summary><c>100755</c> — an executable file.</summary>
    public bool IsExecutable => Mode == "100755";

    public string Name => Path.Name;

    public override string ToString() => Path.Value;
}

/// <summary>
/// A blob's content.
/// </summary>
public sealed record BlobContent
{
    public required CommitId Id { get; init; }

    /// <summary>The object's real size in the repository (even when truncated).</summary>
    public required long Size { get; init; }

    /// <summary>The raw content. When <see cref="IsTruncated"/>, only the first part.</summary>
    public required byte[] Content { get; init; }

    /// <summary>
    /// Is the content binary?
    /// </summary>
    /// <remarks>
    /// The same heuristic as <c>git</c>'s: binary when there is a NUL in the first 8000 bytes.
    /// Not perfect, but being consistent with git beats inventing our own rule.
    /// </remarks>
    public required bool IsBinary { get; init; }

    /// <summary>Was the content shortened because the size limit was exceeded?</summary>
    public bool IsTruncated { get; init; }

    /// <summary>
    /// Returns the content as UTF-8 text.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the content is binary.</exception>
    public string GetText()
    {
        if (IsBinary)
        {
            throw new InvalidOperationException(
                "Binary content cannot be read as text. Check IsBinary first.");
        }

        return System.Text.Encoding.UTF8.GetString(Content);
    }
}
