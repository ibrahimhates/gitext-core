using System.Globalization;
using System.Runtime.CompilerServices;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Commit geçmişini okumak için sorgu (P02-T08).
/// </summary>
public sealed record CommitLogQuery
{
    /// <summary>
    /// Başlangıç noktası: dal adı, tag, SHA veya <c>HEAD</c>. Boşsa mevcut <c>HEAD</c>.
    /// </summary>
    public string? Revision { get; init; }

    /// <summary>Tüm ref'lerden geçmişi oku (<c>--all</c>).</summary>
    public bool IncludeAllRefs { get; init; }

    /// <summary>Merge'lerde yalnızca ilk ebeveyni izle (<c>--first-parent</c>).</summary>
    public bool FirstParentOnly { get; init; }

    /// <summary>En fazla kaç commit okunacak. <see langword="null"/> ise sınırsız.</summary>
    public int? MaxCount { get; init; }

    /// <summary>Baştan kaç commit atlanacak.</summary>
    public int Skip { get; init; }

    /// <summary>Yalnızca bu yolları etkileyen commit'ler.</summary>
    public IReadOnlyList<RepositoryPath> Paths { get; init; } = [];

    /// <summary>Commit mesajında arama (<c>--grep</c>).</summary>
    public string? MessageContains { get; init; }

    /// <summary>Yazara göre filtre (<c>--author</c>).</summary>
    public string? Author { get; init; }
}

/// <summary>
/// Commit geçmişini okur.
/// </summary>
public interface ICommitLogReader
{
    /// <summary>
    /// Geçmişi baştan sona okur ve listeler.
    /// </summary>
    Task<IReadOnlyList<CommitInfo>> ReadAsync(
        string workingDirectory,
        CommitLogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Geçmişi akış hâlinde okur — <c>git</c> tamamlanmadan ilk commit'ler üretilir.
    /// </summary>
    /// <remarks>
    /// Büyük depolarda arayüzün ilk ekranı hemen çizebilmesi için (P02-T04).
    /// </remarks>
    IAsyncEnumerable<CommitInfo> StreamAsync(
        string workingDirectory,
        CommitLogQuery query,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICommitLogReader"/>
public sealed class CommitLogReader : ICommitLogReader
{
    private readonly IGitProcessRunner _runner;

    public CommitLogReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <summary>
    /// Alan sırası. <b>Bu sıra <see cref="FieldCount"/> ve ayrıştırıcıyla birlikte değişmeli.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Alan ayracı <c>%x00</c>; <c>-z</c> ile kayıtlar da NUL ile ayrılır. Bu belirsizlik
    /// yaratmaz çünkü <b>hiçbir alan NUL içeremez</b> — git commit mesajında NUL baytını
    /// açıkça reddediyor (<c>a NUL byte in commit log message not allowed</c>, ölçüldü).
    /// Dolayısıyla tüm akış düz bir NUL ayraçlı parça dizisidir ve sabit alan sayısıyla
    /// güvenle gruplanır.
    /// </para>
    /// <para>
    /// <c>%aI</c> / <c>%cI</c> katı ISO-8601, saat dilimi ofsetiyle:
    /// commit'in atıldığı yerel saat korunur.
    /// </para>
    /// </remarks>
    private const string Format =
        "%H%x00%P%x00%an%x00%ae%x00%aI%x00%cn%x00%ce%x00%cI%x00%D%x00%e%x00%s%x00%b";

    private const int FieldCount = 12;

    public async Task<IReadOnlyList<CommitInfo>> ReadAsync(
        string workingDirectory,
        CommitLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        GitResult result = await _runner
            .RunCheckedAsync(BuildCommand(workingDirectory, query), cancellationToken)
            .ConfigureAwait(false);

        // Boş parçaları KORUYAN bölme şart: gövdesiz bir commit boş bir alan üretir ve
        // atılırsa sonraki tüm alanlar kayar.
        string[] fields = result.SplitStandardOutputAtNulPreservingEmpty();

        List<CommitInfo> commits = new(fields.Length / FieldCount);

        for (int offset = 0; offset + FieldCount <= fields.Length; offset += FieldCount)
        {
            commits.Add(ParseRecord(fields.AsSpan(offset, FieldCount)));
        }

        return commits;
    }

    public async IAsyncEnumerable<CommitInfo> StreamAsync(
        string workingDirectory,
        CommitLogQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string[] window = new string[FieldCount];
        int filled = 0;

        await foreach (string field in _runner
                           .StreamNulSeparatedAsync(BuildCommand(workingDirectory, query), cancellationToken)
                           .ConfigureAwait(false))
        {
            window[filled++] = field;

            if (filled < FieldCount)
            {
                continue;
            }

            filled = 0;
            yield return ParseRecord(window);
        }

        // filled > 0 ise akış yarım bir kayıtla bitmiş demektir. Bu, format dizesiyle
        // FieldCount'un uyuşmadığı anlamına gelir — sessizce yutmak yerine haber ver.
        if (filled > 0)
        {
            throw new InvalidOperationException(
                $"git log çıktısı yarım bir kayıtla bitti ({filled}/{FieldCount} alan). "
                + "Format dizesi ile alan sayısı uyuşmuyor olabilir.");
        }
    }

    private static GitCommand BuildCommand(string workingDirectory, CommitLogQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        List<string> arguments = ["log", "-z", $"--format={Format}"];

        if (query.IncludeAllRefs)
        {
            arguments.Add("--all");
        }

        if (query.FirstParentOnly)
        {
            arguments.Add("--first-parent");
        }

        if (query.MaxCount is { } maxCount)
        {
            arguments.Add($"--max-count={maxCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (query.Skip > 0)
        {
            arguments.Add($"--skip={query.Skip.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            arguments.Add($"--author={query.Author}");
        }

        if (!string.IsNullOrWhiteSpace(query.MessageContains))
        {
            arguments.Add("--fixed-strings");
            arguments.Add($"--grep={query.MessageContains}");
        }

        if (!string.IsNullOrWhiteSpace(query.Revision))
        {
            arguments.Add(query.Revision);
        }

        // `--` ayracı zorunlu: tire ile başlayan veya bir ref adıyla çakışan dosya yolları
        // aksi halde revizyon sanılır.
        if (query.Paths.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(query.Paths.Select(path => path.Value));
        }

        return new GitCommand
        {
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            // Büyük geçmişler uzun sürebilir; varsayılan 2 dakika yetersiz kalabilir.
            Timeout = TimeSpan.FromMinutes(10),
        };
    }

    private static CommitInfo ParseRecord(ReadOnlySpan<string> fields) => new()
    {
        Id = CommitId.Parse(fields[0]),
        Parents = ParseParents(fields[1]),
        Author = new Signature(fields[2], fields[3], ParseTimestamp(fields[4])),
        Committer = new Signature(fields[5], fields[6], ParseTimestamp(fields[7])),
        Refs = ParseRefs(fields[8]),
        Encoding = fields[9],
        Subject = fields[10],
        // git son alandan sonra kayıt ayracı koyar; gövde satır sonuyla bitebilir.
        Body = fields[11].TrimEnd('\n'),
    };

    private static IReadOnlyList<CommitId> ParseParents(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<CommitId> parents = new(parts.Length);

        foreach (string part in parts)
        {
            if (CommitId.TryParse(part, out CommitId id))
            {
                parents.Add(id);
            }
        }

        return parents;
    }

    /// <summary>
    /// <c>%D</c> alanını ayrıştırır — virgülle ayrılmış ref adları.
    /// </summary>
    /// <remarks>
    /// Örnek: <c>HEAD -> main, origin/main, tag: v1.0</c>. Sembolik ok ve <c>tag:</c> öneki
    /// yalnızca gösterim içindir; burada ham ad korunuyor, yorumlama Faz 03'ün işi.
    /// </remarks>
    private static IReadOnlyList<string> ParseRefs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        // %aI katı ISO-8601 üretir. Ayrıştırılamazsa istisna fırlatmak yerine Unix epoch
        // dönüyoruz: tek bir bozuk tarih yüzünden tüm geçmişin okunamaması, o commit'in
        // tarihinin yanlış görünmesinden daha kötü. (Gerçek depolarda bozuk tarihler var.)
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UnixEpoch;
    }
}
