using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>How tags are fetched during a fetch (P06-T06).</summary>
public enum FetchTagMode
{
    /// <summary>git's default: tags <b>pointing at</b> fetched commits come along.</summary>
    Default,

    /// <summary><c>--tags</c>: all tags on the remote.</summary>
    All,

    /// <summary><c>--no-tags</c>: fetch no tags at all.</summary>
    None,
}

/// <summary>Fetch options (P06-T06).</summary>
public sealed record FetchOptions
{
    /// <summary>
    /// Which remote? <see langword="null"/> means <b>all</b> (<c>--all</c>).
    /// </summary>
    public string? Remote { get; init; }

    /// <summary>
    /// <c>--prune</c>: remove tracking refs for branches deleted on the remote.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — this is a destructive option.</b> The reflog of the pruned ref is also
    /// deleted, and a commit that only that ref held is **gone** after a subsequent pruning
    /// <c>gc</c>. The UI should show what will be lost first, via
    /// <see cref="IFetchWriter.PreviewPruneAsync"/>.
    /// </remarks>
    public bool Prune { get; init; }

    /// <summary>
    /// <c>--prune-tags</c>: also remove tags deleted on the remote.
    /// </summary>
    /// <remarks>
    /// MEASURED: <c>--prune</c> <b>alone does not touch tags</b> — a tag deleted on the remote
    /// stays locally. A separate flag is required.
    /// </remarks>
    public bool PruneTags { get; init; }

    /// <summary>Tag behavior.</summary>
    public FetchTagMode Tags { get; init; }

    /// <summary><c>--dry-run</c>: write nothing.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// User-supplied HTTPS credentials (P06-T09).
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, git uses its own channels (credential helper, SSH agent) —
    /// measured, both work fine in our environment. When set, the value is passed via
    /// <c>GIT_ASKPASS</c>, <b>not written</b> to the command line.
    /// </remarks>
    public GitCredentials? Credentials { get; init; }

    /// <summary>Live progress notification (P06-T10).</summary>
    public IProgress<GitProgress>? Progress { get; init; }
}

/// <summary>How a ref changed after a fetch (P06-T06).</summary>
public enum RefChangeKind
{
    /// <summary>New ref.</summary>
    Created,

    /// <summary>An existing ref moved to a different commit.</summary>
    Updated,

    /// <summary>Ref was removed (pruned).</summary>
    Deleted,
}

/// <param name="RefName">Full ref name (<c>refs/remotes/origin/main</c>).</param>
/// <param name="OldId">Previous commit; <see langword="null"/> for a new ref.</param>
/// <param name="NewId">Next commit; <see langword="null"/> for a deleted ref.</param>
/// <param name="Kind">Kind of change.</param>
public sealed record RefChange(string RefName, string? OldId, string? NewId, RefChangeKind Kind)
{
    /// <summary>Short name (<c>origin/main</c>, <c>v1.0</c>).</summary>
    public string ShortName =>
        RefName.StartsWith(RemoteName.RemotesPrefix, StringComparison.Ordinal)
            ? RefName[RemoteName.RemotesPrefix.Length..]
            : RefName.StartsWith(TagsPrefix, StringComparison.Ordinal)
                ? RefName[TagsPrefix.Length..]
                : RefName;

    /// <summary>Is this a tag?</summary>
    public bool IsTag => RefName.StartsWith(TagsPrefix, StringComparison.Ordinal);

    internal const string TagsPrefix = "refs/tags/";
}

/// <param name="Remote">Remote that could not be fetched.</param>
/// <param name="Message">The error line git gave for that remote.</param>
public sealed record FetchFailure(string Remote, string Message);

/// <summary>Fetch result (P06-T06).</summary>
public sealed record FetchResult
{
    /// <summary>Changed refs. An empty list means <b>"everything is up to date"</b>.</summary>
    public IReadOnlyList<RefChange> Changes { get; init; } = [];

    /// <summary>
    /// Remotes that could not be fetched.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED:</b> with <c>--all</c>, if one remote is broken the exit code is
    /// <b>1</b>, <b>but the others are still fetched successfully</b>. Looking at the exit code
    /// and calling it "fetch failed" would hide changes that actually did come in from the
    /// user.
    /// </remarks>
    public IReadOnlyList<FetchFailure> Failures { get; init; } = [];

    /// <summary>Was this only a dry run?</summary>
    public bool DryRun { get; init; }

    /// <summary>Did no remote get fetched at all?</summary>
    public bool FailedCompletely => Failures.Count > 0 && Changes.Count == 0;
}

/// <summary>What pruning would cost (P06-T06).</summary>
/// <remarks>
/// Same rationale as <c>RemoteRemovalPlan</c> in P06-T05: information <b>cannot be read</b>
/// after pruning, so it must be gathered first.
/// </remarks>
public sealed record PrunePreview
{
    /// <summary>Tracking refs that would be pruned, and their current tips.</summary>
    public IReadOnlyList<RefChange> WouldDelete { get; init; } = [];

    /// <summary>Runnable recovery commands (that write the ref back).</summary>
    public IReadOnlyList<string> RecoveryCommands { get; init; } = [];
}

/// <summary>Fetch operations (P06-T06).</summary>
public interface IFetchWriter
{
    /// <summary>Fetches and returns <b>what changed</b>.</summary>
    Task<FetchResult> FetchAsync(
        string workingDirectory,
        FetchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes which refs pruning would delete, <b>before pruning</b>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Hits the network</b> (<c>git ls-remote</c>): there is no other way to know which
    /// branches remain on the remote. <c>--dry-run --prune</c> output says this, but in
    /// <b>human-readable</b> form, and per ADR-0002 it is not parsed.
    /// </remarks>
    Task<PrunePreview> PreviewPruneAsync(
        string workingDirectory,
        string remote,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Wrapper around <c>git fetch</c> (P06-T06).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>MEASURED — ALL of fetch's output is on <c>stderr</c>.</b> Even when there were
/// changes, <c>stdout</c> is <b>completely empty</b>; the summary lines (<c>From …</c>,
/// <c>db56d2e..e0a8e5f main -&gt; origin/main</c>) are written to stderr. A UI reading stdout
/// would show the user <b>nothing</b>.
/// </para>
/// <para>
/// <b>We still don't read what changed from that text.</b> git 2.41 added <c>--porcelain</c>,
/// a machine-readable channel (measured, writes to stdout), but the project's supported
/// <b>minimum version is 2.30</b> (ADR-0002) — no such flag there. Keeping two code paths
/// would mean one going silently untested.
/// → Changes are computed as a <b>ref snapshot diff</b> instead: <c>for-each-ref</c> before and
/// after the fetch. Version-independent, covers deletions and tags too, costs ~1 ms.
/// </para>
/// </remarks>
public sealed class FetchWriter : IFetchWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public FetchWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<FetchResult> FetchAsync(
        string workingDirectory,
        FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyDictionary<string, string> before =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken).ConfigureAwait(false);

        string standardError;

        try
        {
            using AskPassSession? askPass = options.Credentials is { } credentials
                ? AskPassSession.Create(credentials)
                : null;

            GitResult result = await _writer
                .RunWithEnvironmentAsync(
                    workingDirectory,
                    BuildArguments(options),
                    askPass?.Environment,
                    options.Progress,
                    cancellationToken)
                .ConfigureAwait(false);

            standardError = result.StandardError;
        }
        catch (GitException error) when (ParseFailures(error.StandardError).Count > 0)
        {
            // 🔴 MEASURED: with `--all`, if one remote is broken the exit code is 1 BUT the
            // others are still fetched. Letting the exception propagate as-is would hide
            // changes that really did come in — and worse, the screen would say "failed"
            // while the repository had actually changed. A partial result is still a result.
            standardError = error.StandardError;
        }

        IReadOnlyDictionary<string, string> after = options.DryRun
            ? before
            : await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken).ConfigureAwait(false);

        return new FetchResult
        {
            Changes = RefSnapshot.Diff(before, after),
            Failures = ParseFailures(standardError),
            DryRun = options.DryRun,
        };
    }

    public async Task<PrunePreview> PreviewPruneAsync(
        string workingDirectory,
        string remote,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);

        // Branches that STILL exist on the remote. `--heads` is enough: pruning only affects
        // tracking branches, tags are handled by the separate flag (`--prune-tags`).
        GitResult remoteRefs = await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "ls-remote", "--heads", "--", remote),
            cancellationToken).ConfigureAwait(false);

        HashSet<string> alive = [];

        foreach (string line in remoteRefs.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Format: "<sha>\t<refname>" — ref names cannot contain tabs or newlines (P02-T09).
            int tab = line.IndexOf('\t', StringComparison.Ordinal);

            if (tab > 0)
            {
                alive.Add(line[(tab + 1)..].TrimEnd('\r'));
            }
        }

        IReadOnlyDictionary<string, string> local =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken).ConfigureAwait(false);

        string prefix = RemoteName.RemotesPrefix + remote + "/";
        List<RefChange> doomed = [];

        foreach ((string refName, string id) in local)
        {
            if (!refName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string branch = refName[prefix.Length..];

            // Note: `origin/HEAD` never reaches here — the snapshot excludes symbolic refs.
            // If it weren't excluded, since there's no `refs/heads/HEAD` branch on the remote,
            // it would produce a false loss warning on EVERY prune preview.
            if (!alive.Contains(BranchName.HeadsPrefix + branch))
            {
                doomed.Add(new RefChange(refName, id, null, RefChangeKind.Deleted));
            }
        }

        return new PrunePreview
        {
            WouldDelete = doomed,

            // Writing the ref back recovers the commit — but only if the object is still
            // there. If `gc` runs after pruning the object is gone (measured), so the command
            // is given to the user IMMEDIATELY.
            RecoveryCommands =
            [
                .. doomed.Select(change =>
                    $"git update-ref {change.RefName} {change.OldId}"),
            ],
        };
    }

    /// <summary>
    /// Builds the arguments.
    /// </summary>
    /// <remarks>
    /// <c>--progress</c> is always given: measured, git does <b>not</b> write progress lines at
    /// all on non-terminal output, and only the summary remains. Live display is in P06-T10,
    /// but leaving the flag until then would mean saving "why is there no progress?" for that
    /// day.
    /// </remarks>
    private static IReadOnlyList<string> BuildArguments(FetchOptions options)
    {
        List<string> arguments = ["fetch", "--progress"];

        if (options.Prune)
        {
            arguments.Add("--prune");
        }

        if (options.PruneTags)
        {
            // MEASURED: `--prune-tags` alone is not enough, git also requires `--prune`.
            arguments.Add("--prune-tags");

            if (!options.Prune)
            {
                arguments.Add("--prune");
            }
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

        if (options.DryRun)
        {
            arguments.Add("--dry-run");
        }

        if (options.Remote is { Length: > 0 } remote)
        {
            arguments.Add("--");
            arguments.Add(remote);
        }
        else
        {
            arguments.Add("--all");

            // 🔴 MEASURED: git performs multi-remote fetch SEQUENTIALLY BY DEFAULT
            // ("By default fetches are performed sequentially, not in parallel").
            // In a repository with three local remotes, `-j1` takes 47 ms, `-j3` takes 20 ms —
            // 2.4×. And that's locally; over the network, waiting for each remote's handshake
            // in sequence makes the difference much larger.
            //
            // 0 = "a reasonable default": git picks the job count. A fixed number would create
            // needless threads in a two-remote repository, or needless sequentiality in a
            // ten-remote one.
            arguments.Add("--jobs=0");
        }

        return arguments;
    }

    /// <summary>
    /// Collects partial failures with <c>--all</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// git writes a line containing "could not fetch" for each failed remote; this is the
    /// <b>only</b> machine-friendly trace, with preceding <c>fatal:</c> lines carried as detail.
    /// </para>
    /// <para>
    /// 🔴 <b>The line format is NOT THE SAME for sequential vs. parallel fetch</b> (measured in
    /// P09-T13):
    /// </para>
    /// <code>
    /// -j1  →  error: could not fetch broken
    /// -j0  →  could not fetch 'broken' (exit code: 128)
    /// </code>
    /// <para>
    /// In the parallel form there is no <c>error:</c> prefix and the name is quoted. A parser
    /// that only recognizes the sequential form would <b>never see</b> the failure during a
    /// parallel fetch: git exits with 1, no exception is caught, and the user — even though
    /// changes really did come in — would see only an error. This code broke exactly this way
    /// on the first attempt at parallelizing; a test catching it is how it was found.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<FetchFailure> ParseFailures(string standardError)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return [];
        }

        const string marker = "could not fetch ";
        List<FetchFailure> failures = [];
        string detail = string.Empty;

        foreach (string raw in standardError.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.StartsWith("fatal:", StringComparison.Ordinal))
            {
                detail = line["fatal:".Length..].Trim();
                continue;
            }

            int markerIndex = line.IndexOf(marker, StringComparison.Ordinal);

            if (markerIndex < 0)
            {
                continue;
            }

            // The prefix may be `error: ` or absent entirely; in both cases the name starts
            // right after the marker.
            string name = ExtractRemoteName(line[(markerIndex + marker.Length)..]);

            if (name.Length == 0)
            {
                continue;
            }

            failures.Add(new FetchFailure(
                name,
                detail.Length > 0 ? detail : "The remote could not be reached."));

            detail = string.Empty;
        }

        return failures;
    }

    /// <summary>
    /// Extracts the remote name from the remainder after the marker.
    /// </summary>
    /// <remarks>
    /// In the sequential form the name is the rest of the line; in the parallel form it is
    /// quoted, followed by <c>(exit code: N)</c>. A name taken without stripping quotes would
    /// show the user <c>'broken' (exit code: 128)</c>.
    /// </remarks>
    private static string ExtractRemoteName(string rest)
    {
        rest = rest.Trim();

        if (rest.Length == 0)
        {
            return string.Empty;
        }

        if (rest[0] is '\'' or '"')
        {
            char quote = rest[0];
            int end = rest.IndexOf(quote, 1);

            return end > 1 ? rest[1..end] : string.Empty;
        }

        int space = rest.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? rest : rest[..space];
    }
}
