namespace GitExt.Core.Model;

/// <summary>
/// Location information for a discovered Git repository.
/// </summary>
/// <remarks>
/// If an instance of this type exists, the path has been verified to really be a Git repository.
/// </remarks>
public sealed class RepositoryLocation
{
    internal RepositoryLocation(
        string gitDirectory,
        string commonDirectory,
        string? workTreeRoot,
        string? superprojectWorkTree)
    {
        GitDirectory = gitDirectory;
        CommonDirectory = commonDirectory;
        WorkTreeRoot = workTreeRoot;
        SuperprojectWorkTree = superprojectWorkTree;
    }

    /// <summary>
    /// The git directory of this working tree — <c>HEAD</c> and <c>index</c> live here.
    /// </summary>
    /// <remarks>
    /// In a linked worktree this becomes <c>&lt;main&gt;/.git/worktrees/&lt;name&gt;</c>, which is
    /// not the same as <see cref="CommonDirectory"/>.
    /// </remarks>
    public string GitDirectory { get; }

    /// <summary>
    /// The shared git directory — <b>refs, objects and config live here</b>.
    /// </summary>
    /// <remarks>
    /// In a normal repository this equals <see cref="GitDirectory"/>. In worktrees it differs, and
    /// everything that reads refs/objects must use <b>this</b> — not the worktree-specific directory.
    /// </remarks>
    public string CommonDirectory { get; }

    /// <summary>
    /// The root of the working tree. <see langword="null"/> in a bare repository.
    /// </summary>
    public string? WorkTreeRoot { get; }

    /// <summary>
    /// The superproject's working tree if this repository is a submodule; otherwise <see langword="null"/>.
    /// </summary>
    public string? SuperprojectWorkTree { get; }

    /// <summary>Is this a repository without a working tree (bare)?</summary>
    public bool IsBare => WorkTreeRoot is null;

    /// <summary>
    /// Is this a linked worktree (created with <c>git worktree add</c>)?
    /// </summary>
    public bool IsLinkedWorkTree =>
        !string.Equals(GitDirectory, CommonDirectory, StringComparison.Ordinal);

    /// <summary>Is this repository a submodule?</summary>
    public bool IsSubmodule => SuperprojectWorkTree is not null;

    /// <summary>
    /// The directory commands are run in.
    /// </summary>
    /// <remarks>
    /// The working tree root if there is one; the git directory itself in a bare repository.
    /// </remarks>
    public string WorkingDirectory => WorkTreeRoot ?? GitDirectory;

    public override string ToString() => WorkingDirectory;
}
