using System.Globalization;
using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Ham Git nesnelerine erişir: dosya içeriği ve ağaç listeleri (P02-T11).
/// </summary>
public interface IObjectReader
{
    /// <summary>
    /// Bir revizyondaki ağacı listeler.
    /// </summary>
    /// <param name="workingDirectory">Deponun çalışma dizini.</param>
    /// <param name="revision">Revizyon (dal, tag, SHA, <c>HEAD</c>).</param>
    /// <param name="path">Alt dizin; <see langword="null"/> ise kök.</param>
    /// <param name="recursive">Alt dizinlere inilsin mi?</param>
    /// <param name="includeSize">Blob boyutları da alınsın mı (<c>--long</c>)?</param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    Task<IReadOnlyList<TreeEntry>> ReadTreeAsync(
        string workingDirectory,
        string revision,
        RepositoryPath? path = null,
        bool recursive = false,
        bool includeSize = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Nesnelerin tür ve boyutunu içerik okumadan öğrenir.
    /// </summary>
    /// <remarks>
    /// Çok büyük bir dosyayı belleğe almadan önce boyutunu kontrol etmek için.
    /// </remarks>
    /// <param name="workingDirectory">Deponun çalışma dizini.</param>
    /// <param name="revisions">Sorgulanacak nesneler.</param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    Task<IReadOnlyList<GitObjectInfo>> GetInfoAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Birden fazla blob'u <b>tek süreç çağrısında</b> okur.
    /// </summary>
    /// <remarks>
    /// <c>cat-file --batch</c> stdin'den birden çok nesne kabul eder. Her dosya için ayrı
    /// süreç başlatmak (N+1 deseni) ADR-0002'nin bilinen zayıflığıdır; toplu okuma bunun
    /// birincil çözümüdür.
    /// </remarks>
    /// <param name="workingDirectory">Deponun çalışma dizini.</param>
    /// <param name="revisions">Okunacak nesneler, örn. <c>HEAD:src/a.txt</c>.</param>
    /// <param name="maxBytes">
    /// Nesne başına okunacak azami bayt. Aşan içerik kırpılır ve
    /// <see cref="BlobContent.IsTruncated"/> işaretlenir.
    /// </param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    Task<IReadOnlyList<BlobContent>> ReadBlobsAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        long maxBytes = ObjectReader.DefaultMaxBlobBytes,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IObjectReader"/>
public sealed class ObjectReader : IObjectReader
{
    /// <summary>
    /// Varsayılan nesne başına okuma sınırı: 10 MB.
    /// </summary>
    /// <remarks>
    /// Sınır olmadan 200 MB'lık tek bir dosya arayüzü kilitler. Kullanıcı isterse
    /// daha yüksek bir değerle yeniden okuyabilir.
    /// </remarks>
    public const long DefaultMaxBlobBytes = 10L * 1024 * 1024;

    /// <summary>
    /// İkili tespiti için taranan bayt sayısı — <c>git</c>'in kullandığı değer.
    /// </summary>
    private const int BinaryDetectionWindow = 8000;

    private readonly IGitProcessRunner _runner;

    public ObjectReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<TreeEntry>> ReadTreeAsync(
        string workingDirectory,
        string revision,
        RepositoryPath? path = null,
        bool recursive = false,
        bool includeSize = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        // ls-tree -z DESTEKLENİYOR (for-each-ref'in aksine — ölçüldü).
        List<string> arguments = ["ls-tree", "-z"];

        if (recursive)
        {
            arguments.Add("-r");
        }

        if (includeSize)
        {
            arguments.Add("--long");
        }

        arguments.Add(revision);

        if (path is { } subPath)
        {
            arguments.Add("--");
            // Sondaki eğik çizgi olmadan git dizinin kendisini tek girdi olarak döndürür;
            // içeriğini listelemek için gerekli.
            arguments.Add(subPath.Value + "/");
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand { WorkingDirectory = workingDirectory, Arguments = arguments },
            cancellationToken).ConfigureAwait(false);

        List<TreeEntry> entries = [];

        foreach (string record in result.SplitStandardOutputAtNul())
        {
            if (ParseTreeEntry(record) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// <c>&lt;mod&gt; &lt;tip&gt; &lt;sha&gt;[ &lt;boyut&gt;]&lt;TAB&gt;&lt;yol&gt;</c>
    /// </summary>
    /// <remarks>
    /// Metadata ile yol arasındaki ayraç <b>TAB</b>'dır, boşluk değil — yol boşluk içerebilir.
    /// Yolda da TAB olabileceği için yalnızca <b>ilk</b> TAB'da bölünüyor.
    /// </remarks>
    internal static TreeEntry? ParseTreeEntry(string record)
    {
        int tab = record.IndexOf('\t', StringComparison.Ordinal);

        if (tab < 0 || !RepositoryPath.TryParse(record[(tab + 1)..], out RepositoryPath path))
        {
            return null;
        }

        string[] metadata = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (metadata.Length < 3 || !CommitId.TryParse(metadata[2], out CommitId id))
        {
            return null;
        }

        // --long verildiyse dördüncü alan boyuttur; ağaçlarda "-" gelir.
        long? size = metadata.Length > 3
                     && long.TryParse(metadata[3], CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;

        return new TreeEntry
        {
            Path = path,
            Mode = metadata[0],
            Type = ParseObjectType(metadata[1]),
            Id = id,
            Size = size,
        };
    }

    public async Task<IReadOnlyList<GitObjectInfo>> GetInfoAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(revisions);

        if (revisions.Count == 0)
        {
            return [];
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["cat-file", "--batch-check"],
                StandardInput = BuildBatchInput(revisions),
            },
            cancellationToken).ConfigureAwait(false);

        List<GitObjectInfo> infos = [];

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            infos.Add(ParseBatchHeader(line) is { } header
                ? new GitObjectInfo { Id = header.Id, Type = header.Type, Size = header.Size }
                : new GitObjectInfo { Id = default, Type = GitObjectType.Missing, Size = 0 });
        }

        return infos;
    }

    public async Task<IReadOnlyList<BlobContent>> ReadBlobsAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        long maxBytes = DefaultMaxBlobBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        if (revisions.Count == 0)
        {
            return [];
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["cat-file", "--batch"],
                StandardInput = BuildBatchInput(revisions),
            },
            cancellationToken).ConfigureAwait(false);

        return ParseBatchOutput(result.StandardOutput, maxBytes);
    }

    private static ReadOnlyMemory<byte> BuildBatchInput(IReadOnlyList<string> revisions)
    {
        StringBuilder builder = new();

        foreach (string revision in revisions)
        {
            builder.Append(revision).Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// <c>cat-file --batch</c> çıktısını ayrıştırır.
    /// </summary>
    /// <remarks>
    /// Biçim (ölçüldü): <c>&lt;sha&gt; &lt;tip&gt; &lt;boyut&gt;\n&lt;içerik&gt;\n</c>.
    /// Eksik nesne: <c>&lt;girdi&gt; missing\n</c> — içerik yok.
    /// <para>
    /// İçerik <b>ikili olabilir</b>, bu yüzden bayt düzeyinde işleniyor: başlıktan sonra tam
    /// olarak <c>boyut</c> bayt okunur, ardından bir kapanış satır sonu atlanır. Metne çevirip
    /// bölmek içeriği geri dönülemez şekilde bozardı.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<BlobContent> ParseBatchOutput(byte[] output, long maxBytes)
    {
        List<BlobContent> blobs = [];
        int offset = 0;

        while (offset < output.Length)
        {
            int newline = Array.IndexOf(output, (byte)'\n', offset);

            if (newline < 0)
            {
                break;
            }

            string header = Encoding.UTF8.GetString(output, offset, newline - offset);
            offset = newline + 1;

            if (ParseBatchHeader(header) is not { } parsed)
            {
                // "…missing" satırı: içerik gövdesi yok, sıradaki başlığa geç.
                blobs.Add(new BlobContent
                {
                    Id = default,
                    Size = 0,
                    Content = [],
                    IsBinary = false,
                });
                continue;
            }

            int available = (int)Math.Min(parsed.Size, output.Length - offset);
            int take = (int)Math.Min(available, maxBytes);

            byte[] content = new byte[take];
            Array.Copy(output, offset, content, 0, take);

            blobs.Add(new BlobContent
            {
                Id = parsed.Id,
                Size = parsed.Size,
                Content = content,
                IsBinary = LooksBinary(content),
                IsTruncated = take < parsed.Size,
            });

            // İçeriğin tamamını atla, ardından git'in eklediği kapanış satır sonunu da.
            offset += available;

            if (offset < output.Length && output[offset] == (byte)'\n')
            {
                offset++;
            }
        }

        return blobs;
    }

    /// <summary>
    /// <c>&lt;sha&gt; &lt;tip&gt; &lt;boyut&gt;</c> başlığını ayrıştırır.
    /// </summary>
    /// <returns>Satır <c>missing</c> bildiriyorsa <see langword="null"/>.</returns>
    private static (CommitId Id, GitObjectType Type, long Size)? ParseBatchHeader(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3
            || !CommitId.TryParse(parts[0], out CommitId id)
            || !long.TryParse(parts[2], CultureInfo.InvariantCulture, out long size))
        {
            return null;
        }

        return (id, ParseObjectType(parts[1]), size);
    }

    /// <summary>
    /// <c>git</c> ile aynı sezgi: ilk 8000 baytta NUL varsa ikili.
    /// </summary>
    private static bool LooksBinary(ReadOnlySpan<byte> content) =>
        content[..Math.Min(content.Length, BinaryDetectionWindow)].Contains((byte)0);

    private static GitObjectType ParseObjectType(string value) => value switch
    {
        "blob" => GitObjectType.Blob,
        "tree" => GitObjectType.Tree,
        "commit" => GitObjectType.Commit,
        "tag" => GitObjectType.Tag,
        _ => GitObjectType.Missing,
    };
}
