using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Merge strategy (P06-T11).</summary>
/// <remarks>
/// Order follows GitExtensions' <c>FormMergeBranch</c> (§ 9): <i>Keep single branch (fast forward)</i>
/// · <i>Always create a new merge commit</i> · <i>Squash commits</i>.
/// </remarks>
public enum MergeStrategy
{
    /// <summary>Fast-forward when possible, otherwise a merge commit (git's default).</summary>
    Default,

    /// <summary><c>--no-ff</c>: always create a merge commit.</summary>
    NoFastForward,

    /// <summary>
    /// <c>--squash</c>: changes are staged as a single change.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — does NOT commit.</b> git prints <i>"Squash commit -- not updating HEAD"</i>
    /// and returns exit code <b>0</b>; <c>HEAD</c> stays put and the changes sit
    /// in the index. Reporting "success" and leaving it there would mean the user thinks
    /// they've merged and fails to commit. <see cref="MergeResult.RequiresCommit"/> exists for this.
    /// </remarks>
    Squash,

    /// <summary><c>--ff-only</c>: don't do anything if it can't fast-forward.</summary>
    FastForwardOnly,
}

/// <summary>How the merge concluded (P06-T11).</summary>
public enum MergeOutcome
{
    /// <summary>There was nothing to do.</summary>
    AlreadyUpToDate,

    /// <summary>Fast-forwarded; no new commit was created.</summary>
    FastForward,

    /// <summary>A merge commit was created.</summary>
    MergeCommit,

    /// <summary>Changes were staged but <b>not committed</b>.</summary>
    Staged,

    /// <summary>Stopped with a conflict.</summary>
    Conflicted,
}

/// <summary>Merge options (P06-T11).</summary>
public sealed record MergeOptions
{
    /// <summary>Branch or commit to merge.</summary>
    public required string Source { get; init; }

    public MergeStrategy Strategy { get; init; }

    /// <summary>Custom merge message; <see langword="null"/> uses git's default.</summary>
    /// <remarks>
    /// The message could have been passed via <b>stdin</b> instead of as an argument, but
    /// <c>git merge</c> only accepts the message via <c>-m</c>. For messages containing
    /// newlines, <c>-m</c> is given multiple times (git joins them as paragraphs).
    /// </remarks>
    public string? Message { get; init; }

    /// <summary><c>--no-commit</c>: merge but don't commit.</summary>
    public bool NoCommit { get; init; }

    /// <summary><c>--allow-unrelated-histories</c>.</summary>
    public bool AllowUnrelatedHistories { get; init; }
}

/// <summary>Merge result (P06-T11).</summary>
public sealed record MergeResult
{
    public required MergeOutcome Outcome { get; init; }

    /// <summary><c>HEAD</c> before the merge.</summary>
    public required string HeadBefore { get; init; }

    /// <summary><c>HEAD</c> after the merge.</summary>
    public required string HeadAfter { get; init; }

    /// <summary>Unresolved files.</summary>
    public IReadOnlyList<string> ConflictedPaths { get; init; } = [];

    public bool HasConflicts => ConflictedPaths.Count > 0;

    /// <summary>
    /// Does the user still need to commit?
    /// </summary>
    /// <remarks>
    /// 🔴 <c>--squash</c> and <c>--no-commit</c> return "success" but <c>HEAD</c> does not
    /// advance. A screen that doesn't say so would leave the user with a half-finished operation.
    /// </remarks>
    public bool RequiresCommit => Outcome == MergeOutcome.Staged;

    /// <summary>Commit message draft prepared by git (<c>SQUASH_MSG</c>/<c>MERGE_MSG</c>).</summary>
    public string? SuggestedMessage { get; init; }

    /// <summary>
    /// Command that undoes what was done; <see langword="null"/> if <c>HEAD</c> did not advance.
    /// </summary>
    /// <remarks>
    /// The <b>hash</b> is written, not <c>ORIG_HEAD</c>: a later merge/reset overwrites it, and
    /// if the user ran the command afterward it would return them to somewhere else entirely
    /// (the lesson from P06-T07).
    /// </remarks>
    public string? RecoveryCommand => string.Equals(HeadBefore, HeadAfter, StringComparison.Ordinal)
        ? null
        : $"git reset --hard {HeadBefore}";
}

/// <summary>Preview of what the merge will do (P06-T11).</summary>
public sealed record MergePreview
{
    /// <summary>Is there anything to do?</summary>
    public required bool HasChanges { get; init; }

    /// <summary>Can it fast-forward (common ancestor = <c>HEAD</c>)?</summary>
    public required bool CanFastForward { get; init; }

    /// <summary>Is there a common ancestor? If not, the histories are unrelated.</summary>
    public required bool HasCommonAncestor { get; init; }

    /// <summary>Number of commits the source is ahead of <c>HEAD</c>.</summary>
    public int Ahead { get; init; }
}

/// <summary>Merge operations (P06-T11, P06-T12).</summary>
public interface IMergeWriter
{
    /// <summary>Merges and returns <b>what happened</b>.</summary>
    Task<MergeResult> MergeAsync(
        string workingDirectory,
        MergeOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the pre-merge state — to populate the screen.</summary>
    Task<MergePreview> PreviewAsync(
        string workingDirectory,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts an in-progress merge (<c>git merge --abort</c>, P06-T12).
    /// </summary>
    Task<string> AbortAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Produces the command that will be run ("show the command" principle).</summary>
    string DescribeCommand(MergeOptions options);
}

/// <summary>
/// <c>git merge</c> wrapper (P06-T11, P06-T12).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>MEASURED — conflict text is on <c>stdout</c>.</b> The <c>CONFLICT (content): …</c> and
/// <c>Automatic merge failed…</c> lines are written to stdout, stderr is <b>empty</b>. Since the
/// error classifier only looks at stderr it says <c>Unknown</c> — the same trap was hit with
/// pull in P06-T07.
/// </para>
/// <para>
/// → The conflict decision looks at <b>state, not text</b>: <c>diff --diff-filter=U</c>. The
/// same rationale applies to the whole result; what happened is computed from <c>HEAD</c>'s
/// before/after and the index state.
/// </para>
/// </remarks>
public sealed class MergeWriter : IMergeWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public MergeWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<MergeResult> MergeAsync(
        string workingDirectory,
        MergeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Source);

        string before = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        try
        {
            await _writer
                .RunAsync(workingDirectory, BuildArguments(options), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException)
        {
            // A conflict is not an error, it's a STATE. But real errors (dirty tree, unknown
            // ref, unrelated histories) must still propagate up — the distinction is made by
            // looking at the index, not git's text.
            if (await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false)
                is not { Count: > 0 } conflicts)
            {
                throw;
            }

            return new MergeResult
            {
                Outcome = MergeOutcome.Conflicted,
                HeadBefore = before,
                HeadAfter = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false),
                ConflictedPaths = conflicts,
                SuggestedMessage = await ReadDraftAsync(workingDirectory, "MERGE_MSG", cancellationToken)
                    .ConfigureAwait(false),
            };
        }

        string after = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        bool moved = !string.Equals(before, after, StringComparison.Ordinal);

        // 🔴 `--squash` and `--no-commit` return exit code 0 but HEAD stays put. The index is
        // checked for staged changes; if there are none there was truly nothing to do.
        bool staged = !moved
            && await HasStagedChangesAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        MergeOutcome outcome = staged
            ? MergeOutcome.Staged
            : !moved
                ? MergeOutcome.AlreadyUpToDate
                : await IsMergeCommitAsync(workingDirectory, after, cancellationToken).ConfigureAwait(false)
                    ? MergeOutcome.MergeCommit
                    : MergeOutcome.FastForward;

        return new MergeResult
        {
            Outcome = outcome,
            HeadBefore = before,
            HeadAfter = after,
            SuggestedMessage = staged
                ? await ReadDraftAsync(
                        workingDirectory,
                        options.Strategy == MergeStrategy.Squash ? "SQUASH_MSG" : "MERGE_MSG",
                        cancellationToken)
                    .ConfigureAwait(false)
                : null,
        };
    }

    public async Task<MergePreview> PreviewAsync(
        string workingDirectory,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        GitResult ancestor = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "merge-base", "HEAD", source),
            cancellationToken).ConfigureAwait(false);

        if (!ancestor.IsSuccess)
        {
            return new MergePreview { HasChanges = true, CanFastForward = false, HasCommonAncestor = false };
        }

        string mergeBase = ancestor.GetStandardOutputText().Trim();
        string head = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        string counts = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--count", $"HEAD..{source}"),
            cancellationToken).ConfigureAwait(false);

        int ahead = int.TryParse(counts.Trim(), out int parsed) ? parsed : 0;

        return new MergePreview
        {
            HasChanges = ahead > 0,
            CanFastForward = string.Equals(mergeBase, head, StringComparison.Ordinal),
            HasCommonAncestor = true,
            Ahead = ahead,
        };
    }

    public async Task<string> AbortAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        await _writer
            .RunAsync(workingDirectory, ["merge", "--abort"], cancellationToken)
            .ConfigureAwait(false);

        return await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    public string DescribeCommand(MergeOptions options) => Describe(options);

    /// <summary>Produces the command that will be run ("show the command" principle).</summary>
    public static string Describe(MergeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return "git " + string.Join(' ', BuildArguments(options));
    }

    /// <remarks>
    /// The <c>--</c> separator is always used: a branch name starting with <c>-</c> would
    /// otherwise be mistaken for a flag (the lesson from P06-T01).
    /// </remarks>
    private static IReadOnlyList<string> BuildArguments(MergeOptions options)
    {
        List<string> arguments = ["merge"];

        switch (options.Strategy)
        {
            case MergeStrategy.NoFastForward:
                arguments.Add("--no-ff");
                break;
            case MergeStrategy.Squash:
                arguments.Add("--squash");
                break;
            case MergeStrategy.FastForwardOnly:
                arguments.Add("--ff-only");
                break;
            case MergeStrategy.Default:
            default:
                break;
        }

        if (options.NoCommit && options.Strategy != MergeStrategy.Squash)
        {
            // `--squash` already doesn't commit; giving both together is redundant.
            arguments.Add("--no-commit");
        }

        if (options.AllowUnrelatedHistories)
        {
            arguments.Add("--allow-unrelated-histories");
        }

        if (options.Message is { Length: > 0 } message)
        {
            foreach (string paragraph in message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                arguments.Add("-m");
                arguments.Add(paragraph.TrimEnd('\r'));
            }
        }

        arguments.Add("--");
        arguments.Add(options.Source);

        return arguments;
    }

    private async Task<string> ReadHeadAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "HEAD"),
            cancellationToken).ConfigureAwait(false);

        // `rev-parse HEAD` fails in an unborn repository; there's also nothing to merge.
        return result.IsSuccess ? result.GetStandardOutputText().Trim() : string.Empty;
    }

    private async Task<IReadOnlyList<string>> ReadConflictsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? [.. result.GetStandardOutputText().Split('\0', StringSplitOptions.RemoveEmptyEntries)]
            : [];
    }

    private async Task<bool> HasStagedChangesAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // `--quiet` returns 1 when there's a difference; that's not an error (the pattern declared in P02).
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

    private async Task<bool> IsMergeCommitAsync(
        string workingDirectory,
        string commit,
        CancellationToken cancellationToken)
    {
        string parents = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--parents", "-1", commit),
            cancellationToken).ConfigureAwait(false);

        // "<commit> <parent1> <parent2>" — two parents means a merge commit.
        return parents.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 2;
    }

    /// <summary>Reads the message draft left behind by git.</summary>
    private async Task<string?> ReadDraftAsync(
        string workingDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        string gitDirectory = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "--absolute-git-dir"),
            cancellationToken).ConfigureAwait(false);

        string path = Path.Combine(gitDirectory.Trim(), fileName);

        if (!File.Exists(path))
        {
            return null;
        }

        string text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        // Comment lines are not shown to the user; git strips them at commit time anyway.
        return string.Join(
            '\n',
            text.Split('\n').Where(line => !line.StartsWith('#'))).Trim();
    }
}
