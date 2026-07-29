using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Çalışma dizininin durumunu okur (P02-T10).
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
/// <b>Fazın en karmaşık ayrıştırıcısı.</b> <c>--porcelain=v2</c> satırları <b>tek biçimli
/// değildir</b>: <c>1</c>, <c>2</c>, <c>u</c>, <c>?</c>, <c>!</c> önekleri farklı alan
/// düzenlerine sahiptir. <c>git log</c>'daki "sabit alan sayısı" yaklaşımı burada çalışmaz.
/// </para>
/// <para>
/// Ölçülen kritik davranış: <c>-z</c> modunda <b>rename/copy girdisi iki NUL kaydına yayılır</b> —
/// <c>2 …</c> satırı yeni yolla biter, <b>bir sonraki kayıt</b> kaynak yoldur. Tek kayıt
/// varsayılırsa sonraki tüm girdiler kayar ve veri sessizce bozulur.
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
                // status index'i tazelemek isteyebilir; salt okunur sayıp GIT_OPTIONAL_LOCKS=0
                // ile eşzamanlı yazma işlemleriyle çakışmasını önlüyoruz.
                IsReadOnly = true,
            },
            cancellationToken).ConfigureAwait(false);

        return Parse(result.SplitStandardOutputAtNulPreservingEmpty());
    }

    /// <summary>
    /// NUL ile ayrılmış kayıtları ayrıştırır.
    /// </summary>
    /// <remarks>
    /// Kayıtlar üzerinde <b>indeksle</b> ilerlenir çünkü rename girdisi bir sonraki kaydı da
    /// tüketir; <c>foreach</c> ile yazılamaz.
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
                    // Kaynak yol BİR SONRAKİ kayıttadır (ölçüldü).
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
                    // Belge açıkça söylüyor: tanınmayan satırlar yok sayılmalı, çünkü
                    // git ileride yeni satır tipleri ekleyebilir.
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
        // Biçim: "# branch.oid <sha>" / "# branch.head <ad>" / "# branch.ab +N -M"
        string[] parts = record.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
        {
            return;
        }

        switch (parts[1])
        {
            case "branch.oid":
                // Doğmamış depoda "(initial)" gelir — ölçüldü.
                isUnborn = parts[2] == "(initial)";
                if (!isUnborn && CommitId.TryParse(parts[2], out CommitId id))
                {
                    head = id;
                }

                break;

            case "branch.head":
                // Detached durumda "(detached)" gelir — ölçüldü.
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
                // Tanınmayan başlık — belge yok saymamızı söylüyor.
                break;
        }
    }

    /// <summary>
    /// <c># branch.ab +2 -0</c> biçimini ayrıştırır.
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
        // Yol boşluk içerebilir; bu yüzden sınırlı bölme (8 parça) yapılıyor —
        // son parça yolun tamamıdır.
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
    /// <c>2 &lt;XY&gt; … &lt;X&gt;&lt;score&gt; &lt;path&gt;</c> + ayrı kayıtta kaynak yol.
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
        // '.' ve tanınmayan her şey.
        _ => FileChangeKind.Unmodified,
    };

    /// <summary>
    /// Çakışma <c>XY</c> çiftini anlamlandırır.
    /// </summary>
    /// <remarks>
    /// "us" mevcut dal (<c>HEAD</c>), "them" birleştirilen dal.
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
    /// <c>N...</c> (submodule değil) veya <c>S&lt;c&gt;&lt;m&gt;&lt;u&gt;</c>.
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
