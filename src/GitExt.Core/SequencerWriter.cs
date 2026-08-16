using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// The kind of operation that replays commits (P07-T07, P07-T08).
/// </summary>
/// <remarks>
/// <c>cherry-pick</c> and <c>revert</c> are driven by the <b>same sequencer</b> inside git: the same
/// state files, the same <c>--continue</c>/<c>--skip</c>/<c>--abort</c> trio, the same conflict
/// behaviour. That is why they are handled by a single writer.
/// </remarks>
public enum SequencerOperation
{
    /// <summary>Commit'i buraya uygula.</summary>
    CherryPick,

    /// <summary>Produce a new commit that undoes what the commit did.</summary>
    Revert,
}

/// <summary>Cherry-pick / revert options (P07-T07, P07-T08).</summary>
public sealed record SequencerOptions
{
    public required SequencerOperation Operation { get; init; }

    /// <summary>The commits to apply — in the order they are given.</summary>
    public required IReadOnlyList<string> Commits { get; init; }

    /// <summary>
    /// <c>--no-commit</c>: prepare the changes but do not commit.
    /// </summary>
    /// <remarks>
    /// Used together with multiple commits, all of them pile into a single preparation.
    /// </remarks>
    public bool NoCommit { get; init; }

    /// <summary>
    /// <c>-x</c>: adds a <i>"(cherry picked from commit …)"</i> line to the message.
    /// </summary>
    /// <remarks>
    /// Only meaningful for cherry-pick; revert already writes its source into the message.
    /// </remarks>
    public bool RecordOrigin { get; init; }

    /// <summary>
    /// Which parent counts as the "mainline" for a merge commit (1-based).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — reverting a merge commit without <c>-m</c> gives rc=128</b>
    /// (<c>is a merge but no -m option was given</c>). Which parent to revert against is not something
    /// git can guess; the user has to choose.
    /// </remarks>
    public int? MainlineParent { get; init; }
}

/// <summary>Cherry-pick / revert sonucu (P07-T07, P07-T08).</summary>
public sealed record SequencerResult
{
    public required SequencerOperation Operation { get; init; }

    /// <summary>The position before the operation — the undo information comes from this.</summary>
    public required SafetyPoint SafetyPoint { get; init; }

    /// <summary>Did it stop on a conflict?</summary>
    public bool HasConflicts => ConflictedPaths.Count > 0;

    public IReadOnlyList<RepositoryPath> ConflictedPaths { get; init; } = [];

    /// <summary>The number of commits created.</summary>
    public int CommitsCreated { get; init; }

    /// <summary>
    /// Does the user still have to commit?
    /// </summary>
    /// <remarks>
    /// <c>--no-commit</c> returns "success" but <c>HEAD</c> does not advance — the same trap as
    /// <c>--squash</c> in P06-T11.
    /// </remarks>
    public bool RequiresCommit { get; init; }
}

/// <summary>Cherry-pick ve revert (P07-T07, P07-T08).</summary>
public interface ISequencerWriter
{
    Task<SequencerResult> RunAsync(
        string workingDirectory,
        SequencerOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Verilen commit bir merge commit'i mi? (<c>-m</c> gerekiyor mu?)</summary>
    Task<int> CountParentsAsync(
        string workingDirectory,
        string commit,
        CancellationToken cancellationToken = default);

    string DescribeCommand(SequencerOptions options);
}

/// <summary>
/// The <c>git cherry-pick</c> and <c>git revert</c> wrapper (P07-T07, P07-T08).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>A conflict is not an error, it is a STATE.</b> Both give rc=1 on a conflict and write their
/// text to <c>stdout</c> (the same lesson as merge in P06-T11 and pull in P06-T07).
/// The decision is made by looking at the <b>index</b>, not at the text: <c>diff --diff-filter=U</c>.
/// </para>
/// <para>
/// MEASURED — the state left behind on a conflict: <c>.git/CHERRY_PICK_HEAD</c> (or
/// <c>REVERT_HEAD</c>) plus <c>MERGE_MSG</c>. With multiple commits, <c>.git/sequencer/</c> as well.
/// The resolution flow connects to P07-T05.
/// </para>
/// </remarks>
public sealed class SequencerWriter : ISequencerWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly ISafetyPointRecorder _safety;

    public SequencerWriter(IGitWriter writer, IGitProcessRunner runner, ISafetyPointRecorder safety)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(safety);

        _writer = writer;
        _runner = runner;
        _safety = safety;
    }

    public async Task<SequencerResult> RunAsync(
        string workingDirectory,
        SequencerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Commits.Count == 0)
        {
            throw new ArgumentException("En az bir commit gerekli.", nameof(options));
        }

        SafetyPoint point = await _safety
            .CaptureAsync(workingDirectory, Verb(options.Operation), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _writer
                .RunAsync(workingDirectory, BuildArguments(options), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException)
        {
            IReadOnlyList<RepositoryPath> conflicts =
                await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

            // Real errors (an unknown commit, a dirty tree, a missing -m) propagate as they are.
            if (conflicts.Count == 0)
            {
                throw;
            }

            return new SequencerResult
            {
                Operation = options.Operation,
                SafetyPoint = point,
                ConflictedPaths = conflicts,
            };
        }

        string after = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        bool moved = !string.Equals(point.ObjectId, after, StringComparison.Ordinal);

        int created = moved
            ? await CountBetweenAsync(workingDirectory, point.ObjectId, after, cancellationToken)
                .ConfigureAwait(false)
            : 0;

        return new SequencerResult
        {
            Operation = options.Operation,
            SafetyPoint = point,
            CommitsCreated = created,
            RequiresCommit = !moved
                && await HasStagedChangesAsync(workingDirectory, cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<int> CountParentsAsync(
        string workingDirectory,
        string commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--parents", "-1", commit),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return 0;
        }

        // "<commit> <ebeveyn1> <ebeveyn2>…" — ilk alan commit'in kendisi.
        return Math.Max(
            0,
            result.GetStandardOutputText().Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length - 1);
    }

    public string DescribeCommand(SequencerOptions options) => Describe(options);

    /// <summary>Produces the command that will run (the "show the command" principle).</summary>
    public static string Describe(SequencerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return "git " + string.Join(' ', BuildArguments(options));
    }

    internal static string Verb(SequencerOperation operation) =>
        operation == SequencerOperation.Revert ? "revert" : "cherry-pick";

    private static IReadOnlyList<string> BuildArguments(SequencerOptions options)
    {
        List<string> arguments = [Verb(options.Operation)];

        if (options.MainlineParent is { } mainline)
        {
            arguments.Add("-m");
            arguments.Add(mainline.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (options.NoCommit)
        {
            arguments.Add("--no-commit");
        }

        if (options.RecordOrigin && options.Operation == SequencerOperation.CherryPick)
        {
            arguments.Add("-x");
        }

        // Revert can produce its own message; --no-edit keeps it from opening an editor.
        // (Cherry-pick already uses the source message and opens no editor.)
        if (options.Operation == SequencerOperation.Revert && !options.NoCommit)
        {
            arguments.Add("--no-edit");
        }

        arguments.AddRange(options.Commits);
        return arguments;
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

    private async Task<int> CountBetweenAsync(
        string workingDirectory,
        string from,
        string to,
        CancellationToken cancellationToken)
    {
        if (from.Length == 0)
        {
            return 0;
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--count", $"{from}..{to}"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
               && int.TryParse(
                   result.GetStandardOutputText().Trim(),
                   System.Globalization.CultureInfo.InvariantCulture,
                   out int count)
            ? count
            : 0;
    }

    private async Task<bool> HasStagedChangesAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // `--quiet` returns 1 when there is a difference; that is not an error (the pattern declared in P02).
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["diff", "--cached", "--quiet"],
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 1;
    }
}
