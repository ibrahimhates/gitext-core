using System.Globalization;

namespace GitExt.Core.Git;

/// <summary>
/// Uzun süren bir git işleminin ilerleme durumu (P06-T10).
/// </summary>
/// <param name="Phase">Aşama adı (<c>Counting objects</c>, <c>Receiving objects</c>…).</param>
/// <param name="Percent">Yüzde; git yüzde vermiyorsa <see langword="null"/>.</param>
/// <param name="Current">İşlenen nesne sayısı.</param>
/// <param name="Total">Toplam nesne sayısı.</param>
/// <param name="IsRemote">Aşama uzak sunucuda mı çalışıyor (<c>remote:</c> öneki)?</param>
/// <param name="IsDone">Aşama tamamlandı mı (<c>, done.</c>)?</param>
public sealed record GitProgress(
    string Phase,
    double? Percent,
    long? Current,
    long? Total,
    bool IsRemote = false,
    bool IsDone = false)
{
    /// <summary>Kullanıcıya gösterilecek tek satır.</summary>
    public string Describe()
    {
        string prefix = IsRemote ? "Sunucu: " : string.Empty;

        if (IsDone)
        {
            return $"{prefix}{Phase} — tamam";
        }

        return Percent is { } percent
            ? string.Create(CultureInfo.InvariantCulture, $"{prefix}{Phase} — %{percent:0}")
            : $"{prefix}{Phase}…";
    }
}

/// <summary>
/// <c>git --progress</c> satırlarını ayrıştırır (P06-T10).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ — ilerleme satırları <c>\n</c> ile DEĞİL <c>\r</c> ile ayrılıyor.</b>
/// Gerçek bir klonda 404 taşıma dönüşü (<c>\r</c>) karşılık 7 satır sonu (<c>\n</c>) sayıldı:
/// git aynı satırın üstüne yazıyor. <c>ReadLineAsync</c> ya da <c>Split('\n')</c> kullanan
/// bir okuyucu, işlem bitene kadar kullanıcıya <b>hiçbir şey</b> göstermezdi — yani
/// "ilerleme çubuğu" tam da gerektiği anda boş kalırdı.
/// </para>
/// <para>
/// Ölçülen biçimler:
/// <code>
/// remote: Counting objects:   5% (207/4125)
/// remote: Enumerating objects: 16201, done.
/// Receiving objects:  47% (7615/16201), 4.10 MiB | 8.20 MiB/s
/// Resolving deltas: 100% (11603/11603), done.
/// </code>
/// Satır sonundaki boşluk dolgusu da git'ten geliyor (eski, daha uzun satırı silmek için).
/// </para>
/// </remarks>
public static class GitProgressParser
{
    private const string RemotePrefix = "remote: ";

    /// <summary>
    /// Tek bir satırı ayrıştırır; ilerleme satırı değilse <see langword="null"/>.
    /// </summary>
    public static GitProgress? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        ReadOnlySpan<char> span = line.AsSpan().Trim();

        if (span.IsEmpty)
        {
            return null;
        }

        bool remote = span.StartsWith(RemotePrefix, StringComparison.Ordinal);

        if (remote)
        {
            span = span[RemotePrefix.Length..].Trim();
        }

        int colon = span.IndexOf(':');

        if (colon <= 0)
        {
            return null;
        }

        string phase = span[..colon].ToString().Trim();
        ReadOnlySpan<char> rest = span[(colon + 1)..].Trim();

        if (phase.Length == 0 || rest.IsEmpty)
        {
            return null;
        }

        // `Cloning into 'x'...` gibi satırlar da iki nokta içerebiliyor; ilerleme satırının
        // ayırt edici işareti sayıyla başlaması.
        if (!char.IsAsciiDigit(rest[0]))
        {
            return null;
        }

        bool done = rest.EndsWith("done.", StringComparison.Ordinal);
        double? percent = null;
        long? current = null;
        long? total = null;

        int percentSign = rest.IndexOf('%');

        if (percentSign > 0
            && double.TryParse(
                rest[..percentSign].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed))
        {
            percent = parsed;
        }

        int open = rest.IndexOf('(');
        int close = rest.IndexOf(')');

        if (open >= 0 && close > open)
        {
            ReadOnlySpan<char> pair = rest[(open + 1)..close];
            int slash = pair.IndexOf('/');

            if (slash > 0)
            {
                if (long.TryParse(pair[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out long a))
                {
                    current = a;
                }

                if (long.TryParse(pair[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long b))
                {
                    total = b;
                }
            }
        }
        else if (percent is null)
        {
            // `Enumerating objects: 16201, done.` — yüzde yok, yalnızca sayaç.
            int comma = rest.IndexOf(',');
            ReadOnlySpan<char> number = comma > 0 ? rest[..comma] : rest;

            if (long.TryParse(number.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long count))
            {
                current = count;
            }
        }

        return new GitProgress(phase, percent, current, total, remote, done);
    }

    /// <summary>
    /// Bir metin parçasını <b>hem <c>\r</c> hem <c>\n</c></b> ile satırlara böler.
    /// </summary>
    /// <remarks>
    /// Son parça satır sonu görmediyse geri veriliyor: akış hâlinde okurken bir satır iki
    /// parçaya bölünebilir ve yarısını ayrıştırmak yanlış yüzde üretirdi.
    /// </remarks>
    public static (IReadOnlyList<string> Lines, string Remainder) SplitLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> lines = [];
        int start = 0;

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                continue;
            }

            if (index > start)
            {
                lines.Add(text[start..index]);
            }

            start = index + 1;
        }

        return (lines, text[start..]);
    }
}
