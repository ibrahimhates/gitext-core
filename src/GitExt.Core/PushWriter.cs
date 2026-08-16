using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>How tags are sent during push (P06-T08).</summary>
public enum PushTagMode
{
    /// <summary>Don't send tags (git's default).</summary>
    None,

    /// <summary>
    /// <c>--follow-tags</c>: <b>annotated</b> tags reachable from the sent commits.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>MEASURED — SKIPS lightweight tags.</b> With <c>v3</c> (lightweight) and
    /// <c>v4</c> (annotated) present locally, <c>--follow-tags</c> only sent <c>v4</c>.
    /// If the user says "also send tags" and doesn't notice v3 didn't go, they'll assume
    /// the tag is on the remote. The UI has to spell this out.
    /// </remarks>
    FollowAnnotated,

    /// <summary><c>--tags</c>: <b>all</b> local tags.</summary>
    All,
}

/// <summary>
/// A single ref push (P06-T08).
/// </summary>
/// <param name="Source">
/// Local source (<c>main</c>, <c>refs/tags/v1</c>). <b>Empty</b> when deleting.
/// </param>
/// <param name="Destination">Short name of the remote destination branch/tag.</param>
/// <param name="Delete">Should the remote ref be deleted (<c>--delete</c>)?</param>
public sealed record PushSpec(string Source, string Destination, bool Delete = false)
{
    /// <summary>
    /// <b>Expected</b> remote tip for <c>--force-with-lease</c>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Without this field, force-with-lease does NOT PROTECT — measured.</b> Details:
    /// <see cref="PushOptions.ForceWithLease"/>.
    /// </remarks>
    public string? ExpectedRemoteObjectId { get; init; }

    /// <summary>The refspec to hand to git.</summary>
    internal string ToRefspec() => Delete ? Destination : $"{Source}:{Destination}";
}

/// <summary>Push options (P06-T08).</summary>
public sealed record PushOptions
{
    /// <summary>Target remote name.</summary>
    public required string Remote { get; init; }

    /// <summary>Refs to send. If empty, git's default runs.</summary>
    public IReadOnlyList<PushSpec> Refs { get; init; } = [];

    /// <summary>
    /// <c>--set-upstream</c>: set the local branch's upstream after the push.
    /// </summary>
    /// <remarks>
    /// MEASURED: after <c>-u</c>, <c>branch.&lt;branch&gt;.remote</c> and
    /// <c>branch.&lt;branch&gt;.merge</c> are actually written. A bare
    /// <c>git push</c> on a branch without an upstream <b>fails</b> (exit code 128).
    /// </remarks>
    public bool SetUpstream { get; init; }

    /// <summary>
    /// <c>--force-with-lease</c>: reject if the remote tip differs from what's expected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>MEASURED — bare <c>--force-with-lease</c> is NOT safe.</b> git's implicit
    /// lease is the branch's <b>remote-tracking ref</b> at that moment. So <b>any
    /// fetch</b> in between refreshes the lease, and the push then goes through even
    /// though we haven't seen someone else's commits. In the measurement: repo <c>b</c>
    /// did a <c>git fetch</c> without ever seeing <c>a</c>'s commit, and then
    /// <c>--force-with-lease</c> <b>successfully</b> deleted that commit. This project can
    /// do a fetch without the user asking (P05's auto-refresh, the Pull/Fetch screen) —
    /// meaning the protection would collapse precisely because of us.
    /// </para>
    /// <para>
    /// → So the lease is <b>always written explicitly</b>:
    /// <c>--force-with-lease=&lt;target&gt;:&lt;sha the user SAW&gt;</c>. The anchor is
    /// read by <see cref="IPushWriter.PlanAsync"/> when the screen opens and shown to the
    /// user. In the measurement, the same scenario gave <c>[rejected] (stale info)</c>
    /// with this form.
    /// </para>
    /// <para>
    /// <b>Bare <c>--force</c> is never offered</b> (plan decision): it silently deletes
    /// someone else's commits. Anyone who really wants it can go to the terminal.
    /// </para>
    /// </remarks>
    public bool ForceWithLease { get; init; }

    /// <summary>Tag behavior.</summary>
    public PushTagMode Tags { get; init; }

    /// <summary><c>--dry-run</c>: don't send anything, just report what would happen.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// HTTPS credentials supplied by the user (P06-T09).
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, git uses its own channels (credential helper, SSH agent)
    /// — measured, both work fine in our environment. When set, the value is passed via
    /// <c>GIT_ASKPASS</c>, <b>never</b> written to the command line.
    /// </remarks>
    public GitCredentials? Credentials { get; init; }

    /// <summary>Live progress notification (P06-T10).</summary>
    public IProgress<GitProgress>? Progress { get; init; }
}

/// <summary>Post-push status of a ref (P06-T08).</summary>
public enum PushRefStatus
{
    /// <summary>Didn't exist on the remote, was created (<c>*</c>).</summary>
    Created,

    /// <summary>Fast-forwarded (<c>' '</c> — the flag field is <b>a space</b>).</summary>
    FastForward,

    /// <summary>Force-changed (<c>+</c>).</summary>
    Forced,

    /// <summary>Deleted on the remote (<c>-</c>).</summary>
    Deleted,

    /// <summary>Already up to date (<c>=</c>).</summary>
    UpToDate,

    /// <summary>Rejected (<c>!</c>).</summary>
    Rejected,
}

/// <summary>
/// Reason for a rejection (P06-T08).
/// </summary>
/// <remarks>
/// The reason comes from the parenthesized part of the porcelain summary field — <b>not
/// from the <c>hint:</c> lines on stderr.</b> GitExtensions derives this by applying a
/// regular expression to the human-readable output (<c>FormPush.cs</c>); ADR-0002 forbids
/// that.
/// </remarks>
public enum PushRejectionKind
{
    /// <summary>Unrecognized reason; the raw text should be shown.</summary>
    Unknown,

    /// <summary>There are commits on the remote we don't have (<c>fetch first</c> / <c>non-fast-forward</c>).</summary>
    Behind,

    /// <summary>The lease didn't hold: the remote tip differs from what the user expected (<c>stale info</c>).</summary>
    StaleLease,

    /// <summary>The remote side rejected it — a hook, a protected branch, permissions (<c>remote rejected</c>).</summary>
    RemoteRejected,
}

/// <summary>Push result for a single ref (P06-T08).</summary>
/// <param name="Flag">Porcelain flag: <c>* + - = !</c> or a space.</param>
/// <param name="Source">Source ref; <c>(delete)</c> or empty on delete.</param>
/// <param name="Destination">Full ref name on the remote.</param>
/// <param name="Summary">Summary field (<c>abc..def</c>, <c>[new branch]</c>, <c>[rejected]</c>).</param>
/// <param name="Reason">The reason in the summary's parentheses; <see langword="null"/> if absent.</param>
public sealed record PushRefResult(
    char Flag,
    string Source,
    string Destination,
    string Summary,
    string? Reason)
{
    /// <summary>Status derived from the flag.</summary>
    public PushRefStatus Status => Flag switch
    {
        '*' => PushRefStatus.Created,
        '+' => PushRefStatus.Forced,
        '-' => PushRefStatus.Deleted,
        '=' => PushRefStatus.UpToDate,
        '!' => PushRefStatus.Rejected,
        _ => PushRefStatus.FastForward,
    };

    /// <summary>Short name of the destination (<c>main</c>, <c>v1.0</c>).</summary>
    public string ShortDestination =>
        Destination.StartsWith(BranchName.HeadsPrefix, StringComparison.Ordinal)
            ? Destination[BranchName.HeadsPrefix.Length..]
            : Destination.StartsWith(RefChange.TagsPrefix, StringComparison.Ordinal)
                ? Destination[RefChange.TagsPrefix.Length..]
                : Destination;

    /// <summary>Is this a tag?</summary>
    public bool IsTag => Destination.StartsWith(RefChange.TagsPrefix, StringComparison.Ordinal);

    /// <summary>Did the remote actually change?</summary>
    public bool Changed => Status is PushRefStatus.Created or PushRefStatus.FastForward
        or PushRefStatus.Forced or PushRefStatus.Deleted;

    /// <summary>Rejection reason; <see langword="null"/> if not rejected.</summary>
    public PushRejectionKind? Rejection => Status != PushRefStatus.Rejected
        ? null
        : Reason switch
        {
            null => PushRejectionKind.Unknown,
            _ when Reason.Contains("stale info", StringComparison.OrdinalIgnoreCase)
                => PushRejectionKind.StaleLease,
            _ when Reason.Contains("fetch first", StringComparison.OrdinalIgnoreCase)
                || Reason.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
                => PushRejectionKind.Behind,
            _ when Summary.Contains("remote rejected", StringComparison.OrdinalIgnoreCase)
                => PushRejectionKind.RemoteRejected,
            _ => PushRejectionKind.Unknown,
        };
}

/// <summary>Push result (P06-T08).</summary>
public sealed record PushResult
{
    /// <summary>One line for each ref sent.</summary>
    public IReadOnlyList<PushRefResult> Refs { get; init; } = [];

    /// <summary>
    /// Lines the remote side wrote with a <c>remote:</c> prefix.
    /// </summary>
    /// <remarks>
    /// The reason for a protected-branch hook rejection (<i>"protected branch, push
    /// forbidden"</i>) lives only here. The porcelain line just says
    /// <c>(pre-receive hook declined)</c> — it doesn't say <b>why</b>.
    /// </remarks>
    public IReadOnlyList<string> RemoteMessages { get; init; } = [];

    /// <summary>Was this only a dry run?</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Did git fail without writing anything (exit code 128)?
    /// </summary>
    /// <remarks>
    /// MEASURED: if the remote doesn't exist or is unreachable, <c>--porcelain</c> writes
    /// <b>nothing</b> to stdout. So "no lines" ≠ "no changes".
    /// </remarks>
    public bool Aborted { get; init; }

    /// <summary>Rejected refs.</summary>
    public IReadOnlyList<PushRefResult> Rejected =>
        [.. Refs.Where(item => item.Status == PushRefStatus.Rejected)];

    /// <summary>Refs that actually changed the remote.</summary>
    public IReadOnlyList<PushRefResult> Applied => [.. Refs.Where(item => item.Changed)];

    /// <summary>
    /// Did some refs go through while others were rejected?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED:</b> when two branches are pushed and one is rejected, the exit code
    /// is <b>1</b>, but the other branch <b>really did go through</b>. Looking at the exit
    /// code alone and saying "push failed" would make the user think nothing was sent.
    /// </remarks>
    public bool IsPartial => Applied.Count > 0 && Rejected.Count > 0;
}

/// <summary>
/// Pre-push state — to populate the screen and <b>anchor the lease</b> (P06-T08).
/// </summary>
public sealed record PushPlan
{
    public required string Remote { get; init; }

    public required string LocalBranch { get; init; }

    /// <summary>Default destination branch name.</summary>
    public required string RemoteBranch { get; init; }

    /// <summary>
    /// <b>Current</b> tip of the remote-tracking ref — the <c>--force-with-lease</c> anchor.
    /// </summary>
    /// <remarks>
    /// Read when the screen opens and shown to the user. Even if a fetch happens in
    /// between, the lease stays at this value; the reason for
    /// <see cref="PushOptions.ForceWithLease"/>.
    /// </remarks>
    public string? RemoteTipObjectId { get; init; }

    /// <summary>Does this branch exist on the remote (based on the tracking ref)?</summary>
    public bool RemoteBranchExists => RemoteTipObjectId is not null;

    /// <summary>Is the local branch's upstream set up?</summary>
    public bool HasUpstream { get; init; }

    /// <summary>Position relative to the upstream.</summary>
    public UpstreamTracking Tracking { get; init; } = UpstreamTracking.None;

    /// <summary>Will a new branch be created on the remote?</summary>
    public bool WouldCreateBranch => !RemoteBranchExists;

    /// <summary>Local tags available to send.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Remote branches (for the delete tab).</summary>
    public IReadOnlyList<string> RemoteBranches { get; init; } = [];
}

/// <summary>Push operations (P06-T08).</summary>
public interface IPushWriter
{
    /// <summary>
    /// Reads the pre-push state — populates the screen and anchors the lease.
    /// </summary>
    /// <remarks>Doesn't reach the network; only looks at local tracking refs.</remarks>
    Task<PushPlan> PlanAsync(
        string workingDirectory,
        string remote,
        string localBranch,
        CancellationToken cancellationToken = default);

    /// <summary>Pushes and returns <b>what happened for each ref</b>.</summary>
    Task<PushResult> PushAsync(
        string workingDirectory,
        PushOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Produces the command that will run ("show the command" principle).</summary>
    string DescribeCommand(PushOptions options);
}

/// <summary>
/// <c>git push</c> wrapper (P06-T08).
/// </summary>
/// <remarks>
/// <para>
/// <b>The result is read from stdout via <c>--porcelain</c>.</b> This wasn't possible for
/// fetch (the flag was added in git 2.41, the project's baseline is 2.30), but
/// <c>push --porcelain</c> is one of git's oldest flags. The measured format is three
/// tab-separated fields:
/// <c>&lt;flag&gt;\t&lt;source&gt;:&lt;destination&gt;\t&lt;summary&gt;</c>.
/// </para>
/// <para>
/// 🔴 <b>MEASURED — porcelain stdout is NOT pure.</b> Human-readable lines get mixed in:
/// <c>To ../remote.git</c>, <c>branch 'x' set up to track 'origin/x'.</c>
/// (with <c>-u</c>), <c>Would set upstream of …</c> (with <c>push.autoSetupRemote</c>) and
/// <c>Done</c> at the end. Code that parses lines sequentially would silently choke on
/// these. The separator: <b>the ref line has exactly two tabs</b>, the others have none.
/// </para>
/// <para>
/// 🔴 <b>MEASURED — the flag field CAN be a SPACE.</b> On a normal fast-forward the flag
/// is <c>' '</c>; if the line is <c>Trim()</c>'d, fields shift and every fast-forward
/// would be misclassified.
/// </para>
/// </remarks>
public sealed class PushWriter : IPushWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public PushWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<PushPlan> PlanAsync(
        string workingDirectory,
        string remote,
        string localBranch,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(localBranch);

        GitResult refs = await _runner.RunCheckedAsync(
            GitCommand.Create(
                workingDirectory,
                "for-each-ref",
                "--format=%(refname)%00%(objectname)%00%(symref)%00%(upstream:short)%00%(upstream:track)",
                "refs/heads",
                "refs/remotes",
                "refs/tags"),
            cancellationToken).ConfigureAwait(false);

        string prefix = RemoteName.RemotesPrefix + remote + "/";
        string? remoteTip = null;
        string? upstream = null;
        UpstreamTracking tracking = UpstreamTracking.None;
        List<string> tags = [];
        List<string> remoteBranches = [];

        foreach (string line in refs.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split('\0');

            if (fields.Length != 5)
            {
                continue;
            }

            string name = fields[0];

            if (name == BranchName.HeadsPrefix + localBranch)
            {
                upstream = fields[3].Length > 0 ? fields[3] : null;
                tracking = RefReader.ParseTracking(fields[4]);
            }
            else if (name.StartsWith(RefChange.TagsPrefix, StringComparison.Ordinal))
            {
                tags.Add(name[RefChange.TagsPrefix.Length..]);
            }
            else if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                // The symbolic `origin/HEAD` is skipped: there's no `refs/heads/HEAD`
                // branch on the remote; putting it in the delete list would offer the
                // user a branch that doesn't exist. The same ref sets this trap for the
                // fourth time in this project (P03-T12, P06-T05, P06-T06).
                if (fields[2].Length > 0)
                {
                    continue;
                }

                string branch = name[prefix.Length..];
                remoteBranches.Add(branch);

                if (branch == localBranch)
                {
                    remoteTip = fields[1];
                }
            }
        }

        return new PushPlan
        {
            Remote = remote,
            LocalBranch = localBranch,
            RemoteBranch = localBranch,
            RemoteTipObjectId = remoteTip,
            HasUpstream = upstream is not null,
            Tracking = tracking,
            Tags = tags,
            RemoteBranches = remoteBranches,
        };
    }

    public async Task<PushResult> PushAsync(
        string workingDirectory,
        PushOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        string standardOutput;
        string standardError;
        bool aborted = false;

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

            standardOutput = result.GetStandardOutputText();
            standardError = result.StandardError;
        }
        catch (GitException error)
        {
            // 🔴 Exit code 1 does NOT mean "nothing went through": in the measurement two
            // branches were pushed, one was rejected, the other really did go through —
            // the code was still 1. The real result is in the porcelain lines; discarding
            // them would make the user retry a push that already succeeded. If there are
            // no lines at all (code 128, remote missing / unreachable) the failure really
            // is fatal and is rethrown as-is.
            standardOutput = error.StandardOutput;
            standardError = error.StandardError;

            if (PushPorcelainParser.Parse(standardOutput).Count == 0)
            {
                throw;
            }

            aborted = false;
        }

        return new PushResult
        {
            Refs = PushPorcelainParser.Parse(standardOutput),
            RemoteMessages = ParseRemoteMessages(standardError),
            DryRun = options.DryRun,
            Aborted = aborted,
        };
    }

    public string DescribeCommand(PushOptions options) => Describe(options);

    /// <summary>Produces the command that will run ("show the command" principle).</summary>
    public static string Describe(PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return "git " + string.Join(' ', BuildArguments(options).Where(part => part != "--progress"));
    }

    /// <remarks>
    /// <c>--porcelain</c> is always passed: it's the only machine-readable channel for the
    /// result. The <c>--</c> separator is always passed too — a branch name starting with
    /// <c>-</c> would otherwise be mistaken for a flag (the lesson from P06-T01).
    /// </remarks>
    private static IReadOnlyList<string> BuildArguments(PushOptions options)
    {
        List<string> arguments = ["push", "--porcelain", "--progress"];

        if (options.SetUpstream)
        {
            arguments.Add("--set-upstream");
        }

        if (options.Refs.Any(spec => spec.Delete))
        {
            arguments.Add("--delete");
        }

        if (options.ForceWithLease)
        {
            foreach (PushSpec spec in options.Refs)
            {
                // If there's no anchor, we don't fall back to git's implicit form: measured,
                // that form drops the protection entirely after a fetch. A ref without an
                // anchor is not forced.
                if (spec.ExpectedRemoteObjectId is { Length: > 0 } expected)
                {
                    arguments.Add($"--force-with-lease={spec.Destination}:{expected}");
                }
            }
        }

        switch (options.Tags)
        {
            case PushTagMode.All:
                arguments.Add("--tags");
                break;
            case PushTagMode.FollowAnnotated:
                arguments.Add("--follow-tags");
                break;
            case PushTagMode.None:
            default:
                break;
        }

        if (options.DryRun)
        {
            arguments.Add("--dry-run");
        }

        arguments.Add("--");
        arguments.Add(options.Remote);

        foreach (PushSpec spec in options.Refs)
        {
            arguments.Add(spec.ToRefspec());
        }

        return arguments;
    }

    /// <summary>Collects the remote side's <c>remote:</c> lines.</summary>
    private static IReadOnlyList<string> ParseRemoteMessages(string standardError)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return [];
        }

        const string marker = "remote:";
        List<string> messages = [];

        foreach (string raw in standardError.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (!line.StartsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }

            // Measured: git pads the line with spaces up to a fixed width.
            string text = line[marker.Length..].Trim();

            if (text.Length > 0)
            {
                messages.Add(text);
            }
        }

        return messages;
    }
}

/// <summary>
/// <c>git push --porcelain</c> stdout parser (P06-T08).
/// </summary>
internal static class PushPorcelainParser
{
    public static IReadOnlyList<PushRefResult> Parse(string standardOutput)
    {
        if (string.IsNullOrEmpty(standardOutput))
        {
            return [];
        }

        List<PushRefResult> results = [];

        foreach (string raw in standardOutput.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            // ⚠️ NO Trim: on a normal fast-forward the flag field is a single SPACE.
            string[] fields = line.Split('\t');

            // `To …`, `Done`, `branch 'x' set up to track …`, `Would set upstream of …`
            // lines have no tabs at all — that's the separator.
            if (fields.Length != 3 || fields[0].Length != 1)
            {
                continue;
            }

            int colon = fields[1].LastIndexOf(':');

            if (colon < 0)
            {
                continue;
            }

            (string summary, string? reason) = SplitReason(fields[2]);

            results.Add(new PushRefResult(
                fields[0][0],
                fields[1][..colon],
                fields[1][(colon + 1)..],
                summary,
                reason));
        }

        return results;
    }

    /// <summary>
    /// Splits off the trailing <c>(reason)</c> part of the summary.
    /// </summary>
    /// <remarks>
    /// Measured forms: <c>[rejected] (fetch first)</c>, <c>[rejected] (stale info)</c>,
    /// <c>[remote rejected] (pre-receive hook declined)</c>, <c>abc..def</c> (no reason),
    /// <c>abc...def (forced update)</c>.
    /// </remarks>
    private static (string Summary, string? Reason) SplitReason(string field)
    {
        if (!field.EndsWith(')'))
        {
            return (field, null);
        }

        int open = field.LastIndexOf('(');

        return open < 0
            ? (field, null)
            : (field[..open].TrimEnd(), field[(open + 1)..^1]);
    }

    /// <summary>Numeric summary — for test readability.</summary>
    public static string Describe(PushRefResult result) => string.Create(
        CultureInfo.InvariantCulture,
        $"{result.Flag} {result.Source}:{result.Destination} {result.Summary}");
}
