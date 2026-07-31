using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Yan yana görünümdeki tek satır (P04-T10).
/// </summary>
/// <remarks>
/// Bir taraf <see langword="null"/> olabilir: karşılığı olmayan satırın karşısına
/// <b>dolgu</b> konur. Hunk başlığı satırında iki taraf da <see langword="null"/>'dır.
/// </remarks>
public sealed record SideBySideRow
{
    /// <summary>Sol taraf (eski hâl): bağlam veya silinen satır.</summary>
    public DiffLine? Left { get; init; }

    /// <summary>Sağ taraf (yeni hâl): bağlam veya eklenen satır.</summary>
    public DiffLine? Right { get; init; }

    /// <summary>Hunk başlığı; içerik satırında <see langword="null"/>.</summary>
    public string? HunkHeader { get; init; }

    /// <summary>
    /// Satırın ait olduğu hunk'ın indeksi; tek hunk çevrilirken <c>-1</c>.
    /// </summary>
    /// <remarks>
    /// Kısmi staging (P05-T11) yan yana moddaki seçimi <see cref="PatchSelection"/>'a
    /// çevirirken bunu kullanıyor. Ekrandaki satır sırası yeterli değil: bir yan yana satır
    /// <b>iki farklı</b> unified satırı taşıyabiliyor.
    /// </remarks>
    public int HunkIndex { get; init; } = -1;

    /// <summary>Sol satırın hunk içindeki indeksi; sol taraf boşsa <c>-1</c>.</summary>
    public int LeftIndex { get; init; } = -1;

    /// <summary>Sağ satırın hunk içindeki indeksi; sağ taraf boşsa <c>-1</c>.</summary>
    public int RightIndex { get; init; } = -1;

    public bool IsHunkHeader => HunkHeader is not null;

    public override string ToString() =>
        IsHunkHeader ? HunkHeader! : $"{Left?.Content} │ {Right?.Content}";
}

/// <summary>
/// Unified diff satırlarını <b>yan yana</b> yerleşime dönüştürür (P04-T10).
/// </summary>
/// <remarks>
/// <para>
/// Bu dönüşüm bilinçli olarak <b>çekirdek katmanda</b>: saf veri dönüşümü, arayüz
/// bağımlılığı yok, testi arayüz kurmadan yazılabiliyor.
/// </para>
/// <para>
/// <b>Hizalama, satır içi vurgulamayla AYNI eşlemeyi kullanır</b>
/// (<see cref="InlineDiff.MatchLines"/>). İkinci bir eşleme yazmak, vurgulanan çift ile
/// yan yana gösterilen çiftin farklı olabilmesi demekti — aynı ekranda iki çelişkili cevap.
/// </para>
/// <para>
/// <b>Referans yok:</b> GitExtensions'ın yerleşik yan yana görünümü <b>yok</b>; işi harici
/// difftool'a (difftastic) devredip onun iki sütunlu çıktısını ayrıştırıyorlar. Oradan
/// alınan tek şey şu: bir tarafın satırı <b>olmayabilir</b>, yani dolgu gerçek bir durumdur.
/// </para>
/// </remarks>
public static class SideBySideDiff
{
    /// <summary>Bir dosyanın tüm hunk'larını yan yana yerleşime çevirir.</summary>
    public static IReadOnlyList<SideBySideRow> Build(FileDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        List<SideBySideRow> rows = [];

        for (int hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
        {
            DiffHunk hunk = diff.Hunks[hunkIndex];

            rows.Add(new SideBySideRow { HunkHeader = hunk.Header, HunkIndex = hunkIndex });
            rows.AddRange(Build(hunk, hunkIndex));
        }

        return rows;
    }

    /// <summary>Tek bir hunk'ı yan yana yerleşime çevirir (başlık satırı üretilmez).</summary>
    public static IReadOnlyList<SideBySideRow> Build(DiffHunk hunk, int hunkIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(hunk);

        List<SideBySideRow> rows = [];
        IReadOnlyList<DiffLine> lines = hunk.Lines;

        int index = 0;

        while (index < lines.Count)
        {
            DiffLine line = lines[index];

            if (line.Kind == DiffLineKind.Context)
            {
                // Bağlam satırı iki tarafta da aynı; tek nesne yeterli (satır numaralarının
                // ikisi de üzerinde).
                rows.Add(new SideBySideRow
                {
                    Left = line,
                    Right = line,
                    HunkIndex = hunkIndex,
                    LeftIndex = index,
                    RightIndex = index,
                });

                index++;
                continue;
            }

            // git unified diff'te bir değişiklik bloğu her zaman "önce silinenler, sonra
            // eklenenler" biçiminde gelir; blok sınırı bağlam satırıdır.
            int removedStart = index;

            while (index < lines.Count && lines[index].Kind == DiffLineKind.Removed)
            {
                index++;
            }

            int addedStart = index;

            while (index < lines.Count && lines[index].Kind == DiffLineKind.Added)
            {
                index++;
            }

            AppendBlock(
                rows,
                lines,
                removedStart,
                addedStart - removedStart,
                addedStart,
                index - addedStart,
                hunkIndex);
        }

        return rows;
    }

    /// <summary>
    /// Bir silinen/eklenen bloğunu satırlara yerleştirir.
    /// </summary>
    /// <remarks>
    /// <b>Eşlenmeyen satırlar yan yana KONULMAZ.</b> Eşleme algoritması o iki satırın
    /// birbirinin karşılığı olmadığına zaten karar verdi; yine de yan yana koymak
    /// kullanıcıya "bunlar karşılıklı" demek olurdu. Yer tasarrufu için doğruluktan
    /// vazgeçilmiyor — dolgu satırı bırakılıyor.
    /// </remarks>
    private static void AppendBlock(
        List<SideBySideRow> rows,
        IReadOnlyList<DiffLine> lines,
        int removedStart,
        int removedCount,
        int addedStart,
        int addedCount,
        int hunkIndex)
    {
        if (removedCount == 0 || addedCount == 0)
        {
            // Tek taraflı blok: karşısı boş kalır.
            for (int i = 0; i < removedCount; i++)
            {
                rows.Add(Left(lines, removedStart + i, hunkIndex));
            }

            for (int i = 0; i < addedCount; i++)
            {
                rows.Add(Right(lines, addedStart + i, hunkIndex));
            }

            return;
        }

        string[] removed = new string[removedCount];
        string[] added = new string[addedCount];

        for (int i = 0; i < removedCount; i++)
        {
            removed[i] = lines[removedStart + i].Content;
        }

        for (int i = 0; i < addedCount; i++)
        {
            added[i] = lines[addedStart + i].Content;
        }

        IReadOnlyList<(int Removed, int Added)> pairs = InlineDiff.MatchLines(removed, added);

        int nextRemoved = 0;
        int nextAdded = 0;

        foreach ((int pairedRemoved, int pairedAdded) in pairs)
        {
            // Çiftten önce kalan eşsiz satırlar: önce silinenler, sonra eklenenler.
            while (nextRemoved < pairedRemoved)
            {
                rows.Add(Left(lines, removedStart + nextRemoved++, hunkIndex));
            }

            while (nextAdded < pairedAdded)
            {
                rows.Add(Right(lines, addedStart + nextAdded++, hunkIndex));
            }

            rows.Add(new SideBySideRow
            {
                Left = lines[removedStart + pairedRemoved],
                Right = lines[addedStart + pairedAdded],
                HunkIndex = hunkIndex,
                LeftIndex = removedStart + pairedRemoved,
                RightIndex = addedStart + pairedAdded,
            });

            nextRemoved = pairedRemoved + 1;
            nextAdded = pairedAdded + 1;
        }

        while (nextRemoved < removedCount)
        {
            rows.Add(Left(lines, removedStart + nextRemoved++, hunkIndex));
        }

        while (nextAdded < addedCount)
        {
            rows.Add(Right(lines, addedStart + nextAdded++, hunkIndex));
        }
    }

    private static SideBySideRow Left(IReadOnlyList<DiffLine> lines, int index, int hunkIndex) =>
        new() { Left = lines[index], HunkIndex = hunkIndex, LeftIndex = index };

    private static SideBySideRow Right(IReadOnlyList<DiffLine> lines, int index, int hunkIndex) =>
        new() { Right = lines[index], HunkIndex = hunkIndex, RightIndex = index };
}
