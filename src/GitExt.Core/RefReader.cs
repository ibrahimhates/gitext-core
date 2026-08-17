using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Reads branches, tags, remotes and the <c>HEAD</c> state (P02-T09).
/// </summary>
public interface IRefReader
{
    Task<RepositoryRefs> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRefReader"/>
public sealed class RefReader : IRefReader
{
    private readonly IGitProcessRunner _runner;

    public RefReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <summary>
    /// The <c>for-each-ref</c> field order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The field separator is <c>%00</c>, the record separator a line ending.</b> That is safe
    /// because ref names cannot contain a line ending — <c>git check-ref-format</c> rejects it
    /// (measured).
    /// </para>
    /// <para>
    /// ⚠️ <c>for-each-ref</c> <b>does not support the <c>-z</c> flag</b>
    /// (<c>error: unknown switch 'z'</c>, measured). The approach used for <c>git log</c> cannot be
    /// applied here.
    /// </para>
    /// <para>
    /// <c>%(subject)</c> comes last: a surprise line ending would then affect only the end of that
    /// record and not shift the other fields.
    /// </para>
    /// </remarks>
    private const string RefFormat =
        "%(refname)%00%(refname:short)%00%(objecttype)%00%(objectname)%00%(*objectname)"
        + "%00%(HEAD)%00%(upstream:short)%00%(upstream:track)%00%(symref)%00%(subject)";

    private const int RefFieldCount = 10;

    public async Task<RepositoryRefs> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        HeadState head = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "for-each-ref", $"--format={RefFormat}"),
            cancellationToken).ConfigureAwait(false);

        List<BranchInfo> localBranches = [];
        List<BranchInfo> remoteBranches = [];
        List<TagInfo> tags = [];

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Empty fields must be preserved; no split limit is given because the field count is fixed.
            string[] fields = line.Split('\0');

            if (fields.Length < RefFieldCount)
            {
                // An unexpected format: rather than silently produce wrong data, skip that ref.
                continue;
            }

            GitRef reference = BuildRef(fields);

            switch (reference.Kind)
            {
                case GitRefKind.LocalBranch:
                    localBranches.Add(BuildBranch(reference, fields));
                    break;

                case GitRefKind.RemoteBranch:
                    remoteBranches.Add(BuildBranch(reference, fields));
                    break;

                case GitRefKind.Tag:
                    tags.Add(new TagInfo { Ref = reference, Subject = fields[9] });
                    break;

                default:
                    // refs/stash, refs/notes/… — not of interest for now.
                    break;
            }
        }

        IReadOnlyList<RemoteInfo> remotes =
            await ReadRemotesAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        return new RepositoryRefs
        {
            Head = head,
            LocalBranches = localBranches,
            RemoteBranches = remoteBranches,
            Tags = tags,
            Remotes = remotes,
        };
    }

    private static GitRef BuildRef(string[] fields)
    {
        string fullName = fields[0];
        bool isAnnotatedTag = string.Equals(fields[2], "tag", StringComparison.Ordinal);

        CommitId objectId = CommitId.TryParse(fields[3], out CommitId parsed) ? parsed : default;

        // On an annotated tag %(*objectname) is the commit the tag points at; on others it is empty.
        CommitId target = CommitId.TryParse(fields[4], out CommitId dereferenced)
            ? dereferenced
            : objectId;

        // %(symref) is only filled in for symbolic refs; on a normal branch it is an EMPTY string (measured).
        string symref = fields[8];
        string shortName = fields[1];

        // Old git versions may print refs/remotes/<name>/HEAD as "<name>/HEAD" while newer
        // ones abbreviate it to "<name>". Normalize so filtering logic is version-independent.
        if (!string.IsNullOrEmpty(symref)
            && fullName.StartsWith(RemoteName.RemotesPrefix, StringComparison.Ordinal)
            && fullName.EndsWith("/HEAD", StringComparison.Ordinal))
        {
            shortName = fullName[
                RemoteName.RemotesPrefix.Length..^"/HEAD".Length];
        }

        return new GitRef
        {
            FullName = fullName,
            ShortName = shortName,
            Kind = GitRef.ClassifyKind(fullName),
            ObjectId = objectId,
            TargetCommit = target,
            IsAnnotatedTag = isAnnotatedTag,
            SymbolicTarget = string.IsNullOrEmpty(symref) ? null : symref,
        };
    }

    private static BranchInfo BuildBranch(GitRef reference, string[] fields)
    {
        // %(HEAD) returns "*" for the current branch and a SPACE for the others — not an empty string (measured).
        bool isCurrent = fields[5].Trim() == "*";
        string upstream = fields[6];

        return new BranchInfo
        {
            Ref = reference,
            IsCurrent = isCurrent,
            Upstream = string.IsNullOrEmpty(upstream) ? null : upstream,
            Tracking = ParseTracking(fields[7]),
        };
    }

    /// <summary>
    /// Parses the <c>%(upstream:track)</c> field.
    /// </summary>
    /// <remarks>
    /// The measured forms: <c>[ahead 3, behind 2]</c> · <c>[ahead 1]</c> · <c>[behind 4]</c> ·
    /// <c>[gone]</c> · empty (in sync, or no upstream).
    /// </remarks>
    internal static UpstreamTracking ParseTracking(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UpstreamTracking.None;
        }

        ReadOnlySpan<char> span = value.AsSpan().Trim().Trim('[').Trim(']');

        if (span.Equals("gone", StringComparison.OrdinalIgnoreCase))
        {
            return new UpstreamTracking(0, 0, IsGone: true);
        }

        int ahead = ReadCount(span, "ahead");
        int behind = ReadCount(span, "behind");

        return new UpstreamTracking(ahead, behind, IsGone: false);
    }

    private static int ReadCount(ReadOnlySpan<char> span, string keyword)
    {
        int index = span.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return 0;
        }

        ReadOnlySpan<char> rest = span[(index + keyword.Length)..].TrimStart();

        int digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        return digits == 0
            ? 0
            : int.Parse(rest[..digits], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Determines whether <c>HEAD</c> looks at a branch or straight at a commit.
    /// </summary>
    /// <remarks>
    /// The <c>%(HEAD)</c> field is not enough: in a detached state <b>no branch</b> is marked
    /// (measured). That is why <c>symbolic-ref</c> is asked separately.
    /// </remarks>
    private async Task<HeadState> ReadHeadAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // -q: in a detached state it prints no error, it just returns a non-zero code.
        GitResult symbolic = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["symbolic-ref", "-q", "--short", "HEAD"],
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        string branchName = symbolic.GetStandardOutputText().Trim();
        bool isDetached = symbolic.ExitCode != 0 || branchName.Length == 0;

        GitResult revParse = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", "HEAD"],
                // In an unborn repository there is no commit, and that is not an error.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        CommitId commit = CommitId.TryParse(revParse.GetStandardOutputText().Trim(), out CommitId id)
            ? id
            : default;

        return new HeadState
        {
            IsDetached = isDetached,
            IsUnborn = commit.IsEmpty,
            BranchName = isDetached ? null : branchName,
            Commit = commit,
        };
    }

    /// <summary>
    /// Reads the configured remotes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>git remote -v</c> is human-readable; <c>config</c> is used instead (ADR-0002: human-readable
    /// output is not parsed).
    /// </para>
    /// <para>
    /// 🔴 <b>The parsing is NOT here but in <see cref="RemoteConfigParser"/></b> (P06-T05). There used
    /// to be a second parser here, and measurement showed three silent differences: because it did
    /// not use <c>-z</c>, a URL containing a line ending was <b>split in two</b>; with multiple URLs
    /// <b>the last one</b> won; and a remote with no URL <b>dropped out</b> of the list. Answering the
    /// same question by two routes had allowed one of them to be silently wrong (the lesson of
    /// P06-T04).
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<RemoteInfo>> ReadRemotesAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", "-z", "--get-regexp", RemoteConfigParser.KeyPattern],
                // With no remotes at all it returns 1; that is not an error.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<GitRemote> remotes = RemoteConfigParser.Parse(
            result.ExitCode == 0 ? result.SplitStandardOutputAtNul() : [],
            knownNames: null);

        return
        [
            // A remote with no URL continues to be filtered out here: `RemoteInfo.FetchUrl` is
            // required, and callers of this kind (badges, the branch list) can do nothing with a
            // remote that has no address. The remote management screen has to show those too, which is
            // why it uses `IRemoteReader`.
            .. remotes
                .Where(remote => remote.FetchUrls.Count > 0 || remote.PushUrls.Count > 0)
                .Select(remote => new RemoteInfo
                {
                    Name = remote.Name,
                    FetchUrl = remote.Url ?? remote.PushUrls[0],
                    PushUrl = remote.EffectivePushUrls[0],
                }),
        ];
    }
}
