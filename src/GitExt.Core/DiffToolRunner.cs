using GitExt.Core.Git;

namespace GitExt.Core;

public sealed class NoDiffToolConfiguredException : Exception
{
    public NoDiffToolConfiguredException()
        : base("No diff tool is configured (diff.guitool, merge.guitool, diff.tool or merge.tool).")
    {
    }
}

public interface IDiffToolRunner
{
    /// <summary>The tool <c>difftool --gui</c> will pick, or <see langword="null"/> when none is set.</summary>
    Task<string?> GetConfiguredToolAsync(string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>GitExtensions' <c>OpenWithDifftoolDirDiff</c>; returns when the tool is closed.</summary>
    /// <exception cref="NoDiffToolConfiguredException">No tool is configured.</exception>
    Task RunDirDiffAsync(
        string workingDirectory,
        string firstRevision,
        string secondRevision,
        CancellationToken cancellationToken = default);
}

public sealed class DiffToolRunner : IDiffToolRunner
{
    // The order `difftool --gui` falls back through.
    private static readonly string[] _keys = ["diff.guitool", "merge.guitool", "diff.tool", "merge.tool"];

    private readonly IGitProcessRunner _runner;

    public DiffToolRunner(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<string?> GetConfiguredToolAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        foreach (string key in _keys)
        {
            GitResult result = await _runner.RunAsync(
                new GitCommand
                {
                    WorkingDirectory = workingDirectory,
                    Arguments = ["config", "--get", key],
                    SuccessExitCodes = [0, 1],
                },
                cancellationToken).ConfigureAwait(false);

            if (result.GetStandardOutputText().Trim() is { Length: > 0 } tool)
            {
                return tool;
            }
        }

        return null;
    }

    public async Task RunDirDiffAsync(
        string workingDirectory,
        string firstRevision,
        string secondRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondRevision);

        // Unconfigured, git walks its own list of tools; measured, the attempt crashed (exit 139).
        if (await GetConfiguredToolAsync(workingDirectory, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new NoDiffToolConfiguredException();
        }

        await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments =
                [
                    "difftool", "--gui", "--find-renames", "--find-copies", "--no-prompt", "--dir-diff",
                    firstRevision, secondRevision,
                ],
                Timeout = TimeSpan.FromHours(24),
            },
            cancellationToken).ConfigureAwait(false);
    }
}
