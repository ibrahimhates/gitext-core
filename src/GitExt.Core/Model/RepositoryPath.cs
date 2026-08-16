using System.Diagnostics.CodeAnalysis;

namespace GitExt.Core.Model;

/// <summary>
/// A path relative to the repository root; the separator is <b>always</b> <c>/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Git separates paths with a forward slash regardless of platform — even on Windows. This type
/// guarantees the path is in the right form when handed to git, and in the platform's form when handed
/// to the file system.
/// </para>
/// <para>
/// Using a plain <see cref="string"/> leads to silent bugs on Windows: <c>Path.Combine</c> produces a
/// backslash, git takes it for part of the file name, and the file is "not found".
/// </para>
/// </remarks>
public readonly record struct RepositoryPath : IComparable<RepositoryPath>
{
    private readonly string? _value;

    private RepositoryPath(string value)
    {
        _value = value;
    }

    /// <summary>The path relative to the repository root, with <c>/</c> separators.</summary>
    public string Value => _value ?? string.Empty;

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>The last component of the path (the file or folder name).</summary>
    public string Name
    {
        get
        {
            string value = Value;
            int index = value.LastIndexOf('/');
            return index < 0 ? value : value[(index + 1)..];
        }
    }

    /// <summary>The parent directory; empty at the root.</summary>
    public RepositoryPath Parent
    {
        get
        {
            string value = Value;
            int index = value.LastIndexOf('/');
            return index < 0 ? default : new RepositoryPath(value[..index]);
        }
    }

    /// <summary>The extension (dot included); empty when there is none.</summary>
    public string Extension
    {
        get
        {
            string name = Name;
            int index = name.LastIndexOf('.');
            return index <= 0 ? string.Empty : name[index..];
        }
    }

    /// <summary>
    /// Parses a path coming from git.
    /// </summary>
    /// <exception cref="ArgumentException">When the path is empty or absolute.</exception>
    public static RepositoryPath Parse(string value)
    {
        if (!TryParse(value, out RepositoryPath path))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid repository-relative path.", nameof(value));
        }

        return path;
    }

    /// <summary>
    /// Tries to parse a path coming from git.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out RepositoryPath path)
    {
        path = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // It may have come from Windows; normalise the separator.
        //
        // 🔴 On Windows only (measured in P05-T08): on Linux `\` is a VALID character in a file name
        // and git reports it as it is. Converting on every platform turned a file called
        // `ters\slash.txt` into `ters/slash.txt` and showed the path SILENTLY wrong — adding it to
        // `.gitignore` did nothing at all, because the pattern produced pointed at a subdirectory that
        // did not exist.
        string normalized = OperatingSystem.IsWindows()
            ? value.Replace('\\', '/').Trim('/')
            : value.Trim('/');

        if (normalized.Length == 0)
        {
            return false;
        }

        // Absolute paths and drive letters are not repository-relative.
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            return false;
        }

        path = new RepositoryPath(normalized);
        return true;
    }

    /// <summary>
    /// Produces the absolute file system path, given the repository root.
    /// </summary>
    public string ToAbsolutePath(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        // Path.Combine uses the platform's separator; we split git's /-separated path into its
        // components and hand those over.
        return Path.GetFullPath(Path.Combine([repositoryRoot, .. Value.Split('/')]));
    }

    public int CompareTo(RepositoryPath other) =>
        string.CompareOrdinal(Value, other.Value);

    public override string ToString() => Value;
}
