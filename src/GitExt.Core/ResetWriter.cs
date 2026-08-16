using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// <c>git reset</c> mode (P07-T06).
/// </summary>
/// <remarks>
/// Order follows GitExtensions' <c>FormResetCurrentBranch</c> (§ 9): least destructive to most.
/// </remarks>
public enum ResetMode
{
    /// <summary>
    /// <c>--soft</c>: only <c>HEAD</c> moves.
    /// </summary>
    /// <remarks>
    /// MEASURED: the file stays on disk with its <b>new</b> content, and appears
    /// <b>staged</b> in the index (<c>M.</c>) — i.e. the commit is undone and the change
    /// waits ready to be committed. The tool for the "split a commit" or "fix the message"
    /// scenario.
    /// </remarks>
    Soft,

    /// <summary>
    /// <c>--mixed</c> (git's default): <c>HEAD</c> and the index move.
    /// </summary>
    /// <remarks>
    /// MEASURED: the file stays on disk with the new content, but is now <b>unstaged</b>
    /// (<c>.M</c>). The change remains, but needs to be re-selected and staged.
    /// </remarks>
    Mixed,

    /// <summary>
    /// <c>--hard</c>: <c>HEAD</c>, the index, <b>and the working tree</b> move.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>DESTRUCTIVE.</b> MEASURED: the file reverted to its old content and the working
    /// tree is <b>completely clean</b>. Every uncommitted change is <b>lost</b>, and the
    /// reflog does not bring it back — the reflog only holds commits. This is why it requires
    /// a separate confirmation.
    /// </remarks>
    Hard,

    /// <summary>
    /// <c>--keep</c>: <c>HEAD</c> moves but local changes are preserved.
    /// </summary>
    /// <remarks>
    /// MEASURED: unrelated local changes survived. If there's a conflict, git refuses —
    /// i.e. the "ask first" version of <c>--hard</c>.
    /// </remarks>
    Keep,
}

/// <summary>Reset options (P07-T06).</summary>
public sealed record ResetOptions
{
    /// <summary>Commit or reference to reset to.</summary>
    public required string Target { get; init; }

    public ResetMode Mode { get; init; } = ResetMode.Mixed;
}

/// <summary>
/// Preview of <b>what a reset will do</b> (P07-T06).
/// </summary>
/// <remarks>
/// The plan explicitly asks for this: <i>"A dialog that clearly explains what each mode will
/// do: which commits will be lost, what will happen to the working directory."</i>
/// </remarks>
public sealed record ResetPreview
{
    /// <summary>Commits after the target, i.e. those that will drop off <c>HEAD</c>.</summary>
    public IReadOnlyList<string> DroppedCommits { get; init; } = [];

    public int DroppedCount => DroppedCommits.Count;

    /// <summary>Are there uncommitted changes in the working tree?</summary>
    public bool HasUncommittedChanges { get; init; }

    /// <summary>Does the target resolve to a valid commit?</summary>
    public required bool IsTargetValid { get; init; }

    /// <summary>Full SHA of the target.</summary>
    public string TargetObjectId { get; init; } = string.Empty;

    /// <summary>
    /// Would this mode cause an <b>unrecoverable</b> loss?
    /// </summary>
    /// <remarks>
    /// Dropped commits stay in the reflog and can be recovered; what's actually unrecoverable
    /// is the <b>uncommitted</b> changes that <c>--hard</c> deletes.
    /// </remarks>
    public bool LosesUncommittedWork(ResetMode mode) =>
        mode == ResetMode.Hard && HasUncommittedChanges;
}

/// <summary>Reset operations (P07-T06).</summary>
public interface IResetWriter
{
    Task<SafetyPoint> ResetAsync(
        string workingDirectory,
        ResetOptions options,
        CancellationToken cancellationToken = default);

    Task<ResetPreview> PreviewAsync(
        string workingDirectory,
        string target,
        CancellationToken cancellationToken = default);

    string DescribeCommand(ResetOptions options);
}

/// <summary>
/// Wrapper around <c>git reset</c> (P07-T06).
/// </summary>
public sealed class ResetWriter : IResetWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly ISafetyPointRecorder _safety;

    public ResetWriter(IGitWriter writer, IGitProcessRunner runner, ISafetyPointRecorder safety)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(safety);

        _writer = writer;
        _runner = runner;
        _safety = safety;
    }

    /// <returns>Position <b>before</b> the operation — undo information is given via this.</returns>
    public async Task<SafetyPoint> ResetAsync(
        string workingDirectory,
        ResetOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Target);

        // Phase rule: position is recorded BEFORE every history-altering operation.
        SafetyPoint point = await _safety
            .CaptureAsync(workingDirectory, "reset", cancellationToken)
            .ConfigureAwait(false);

        await _writer
            .RunAsync(workingDirectory, BuildArguments(options), cancellationToken)
            .ConfigureAwait(false);

        return point;
    }

    public async Task<ResetPreview> PreviewAsync(
        string workingDirectory,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        GitResult resolved = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "--verify", "--quiet", $"{target}^{{commit}}"),
            cancellationToken).ConfigureAwait(false);

        if (!resolved.IsSuccess || resolved.GetStandardOutputText().Trim().Length == 0)
        {
            return new ResetPreview { IsTargetValid = false };
        }

        // Everything from the target up to HEAD is what will drop. Not `--oneline`; the
        // subject is read separately: parsing human-formatted output goes against ADR-0002.
        GitResult dropped = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory, "log", "--format=%x1e%H%x00%s", $"{target}..HEAD"),
            cancellationToken).ConfigureAwait(false);

        List<string> commits = [];

        if (dropped.IsSuccess)
        {
            foreach (string record in dropped.GetStandardOutputText()
                         .Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = record.Trim('\n', '\r').Split('\0');

                if (fields.Length >= 2 && fields[0].Length > 0)
                {
                    commits.Add(fields[1]);
                }
            }
        }

        GitResult status = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["status", "--porcelain=v2", "-z", "--untracked-files=no"],
            },
            cancellationToken).ConfigureAwait(false);

        return new ResetPreview
        {
            IsTargetValid = true,
            TargetObjectId = resolved.GetStandardOutputText().Trim(),
            DroppedCommits = commits,
            HasUncommittedChanges = status.IsSuccess && status.GetStandardOutputText().Length > 0,
        };
    }

    public string DescribeCommand(ResetOptions options) => Describe(options);

    /// <summary>Produces the command to be run ("show the command" principle).</summary>
    public static string Describe(ResetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return "git " + string.Join(' ', BuildArguments(options));
    }

    /// <remarks>
    /// 🔴 <b>The POSITION of the separator here differs from other commands.</b> The first
    /// draft used the <c>… -- &lt;target&gt;</c> idiom copied from merge, and MEASUREMENT
    /// killed it with <c>fatal: Cannot do hard reset with paths</c>: for <c>reset</c>,
    /// what follows <c>--</c> means a <b>path</b>, not a commit.
    /// <para>
    /// The correct approach is to put the separator at the <b>end</b>. And it's not
    /// unnecessary: when a file shares a name with a branch, calling without the separator
    /// gives <c>fatal: ambiguous argument … both revision and filename</c>; the trailing
    /// <c>--</c> removes the ambiguity (measured).
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> BuildArguments(ResetOptions options) =>
    [
        "reset",
        options.Mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Hard => "--hard",
            ResetMode.Keep => "--keep",
            _ => "--mixed",
        },
        options.Target,
        "--",
    ];
}
