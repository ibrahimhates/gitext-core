using System.Globalization;
using System.Text;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// <c>git diff --raw -z --patch</c> çıktısını ayrıştırır (P04-T02).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden tek çağrıda hem <c>--raw</c> hem <c>--patch</c>?</b> Ölçüldü:
/// <c>diff --git a/… b/…</c> başlığı genel olarak <b>ayrıştırılamıyor</b> — boşluk içeren
/// yollarda iki yolu ayırmanın güvenli yolu yok (<c>a/alt dizin/b -&gt; c.txt b/alt dizin/b
/// -&gt; c.txt</c>) ve ASCII dışı adlar C tarzı sekizlik kaçışla tırnaklanıyor. Bu yüzden
/// yollar, modlar, blob'lar ve değişim türü <b>yalnızca <c>--raw -z</c> bölümünden</b>
/// okunuyor; yama bölümü <b>yalnızca hunk içeriği</b> için ayrıştırılıyor.
/// </para>
/// <para>
/// <b>Eşleme iki aşamalı.</b> Sayılar eşitse <b>sıraya</b> göre — bu ölçülerek doğrulandı:
/// git/git deposunda <b>700 commit</b> tarandı, sayılar her seferinde uyuştu ve sıralama aynı
/// çıktı. Sayılar eşit değilse (ölçüldü: <c>--ignore-blank-lines</c> dosyayı ham bölümde
/// bırakıp yama bloğu üretmiyor) <c>index</c> satırındaki <b>blob kimliklerine</b> göre
/// eşlenir. İkisi de olmazsa <see cref="DiffParseException"/> — hunk'ları yanlış dosyaya
/// bağlamak sessiz veri bozulmasıdır.
/// </para>
/// <para>
/// <b>Kodlama (P04-T07).</b> Girdi <b>kayıpsız</b> okunmuş olmalı
/// (<see cref="Git.GitResult.GetStandardOutputLossless"/>): <c>git diff</c> çıktısı tek bir
/// kodlamada değil — başlıklar ASCII, satır içerikleri <b>dosyanın kendi baytları</b>.
/// Yollar UTF-8 olarak, satır içerikleri verilen kodlamayla yeniden çözülür.
/// </para>
/// </remarks>
public static class DiffParser
{
    /// <summary>
    /// Ham bölümdeki bir kaydın sabit alanları: eski mod, yeni mod, eski blob, yeni blob, durum.
    /// </summary>
    private const int RawFieldCount = 5;

    /// <summary>
    /// Birleşik <c>--raw -z --patch</c> çıktısını dosya diff'lerine çevirir.
    /// </summary>
    /// <param name="output">Birleşik <c>git diff</c> çıktısı.</param>
    /// <param name="inlineSegments">
    /// Satır içi parçalar da hesaplansın mı (P04-T05)?
    /// </param>
    /// <param name="maximumChangedLines">
    /// Bu sayıdan fazla satırı değişen dosyanın <b>içeriği ayrıştırılmaz</b>; 0 veya negatif
    /// ise sınır yok (P04-T06).
    /// </param>
    /// <param name="contentEncoding">
    /// Satır içeriklerinin kodlaması; <see langword="null"/> ise UTF-8 (P04-T07).
    /// </param>
    public static IReadOnlyList<FileDiff> Parse(
        string output,
        bool inlineSegments = false,
        int maximumChangedLines = 0,
        Encoding? contentEncoding = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        Encoding content = contentEncoding ?? Encoding.UTF8;

        if (output.Length == 0)
        {
            return [];
        }

        (List<RawRecord> records, int position) = ParseRawSection(output);

        if (records.Count == 0)
        {
            return [];
        }

        // `--numstat` istenmişse ham bölümden hemen sonra gelir ve içerik üretmeden
        // dosya başına değişen satır sayısını verir — boyut koruması buna dayanıyor.
        (List<NumStatRecord> stats, int patchStart) = ParseNumStatSection(output, position);

        AttachStats(records, stats);

        List<PatchBlock> blocks = SplitPatchBlocks(
            output, patchStart, inlineSegments, records, maximumChangedLines, content);

        return blocks.Count == records.Count
            ? MatchByOrder(records, blocks, maximumChangedLines)
            : MatchByBlob(records, blocks, maximumChangedLines);
    }

    /// <summary>
    /// <c>--numstat -z</c> bölümünü okur.
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ — yeniden adlandırmada biçim farklı.</b> Normal kayıt
    /// <c>eklenen⇥silinen⇥yol</c> şeklinde tek jetonken, rename/kopyalamada <b>yol alanı boş
    /// bırakılıp</b> eski ve yeni yol <b>ayrı NUL jetonları</b> olarak geliyor
    /// (<c>0⇥0⇥</c> + <c>eski.txt</c> + <c>yeni.txt</c>). Binary dosyada sayılar <c>-</c>.
    /// </remarks>
    private static (List<NumStatRecord> Stats, int PatchStart) ParseNumStatSection(
        string output,
        int position)
    {
        List<NumStatRecord> stats = [];

        while (position < output.Length && output[position] != '\0')
        {
            int probe = position;

            if (!TryReadToken(output, ref probe, out string token))
            {
                break;
            }

            string[] fields = token.Split('\t');

            if (fields.Length < 3)
            {
                // numstat istenmemiş: buradan itibarı yama bölümü.
                break;
            }

            position = probe;

            if (fields[2].Length == 0)
            {
                // Yeniden adlandırma: iki yol ayrı jetonlarda.
                TryReadToken(output, ref position, out _);
                TryReadToken(output, ref position, out _);
            }

            stats.Add(new NumStatRecord(ParseCount(fields[0]), ParseCount(fields[1])));
        }

        while (position < output.Length && output[position] == '\0')
        {
            position++;
        }

        return (stats, position);
    }

    /// <summary>Binary dosyada <c>-</c> geliyor; sayı yok demektir.</summary>
    private static int? ParseCount(string value) =>
        int.TryParse(value, CultureInfo.InvariantCulture, out int count) ? count : null;

    /// <summary>
    /// numstat kayıtlarını ham kayıtlarla eşler.
    /// </summary>
    /// <remarks>
    /// İkisi de aynı sırada geliyor. Sayılar uyuşmazsa eşleme yapılmaz — uydurma bir hizalama,
    /// yanlış dosyaya yanlış satır sayısı yazmak olurdu.
    /// </remarks>
    private static void AttachStats(List<RawRecord> records, List<NumStatRecord> stats)
    {
        if (stats.Count != records.Count)
        {
            return;
        }

        for (int i = 0; i < records.Count; i++)
        {
            records[i] = records[i] with { Added = stats[i].Added, Removed = stats[i].Removed };
        }
    }

    /// <summary>
    /// Sayılar eşitken sıraya göre eşler.
    /// </summary>
    /// <remarks>
    /// Yaygın durum bu ve <b>700 gerçek commit'te doğrulandı</b>: ham kayıt sayısı ile yama
    /// bloğu sayısı her seferinde uyuştu ve sıralama aynı çıktı.
    /// </remarks>
    private static FileDiff[] MatchByOrder(
        List<RawRecord> records,
        List<PatchBlock> blocks,
        int maximumChangedLines)
    {
        FileDiff[] diffs = new FileDiff[records.Count];

        for (int i = 0; i < records.Count; i++)
        {
            diffs[i] = Build(records[i], blocks[i], maximumChangedLines);
        }

        return diffs;
    }

    /// <summary>
    /// Sayılar eşit değilken blob kimliklerine göre eşler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ÖLÇÜLDÜ:</b> <c>--ignore-blank-lines</c> ile git, yalnızca boş satırı değişmiş bir
    /// dosyayı <b>ham bölümde bırakıyor ama yama bloğu üretmiyor</b>. (Bu, <c>-w</c>'den
    /// farklı: orada dosya aynılaştığında iki bölümden de düşüyor, yani sayılar hizalı kalıyor.)
    /// </para>
    /// <para>
    /// Böyle bir durumda sıraya güvenmek hunk'ları <b>yanlış dosyaya</b> bağlardı. Yama
    /// bloğundaki <c>index &lt;eski&gt;..&lt;yeni&gt;</c> satırı ham kayıttaki blob'larla
    /// aynı kimlikleri taşıyor; eşleme buradan yapılıyor. Eşleşmeyen kayıt hunk'sız kalır
    /// (değişiklik gerçekten yoksayılmış demektir); eşleşemeyen <b>blok</b> varsa durulur.
    /// </para>
    /// </remarks>
    private static FileDiff[] MatchByBlob(
        List<RawRecord> records,
        List<PatchBlock> blocks,
        int maximumChangedLines)
    {
        PatchBlock?[] matched = new PatchBlock?[records.Count];

        foreach (PatchBlock block in blocks)
        {
            int target = -1;

            for (int r = 0; r < records.Count; r++)
            {
                if (matched[r] is not null || !block.MatchesBlobs(records[r].OldBlob, records[r].NewBlob))
                {
                    continue;
                }

                target = r;
                break;
            }

            if (target < 0)
            {
                throw new DiffParseException(
                    $"A patch block could not be matched to any raw record ({records.Count} records, "
                    + $"{blocks.Count} blocks). Stopping is better than attaching hunks to the wrong file.");
            }

            matched[target] = block;
        }

        FileDiff[] diffs = new FileDiff[records.Count];

        for (int i = 0; i < records.Count; i++)
        {
            diffs[i] = Build(records[i], matched[i] ?? PatchBlock.Empty, maximumChangedLines);
        }

        return diffs;
    }

    /// <summary>
    /// NUL ayraçlı ham bölümü okur ve yama bölümünün başladığı indeksi döndürür.
    /// </summary>
    /// <remarks>
    /// Bölme (<c>Split</c>) yerine elle gezinmek gerekiyor: yama metninin nerede başladığını
    /// bilmek için <b>bayt konumunu</b> korumak şart. "İlk <c>diff --git</c> geçtiği yer"
    /// aramak yanlış olurdu — bir dosya adı bu metni içerebilir.
    /// </remarks>
    private static (List<RawRecord> Records, int PatchStart) ParseRawSection(string output)
    {
        List<RawRecord> records = [];
        int position = 0;

        while (position < output.Length && output[position] == ':')
        {
            if (!TryReadToken(output, ref position, out string meta))
            {
                break;
            }

            string[] fields = meta.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < RawFieldCount)
            {
                // Beklenmeyen biçim: sessizce yanlış veri üretmektense dur.
                throw new DiffParseException($"Could not parse the raw record: '{meta}'");
            }

            string status = fields[4];

            // Yeniden adlandırma ve kopyalama İKİ yol taşır (ölçüldü: `R100<NUL>eski<NUL>yeni`).
            int pathCount = status[0] is 'R' or 'C' ? 2 : 1;

            string[] paths = new string[pathCount];

            for (int i = 0; i < pathCount; i++)
            {
                if (!TryReadToken(output, ref position, out paths[i]))
                {
                    throw new DiffParseException("The raw record paths are incomplete.");
                }
            }

            records.Add(new RawRecord(
                OldMode: fields[0].TrimStart(':'),
                NewMode: fields[1],
                OldBlob: fields[2],
                NewBlob: fields[3],
                Status: status,
                Paths: paths));
        }

        // Ham bölümü yamadan ayıran boş jeton(lar).
        while (position < output.Length && output[position] == '\0')
        {
            position++;
        }

        return (records, position);
    }

    private static bool TryReadToken(string text, ref int position, out string token)
    {
        int end = text.IndexOf('\0', position);

        if (end < 0)
        {
            token = string.Empty;
            return false;
        }

        token = text[position..end];
        position = end + 1;
        return true;
    }

    /// <summary>
    /// Yama bölümünü <c>diff --git</c> satırlarından bloklara ayırır.
    /// </summary>
    private static List<PatchBlock> SplitPatchBlocks(
        string output,
        int start,
        bool inlineSegments,
        List<RawRecord> records,
        int maximumChangedLines,
        Encoding contentEncoding)
    {
        List<PatchBlock> blocks = [];

        if (start >= output.Length)
        {
            return blocks;
        }

        string[] lines = output[start..].Split('\n');

        List<string>? current = null;

        foreach (string line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal)
                || line.StartsWith("diff --cc ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    blocks.Add(Create(current));
                }

                current = [];
                continue;
            }

            current?.Add(line);
        }

        if (current is not null)
        {
            blocks.Add(Create(current));
        }

        return blocks;

        // Sınırı aşan dosyada satırlar HİÇ üretilmez: 800 bin DiffLine nesnesi yaratmak
        // tam olarak kaçınmak istediğimiz şey (Faz 03'te nesne başı ek yük ölçüldü).
        PatchBlock Create(List<string> lines)
        {
            int index = blocks.Count;

            bool skip = maximumChangedLines > 0
                && index < records.Count
                && records[index].ChangedLines > maximumChangedLines;

            return new PatchBlock(lines, inlineSegments, skip, contentEncoding);
        }
    }

    private static FileDiff Build(RawRecord record, PatchBlock block, int maximumChangedLines)
    {
        // Yollar YALNIZCA ham bölümden; yama başlığındaki yollar ayrıştırılamaz.
        // Yollar UTF-8: git `-z` ile ham bayt veriyor ve dosya adları depoda UTF-8'dir.
        RepositoryPath newPath = RepositoryPath.Parse(Reencode(record.Paths[^1], Encoding.UTF8));
        RepositoryPath? oldPath = record.Paths.Length > 1
            ? RepositoryPath.Parse(Reencode(record.Paths[0], Encoding.UTF8))
            : null;

        // Silinen dosyada "yeni yol" yoktur; alan eski yolu taşır (modelde belgelendi).
        return new FileDiff
        {
            Path = newPath,
            OldPath = oldPath,
            Change = ParseStatus(record.Status),
            SimilarityScore = ParseSimilarity(record.Status),
            OldMode = record.OldMode,
            NewMode = record.NewMode,
            OldBlob = ParseBlob(record.OldBlob),
            NewBlob = ParseBlob(record.NewBlob),
            IsBinary = block.IsBinary,
            StatAdded = record.Added,
            StatRemoved = record.Removed,
            IsTooLarge = maximumChangedLines > 0 && record.ChangedLines > maximumChangedLines,
            Hunks = block.Hunks,
        };
    }

    /// <summary>
    /// Kayıpsız okunmuş metni hedef kodlamayla yeniden çözer.
    /// </summary>
    /// <remarks>
    /// Girdi <see cref="Git.GitResult.GetStandardOutputLossless"/> ile üretilmiş olmalı:
    /// her karakter tek bir bayta karşılık gelir. ASCII metin için sonuç değişmez, bu yüzden
    /// başlıklar ve işaretler etkilenmez.
    /// </remarks>
    private static string Reencode(string lossless, Encoding target)
    {
        if (lossless.Length == 0 || ReferenceEquals(target, Encoding.Latin1))
        {
            return lossless;
        }

        // Yalnızca ASCII ise dönüşüm gereksiz — yaygın durum bu.
        bool ascii = true;

        foreach (char c in lossless)
        {
            if (c > 0x7F)
            {
                ascii = false;
                break;
            }
        }

        return ascii ? lossless : target.GetString(Encoding.Latin1.GetBytes(lossless));
    }

    private static CommitId ParseBlob(string value) =>
        // Yok olan taraf sıfırlarla gelir (`0000000`); bunu kimlik saymak yanıltıcı olur.
        value.All(c => c == '0') || !CommitId.TryParse(value, out CommitId id)
            ? default
            : id;

    private static FileChangeKind ParseStatus(string status) => status[0] switch
    {
        'A' => FileChangeKind.Added,
        'M' => FileChangeKind.Modified,
        'D' => FileChangeKind.Deleted,
        'R' => FileChangeKind.Renamed,
        'C' => FileChangeKind.Copied,
        'T' => FileChangeKind.TypeChanged,
        'U' => FileChangeKind.Unmerged,
        _ => FileChangeKind.Unmodified,
    };

    /// <summary>Durum harfinin ardındaki benzerlik yüzdesi (<c>R100</c> → 100).</summary>
    private static int? ParseSimilarity(string status) =>
        status.Length > 1 && int.TryParse(status[1..], CultureInfo.InvariantCulture, out int score)
            ? score
            : null;

    private sealed record RawRecord(
        string OldMode,
        string NewMode,
        string OldBlob,
        string NewBlob,
        string Status,
        string[] Paths)
    {
        public int? Added { get; init; }

        public int? Removed { get; init; }

        /// <summary>Toplam değişen satır; numstat yoksa 0 (sınır uygulanamaz).</summary>
        public int ChangedLines => (Added ?? 0) + (Removed ?? 0);
    }

    private readonly record struct NumStatRecord(int? Added, int? Removed);

    /// <summary>
    /// Tek bir dosyanın yama bloğu — hunk'lar ve binary bayrağı.
    /// </summary>
    private sealed class PatchBlock
    {
        /// <summary>Yama bloğu olmayan kayıtlar için: hunk yok, binary değil.</summary>
        public static PatchBlock Empty { get; } = new([], inlineSegments: false, skipContent: false, Encoding.UTF8);

        private readonly bool _inlineSegments;

        public PatchBlock(
            List<string> lines,
            bool inlineSegments,
            bool skipContent,
            Encoding contentEncoding)
        {
            _inlineSegments = inlineSegments;

            // Sınırı aşan dosyada satırlar HİÇ üretilmez: 800 bin DiffLine nesnesi yaratmak
            // tam olarak kaçınmak istediğimiz şey (Faz 03'te nesne başı ek yük ölçüldü).
            if (skipContent)
            {
                Hunks = [];

                foreach (string skipped in lines)
                {
                    if (skipped.StartsWith("Binary files ", StringComparison.Ordinal)
                        || skipped.StartsWith("GIT binary patch", StringComparison.Ordinal))
                    {
                        IsBinary = true;
                    }
                    else if (skipped.StartsWith("index ", StringComparison.Ordinal))
                    {
                        ReadIndexLine(skipped);
                    }
                }

                return;
            }

            List<DiffHunk> hunks = [];
            List<DiffLine>? currentLines = null;
            HunkHeader header = default;
            int oldLine = 0;
            int newLine = 0;


            foreach (string line in lines)
            {
                // Ölçüldü: binary dosyalarda içerik yerine bu satır gelir ve hunk hiç yoktur.
                if (line.StartsWith("Binary files ", StringComparison.Ordinal)
                    || line.StartsWith("GIT binary patch", StringComparison.Ordinal))
                {
                    IsBinary = true;
                    continue;
                }

                if (line.StartsWith("index ", StringComparison.Ordinal))
                {
                    // `index <eski>..<yeni> <mod>` — blob kimlikleri, sayı uyuşmazlığında
                    // eşleme anahtarı olarak kullanılıyor.
                    ReadIndexLine(line);
                    continue;
                }

                if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    Flush(hunks, ref currentLines, header);

                    if (TryParseHunkHeader(line, out header))
                    {
                        currentLines = [];
                        oldLine = header.OldStart;
                        newLine = header.NewStart;
                    }

                    continue;
                }

                if (currentLines is null)
                {
                    continue;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                switch (line[0])
                {
                    case ' ':
                        currentLines.Add(new DiffLine(DiffLineKind.Context, Reencode(line[1..], contentEncoding))
                        {
                            OldLineNumber = oldLine++,
                            NewLineNumber = newLine++,
                        });
                        break;

                    case '+':
                        currentLines.Add(new DiffLine(DiffLineKind.Added, Reencode(line[1..], contentEncoding))
                        {
                            NewLineNumber = newLine++,
                        });
                        break;

                    case '-':
                        currentLines.Add(new DiffLine(DiffLineKind.Removed, Reencode(line[1..], contentEncoding))
                        {
                            OldLineNumber = oldLine++,
                        });
                        break;

                    case '\\':
                        // `\ No newline at end of file` — ÖLÇÜLDÜ: kendi başına bir satır değil,
                        // KENDİNDEN ÖNCEKİ satıra ait ve aynı hunk'ta iki kez çıkabilir.
                        if (currentLines.Count > 0)
                        {
                            currentLines[^1] = currentLines[^1] with { EndsWithoutNewline = true };
                        }

                        break;

                    default:
                        // `index …`, `old mode`, `new mode`, `--- `, `+++ `, `similarity index`,
                        // `rename from/to`: bilgi ham bölümden geliyor, burada yok sayılır.
                        break;
                }
            }

            Flush(hunks, ref currentLines, header);

            Hunks = hunks;
        }

        public IReadOnlyList<DiffHunk> Hunks { get; }

        public bool IsBinary { get; }

        private string _indexOld = string.Empty;
        private string _indexNew = string.Empty;

        /// <summary>
        /// Bu bloğun <c>index</c> satırındaki blob'lar verilen kayıtla uyuşuyor mu?
        /// </summary>
        /// <remarks>
        /// Kısaltma uzunlukları iki çıktıda farklı olabileceği için önek karşılaştırması
        /// yapılıyor. <c>index</c> satırı yoksa (rename/mod değişikliği) eşleme yapılamaz.
        /// </remarks>
        public bool MatchesBlobs(string oldBlob, string newBlob) =>
            _indexOld.Length > 0
            && _indexNew.Length > 0
            && PrefixEquals(_indexOld, oldBlob)
            && PrefixEquals(_indexNew, newBlob);

        private static bool PrefixEquals(string left, string right)
        {
            int length = Math.Min(left.Length, right.Length);

            return length > 0
                && left.AsSpan(0, length).SequenceEqual(right.AsSpan(0, length));
        }

        private void ReadIndexLine(string line)
        {
            ReadOnlySpan<char> rest = line.AsSpan("index ".Length);
            int separator = rest.IndexOf("..", StringComparison.Ordinal);

            if (separator < 0)
            {
                return;
            }

            _indexOld = rest[..separator].ToString();

            ReadOnlySpan<char> tail = rest[(separator + 2)..];
            int space = tail.IndexOf(' ');

            _indexNew = (space < 0 ? tail : tail[..space]).ToString();
        }

        private void Flush(List<DiffHunk> hunks, ref List<DiffLine>? lines, HunkHeader header)
        {
            if (lines is null)
            {
                return;
            }

            // Satır içi parçalar KESİN satır metinleri üzerinde hesaplanıyor; git'in
            // --word-diff'i boş satırların hangi tarafa ait olduğunu kaybediyor (ölçüldü).
            IReadOnlyList<DiffLine> finalLines = _inlineSegments ? InlineDiff.Annotate(lines) : lines;

            hunks.Add(new DiffHunk
            {
                Header = header.Raw,
                OldStart = header.OldStart,
                OldLength = header.OldLength,
                NewStart = header.NewStart,
                NewLength = header.NewLength,
                Section = header.Section,
                Lines = finalLines,
            });

            lines = null;
        }
    }

    private readonly record struct HunkHeader(
        string Raw,
        int OldStart,
        int OldLength,
        int NewStart,
        int NewLength,
        string Section);

    /// <summary>
    /// <c>@@ -a,b +c,d @@ bağlam</c> satırını ayrıştırır.
    /// </summary>
    /// <remarks>
    /// Uzunluk <b>yazılmayabilir</b>: tek satırlık hunk'ta git <c>@@ -1 +1 @@</c> yazıyor ve
    /// eksik uzunluk <c>1</c> demektir. Varsayılanı 0 almak satır numaralarını kaydırırdı.
    /// </remarks>
    private static bool TryParseHunkHeader(string line, out HunkHeader header)
    {
        header = default;

        int open = line.IndexOf("@@ ", StringComparison.Ordinal);
        int close = line.IndexOf(" @@", StringComparison.Ordinal);

        if (open != 0 || close <= open)
        {
            return false;
        }

        string ranges = line[(open + 3)..close];
        string[] parts = ranges.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || parts[0][0] != '-' || parts[1][0] != '+')
        {
            return false;
        }

        (int oldStart, int oldLength) = ParseRange(parts[0][1..]);
        (int newStart, int newLength) = ParseRange(parts[1][1..]);

        // Başlıktaki bağlam metni DOSYADAN geliyor (kapsayan fonksiyon satırı), yani
        // dosyanın kodlamasında. Ham hâli korunuyor; yeniden kodlama çağıranda yapılıyor.
        string section = line.Length > close + 3 ? line[(close + 3)..].Trim() : string.Empty;

        header = new HunkHeader(line, oldStart, oldLength, newStart, newLength, section);
        return true;
    }

    private static (int Start, int Length) ParseRange(string range)
    {
        int comma = range.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0)
        {
            // Uzunluk yazılmamış → 1 (git tek satırlık hunk'ta böyle yazıyor).
            return (ParseInt(range), 1);
        }

        return (ParseInt(range[..comma]), ParseInt(range[(comma + 1)..]));
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, CultureInfo.InvariantCulture, out int result) ? result : 0;
}

/// <summary>
/// Diff çıktısı beklenen biçimde değilse fırlatılır.
/// </summary>
/// <remarks>
/// Sessizce yanlış veri üretmektense durmak tercih edildi: hunk'ları yanlış dosyaya bağlamak
/// kullanıcıya <b>başka bir dosyanın değişikliklerini</b> göstermek demektir.
/// </remarks>
public sealed class DiffParseException : Exception
{
    public DiffParseException(string message)
        : base(message)
    {
    }

    public DiffParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DiffParseException()
    {
    }
}
