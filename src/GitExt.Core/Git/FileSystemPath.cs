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
    /// How many links are followed in one <see cref="Resolve"/> call before giving up.
    /// </summary>
    /// <remarks>
    /// A link may point at another link, and a target may sit under further links. The budget is
    /// shared by the whole path, guards against a cycle, and is the same order of magnitude as the
    /// kernel's own <c>ELOOP</c> threshold.
    /// </remarks>
    private const int MaxLinkHops = 40;

    /// <summary>
    /// Resolves the path to its real location, following symbolic links in <b>every</b> component.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path does not have to exist: unresolvable components are left as they are, so the result
    /// is always at least <see cref="Path.GetFullPath(string)"/>. That matters because this also runs
    /// on paths that are about to be created.
    /// </para>
    /// <para>
    /// 🔴 MEASURED — <b>a link's target has to be walked from the root again.</b> Following the chain
    /// and taking the target as it is leaves the target's OWN prefix unresolved: on macOS a link
    /// pointing at <c>/var/folders/…/repo</c> resolves to exactly that string, and <c>/var</c> is
    /// still a symlink. git's answer (<c>/private/var/…</c>) then compares as a different directory
    /// and the repository is reported as a linked worktree — the same symptom this class exists to
    /// prevent, one level deeper. So the target's segments go back to the FRONT of the queue.
    /// </para>
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
        LinkedList<string> pending = new(SplitSegments(full[root.Length..]));
        int hops = 0;

        while (pending.First is { } first)
        {
            pending.RemoveFirst();

            string candidate = Path.Combine(current, first.Value);
            FileSystemInfo? target = hops < MaxLinkHops ? TryResolveLink(candidate) : null;

            if (target is null)
            {
                current = candidate;
                continue;
            }

            hops++;

            // An absolute target starts from its own root; a relative one has already been resolved
            // against the link's directory (see TryResolveLink), so both arrive here absolute.
            string resolved = Path.GetFullPath(target.FullName);
            string targetRoot = Path.GetPathRoot(resolved) ?? root;

            current = targetRoot;

            foreach (string segment in SplitSegments(resolved[targetRoot.Length..]).Reverse())
            {
                pending.AddFirst(segment);
            }
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static string[] SplitSegments(string path) =>
        path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Reads a single component's link target; <see langword="null"/> when it is not a link.
    /// </summary>
    /// <remarks>
    /// MEASURED — with <c>returnFinalTarget: false</c> a <b>relative</b> link target is resolved
    /// against the link's own directory, not against the process's working directory, so
    /// <see cref="FileSystemInfo.FullName"/> can be used as is. The chain is walked by hand rather
    /// than with <c>returnFinalTarget: true</c> so that a broken link leaves the path untouched
    /// instead of throwing.
    /// </remarks>
    private static FileSystemInfo? TryResolveLink(string path)
    {
        try
        {
            return Directory.ResolveLinkTarget(path, returnFinalTarget: false)
                   ?? File.ResolveLinkTarget(path, returnFinalTarget: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A path we cannot inspect is left as it is; the caller loses normalisation, not
            // correctness.
            return null;
        }
    }
}
