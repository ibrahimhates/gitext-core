using GitExt.Core.Git;

namespace GitExt.Core;

public enum ArchiveFormat
{
    Zip,
    Tar,
}

public sealed record ArchiveOptions
{
    public required string Revision { get; init; }

    public required string OutputPath { get; init; }

    public ArchiveFormat Format { get; init; } = ArchiveFormat.Zip;

    /// <summary>Pathspecs typed by the user; globs allowed, as in GitExtensions.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>Archive only the files changed from this revision to <see cref="Revision"/>.</summary>
    public string? ChangedSince { get; init; }
}

public sealed class NothingToArchiveException : Exception
{
    public NothingToArchiveException()
        : base("No file is left to archive.")
    {
    }
}

public interface IArchiveWriter
{
    /// <exception cref="NothingToArchiveException">The change filter left no file.</exception>
    /// <exception cref="GitException">git refused, e.g. a path that matches nothing.</exception>
    Task ArchiveAsync(string workingDirectory, ArchiveOptions options, CancellationToken cancellationToken = default);
}

public sealed class ArchiveWriter : IArchiveWriter
{
    private readonly IGitProcessRunner _runner;

    public ArchiveWriter(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task ArchiveAsync(
        string workingDirectory,
        ArchiveOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputPath);

        IReadOnlyList<string> pathspecs = options.ChangedSince is { Length: > 0 } since
            ? await ChangedFilesAsync(workingDirectory, since, options, cancellationToken).ConfigureAwait(false)
            : options.Paths;

        // A failed `git archive -o` still leaves its output file behind (measured: 0 bytes).
        string temporary = options.OutputPath + ".part";

        List<string> arguments =
        [
            "archive",
            options.Format == ArchiveFormat.Tar ? "--format=tar" : "--format=zip",
            "-o",
            temporary,
            options.Revision,
        ];

        if (pathspecs.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(pathspecs);
        }

        try
        {
            await _runner
                .RunCheckedAsync(
                    new GitCommand
                    {
                        WorkingDirectory = workingDirectory,
                        Arguments = arguments,
                        Timeout = TimeSpan.FromMinutes(30),
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            File.Move(temporary, options.OutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private async Task<IReadOnlyList<string>> ChangedFilesAsync(
        string workingDirectory,
        string since,
        ArchiveOptions options,
        CancellationToken cancellationToken)
    {
        // Deleted files are left out: a path missing from the archived revision makes git archive
        // fail as a whole (measured: "pathspec did not match any files").
        List<string> arguments = ["diff", "--name-only", "--diff-filter=d", "-z", since, options.Revision];

        if (options.Paths.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(options.Paths);
        }

        GitResult result = await _runner
            .RunCheckedAsync(new GitCommand { WorkingDirectory = workingDirectory, Arguments = arguments }, cancellationToken)
            .ConfigureAwait(false);

        List<string> files =
        [
            .. result.GetStandardOutputText()
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => ":(literal)" + path),
        ];

        return files.Count > 0 ? files : throw new NothingToArchiveException();
    }
}
