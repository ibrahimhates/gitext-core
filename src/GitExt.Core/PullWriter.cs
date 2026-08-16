using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Pull's merge strategy (P06-T07).</summary>
public enum PullStrategy
{
    /// <summary>
    /// Whatever the user's setting says.
    /// </summary>
    /// <remarks>
    /// This value is <b>never passed to the command</b>: it is resolved to the actual
    /// strategy via <see cref="IPullWriter.ResolveStrategyAsync"/> and an <b>explicit</b>
    /// flag is written to the command. The rationale is in the <see cref="PullWriter"/>
    /// notes.
    /// </remarks>
    Default,

    /// <summary><c>--no-rebase</c>: merge the remote branch into the current branch.</summary>
    Merge,

    /// <summary><c>--rebase</c>: move local commits on top of the remote branch.</summary>
    Rebase,

    /// <summary><c>--ff-only</c>: only if it can fast-forward.</summary>
    FastForwardOnly,
}

/// <summary><b>Where</b> the strategy came from — shown to the user (P06-T07).</summary>
public enum PullStrategySource
{
    /// <summary>The user chose it on this screen.</summary>
    UserChoice,

    /// <summary>The <c>branch.&lt;name&gt;.rebase</c> setting.</summary>
    BranchSetting,

    /// <summary>The <c>pull.rebase</c> setting.</summary>
    PullRebaseSetting,

    /// <summary>The <c>pull.ff</c> setting.</summary>
    PullFfSetting,

    /// <summary>No setting at all; the application's default (merge).</summary>
    ApplicationDefault,
}

/// <param name="Strategy">The strategy to apply.</param>
/// <param name="Source">The source of the decision.</param>
/// <param name="ConfigValue">The raw value of the setting, if any.</param>
public sealed record ResolvedPullStrategy(
    PullStrategy Strategy,
    PullStrategySource Source,
    string? ConfigValue);

/// <summary>Pull options (P06-T07).</summary>
public sealed record PullOptions
{
    /// <summary>Which remote? If <see langword="null"/>, the branch's upstream.</summary>
    public string? Remote { get; init; }

    /// <summary>Which remote branch? If <see langword="null"/>, the upstream's branch.</summary>
    public string? Branch { get; init; }

    /// <summary>The strategy; resolved from settings if <see cref="PullStrategy.Default"/>.</summary>
    public PullStrategy Strategy { get; init; }

    /// <summary>
    /// <c>--autostash</c>: stash work in a dirty tree and restore it afterward.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — restoring the stash CAN CONFLICT and the exit code is still 0.</b>
    /// In that case the working tree ends up with <c>UU</c> files and <b>conflict
    /// markers inside the file</b>, and the stash still sits in the list. The result is
    /// reported separately via <see cref="PullResult.AutoStashConflict"/>.
    /// </remarks>
    public bool AutoStash { get; init; }

    /// <summary><c>--prune</c>: prune during the fetch stage.</summary>
    public bool Prune { get; init; }

    /// <summary>Tag behavior (fetch stage).</summary>
    public FetchTagMode Tags { get; init; }

    /// <summary>
    /// The HTTPS credentials supplied by the user (P06-T09).
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, git uses its own channels (credential helper, SSH
    /// agent) — measured, both work fine in our environment. When set, the value is
    /// passed via <c>GIT_ASKPASS</c>; it is <b>not written</b> to the command line.
    /// </remarks>
    public GitCredentials? Credentials { get; init; }

    /// <summary>Live progress notification (P06-T10).</summary>
    public IProgress<GitProgress>? Progress { get; init; }
}

/// <summary>The result of a pull (P06-T07).</summary>
public sealed record PullResult
{
    /// <summary>The strategy that was actually applied, and its source.</summary>
    public required ResolvedPullStrategy Strategy { get; init; }

    /// <summary><c>HEAD</c> before the pull.</summary>
    public required string HeadBefore { get; init; }

    /// <summary><c>HEAD</c> after the pull.</summary>
    public required string HeadAfter { get; init; }

    /// <summary>Remote-tracking refs that changed during the fetch stage.</summary>
    public IReadOnlyList<RefChange> Changes { get; init; } = [];

    /// <summary>
    /// Are there any unresolved files left?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Cannot be determined</b> from the exit code alone: a conflict yields rc=1,
    /// but an <c>--autostash</c> restore conflict yields rc <b>0</b>. The state is read
    /// separately.
    /// </remarks>
    public bool HasConflicts { get; init; }

    /// <summary>Did the conflict come from restoring the stash?</summary>
    /// <remarks>
    /// This must be distinguished: what the user needs to do differs. The pull itself
    /// <b>succeeded</b>; what needs resolving is their own uncommitted change, and the
    /// stash is still there.
    /// </remarks>
    public bool AutoStashConflict { get; init; }

    /// <summary>Did <c>HEAD</c> not move at all ("already up to date")?</summary>
    public bool AlreadyUpToDate => string.Equals(HeadBefore, HeadAfter, StringComparison.Ordinal);

    /// <summary>
    /// The <b>runnable</b> command to revert the pull.
    /// </summary>
    /// <remarks>
    /// MEASURED: git sets <c>ORIG_HEAD</c> to the pre-pull commit in all three paths
    /// (fast-forward, merge, rebase), and <c>reset --hard</c> restores the previous
    /// state exactly. Even so, the <b>hash is written</b>, not <c>ORIG_HEAD</c>: the
    /// next merge/rebase overwrites it, and the user might run the command half an hour
    /// later.
    /// </remarks>
    public string RecoveryCommand => $"git reset --hard {HeadBefore}";
}

/// <summary>Pull operations (P06-T07).</summary>
public interface IPullWriter
{
    /// <summary>
    /// Reports <b>which strategy</b> will be applied based on the user's settings.
    /// </summary>
    /// <remarks>
    /// The UI shows this <b>before the pull</b>: the README's "show the command"
    /// principle and the plan's rule that "what the pull button does must not remain
    /// ambiguous".
    /// </remarks>
    Task<ResolvedPullStrategy> ResolveStrategyAsync(
        string workingDirectory,
        PullStrategy requested = PullStrategy.Default,
        CancellationToken cancellationToken = default);

    /// <summary>Performs a pull.</summary>
    Task<PullResult> PullAsync(
        string workingDirectory,
        PullOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A <c>git pull</c> wrapper (P06-T07).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The strategy is NEVER left up to git.</b> MEASURED: with no setting present and
/// diverged branches, <c>git pull</c> <b>refuses to run</b> (exit code 128) and prints a
/// nine-line <c>hint:</c> block. Worse: it <b>completes the fetch stage before
/// refusing</b>, meaning the repository has changed but the user sees "failed". That is
/// why the strategy is resolved first via <see cref="ResolveStrategyAsync"/>, and an
/// <b>always-explicit flag</b> (<c>--rebase</c>/<c>--no-rebase</c>/<c>--ff-only</c>) is
/// written to the command.
/// </para>
/// <para>
/// <b>Setting priority measured:</b> <c>branch.&lt;name&gt;.rebase</c> <b>overrides</b>
/// <c>pull.rebase</c> (<c>pull.rebase=true</c> + <c>branch.main.rebase=false</c> →
/// a merge was performed).
/// </para>
/// </remarks>
public sealed class PullWriter : IPullWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly IGitConfigReader _config;

    public PullWriter(IGitWriter writer, IGitProcessRunner runner, IGitConfigReader config)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(config);

        _writer = writer;
        _runner = runner;
        _config = config;
    }

    public async Task<ResolvedPullStrategy> ResolveStrategyAsync(
        string workingDirectory,
        PullStrategy requested = PullStrategy.Default,
        CancellationToken cancellationToken = default)
    {
        if (requested != PullStrategy.Default)
        {
            return new ResolvedPullStrategy(requested, PullStrategySource.UserChoice, null);
        }

        string? branch = await CurrentBranchAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        // The order is fixed by measurement: the branch setting is the strongest.
        if (branch is { Length: > 0 })
        {
            string? branchSetting = await _config
                .GetAsync(workingDirectory, $"branch.{branch}.rebase", cancellationToken)
                .ConfigureAwait(false);

            if (ParseRebase(branchSetting) is { } fromBranch)
            {
                return new ResolvedPullStrategy(
                    fromBranch, PullStrategySource.BranchSetting, branchSetting);
            }
        }

        string? pullRebase = await _config
            .GetAsync(workingDirectory, "pull.rebase", cancellationToken)
            .ConfigureAwait(false);

        if (ParseRebase(pullRebase) is { } fromPull)
        {
            return new ResolvedPullStrategy(
                fromPull, PullStrategySource.PullRebaseSetting, pullRebase);
        }

        string? pullFf = await _config
            .GetAsync(workingDirectory, "pull.ff", cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(pullFf, "only", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedPullStrategy(
                PullStrategy.FastForwardOnly, PullStrategySource.PullFfSetting, pullFf);
        }

        // git's behaviour at this point is "refuse"; ours is the historical default git documents,
        // which is merging. The user sees on screen what will happen and can change it, so it is not a
        // silent choice.
        return new ResolvedPullStrategy(
            PullStrategy.Merge, PullStrategySource.ApplicationDefault, null);
    }

    public async Task<PullResult> PullAsync(
        string workingDirectory,
        PullOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        ResolvedPullStrategy strategy = await ResolveStrategyAsync(
                workingDirectory, options.Strategy, cancellationToken)
            .ConfigureAwait(false);

        string before = await RevisionAsync(workingDirectory, "HEAD", cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> refsBefore =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken)
                .ConfigureAwait(false);

        // Only asked for when autostash is actually in play — an extra process on every pull would
        // cost more than the answer is worth. See AutoStashRemainsAsync for what it is compared to.
        string stashBefore = options.AutoStash
            ? await RevisionAsync(workingDirectory, "refs/stash", cancellationToken)
                .ConfigureAwait(false)
            : string.Empty;

        try
        {
            using AskPassSession? askPass = options.Credentials is { } credentials
                ? AskPassSession.Create(credentials)
                : null;

            await _writer
                .RunWithEnvironmentAsync(
                    workingDirectory,
                    BuildArguments(options, strategy.Strategy),
                    askPass?.Environment,
                    options.Progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException)
        {
            // A conflict is not an "error" but an outcome: the user will resolve the files. Raised as
            // an exception, the UI cannot explain what happened and just shows a red box — whereas the
            // repository is in a conflict state right now and the work to do is clear.
            //
            // 🔴 The distinction CANNOT be made from `Kind`: MEASURED, pull's conflict text
            // (`Auto-merging…`, `CONFLICT (content):`, `Automatic merge failed`) is written to
            // **stdout**; because the classifier only sees stderr it says `Unknown`.
            // So the decision is made by looking at the **state**, not at the text: are there unmerged
            // files? That stays right even if the channel changes.
            if (!await HasUnmergedAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }
        }

        string after = await RevisionAsync(workingDirectory, "HEAD", cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> refsAfter =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken)
                .ConfigureAwait(false);

        bool conflicts = await HasUnmergedAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return new PullResult
        {
            Strategy = strategy,
            HeadBefore = before,
            HeadAfter = after,
            Changes = RefSnapshot.Diff(refsBefore, refsAfter),
            HasConflicts = conflicts,

            // The distinction has to be made: what the user must do differs. The pull itself
            // succeeded; what needs resolving is their own uncommitted change, and it is still in
            // the stash.
            AutoStashConflict = conflicts
                && options.AutoStash
                && await AutoStashRemainsAsync(workingDirectory, stashBefore, cancellationToken)
                    .ConfigureAwait(false),
        };
    }

    private static IReadOnlyList<string> BuildArguments(PullOptions options, PullStrategy strategy)
    {
        List<string> arguments = ["pull", "--progress"];

        arguments.Add(strategy switch
        {
            PullStrategy.Rebase => "--rebase",
            PullStrategy.FastForwardOnly => "--ff-only",

            // ⚠️ `--no-rebase` is written EXPLICITLY. Called without the flag, git goes into "refuse"
            // mode in an unconfigured and diverged repository (measured, rc=128).
            _ => "--no-rebase",
        });

        if (options.AutoStash)
        {
            arguments.Add("--autostash");
        }

        if (options.Prune)
        {
            arguments.Add("--prune");
        }

        switch (options.Tags)
        {
            case FetchTagMode.All:
                arguments.Add("--tags");
                break;
            case FetchTagMode.None:
                arguments.Add("--no-tags");
                break;
            case FetchTagMode.Default:
            default:
                break;
        }

        if (options.Remote is { Length: > 0 } remote)
        {
            arguments.Add("--");
            arguments.Add(remote);

            if (options.Branch is { Length: > 0 } branch)
            {
                arguments.Add(branch);
            }
        }

        return arguments;
    }

    /// <summary>
    /// Turns the <c>branch.&lt;branch&gt;.rebase</c> / <c>pull.rebase</c> value into a strategy.
    /// </summary>
    /// <remarks>
    /// MEASURED: git accepts the values <c>true</c>, <c>false</c>, <c>only</c>, <c>interactive</c> and
    /// <c>merges</c>. <c>interactive</c> and <c>merges</c> fall back to a plain rebase here —
    /// interactive rebase is P07-T10's subject, and an editor opening <b>here</b> would surprise the
    /// user.
    /// </remarks>
    private static PullStrategy? ParseRebase(string? value) => value?.ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" or "interactive" or "merges" => PullStrategy.Rebase,
        "false" or "no" or "off" or "0" => PullStrategy.Merge,
        _ => null,
    };

    private async Task<string?> CurrentBranchAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["symbolic-ref", "-q", "--short", "HEAD"],

                // On a detached HEAD the exit code is 1; that is not an error but the answer "no branch" (P02-T09).
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0
            ? result.GetStandardOutputText().Trim()
            : null;
    }

    private async Task<string> RevisionAsync(
        string workingDirectory,
        string revision,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", revision],

                // An unborn HEAD: exit code 1, empty output.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.GetStandardOutputText().Trim();
    }

    /// <summary>
    /// Did the autostash entry survive the pull — i.e. did putting it back conflict?
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 MEASURED — <c>--autostash</c> DROPS the entry it created once it has been put back
    /// cleanly (<c>refs/stash</c> goes back to what it was), and LEAVES IT IN PLACE when putting it
    /// back conflicts. So the state answers the question exactly: the stash ref moved and still
    /// exists.
    /// </para>
    /// <para>
    /// 🔴 This used to be read out of git's message ("…however applying them resulted in
    /// conflicts…"), and that WORDING CHANGES BETWEEN GIT VERSIONS: older builds write "Applying
    /// autostash resulted in conflicts." instead, so on a CI runner with an older git the flag came
    /// back false and the user would have been told "the merge conflicted" — pointed at the wrong
    /// files entirely. It is also unusable under a non-English locale. The state is version- and
    /// language-independent.
    /// </para>
    /// <para>
    /// A stash entry left behind by an EARLIER pull does not produce a false positive: what is
    /// compared is the ref read before this pull, not merely whether a stash exists.
    /// </para>
    /// </remarks>
    private async Task<bool> AutoStashRemainsAsync(
        string workingDirectory,
        string stashBefore,
        CancellationToken cancellationToken)
    {
        string stashAfter = await RevisionAsync(workingDirectory, "refs/stash", cancellationToken)
            .ConfigureAwait(false);

        return stashAfter.Length > 0
               && !string.Equals(stashAfter, stashBefore, StringComparison.Ordinal);
    }

    private async Task<bool> HasUnmergedAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        return result.StandardOutput.Length > 0;
    }

}
