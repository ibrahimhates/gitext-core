using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>The state of a submodule (P07-T19).</summary>
public enum SubmoduleStatusKind
{
    /// <summary>Registered but empty — it needs <c>init</c>/<c>update</c>.</summary>
    NotInitialized,

    /// <summary>It matches the recorded commit.</summary>
    UpToDate,

    /// <summary>It is on a different commit from the one the superproject expects.</summary>
    Modified,

    /// <summary>There is a merge conflict.</summary>
    Conflicted,
}

/// <summary>A submodule (P07-T19).</summary>
public sealed record Submodule
{
    public required RepositoryPath Path { get; init; }

    /// <summary>The commit the submodule is on.</summary>
    public required string ObjectId { get; init; }

    public required SubmoduleStatusKind Status { get; init; }

    /// <summary>The description git gives in parentheses (a tag/description).</summary>
    public string Describe { get; init; } = string.Empty;

    /// <summary>The absolute path for "entering" the submodule and examining it as a separate repository.</summary>
    public string ResolvePath(string repositoryRoot) => Path.ToAbsolutePath(repositoryRoot);
}

/// <summary>Submodule operations (P07-T19).</summary>
public interface ISubmoduleReader
{
    Task<IReadOnlyList<Submodule>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary><c>git submodule update --init</c>.</summary>
    Task InitializeAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        bool recursive = false,
        CancellationToken cancellationToken = default);

    /// <summary><c>git submodule sync</c>: propagates URL changes.</summary>
    Task SyncAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The <c>git submodule status</c> reader (P07-T19).
/// </summary>
/// <remarks>
/// The output lines take the form <c>&lt;marker&gt;&lt;sha&gt; &lt;path&gt; (&lt;describe&gt;)</c>.
/// The leading marker gives the state: <c>-</c> uninitialised, <c>+</c> on a different commit,
/// <c>U</c> conflicted, <b>a space</b> up to date.
/// </remarks>
public sealed class SubmoduleReader : ISubmoduleReader
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public SubmoduleReader(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    /// <summary>
    /// Lists the submodules; an empty list when the repository has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>MEASURED (P12-T13):</b> <c>git submodule status</c> costs <b>12–49 ms</b> in a
    /// repository that has <b>no submodules at all</b> — it is one of the few git commands that is
    /// still a shell script, so the price is the shell, not the work. Three benchmark repositories
    /// (linux, git, this one) all have no <c>.gitmodules</c>, and the left panel refreshes on every
    /// repository change; that would be tens of milliseconds paid on every refresh for a list that
    /// is always empty.
    /// </para>
    /// <para>
    /// Checking for <c>.gitmodules</c> first is a file-existence test. It is also the right
    /// question: a repository without that file has no submodules by definition, and one whose
    /// file lists them still goes through git for their status.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<Submodule>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (!HasSubmodules(workingDirectory))
        {
            return [];
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "submodule", "status"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    /// <summary>
    /// Does the working directory declare submodules at all?
    /// </summary>
    /// <remarks>
    /// A failure to look (an unreadable path) answers <see langword="true"/>: git is then asked and
    /// gives the real answer. Guessing "no" on an error would hide submodules that are there.
    /// </remarks>
    private static bool HasSubmodules(string workingDirectory)
    {
        try
        {
            return File.Exists(Path.Combine(workingDirectory, ".gitmodules"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    internal static IReadOnlyList<Submodule> Parse(string output)
    {
        List<Submodule> modules = [];

        foreach (string raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.TrimEnd('\r');

            if (line.Length < 2)
            {
                continue;
            }

            SubmoduleStatusKind status = line[0] switch
            {
                '-' => SubmoduleStatusKind.NotInitialized,
                '+' => SubmoduleStatusKind.Modified,
                'U' => SubmoduleStatusKind.Conflicted,
                _ => SubmoduleStatusKind.UpToDate,
            };

            // In the up-to-date state the line starts with a space; in the others, with a marker.
            string body = line[0] is '-' or '+' or 'U' ? line[1..] : line.TrimStart();

            int space = body.IndexOf(' ', StringComparison.Ordinal);

            if (space <= 0)
            {
                continue;
            }

            string objectId = body[..space];
            string rest = body[(space + 1)..];

            // The path can contain spaces; the describe part is inside the LAST parentheses.
            string describe = string.Empty;
            int open = rest.LastIndexOf(" (", StringComparison.Ordinal);

            if (open > 0 && rest.EndsWith(')'))
            {
                describe = rest[(open + 2)..^1];
                rest = rest[..open];
            }

            if (RepositoryPath.TryParse(rest, out RepositoryPath path))
            {
                modules.Add(new Submodule
                {
                    Path = path,
                    ObjectId = objectId,
                    Status = status,
                    Describe = describe,
                });
            }
        }

        return modules;
    }

    public Task InitializeAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["submodule", "update", "--init"];

        if (recursive)
        {
            arguments.Add("--recursive");
        }

        if (path is { } target && !target.IsEmpty)
        {
            arguments.Add("--");
            arguments.Add(target.Value);
        }

        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }

    public Task SyncAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["submodule", "sync"];

        if (path is { } target && !target.IsEmpty)
        {
            arguments.Add("--");
            arguments.Add(target.Value);
        }

        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }
}
