using System.Text;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Diff metninin <b>gösterim</b> ayarları (P04-T13).
/// </summary>
/// <remarks>
/// Yalnızca ekranda görüneni etkiler; model içeriği değişmez (Faz 05'te yamayı
/// <c>git apply</c>'a birebir geri vermek gerekiyor).
/// </remarks>
public sealed record DiffTextOptions
{
    public static DiffTextOptions Default { get; } = new();

    /// <summary>Bir sekmenin kaç sütun ilerlettiği.</summary>
    public int TabWidth { get; init; } = 4;

    /// <summary>Boşluk ve sekmeler görünür karakterlerle gösterilsin mi?</summary>
    /// <remarks>
    /// GitExtensions'ta da <b>tek anahtar</b>: boşluk ve sekme ayrı ayrı değil, birlikte
    /// açılıyor (<c>ShowSpaces = ShowTabs = show</c>).
    /// </remarks>
    public bool ShowWhitespace { get; init; }

    /// <summary>Hiçbir dönüşüm gerekmiyor mu?</summary>
    internal bool IsIdentity => !ShowWhitespace && TabWidth <= 0;
}

/// <summary>
/// Diff satırlarını gösterime hazırlar: sekmeleri açar, isteğe bağlı olarak boşlukları
/// görünür kılar (P04-T13).
/// </summary>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ:</b> Avalonia'nın <c>TextBlock</c>'unda sekme <b>tab-stop değil</b>, sabit
/// dört boşluk genişliğinde çiziliyor (<c>"ab\tc"</c> ile <c>"ab    c"</c> aynı genişlikte;
/// gerçek tab-stop olsaydı iki boşluk olurdu). Ayrıca sekme genişliğini ayarlayan bir
/// özellik yok. Bu yüzden dönüşüm <b>burada</b> yapılıyor.
/// </para>
/// <para>
/// Dönüşüm <b>parça sınırlarını koruyor</b> ve sütun sayacı parçalar arasında devam ediyor:
/// tab-stop satırın başından itibaren hesaplanır, parça başından değil. Aksi hâlde satır içi
/// vurgulama olan satırlarda sekmeler farklı yere hizalanırdı.
/// </para>
/// </remarks>
public static class DiffTextFormatter
{
    /// <summary>Boşluk göstergesi (orta nokta).</summary>
    public const char SpaceMarker = '·';

    /// <summary>Sekme göstergesi (çift ok) — ICSharpCode/GitExtensions ile aynı.</summary>
    public const char TabMarker = '»';

    /// <summary>Bir satırın parçalarını gösterime hazırlar.</summary>
    public static IReadOnlyList<DiffSegment> Format(
        IReadOnlyList<DiffSegment> segments,
        DiffTextOptions options)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsIdentity || segments.Count == 0)
        {
            return segments;
        }

        DiffSegment[] result = new DiffSegment[segments.Count];
        int column = 0;

        for (int i = 0; i < segments.Count; i++)
        {
            string text = Expand(segments[i].Text, options, ref column);
            result[i] = segments[i] with { Text = text };
        }

        return result;
    }

    /// <summary>Tek bir metni gösterime hazırlar (satırın başından başlayarak).</summary>
    public static string Format(string text, DiffTextOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsIdentity)
        {
            return text;
        }

        int column = 0;
        return Expand(text, options, ref column);
    }

    /// <summary>
    /// Metni açar ve <paramref name="column"/>'u ilerletir.
    /// </summary>
    private static string Expand(string text, DiffTextOptions options, ref int column)
    {
        if (text.Length == 0)
        {
            return text;
        }

        // Yaygın durum: ne sekme var ne de boşluk gösterimi isteniyor. Yeni dize üretmemek
        // için önce kontrol ediliyor — satır sayısı on binlere çıkabiliyor.
        if (!options.ShowWhitespace && !text.Contains('\t'))
        {
            column += text.Length;
            return text;
        }

        StringBuilder builder = new(text.Length + 8);

        foreach (char character in text)
        {
            if (character == '\t' && options.TabWidth > 0)
            {
                // Tab-stop: bir sonraki katına kadar doldur (hiç ilerletmemek yerine en az 1).
                int width = options.TabWidth - (column % options.TabWidth);

                builder.Append(options.ShowWhitespace ? TabMarker : ' ');
                builder.Append(' ', width - 1);

                column += width;
                continue;
            }

            if (character == ' ' && options.ShowWhitespace)
            {
                builder.Append(SpaceMarker);
                column++;
                continue;
            }

            builder.Append(character);
            column++;
        }

        return builder.ToString();
    }
}
