using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Blame'de bir satırın kaynağı (P07-T16).
/// </summary>
public sealed record BlameLine
{
    /// <summary>Satırı en son değiştiren commit.</summary>
    public required string ObjectId { get; init; }

    /// <summary>Şu anki dosyadaki satır numarası (1 tabanlı).</summary>
    public required int LineNumber { get; init; }

    /// <summary>O commit'teki satır numarası.</summary>
    public int OriginalLineNumber { get; init; }

    /// <summary>Satırın içeriği.</summary>
    public string Content { get; init; } = string.Empty;

    public string AuthorName { get; init; } = string.Empty;

    public string AuthorEmail { get; init; } = string.Empty;

    public DateTimeOffset AuthorTime { get; init; }

    /// <summary>Commit'in konusu.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Satırın o commit'teki dosya adı.
    /// </summary>
    /// <remarks>
    /// Yeniden adlandırmalarda şu ankinden farklı olur; "önceki sürüme git" bunu kullanıyor.
    /// </remarks>
    public string FileName { get; init; } = string.Empty;

    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;

    /// <summary>
    /// Bu satır blame'in kapsadığı en eski commit'ten mi geliyor?
    /// </summary>
    /// <remarks>
    /// git bunu <c>boundary</c> ile işaretliyor; daha geriye gidilemez.
    /// </remarks>
    public bool IsBoundary { get; init; }
}

/// <summary>Blame okuma (P07-T16).</summary>
public interface IBlameReader
{
    /// <param name="workingDirectory">Deponun çalışma dizini.</param>
    /// <param name="path">Blame'i alınacak dosya.</param>
    /// <param name="revision">
    /// Hangi sürümden bakılacağı; <see langword="null"/> ise <c>HEAD</c>.
    /// </param>
    /// <param name="cancellationToken">İptal belirteci.</param>
    Task<IReadOnlyList<BlameLine>> ReadAsync(
        string workingDirectory,
        RepositoryPath path,
        string? revision = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git blame --porcelain</c> okuyucusu (P07-T16).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ — <c>--porcelain</c> meta veriyi commit başına BİR KEZ yazıyor.</b>
/// Aynı commit'ten gelen ikinci satırda yalnızca başlık satırı var; <c>author</c>,
/// <c>summary</c> ve diğerleri <b>tekrarlanmıyor</b>. Her satırı bağımsız ayrıştıran bir
/// okuyucu, o satırların yazarını <b>boş</b> gösterirdi — hem de en sık görülen durumda
/// (aynı commit'ten gelen ardışık satırlar).
/// </para>
/// <para>
/// → Meta veri SHA'ya göre önbellekleniyor. <c>--line-porcelain</c> her satırda tekrar
/// ederdi ama çıktıyı birkaç kat büyütür; büyük dosyalarda bu gereksiz bir maliyet.
/// </para>
/// </remarks>
public sealed class BlameReader : IBlameReader
{
    private readonly IGitProcessRunner _runner;

    public BlameReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<BlameLine>> ReadAsync(
        string workingDirectory,
        RepositoryPath path,
        string? revision = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (path.IsEmpty)
        {
            return [];
        }

        List<string> arguments = ["blame", "--porcelain"];

        if (revision is { Length: > 0 } target)
        {
            arguments.Add(target);
        }

        arguments.Add("--");
        arguments.Add(path.Value);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, [.. arguments]),
            cancellationToken).ConfigureAwait(false);

        // İkili dosya ya da yok olan yol: blame yok, hata da değil.
        return result.IsSuccess ? Parse(result.GetStandardOutputLossless()) : [];
    }

    /// <summary>Commit başına bir kez gelen meta veri.</summary>
    private sealed record CommitInfo
    {
        public string AuthorName { get; set; } = string.Empty;

        public string AuthorEmail { get; set; } = string.Empty;

        public DateTimeOffset AuthorTime { get; set; }

        public string Summary { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public bool IsBoundary { get; set; }
    }

    /// <summary><c>--porcelain</c> çıktısını ayrıştırır.</summary>
    internal static IReadOnlyList<BlameLine> Parse(string output)
    {
        List<BlameLine> lines = [];

        // 🔴 Önbellek: meta veri commit başına BİR KEZ geliyor.
        Dictionary<string, CommitInfo> commits = new(StringComparer.Ordinal);

        string currentSha = string.Empty;
        int finalLine = 0;
        int originalLine = 0;
        CommitInfo? current = null;

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            // İçerik satırı sekmeyle başlıyor — başlıklardan tek ayırt edici işaret bu.
            if (line[0] == '\t')
            {
                if (current is not null)
                {
                    lines.Add(new BlameLine
                    {
                        ObjectId = currentSha,
                        LineNumber = finalLine,
                        OriginalLineNumber = originalLine,
                        Content = line[1..],
                        AuthorName = current.AuthorName,
                        AuthorEmail = current.AuthorEmail,
                        AuthorTime = current.AuthorTime,
                        Summary = current.Summary,
                        FileName = current.FileName,
                        IsBoundary = current.IsBoundary,
                    });
                }

                continue;
            }

            string[] parts = line.Split(' ');

            // Başlık: "<sha> <özgün satır> <son satır> [<satır sayısı>]"
            if (parts.Length >= 3 && IsObjectId(parts[0]))
            {
                currentSha = parts[0];

                originalLine = int.TryParse(parts[1], CultureInfo.InvariantCulture, out int original)
                    ? original
                    : 0;

                finalLine = int.TryParse(parts[2], CultureInfo.InvariantCulture, out int final)
                    ? final
                    : 0;

                if (!commits.TryGetValue(currentSha, out CommitInfo? info))
                {
                    info = new CommitInfo();
                    commits[currentSha] = info;
                }

                current = info;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            // Anahtar/değer satırları — yalnızca commit'in İLK görülüşünde geliyorlar.
            int space = line.IndexOf(' ', StringComparison.Ordinal);
            string key = space < 0 ? line : line[..space];
            string value = space < 0 ? string.Empty : line[(space + 1)..];

            switch (key)
            {
                case "author":
                    current.AuthorName = value;
                    break;
                case "author-mail":
                    current.AuthorEmail = value.Trim('<', '>');
                    break;
                case "author-time":
                    if (long.TryParse(value, CultureInfo.InvariantCulture, out long seconds))
                    {
                        current.AuthorTime = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    }

                    break;
                case "summary":
                    current.Summary = value;
                    break;
                case "filename":
                    current.FileName = value;
                    break;
                case "boundary":
                    current.IsBoundary = true;
                    break;
                default:
                    break;
            }
        }

        return lines;
    }

    private static bool IsObjectId(string value) =>
        value.Length is >= 7 and <= 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
