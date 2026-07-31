using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// İki satır arasındaki <b>satır içi</b> farkı hesaplar (P04-T05).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden git'in <c>--word-diff</c>'i kullanılmıyor?</b> Plan onunla başlamayı öneriyordu
/// ("bedava ve doğru"). Ölçüldü ve <b>doğru olmadığı</b> görüldü:
/// </para>
/// <list type="number">
/// <item>Varsayılan kelime ayracıyla satır parçalardan geri kurulduğunda eski tarafın sonuna
/// <b>sahte bir boşluk</b> ekleniyor.</item>
/// <item>Karakter seviyeli ayraç bunu düzeltiyor ama daha büyük bir sorun kalıyor:
/// <b>eklenen/silinen boş satır için git yalnızca çıplak bir <c>~</c> üretiyor</b> ve satırın
/// hangi tarafa ait olduğu çıktıda <b>hiç yok</b>. Gerçek depoda 150 commit tarandığında
/// bu yüzden 5.701 satır yanlış tarafa düşüyordu.</item>
/// <item>Ayrıca kelime diff'i satır tabanlı çıktının <b>yerine geçiyor</b>, yani ek bir
/// <c>git</c> çalıştırması gerektiriyor.</item>
/// </list>
/// <para>
/// Bu yüzden satır içi fark <b>yerel olarak</b>, ayrıştırıcının zaten ürettiği <b>kesin</b>
/// satır metinleri üzerinde hesaplanıyor. Sadakat riski yok: girdi zaten doğru satırlar.
/// </para>
/// </remarks>
public static class InlineDiff
{
    /// <summary>
    /// Bu uzunluğun üstündeki satırlarda satır içi fark hesaplanmaz.
    /// </summary>
    /// <remarks>
    /// Küçültülmüş (minified) JS, tek satırlık JSON gibi dosyalarda satırlar on binlerce
    /// karakter olabiliyor. Böyle bir satırda vurgulama zaten okunmaz; hesaplamak da boşuna.
    /// </remarks>
    public const int MaximumLineLength = 4000;

    /// <summary>
    /// Ortadaki farkın kelime bazında çözümleneceği en büyük parça uzunluğu.
    /// </summary>
    /// <remarks>
    /// Bunun üstünde ortak önek/sonek kırpması yeterli sayılır. Sınır olmadan uzun satırlarda
    /// kareselleşen bir çalışma süresi oluşurdu.
    /// </remarks>
    private const int MaximumMiddleLength = 400;

    /// <summary>
    /// Bu sayıdan fazla olası satır çifti varsa en iyi eşleme aranmaz.
    /// </summary>
    /// <remarks>
    /// Eşleme arama O(n·m); sınırsız bırakılırsa büyük hunk'larda karesel maliyet oluşur.
    /// Sınır ve aşıldığında sıraya göre eşleme, <b>GitExtensions</b>'ın aynı problemdeki
    /// çözümüyle aynı (<c>GitUI/Editor/Diff/LinesMatcher.cs</c>).
    /// </remarks>
    private const int MaximumPairCombinations = 100 * 100;

    /// <summary>
    /// Bir eşleşmenin anlamlı sayılması için gereken en düşük benzerlik.
    /// </summary>
    /// <remarks>Bunun altındaki skorlar gürültü; GitExtensions da aynı eşiği kullanıyor.</remarks>
    private const double InsignificantScore = 0.1;

    /// <summary>
    /// Bir hunk'ın satırlarına satır içi parçaları ekler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Eşleme:</b> art arda gelen silinen ve eklenen satır dizileri, <b>en çok kelime
    /// paylaşan çift</b> çapa alınarak özyinelemeli eşlenir; çapadan öncesi ve sonrası aynı
    /// yöntemle bölünür. Sırayla eşlemek (i'inci ↔ i'inci) tek satır eklenip silindiğinde
    /// doğru ama satır sayıları farklı olduğunda <b>yanlış satırları</b> karşılaştırır.
    /// </para>
    /// <para>
    /// Bu yaklaşım <b>GitExtensions</b>'ın çözümünden alındı
    /// (<c>GitUI/Editor/Diff/LinesMatcher.cs</c>): skor
    /// <i>ortak kelimelerin toplam uzunluğu ÷ iki satırın kelime uzunluğunun büyüğü</i>,
    /// eşik altındaki eşleşmeler yok sayılıp sıraya düşülüyor.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DiffLine> Annotate(IReadOnlyList<DiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        DiffLine[] result = [.. lines];

        int index = 0;

        while (index < result.Length)
        {
            if (result[index].Kind != DiffLineKind.Removed)
            {
                index++;
                continue;
            }

            int removedStart = index;

            while (index < result.Length && result[index].Kind == DiffLineKind.Removed)
            {
                index++;
            }

            int addedStart = index;

            while (index < result.Length && result[index].Kind == DiffLineKind.Added)
            {
                index++;
            }

            int removedCount = addedStart - removedStart;
            int addedCount = index - addedStart;

            foreach ((int Removed, int Added) pair in FindPairs(result, removedStart, removedCount, addedStart, addedCount))
            {
                DiffLine oldLine = result[pair.Removed];
                DiffLine newLine = result[pair.Added];

                (IReadOnlyList<DiffSegment> oldSegments, IReadOnlyList<DiffSegment> newSegments) =
                    Compute(oldLine.Content, newLine.Content);

                result[pair.Removed] = oldLine with { Segments = oldSegments };
                result[pair.Added] = newLine with { Segments = newSegments };
            }
        }

        return result;
    }

    /// <summary>
    /// Silinen ve eklenen satır metinlerini birbirine eşler.
    /// </summary>
    /// <returns>
    /// <paramref name="removed"/> ve <paramref name="added"/> içindeki indeks çiftleri,
    /// sol indekse göre artan sırada. Eşlenmeyen satırlar sonuçta <b>hiç yer almaz</b>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Bu, satır içi vurgulamanın kullandığı eşlemenin ta kendisidir</b> ve dışarı
    /// bilinçli olarak açıldı: yan yana görünüm (P04-T10) hizalamayı buradan alıyor. İkinci
    /// bir eşleme yazılsaydı, vurgulanan çift ile yan yana gösterilen çift <b>farklı
    /// olabilirdi</b> — kullanıcıya aynı ekranda iki çelişkili cevap.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(int Removed, int Added)> MatchLines(
        IReadOnlyList<string> removed,
        IReadOnlyList<string> added)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(added);

        return Match(
            removed.Count,
            added.Count,
            i => removed[i],
            i => added[i],
            removedStart: 0,
            addedStart: 0);
    }

    /// <summary>
    /// Bir hunk içindeki silinen/eklenen dizileri eşler (mutlak indekslerle).
    /// </summary>
    private static List<(int Removed, int Added)> FindPairs(
        DiffLine[] lines,
        int removedStart,
        int removedCount,
        int addedStart,
        int addedCount) =>
        Match(
            removedCount,
            addedCount,
            i => lines[removedStart + i].Content,
            i => lines[addedStart + i].Content,
            removedStart,
            addedStart);

    private static List<(int Removed, int Added)> Match(
        int removedCount,
        int addedCount,
        Func<int, string> removedContent,
        Func<int, string> addedContent,
        int removedStart,
        int addedStart)
    {
        List<(int, int)> pairs = [];

        if (removedCount == 0 || addedCount == 0)
        {
            return pairs;
        }

        // Tek satırlık taraf ya da çok büyük hunk: sıraya göre eşle.
        if (removedCount == 1 || addedCount == 1
            || removedCount * addedCount > MaximumPairCombinations)
        {
            int count = Math.Min(removedCount, addedCount);

            for (int i = 0; i < count; i++)
            {
                pairs.Add((removedStart + i, addedStart + i));
            }

            return pairs;
        }

        string[][] removedWords = new string[removedCount][];
        string[][] addedWords = new string[addedCount][];

        for (int i = 0; i < removedCount; i++)
        {
            removedWords[i] = Words(removedContent(i));
        }

        for (int i = 0; i < addedCount; i++)
        {
            addedWords[i] = Words(addedContent(i));
        }

        Pair(0, removedCount, 0, addedCount);

        pairs.Sort((left, right) => left.Item1.CompareTo(right.Item1));

        return pairs;

        void Pair(int removedFrom, int removedTo, int addedFrom, int addedTo)
        {
            if (removedFrom >= removedTo || addedFrom >= addedTo)
            {
                return;
            }

            (int bestRemoved, int bestAdded, double score) =
                FindBestMatch(removedWords, addedWords, removedFrom, removedTo, addedFrom, addedTo);

            if (score <= InsignificantScore)
            {
                // Anlamlı bir çapa yok: kalanı sırayla eşle.
                int count = Math.Min(removedTo - removedFrom, addedTo - addedFrom);

                for (int i = 0; i < count; i++)
                {
                    pairs.Add((removedStart + removedFrom + i, addedStart + addedFrom + i));
                }

                return;
            }

            Pair(removedFrom, bestRemoved, addedFrom, bestAdded);

            pairs.Add((removedStart + bestRemoved, addedStart + bestAdded));

            Pair(bestRemoved + 1, removedTo, bestAdded + 1, addedTo);
        }
    }

    /// <summary>
    /// En çok kelime paylaşan satır çiftini bulur.
    /// </summary>
    /// <remarks>
    /// Skor: <i>ortak kelimelerin toplam uzunluğu ÷ iki satırın kelime uzunluğunun büyüğü</i>.
    /// GitExtensions'ın <c>LinesMatcher.GetWordMatchScore</c>'uyla aynı ölçüt.
    /// </remarks>
    private static (int Removed, int Added, double Score) FindBestMatch(
        string[][] removedWords,
        string[][] addedWords,
        int removedFrom,
        int removedTo,
        int addedFrom,
        int addedTo)
    {
        int bestRemoved = removedFrom;
        int bestAdded = addedFrom;
        double best = -1;

        for (int r = removedFrom; r < removedTo; r++)
        {
            for (int a = addedFrom; a < addedTo; a++)
            {
                double score = Score(removedWords[r], addedWords[a]);

                if (score <= best)
                {
                    continue;
                }

                best = score;
                bestRemoved = r;
                bestAdded = a;

                if (best >= 1)
                {
                    return (bestRemoved, bestAdded, best);
                }
            }
        }

        return (bestRemoved, bestAdded, best);
    }

    private static double Score(string[] left, string[] right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return -1;
        }

        int leftLength = left.Sum(w => w.Length);
        int rightLength = right.Sum(w => w.Length);

        HashSet<string> shared = new(left, StringComparer.Ordinal);
        shared.IntersectWith(right);

        int common = shared.Sum(w => w.Length);

        return (double)common / Math.Max(leftLength, rightLength);
    }

    /// <summary>Skorlamada kullanılan kelimeler: yalnızca harf/rakam dizileri.</summary>
    private static string[] Words(string text) =>
        [.. Tokenize(text).Where(t => t.Length > 0 && char.IsLetterOrDigit(t[0]))];

    /// <summary>
    /// İki satırın parçalarını hesaplar.
    /// </summary>
    /// <returns>Eski ve yeni satırın parça listeleri; birleştirildiklerinde girdiyi verirler.</returns>
    public static (IReadOnlyList<DiffSegment> Old, IReadOnlyList<DiffSegment> New) Compute(
        string oldLine,
        string newLine)
    {
        ArgumentNullException.ThrowIfNull(oldLine);
        ArgumentNullException.ThrowIfNull(newLine);

        if (oldLine.Length > MaximumLineLength || newLine.Length > MaximumLineLength)
        {
            return (Whole(oldLine, DiffLineKind.Removed), Whole(newLine, DiffLineKind.Added));
        }

        if (string.Equals(oldLine, newLine, StringComparison.Ordinal))
        {
            return (Whole(oldLine, DiffLineKind.Context), Whole(newLine, DiffLineKind.Context));
        }

        // Ortak önek ve sonek kırpılır: tipik düzenlemede iş burada biter ve kalan orta
        // parça kısadır.
        int prefix = CommonPrefixLength(oldLine, newLine);
        int suffix = CommonSuffixLength(oldLine, newLine, prefix);

        string oldMiddle = oldLine[prefix..(oldLine.Length - suffix)];
        string newMiddle = newLine[prefix..(newLine.Length - suffix)];

        (string[] oldTokens, string[] newTokens) = (Tokenize(oldMiddle), Tokenize(newMiddle));

        List<DiffSegment> oldSegments = [];
        List<DiffSegment> newSegments = [];

        Append(oldSegments, DiffLineKind.Context, oldLine[..prefix]);
        Append(newSegments, DiffLineKind.Context, newLine[..prefix]);

        if (oldTokens.Length <= MaximumMiddleLength && newTokens.Length <= MaximumMiddleLength)
        {
            AppendTokenDiff(oldSegments, newSegments, oldTokens, newTokens);
        }
        else
        {
            // Orta parça çok uzun: tamamı değişmiş sayılır. Kaba ama doğru — yanlış yeri
            // vurgulamaktansa geniş vurgulamak yeğ.
            Append(oldSegments, DiffLineKind.Removed, oldMiddle);
            Append(newSegments, DiffLineKind.Added, newMiddle);
        }

        Append(oldSegments, DiffLineKind.Context, oldLine[(oldLine.Length - suffix)..]);
        Append(newSegments, DiffLineKind.Context, newLine[(newLine.Length - suffix)..]);

        return (oldSegments, newSegments);
    }

    /// <summary>
    /// Ortadaki jetonları en uzun ortak alt dizi (LCS) ile eşleyip parçalara böler.
    /// </summary>
    private static void AppendTokenDiff(
        List<DiffSegment> oldSegments,
        List<DiffSegment> newSegments,
        string[] oldTokens,
        string[] newTokens)
    {
        int[,] lengths = new int[oldTokens.Length + 1, newTokens.Length + 1];

        for (int i = oldTokens.Length - 1; i >= 0; i--)
        {
            for (int j = newTokens.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(oldTokens[i], newTokens[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        int x = 0;
        int y = 0;

        while (x < oldTokens.Length && y < newTokens.Length)
        {
            if (string.Equals(oldTokens[x], newTokens[y], StringComparison.Ordinal))
            {
                Append(oldSegments, DiffLineKind.Context, oldTokens[x]);
                Append(newSegments, DiffLineKind.Context, newTokens[y]);
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                Append(oldSegments, DiffLineKind.Removed, oldTokens[x++]);
            }
            else
            {
                Append(newSegments, DiffLineKind.Added, newTokens[y++]);
            }
        }

        while (x < oldTokens.Length)
        {
            Append(oldSegments, DiffLineKind.Removed, oldTokens[x++]);
        }

        while (y < newTokens.Length)
        {
            Append(newSegments, DiffLineKind.Added, newTokens[y++]);
        }
    }

    /// <summary>
    /// Satırı jetonlara böler: harf/rakam dizileri bir jeton, diğer her karakter tek başına.
    /// </summary>
    /// <remarks>
    /// Karakter karakter bölmek gürültülü vurgulama üretiyor; kelime bütünlüğünü korumak
    /// okunabilirliği belirgin biçimde artırıyor. Noktalama ayrı jeton olduğu için
    /// <c>foo(bar)</c> → <c>foo</c> <c>(</c> <c>bar</c> <c>)</c>.
    /// </remarks>
    private static string[] Tokenize(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        List<string> tokens = [];
        int start = 0;

        while (start < text.Length)
        {
            if (char.IsLetterOrDigit(text[start]))
            {
                int end = start;

                while (end < text.Length && char.IsLetterOrDigit(text[end]))
                {
                    end++;
                }

                tokens.Add(text[start..end]);
                start = end;
            }
            else
            {
                tokens.Add(text[start].ToString());
                start++;
            }
        }

        return [.. tokens];
    }

    /// <summary>
    /// Parçayı listeye ekler; aynı türdeki ardışık parçalar <b>birleştirilir</b>.
    /// </summary>
    /// <remarks>
    /// Birleştirmeden her jeton ayrı parça olurdu; arayüz tarafında yüzlerce gereksiz
    /// çizim öğesi demek.
    /// </remarks>
    private static void Append(List<DiffSegment> segments, DiffLineKind kind, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (segments.Count > 0 && segments[^1].Kind == kind)
        {
            segments[^1] = segments[^1] with { Text = segments[^1].Text + text };
            return;
        }

        segments.Add(new DiffSegment(kind, text));
    }

    private static IReadOnlyList<DiffSegment> Whole(string text, DiffLineKind kind) =>
        text.Length == 0 ? [] : [new DiffSegment(kind, text)];

    private static int CommonPrefixLength(string left, string right)
    {
        int limit = Math.Min(left.Length, right.Length);
        int index = 0;

        while (index < limit && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static int CommonSuffixLength(string left, string right, int prefix)
    {
        int limit = Math.Min(left.Length, right.Length) - prefix;
        int index = 0;

        while (index < limit && left[left.Length - 1 - index] == right[right.Length - 1 - index])
        {
            index++;
        }

        return index;
    }
}
