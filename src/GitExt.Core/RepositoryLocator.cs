using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Discovers a Git repository from a file system path (P02-T06).
/// </summary>
public interface IRepositoryLocator
{
    /// <summary>
    /// Finds the repository containing the given path.
    /// </summary>
    /// <remarks>
    /// The path may be one of the repository's subdirectories; git searches upwards.
    /// </remarks>
    /// <exception cref="GitException">
    /// With <see cref="GitFailureKind.NotARepository"/> when the path is not a repository.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">When the directory does not exist.</exception>
    Task<RepositoryLocation> LocateAsync(string path, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRepositoryLocator"/>
public sealed class RepositoryLocator : IRepositoryLocator
{
    private readonly IGitProcessRunner _runner;

    public RepositoryLocator(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<RepositoryLocation> LocateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string directory = Path.GetFullPath(path);

        if (File.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory)
                        ?? throw new DirectoryNotFoundException($"Dizin belirlenemedi: {path}");
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        // Everything obtainable in a single call. --show-toplevel CANNOT BE HERE:
        // in a bare repository it returns 128 with "fatal: this operation must be run in a work tree"
        // and breaks the whole call. Verified against real git.
        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(
                directory,
                "rev-parse",
                "--absolute-git-dir",
                "--git-common-dir",
                "--is-bare-repository"),
            cancellationToken).ConfigureAwait(false);

        string[] lines = SplitLines(result.GetStandardOutputText());

        if (lines.Length < 3)
        {
            throw new GitException(
                GitFailureKind.Unknown,
                "git rev-parse did not return the expected fields.",
                result.Command.ToDisplayString(),
                result.ExitCode,
                result.StandardError);
        }

        string gitDirectory = Path.GetFullPath(lines[0]);

        // --git-common-dir returns a RELATIVE path (".git") in a normal repository, and an absolute one
        // in a worktree. --path-format=absolute would solve it but needs git 2.31+; our minimum is 2.30.
        // So we resolve it ourselves against the working directory.
        string commonDirectory = Path.GetFullPath(lines[1], directory);

        bool isBare = string.Equals(lines[2], "true", StringComparison.OrdinalIgnoreCase);

        if (isBare)
        {
            return new RepositoryLocation(gitDirectory, commonDirectory, null, null);
        }

        (string workTreeRoot, string? superproject) =
            await ReadWorkTreeAsync(directory, cancellationToken).ConfigureAwait(false);

        return new RepositoryLocation(gitDirectory, commonDirectory, workTreeRoot, superproject);
    }

    /// <summary>
    /// Reads the working tree's root and — when the repository is a submodule — the superproject's tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two are obtained in <b>a single call</b> (P09-T06). Asked separately, opening a repository
    /// was starting three processes instead of two; process startup costs a few ms on Linux but is many
    /// times higher on Windows, and that is exactly ADR-0002's known weakness.
    /// </para>
    /// <para>
    /// ⚠️ The combination works because <c>--show-superproject-working-tree</c> <b>prints no line at
    /// all</b> in a repository that is not a submodule — not an error, just empty output and 0. So the
    /// line count makes the distinction: one line means the root alone, two lines mean the root plus
    /// the superproject. Measured against real git in both cases.
    /// </para>
    /// <para>
    /// <c>--show-toplevel</c> cannot be added to the first call above: in a bare repository it breaks
    /// the whole call with 128. That is why the two calls come down from three to two, not to one.
    /// </para>
    /// </remarks>
    private async Task<(string WorkTreeRoot, string? Superproject)> ReadWorkTreeAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        string output = await _runner.RunForTextAsync(
            GitCommand.Create(
                directory,
                "rev-parse",
                "--show-toplevel",
                "--show-superproject-working-tree"),
            cancellationToken).ConfigureAwait(false);

        string[] lines = SplitLines(output);

        if (lines.Length == 0)
        {
            throw new GitException(
                GitFailureKind.Unknown,
                "git rev-parse did not return the root of the working tree.",
                "git rev-parse --show-toplevel --show-superproject-working-tree",
                exitCode: 0,
                standardError: string.Empty);
        }

        string root = Path.GetFullPath(lines[0]);

        string? superproject = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1])
            ? Path.GetFullPath(lines[1])
            : null;

        return (root, superproject);
    }

    private static string[] SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
