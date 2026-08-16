using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Reads the state of the working directory (P02-T10).
/// </summary>
public interface IStatusReader
{
    Task<WorkingTreeStatus> ReadAsync(
        string workingDirectory,
        bool includeIgnored = false,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IStatusReader"/>
/// <remarks>
/// <para>
/// <b>The most complex parser in this phase.</b> <c>--porcelain=v2</c> lines are <b>not
/// uniform</b>: the <c>1</c>, <c>2</c>, <c>u</c>, <c>?</c> and <c>!</c> prefixes have different field
/// layouts. The "fixed field count" approach from <c>git log</c> does not work here.
/// </para>
/// <para>
/// The critical measured behaviour: in <c>-z</c> mode <b>a rename/copy entry spans two NUL
/// records</b> — the <c>2 …</c> line ends with the new path, and <b>the next record</b> is the
/// source path. Assume a single record and every following entry shifts, corrupting the data
/// silently.
/// </para>
/// </remarks>
public sealed class StatusReader : IStatusReader
{
    private readonly IGitProcessRunner _runner;

    public StatusReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<WorkingTreeStatus> ReadAsync(
        string workingDirectory,
        bool includeIgnored = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        List<string> arguments =
        [
            "status",
            "--porcelain=v2",
            "-z",
            "--branch",
            "--untracked-files=all",
        ];

        if (includeIgnored)
        {
            arguments.Add("--ignored");
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = arguments,
                // status may want to refresh the index; we treat it as read-only and use
                // GIT_OPTIONAL_LOCKS=0 to keep it from colliding with concurrent writes.
                IsReadOnly = true,
            },
            cancellationToken).ConfigureAwait(false);

        WorkingTreeStatus status = Parse(result.SplitStandardOutputAtNulPreservingEmpty());

        return status.IsDetached
            ? await DisambiguateDetachedAsync(workingDirectory, status, cancellationToken)
                .ConfigureAwait(false)
            : status;
    }

    /// <summary>
    /// 🔴 <c>(detached)</c> is <b>a valid branch name</b> — the distinction is put to git.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MEASURED (P06-T04):</b> <c>git check-ref-format --branch "(detached)"</c> accepts it and
    /// <c>git branch "(detached)"</c> really does create it. While on that branch,
    /// <c>--porcelain=v2</c> still prints <c>&#35; branch.head (detached)</c> — so the output is
    /// <b>indistinguishable</b>, and a user sitting on a branch would get the warning "detached HEAD,
    /// your commits may be lost".
    /// </para>
    /// <para>
    /// The extra call is made <b>only</b> when the value is literally <c>(detached)</c>: zero cost on
    /// the common path, the right answer on the rare one. <c>symbolic-ref</c> gives exit code 1 on a
    /// genuinely detached HEAD (<see cref="RefReader"/> uses the same route).
    /// </para>
    /// </remarks>
    private async Task<WorkingTreeStatus> DisambiguateDetachedAsync(
        string workingDirectory,
        WorkingTreeStatus status,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["symbolic-ref", "-q", "--short", "HEAD"],

                // It returns 1 on a detached HEAD; not an error, but the answer itself.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return status;
        }

        string branch = result.GetStandardOutputText().Trim();

        return branch.Length == 0
            ? status
            : status with { IsDetached = false, BranchName = branch };
    }

    /// <summary>
    /// Parses the NUL-separated records.
    /// </summary>
    /// <remarks>
    /// The records are walked <b>by index</b> because a rename entry consumes the next record as
    /// well; it cannot be written with a <c>foreach</c>.
    /// </remarks>
    internal static WorkingTreeStatus Parse(string[] records)
    {
        CommitId head = default;
        string? branchName = null;
        string? upstream = null;
        bool isDetached = false;
        bool isUnborn = false;
        UpstreamTracking tracking = UpstreamTracking.None;

        List<FileStatus> entries = [];

        for (int i = 0; i < records.Length; i++)
        {
            string record = records[i];

            if (record.Length == 0)
            {
                continue;
            }

            switch (record[0])
            {
                case '#':
                    ApplyHeader(record, ref head, ref branchName, ref upstream,
                        ref isDetached, ref isUnborn, ref tracking);
                    break;

                case '1':
                    if (ParseOrdinary(record) is { } ordinary)
                    {
                        entries.Add(ordinary);
                    }

                    break;

                case '2':
                    // The source path is in the NEXT record (measured).
                    string? originalPath = i + 1 < records.Length ? records[++i] : null;

                    if (ParseRenamed(record, originalPath) is { } renamed)
                    {
                        entries.Add(renamed);
                    }

                    break;

                case 'u':
                    if (ParseUnmerged(record) is { } unmerged)
                    {
                        entries.Add(unmerged);
                    }

                    break;

                case '?':
                    if (RepositoryPath.TryParse(record[2..], out RepositoryPath untracked))
                    {
                        entries.Add(new FileStatus { Path = untracked, IsUntracked = true });
                    }

                    break;

                case '!':
                    if (RepositoryPath.TryParse(record[2..], out RepositoryPath ignored))
                    {
                        entries.Add(new FileStatus { Path = ignored, IsIgnored = true });
                    }

                    break;

                default:
                    // The documentation says so explicitly: unrecognised lines must be ignored,
                    // because git may add new line types in future.
                    break;
            }
        }

        return new WorkingTreeStatus
        {
            Head = head,
            BranchName = branchName,
            IsDetached = isDetached,
            IsUnborn = isUnborn,
            Upstream = upstream,
            Tracking = tracking,
            Entries = entries,
        };
    }

    private static void ApplyHeader(
        string record,
        ref CommitId head,
        ref string? branchName,
        ref string? upstream,
        ref bool isDetached,
        ref bool isUnborn,
        ref UpstreamTracking tracking)
    {
        // The format: "# branch.oid <sha>" / "# branch.head <name>" / "# branch.ab +N -M"
        string[] parts = record.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
        {
            return;
        }

        switch (parts[1])
        {
            case "branch.oid":
                // In an unborn repository "(initial)" arrives — measured.
                isUnborn = parts[2] == "(initial)";
                if (!isUnborn && CommitId.TryParse(parts[2], out CommitId id))
                {
                    head = id;
                }

                break;

            case "branch.head":
                // In a detached state "(detached)" arrives — measured.
                isDetached = parts[2] == "(detached)";
                branchName = isDetached ? null : parts[2];
                break;

            case "branch.upstream":
                upstream = parts[2];
                break;

            case "branch.ab":
                tracking = ParseAheadBehind(parts[2]);
                break;

            default:
                // An unrecognised header — the documentation tells us to ignore it.
                break;
        }
    }

    /// <summary>
    /// Parses the <c># branch.ab +2 -0</c> format.
    /// </summary>
    internal static UpstreamTracking ParseAheadBehind(string value)
    {
        int ahead = 0;
        int behind = 0;

        foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 2 || !int.TryParse(
                    token.AsSpan(1), CultureInfo.InvariantCulture, out int count))
            {
                continue;
            }

            if (token[0] == '+')
            {
                ahead = count;
            }
            else if (token[0] == '-')
            {
                behind = count;
            }
        }

        return new UpstreamTracking(ahead, behind, IsGone: false);
    }

    /// <summary>
    /// <c>1 &lt;XY&gt; &lt;sub&gt; &lt;mH&gt; &lt;mI&gt; &lt;mW&gt; &lt;hH&gt; &lt;hI&gt; &lt;path&gt;</c>
    /// </summary>
    private static FileStatus? ParseOrdinary(string record)
    {
        // A path can contain spaces; that is why a limited split (8 parts) is used —
        // the last part is the whole path.
        string[] parts = record.Split(' ', 9);

        if (parts.Length < 9 || !RepositoryPath.TryParse(parts[8], out RepositoryPath path))
        {
            return null;
        }

        (FileChangeKind staged, FileChangeKind unstaged) = ParseXy(parts[1]);

        return new FileStatus
        {
            Path = path,
            StagedChange = staged,
            UnstagedChange = unstaged,
            Submodule = ParseSubmodule(parts[2]),
        };
    }

    /// <summary>
    /// <c>2 &lt;XY&gt; … &lt;X&gt;&lt;score&gt; &lt;path&gt;</c> plus the source path in a separate record.
    /// </summary>
    private static FileStatus? ParseRenamed(string record, string? originalPath)
    {
        string[] parts = record.Split(' ', 10);

        if (parts.Length < 10 || !RepositoryPath.TryParse(parts[9], out RepositoryPath path))
        {
            return null;
        }

        (FileChangeKind staged, FileChangeKind unstaged) = ParseXy(parts[1]);

        // parts[8] = "R100" veya "C75"
        int? score = parts[8].Length > 1
                     && int.TryParse(parts[8].AsSpan(1), CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

        RepositoryPath? source =
            RepositoryPath.TryParse(originalPath, out RepositoryPath original) ? original : null;

        return new FileStatus
        {
            Path = path,
            StagedChange = staged,
            UnstagedChange = unstaged,
            Submodule = ParseSubmodule(parts[2]),
            OriginalPath = source,
            SimilarityScore = score,
        };
    }

    /// <summary>
    /// <c>u &lt;XY&gt; &lt;sub&gt; &lt;m1&gt; &lt;m2&gt; &lt;m3&gt; &lt;mW&gt; &lt;h1&gt; &lt;h2&gt; &lt;h3&gt; &lt;path&gt;</c>
    /// </summary>
    private static FileStatus? ParseUnmerged(string record)
    {
        string[] parts = record.Split(' ', 11);

        if (parts.Length < 11 || !RepositoryPath.TryParse(parts[10], out RepositoryPath path))
        {
            return null;
        }

        return new FileStatus
        {
            Path = path,
            StagedChange = FileChangeKind.Unmerged,
            UnstagedChange = FileChangeKind.Unmerged,
            Conflict = ParseConflict(parts[1]),
            Submodule = ParseSubmodule(parts[2]),
        };
    }

    private static (FileChangeKind Staged, FileChangeKind Unstaged) ParseXy(string xy) =>
        xy.Length < 2
            ? (FileChangeKind.Unmodified, FileChangeKind.Unmodified)
            : (ToChangeKind(xy[0]), ToChangeKind(xy[1]));

    private static FileChangeKind ToChangeKind(char code) => code switch
    {
        'M' => FileChangeKind.Modified,
        'A' => FileChangeKind.Added,
        'D' => FileChangeKind.Deleted,
        'R' => FileChangeKind.Renamed,
        'C' => FileChangeKind.Copied,
        'T' => FileChangeKind.TypeChanged,
        'U' => FileChangeKind.Unmerged,
        // '.' and anything unrecognised.
        _ => FileChangeKind.Unmodified,
    };

    /// <summary>
    /// Makes sense of the conflict <c>XY</c> pair.
    /// </summary>
    /// <remarks>
    /// "us" is the current branch (<c>HEAD</c>), "them" the branch being merged.
    /// </remarks>
    internal static ConflictKind ParseConflict(string xy) => xy switch
    {
        "UU" => ConflictKind.BothModified,
        "AA" => ConflictKind.BothAdded,
        "DD" => ConflictKind.BothDeleted,
        "AU" => ConflictKind.AddedByUs,
        "UA" => ConflictKind.AddedByThem,
        "DU" => ConflictKind.DeletedByUs,
        "UD" => ConflictKind.DeletedByThem,
        _ => ConflictKind.None,
    };

    /// <summary>
    /// <c>N...</c> (not a submodule) or <c>S&lt;c&gt;&lt;m&gt;&lt;u&gt;</c>.
    /// </summary>
    private static SubmoduleState? ParseSubmodule(string field)
    {
        if (field.Length < 4 || field[0] != 'S')
        {
            return null;
        }

        return new SubmoduleState(
            CommitChanged: field[1] == 'C',
            HasTrackedChanges: field[2] == 'M',
            HasUntrackedChanges: field[3] == 'U');
    }
}
