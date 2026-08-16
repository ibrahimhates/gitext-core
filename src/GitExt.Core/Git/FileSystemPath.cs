namespace GitExt.Core.Git;

/// <summary>
/// Canonicalisation of file system paths — the equivalent of POSIX <c>realpath</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 MEASURED (macOS) — git ALWAYS answers with every symbolic link resolved:
/// <c>rev-parse --absolute-git-dir</c> returns <c>/private/var/folders/…/repo/.git</c> while the
/// path the user handed us is <c>/var/folders/…/repo</c>, because <c>/var</c> is a symlink to
/// <c>/private/var</c>. Comparing git's answer against a path we assembled ourselves therefore
/// reported "different" for two names of the SAME directory, and every ordinary repository on macOS
/// was classified as a linked worktree.
/// </para>
/// <para>
/// <see cref="Directory.ResolveLinkTarget"/> alone is not enough: it looks at the <b>last</b>
/// component only and returns <see langword="null"/> when that component is not itself a link —
/// exactly the macOS case, where the symlink sits at the very front of the path. So the path is
/// walked component by component.
/// </para>
/// </remarks>
public static class FileSystemPath
{
    /// <summary>
    /// How many links are followed for a single component before giving up.
    /// </summary>
    /// <remarks>
    /// A link may point at another link. The limit guards against a cycle and is the same order of
    /// magnitude as the kernel's own <c>ELOOP</c> threshold.
    /// </remarks>
    private const int MaxLinkHops = 40;

    /// <summary>
    /// Resolves the path to its real location, following symbolic links in <b>every</b> component.
    /// </summary>
    /// <remarks>
    /// The path does not have to exist: unresolvable components are left as they are, so the result
    /// is always at least <see cref="Path.GetFullPath(string)"/>. That matters because this also runs
    /// on paths that are about to be created.
    /// </remarks>
    public static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? string.Empty;

        if (root.Length == 0)
        {
            return full;
        }

        string current = root;

        foreach (string segment in full[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = ResolveComponent(Path.Combine(current, segment));
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>
    /// Follows the link chain of a path whose parent is already resolved.
    /// </summary>
    /// <remarks>
    /// MEASURED — with <c>returnFinalTarget: false</c> a <b>relative</b> link target is resolved
    /// against the link's own directory, not against the process's working directory, so
    /// <see cref="FileSystemInfo.FullName"/> can be used as is. The chain is walked by hand rather
    /// than with <c>returnFinalTarget: true</c> so that a broken link leaves the path untouched
    /// instead of throwing.
    /// </remarks>
    private static string ResolveComponent(string path)
    {
        string current = path;

        for (int hop = 0; hop < MaxLinkHops; hop++)
        {
            FileSystemInfo? target;

            try
            {
                target = Directory.ResolveLinkTarget(current, returnFinalTarget: false)
                         ?? File.ResolveLinkTarget(current, returnFinalTarget: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A path we cannot inspect is left as it is; the caller loses normalisation, not
                // correctness.
                return current;
            }

            if (target is null)
            {
                return current;
            }

            current = target.FullName;
        }

        return current;
    }
}
