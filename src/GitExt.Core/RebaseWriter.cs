using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>Rebase options (P07-T09, P07-T10).</summary>
public sealed record RebaseOptions
{
    /// <summary>The branch or commit to replay onto.</summary>
    public required string Upstream { get; init; }

    /// <summary>
    /// <c>--onto</c>: the new base the commits will be moved to.
    /// </summary>
    /// <remarks>
    /// <c>upstream</c> decides "which commits move", <c>--onto</c> decides "where to". When the two
    /// differ, the branch's base has been changed.
    /// </remarks>
    public string? Onto { get; init; }

    /// <summary>Rebase edilecek dal; <see langword="null"/> ise mevcut dal.</summary>
    public string? Branch { get; init; }

    /// <summary>
    /// The interactive rebase steps; a plain rebase when <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<RebaseStep>? Steps { get; init; }

    /// <summary>The message the user typed for <c>reword</c>.</summary>
    public string? NewMessage { get; init; }

    /// <summary><c>--autostash</c>: set a dirty tree aside temporarily.</summary>
    /// <remarks>
    /// A rebase does not run on a dirty tree. Rather than sending the user back with "stash first",
    /// autostash does it itself and puts it back at the end.
    /// </remarks>
    public bool AutoStash { get; init; }

    public bool IsInteractive => Steps is { Count: > 0 };
}

/// <summary>How the rebase ended up (P07-T09).</summary>
public enum RebaseOutcome
{
    /// <summary>There was nothing to do.</summary>
    AlreadyUpToDate,

    /// <summary>It completed.</summary>
    Completed,

    /// <summary>It stopped on a conflict.</summary>
    Conflicted,

    /// <summary>It stopped for the user at an <c>edit</c> step.</summary>
    StoppedForEdit,
}

/// <summary>Rebase sonucu (P07-T09, P07-T10).</summary>
public sealed record RebaseResult
{
    public required RebaseOutcome Outcome { get; init; }

    public required SafetyPoint SafetyPoint { get; init; }

    public IReadOnlyList<RepositoryPath> ConflictedPaths { get; init; } = [];

    /// <summary>Which step we are on (<c>.git/rebase-merge/msgnum</c>).</summary>
    public int CurrentStep { get; init; }

    /// <summary>The total number of steps (<c>.git/rebase-merge/end</c>).</summary>
    public int TotalSteps { get; init; }

    public bool IsStopped => Outcome is RebaseOutcome.Conflicted or RebaseOutcome.StoppedForEdit;
}

/// <summary>Rebase operations (P07-T09, P07-T10).</summary>
public interface IRebaseWriter
{
    Task<RebaseResult> RebaseAsync(
        string workingDirectory,
        RebaseOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the commits to be moved, for populating the interactive rebase screen.
    /// </summary>
    Task<IReadOnlyList<RebaseStep>> ReadStepsAsync(
        string workingDirectory,
        string upstream,
        string? branch = null,
        CancellationToken cancellationToken = default);

    /// <summary>Skips the in-progress rebase (<c>--skip</c>).</summary>
    Task SkipAsync(string workingDirectory, CancellationToken cancellationToken = default);

    string DescribeCommand(RebaseOptions options);
}

/// <summary>
/// The <c>git rebase</c> wrapper (P07-T09, P07-T10).
/// </summary>
/// <remarks>
/// <para>
/// An interactive rebase is done by writing the todo list through <c>GIT_SEQUENCE_EDITOR</c> — see
/// <see cref="RebaseTodoSession"/>. The plan had marked this mechanism "prototype it at the start of
/// the phase"; it was measured and it works.
/// </para>
/// <para>
/// MEASURED — the state left behind on a conflict is <c>.git/rebase-merge/</c>: <c>head-name</c>
/// (the original branch), <c>onto</c>, <c>msgnum</c>/<c>end</c> (progress), <c>orig-head</c>. At an
/// <c>edit</c> step an <c>amend</c> file also appears and <c>HEAD</c> stays <b>detached</b> — which
/// is why <c>head-name</c> is what answers "which branch are we on".
/// </para>
/// </remarks>
public sealed class RebaseWriter : IRebaseWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly ISafetyPointRecorder _safety;

    public RebaseWriter(IGitWriter writer, IGitProcessRunner runner, ISafetyPointRecorder safety)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(safety);

        _writer = writer;
        _runner = runner;
        _safety = safety;
    }

    public async Task<RebaseResult> RebaseAsync(
        string workingDirectory,
        RebaseOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Upstream);

        if (options.Steps is { } steps && RebaseTodo.Validate(steps) is { } problem)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        SafetyPoint point = await _safety
            .CaptureAsync(workingDirectory, "rebase", cancellationToken)
            .ConfigureAwait(false);

        using RebaseTodoSession? session = options.IsInteractive
            ? RebaseTodoSession.Create(RebaseTodo.Render(options.Steps!), options.NewMessage)
            : null;

        try
        {
            await _writer.RunWithEnvironmentAsync(
                workingDirectory,
                BuildArguments(options),
                session?.Environment ?? NonInteractiveEditor,
                progress: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitException)
        {
            // A conflict and an `edit` stop are both STATES; real errors (a dirty tree, an unknown
            // upstream) must propagate as they are. The distinction comes from whether the rebase
            // directory exists.
            RebaseState? state =
                await ReadStateAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

            if (state is null)
            {
                throw;
            }

            IReadOnlyList<RepositoryPath> conflicts =
                await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

            return new RebaseResult
            {
                Outcome = conflicts.Count > 0
                    ? RebaseOutcome.Conflicted
                    : RebaseOutcome.StoppedForEdit,
                SafetyPoint = point,
                ConflictedPaths = conflicts,
                CurrentStep = state.Current,
                TotalSteps = state.Total,
            };
        }

        // Even with exit code 0 we may have stopped at an `edit` step: git prints "Stopped at …" and
        // exits SUCCESSFULLY in that case. The decision is again made by looking at the state.
        RebaseState? after = await ReadStateAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (after is not null)
        {
            IReadOnlyList<RepositoryPath> conflicts =
                await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

            return new RebaseResult
            {
                Outcome = conflicts.Count > 0
                    ? RebaseOutcome.Conflicted
                    : RebaseOutcome.StoppedForEdit,
                SafetyPoint = point,
                ConflictedPaths = conflicts,
                CurrentStep = after.Current,
                TotalSteps = after.Total,
            };
        }

        string head = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        return new RebaseResult
        {
            Outcome = string.Equals(head, point.ObjectId, StringComparison.Ordinal)
                ? RebaseOutcome.AlreadyUpToDate
                : RebaseOutcome.Completed,
            SafetyPoint = point,
        };
    }

    public async Task<IReadOnlyList<RebaseStep>> ReadStepsAsync(
        string workingDirectory,
        string upstream,
        string? branch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upstream);

        string range = $"{upstream}..{branch ?? "HEAD"}";

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory, "log", "--reverse", "--format=%x1e%H%x00%s", range),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return [];
        }

        List<RebaseStep> steps = [];

        foreach (string record in result.GetStandardOutputText()
                     .Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Trim('\n', '\r').Split('\0');

            if (fields.Length >= 2 && fields[0].Length > 0)
            {
                steps.Add(new RebaseStep { ObjectId = fields[0], Subject = fields[1] });
            }
        }

        return steps;
    }

    public Task SkipAsync(string workingDirectory, CancellationToken cancellationToken = default) =>
        _writer.RunWithEnvironmentAsync(
            workingDirectory,
            ["rebase", "--skip"],
            NonInteractiveEditor,
            progress: null,
            cancellationToken);

    public string DescribeCommand(RebaseOptions options) => Describe(options);

    /// <summary>Produces the command that will run (the "show the command" principle).</summary>
    public static string Describe(RebaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return "git " + string.Join(' ', BuildArguments(options));
    }

    private static IReadOnlyList<string> BuildArguments(RebaseOptions options)
    {
        List<string> arguments = ["rebase"];

        if (options.IsInteractive)
        {
            arguments.Add("--interactive");
        }

        if (options.AutoStash)
        {
            arguments.Add("--autostash");
        }

        if (options.Onto is { Length: > 0 } onto)
        {
            arguments.Add("--onto");
            arguments.Add(onto);
        }

        arguments.Add(options.Upstream);

        if (options.Branch is { Length: > 0 } branch)
        {
            arguments.Add(branch);
        }

        return arguments;
    }

    /// <summary>The environment that makes it impossible for an editor to lock up the UI.</summary>
    /// <remarks>
    /// <c>true</c> on Windows as well: git runs the editor through the bundled MSYS <c>sh</c>, where
    /// it is a builtin. Measured — see <c>ConflictResolver.NonInteractiveEditor</c>.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> NonInteractiveEditor =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_EDITOR"] = "true",
        };

    private sealed record RebaseState(int Current, int Total, string BranchName);

    /// <summary>
    /// Reads the in-progress rebase's state from the files under <c>.git</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ The path is obtained with <c>--absolute-git-dir</c>: <c>--git-path</c> returns a relative
    /// path that depends on the working directory (the lesson of P05-T13).
    /// </remarks>
    private async Task<RebaseState?> ReadStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "--absolute-git-dir"),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return null;
        }

        string gitDirectory = result.GetStandardOutputText().Trim();

        foreach (string name in new[] { "rebase-merge", "rebase-apply" })
        {
            string directory = Path.Combine(gitDirectory, name);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            return new RebaseState(
                ReadNumber(directory, "msgnum"),
                ReadNumber(directory, "end"),
                ReadLine(directory, "head-name"));
        }

        return null;
    }

    private static int ReadNumber(string directory, string fileName) =>
        int.TryParse(ReadLine(directory, fileName), CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;

    private static string ReadLine(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);

        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (IOException)
        {
            // The rebase may be advancing at that very moment; failing to read it is not an error.
            return string.Empty;
        }
    }

    private async Task<IReadOnlyList<RepositoryPath>> ReadConflictsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return [];
        }

        List<RepositoryPath> paths = [];

        foreach (string value in result.GetStandardOutputText()
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (RepositoryPath.TryParse(value, out RepositoryPath path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private async Task<string> ReadHeadAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "HEAD"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? result.GetStandardOutputText().Trim() : string.Empty;
    }
}
