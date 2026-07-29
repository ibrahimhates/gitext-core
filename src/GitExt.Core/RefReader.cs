using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Dalları, tag'leri, uzak depoları ve <c>HEAD</c> durumunu okur (P02-T09).
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
    /// <c>for-each-ref</c> alan sırası.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Alan ayracı <c>%00</c>, kayıt ayracı satır sonu.</b> Bu güvenli çünkü ref adları
    /// satır sonu içeremez — <c>git check-ref-format</c> reddediyor (ölçüldü).
    /// </para>
    /// <para>
    /// ⚠️ <c>for-each-ref</c> <b><c>-z</c> bayrağını desteklemiyor</b>
    /// (<c>error: unknown switch 'z'</c>, ölçüldü). <c>git log</c>'daki yaklaşımın aynısı
    /// burada uygulanamaz.
    /// </para>
    /// <para>
    /// <c>%(subject)</c> en sonda: olası bir sürpriz satır sonu yalnızca o kaydın sonunu
    /// etkiler, diğer alanları kaydırmaz.
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
            // Boş alanlar korunmalı; bölme sınırı verilmiyor çünkü alan sayısı sabit.
            string[] fields = line.Split('\0');

            if (fields.Length < RefFieldCount)
            {
                // Beklenmeyen biçim: sessizce yanlış veri üretmektense o ref'i atla.
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
                    // refs/stash, refs/notes/… — şimdilik ilgilenmiyoruz.
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

        // Annotated tag'de %(*objectname) tag'in işaret ettiği commit'tir; diğerlerinde boştur.
        CommitId target = CommitId.TryParse(fields[4], out CommitId dereferenced)
            ? dereferenced
            : objectId;

        // %(symref) yalnızca sembolik ref'lerde dolu; normal dalda BOŞ dize (ölçüldü).
        string symref = fields[8];

        return new GitRef
        {
            FullName = fullName,
            ShortName = fields[1],
            Kind = GitRef.ClassifyKind(fullName),
            ObjectId = objectId,
            TargetCommit = target,
            IsAnnotatedTag = isAnnotatedTag,
            SymbolicTarget = string.IsNullOrEmpty(symref) ? null : symref,
        };
    }

    private static BranchInfo BuildBranch(GitRef reference, string[] fields)
    {
        // %(HEAD) mevcut dal için "*", diğerleri için BOŞLUK döner — boş dize değil (ölçüldü).
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
    /// <c>%(upstream:track)</c> alanını ayrıştırır.
    /// </summary>
    /// <remarks>
    /// Ölçülen biçimler: <c>[ahead 3, behind 2]</c> · <c>[ahead 1]</c> · <c>[behind 4]</c> ·
    /// <c>[gone]</c> · boş (senkron ya da upstream yok).
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
    /// <c>HEAD</c>'in dala mı yoksa doğrudan commit'e mi baktığını belirler.
    /// </summary>
    /// <remarks>
    /// <c>%(HEAD)</c> alanı yeterli değil: detached durumda <b>hiçbir dal</b> işaretlenmiyor
    /// (ölçüldü). Bu yüzden <c>symbolic-ref</c> ile ayrıca soruluyor.
    /// </remarks>
    private async Task<HeadState> ReadHeadAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // -q: detached durumda hata yazmaz, yalnızca sıfır olmayan kod döner.
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
                // Doğmamış depoda commit yoktur ve bu bir hata değildir.
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
    /// Yapılandırılmış uzak depoları okur.
    /// </summary>
    /// <remarks>
    /// <c>git remote -v</c> insan-okunur; onun yerine <c>config --get-regexp</c> kullanılıyor
    /// (ADR-0002: insan-okunur çıktı ayrıştırılmaz).
    /// </remarks>
    private async Task<IReadOnlyList<RemoteInfo>> ReadRemotesAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", "--get-regexp", "^remote\\..*\\.(url|pushurl)$"],
                // Hiç remote yoksa 1 döner; bu bir hata değil.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        Dictionary<string, (string? Url, string? PushUrl)> remotes = [];

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Biçim: "remote.origin.url https://…"
            int space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space <= 0)
            {
                continue;
            }

            string key = line[..space];
            string value = line[(space + 1)..].Trim();

            if (!key.StartsWith("remote.", StringComparison.Ordinal))
            {
                continue;
            }

            int lastDot = key.LastIndexOf('.');
            if (lastDot <= "remote.".Length)
            {
                continue;
            }

            // Uzak adı nokta içerebilir; bu yüzden baştan ve sondan kırpıyoruz.
            string name = key["remote.".Length..lastDot];
            string suffix = key[(lastDot + 1)..];

            remotes.TryGetValue(name, out (string? Url, string? PushUrl) entry);

            remotes[name] = suffix switch
            {
                "url" => (value, entry.PushUrl),
                "pushurl" => (entry.Url, value),
                _ => entry,
            };
        }

        return
        [
            .. remotes
                .Where(pair => pair.Value.Url is not null || pair.Value.PushUrl is not null)
                .Select(pair => new RemoteInfo
                {
                    Name = pair.Key,
                    FetchUrl = pair.Value.Url ?? pair.Value.PushUrl!,
                    PushUrl = pair.Value.PushUrl ?? pair.Value.Url!,
                })
                .OrderBy(remote => remote.Name, StringComparer.Ordinal),
        ];
    }
}
