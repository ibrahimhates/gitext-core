namespace GitExt.Core.Model;

/// <summary>
/// Diff içindeki tek bir satırın türü.
/// </summary>
/// <remarks>
/// <b>Plandan sapma:</b> <c>P04-T01</c> <c>\ No newline at end of file</c> işaretini ayrı bir
/// satır <i>türü</i> olarak listeliyordu. Ölçüldü ve öyle olmadığı görüldü: bu işaret kendi
/// başına bir satır değil, <b>kendinden önceki satıra ait bir nitelik</b> — üstelik aynı
/// hunk içinde hem <c>-</c> hem <c>+</c> satırından sonra ayrı ayrı çıkabiliyor. Bu yüzden
/// tür değil, <see cref="DiffLine.EndsWithoutNewline"/> bayrağı olarak modellendi; yamayı
/// birebir geri üretmek de ancak böyle mümkün (Faz 05).
/// </remarks>
public enum DiffLineKind
{
    /// <summary>Değişmemiş bağlam satırı (satır başı boşluk).</summary>
    Context,

    /// <summary>Eklenen satır (<c>+</c>).</summary>
    Added,

    /// <summary>Silinen satır (<c>-</c>).</summary>
    Removed,
}

/// <summary>
/// Bir satır içindeki değişmiş/değişmemiş parça (P04-T05).
/// </summary>
/// <param name="Kind">
/// <see cref="DiffLineKind.Context"/> parça iki tarafta da aynı; <see cref="DiffLineKind.Added"/>
/// ve <see cref="DiffLineKind.Removed"/> yalnızca kendi tarafında var.
/// </param>
/// <param name="Text">Parçanın ham metni.</param>
public sealed record DiffSegment(DiffLineKind Kind, string Text)
{
    public bool IsAdded => Kind == DiffLineKind.Added;

    public bool IsRemoved => Kind == DiffLineKind.Removed;

    public override string ToString() => Text;
}

/// <summary>
/// Diff'teki tek bir satır.
/// </summary>
/// <param name="Kind">Satırın türü.</param>
/// <param name="Content">Satır içeriği — baştaki <c>+</c>/<c>-</c>/boşluk <b>dahil değil</b>.</param>
public sealed record DiffLine(DiffLineKind Kind, string Content)
{
    /// <summary>Eski dosyadaki satır numarası; eklenen satırlarda <see langword="null"/>.</summary>
    public int? OldLineNumber { get; init; }

    /// <summary>Yeni dosyadaki satır numarası; silinen satırlarda <see langword="null"/>.</summary>
    public int? NewLineNumber { get; init; }

    /// <summary>
    /// Bu satırdan sonra <c>\ No newline at end of file</c> işareti geliyor mu?
    /// </summary>
    /// <remarks>
    /// Dosyanın sonunda satır sonu karakteri yok demektir. Yamayı yeniden üretirken bu işaret
    /// <b>tam olarak bu satırın ardına</b> yazılmalı, aksi halde <c>git apply</c> reddeder.
    /// </remarks>
    public bool EndsWithoutNewline { get; init; }

    /// <summary>
    /// Satır içi değişiklik parçaları (P04-T05); kelime seviyesi diff istenmediyse boş.
    /// </summary>
    /// <remarks>
    /// Parçalar birleştirildiğinde <see cref="Content"/> <b>birebir</b> elde edilir —
    /// bu ölçülerek doğrulandı ve tasarımı belirledi: git'in <b>varsayılan</b> kelime
    /// ayracıyla eski satır sonuna sahte bir boşluk ekleniyordu, karakter seviyeli
    /// ayraçla (<c>--word-diff-regex=.</c>) ise kurtarma tam doğru.
    /// </remarks>
    public IReadOnlyList<DiffSegment> Segments { get; init; } = [];

    /// <summary>Satır içi parçalar hesaplandı mı?</summary>
    public bool HasSegments => Segments.Count > 0;

    public override string ToString() => Kind switch
    {
        DiffLineKind.Added => "+" + Content,
        DiffLineKind.Removed => "-" + Content,
        _ => " " + Content,
    };
}

/// <summary>
/// Bir dosyanın diff'indeki tek bir değişiklik bloğu (hunk).
/// </summary>
public sealed record DiffHunk
{
    /// <summary>
    /// Hunk başlığının <b>ham metni</b> — <c>@@ -1,3 +1,3 @@ bağlam</c> satırının tamamı.
    /// </summary>
    /// <remarks>
    /// Ayrıştırılmış alanlar zaten var; ham metin ayrıca saklanıyor çünkü Faz 05'te
    /// <b>değiştirilmiş bir yamayı <c>git apply</c>'a geri vermemiz</b> gerekecek ve bu ancak
    /// orijinal biçim korunursa güvenli olur. Yeniden üretmeye kalkmak, git'in biçimindeki her
    /// ince ayrıntıyı (tek satırlık hunk'ta uzunluğun yazılmaması gibi) taklit etmek demektir.
    /// </remarks>
    public required string Header { get; init; }

    /// <summary>Eski dosyadaki başlangıç satırı (1 tabanlı).</summary>
    public required int OldStart { get; init; }

    /// <summary>Eski dosyadan kaç satır kapsanıyor.</summary>
    public required int OldLength { get; init; }

    /// <summary>Yeni dosyadaki başlangıç satırı (1 tabanlı).</summary>
    public required int NewStart { get; init; }

    /// <summary>Yeni dosyadan kaç satır kapsanıyor.</summary>
    public required int NewLength { get; init; }

    /// <summary>
    /// Başlığın ikinci <c>@@</c>'inden sonraki bağlam metni (genelde kapsayan fonksiyon adı).
    /// </summary>
    /// <remarks>Boş olabilir; git bu alanı her zaman doldurmaz.</remarks>
    public string Section { get; init; } = string.Empty;

    public required IReadOnlyList<DiffLine> Lines { get; init; }

    public int AddedCount => Lines.Count(l => l.Kind == DiffLineKind.Added);

    public int RemovedCount => Lines.Count(l => l.Kind == DiffLineKind.Removed);

    public override string ToString() => Header;
}

/// <summary>
/// Tek bir dosyanın diff'i (P04-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>⚠️ Yollar <c>diff --git</c> başlığından ALINMAZ.</b> Ölçüldü: o satır genel olarak
/// ayrıştırılamıyor — boşluk içeren yollarda iki yolu ayırmanın güvenli yolu yok
/// (<c>a/alt dizin/b -&gt; c.txt b/alt dizin/b -&gt; c.txt</c>) ve ASCII dışı adlar C tarzı
/// sekizlik kaçışla tırnaklanıyor. Yollar, değişim türü ve mod/blob bilgisi
/// <c>git diff --raw -z</c> çıktısından gelir (ADR-0003: makine-okunur kanal).
/// </para>
/// <para>
/// <b>⚠️ <see cref="Hunks"/> BOŞ olabilir ve bu normaldir.</b> Ölçülen dört durumda git
/// hiç hunk üretmiyor: %100 benzerlikli rename, yalnızca mod değişikliği, boş yeni dosya,
/// ve binary dosya. Her dosyanın hunk'ı olduğunu varsayan kod bu depolarda kırılır.
/// </para>
/// </remarks>
public sealed record FileDiff
{
    /// <summary>
    /// Dosyanın yolu — silme dışında <b>yeni</b> yol.
    /// </summary>
    /// <remarks>
    /// Silinen dosyada yeni yol yoktur, bu alan eski yolu taşır. Yeniden adlandırmada yeni
    /// yol buradadır, eskisi <see cref="OldPath"/>'tedir.
    /// </remarks>
    public required RepositoryPath Path { get; init; }

    /// <summary>
    /// Önceki yol; yalnızca yeniden adlandırma ve kopyalamada dolu.
    /// </summary>
    public RepositoryPath? OldPath { get; init; }

    public required FileChangeKind Change { get; init; }

    /// <summary>
    /// Yeniden adlandırma/kopyalama benzerlik yüzdesi (<c>R100</c> → 100).
    /// </summary>
    public int? SimilarityScore { get; init; }

    /// <summary>Eski dosya modu (<c>100644</c>, <c>100755</c>, <c>120000</c>, <c>160000</c>).</summary>
    public string OldMode { get; init; } = string.Empty;

    /// <summary>Yeni dosya modu.</summary>
    public string NewMode { get; init; } = string.Empty;

    /// <summary>Eski içeriğin blob kimliği.</summary>
    /// <remarks>
    /// Tür olarak <see cref="CommitId"/> kullanılıyor: depoda tüm nesne kimlikleri aynı
    /// biçimde ve bu tip zaten <c>TreeEntry</c>/<c>BlobContent</c>'te de böyle kullanılıyor.
    /// </remarks>
    public CommitId OldBlob { get; init; }

    /// <summary>Yeni içeriğin blob kimliği.</summary>
    public CommitId NewBlob { get; init; }

    /// <summary>
    /// git bu dosyayı binary olarak mı bildirdi?
    /// </summary>
    /// <remarks>
    /// Binary dosyalarda içerik yerine <c>Binary files a/… and b/… differ</c> satırı gelir;
    /// <see cref="Hunks"/> boştur.
    /// </remarks>
    public bool IsBinary { get; init; }

    public required IReadOnlyList<DiffHunk> Hunks { get; init; }

    public bool HasHunks => Hunks.Count > 0;

    /// <summary>
    /// Yalnızca dosya modu mu değişti (içerik aynı)?
    /// </summary>
    /// <remarks>
    /// Ölçüldü: bu durumda <c>--raw</c> çıktısında <b>iki blob kimliği de aynı</b> ve durum
    /// harfi <c>M</c>; unified diff çıktısında ise yalnızca <c>old mode</c>/<c>new mode</c>
    /// satırları var, hunk yok. Ayrı bir gösterim gerektirir — "değişti" deyip boş diff
    /// göstermek kullanıcıya hata gibi görünür.
    /// </remarks>
    public bool IsModeOnlyChange =>
        !OldBlob.IsEmpty
        && OldBlob == NewBlob
        && OldMode.Length > 0
        && NewMode.Length > 0
        && OldMode != NewMode;

    /// <summary>Dosya bir alt modül mü (mod <c>160000</c>)?</summary>
    public bool IsSubmodule => OldMode == "160000" || NewMode == "160000";

    /// <summary>Sembolik bağ mı (mod <c>120000</c>)?</summary>
    public bool IsSymlink => OldMode == "120000" || NewMode == "120000";

    /// <summary>Çalıştırılabilir bayrağı değişti mi?</summary>
    public bool IsExecutableChanged =>
        OldMode.Length > 0 && NewMode.Length > 0
        && (OldMode == "100755") != (NewMode == "100755");

    /// <summary>
    /// Dosya, ayarlanan değişiklik sınırını aştığı için içeriği <b>okunmadı</b> (P04-T06).
    /// </summary>
    /// <remarks>
    /// Hunk'lar boştur ama <see cref="AddedLines"/>/<see cref="RemovedLines"/> yine doğrudur:
    /// sayılar <c>--numstat</c>'tan geliyor ve içerik üretmeden alınıyor. Arayüz bu durumda
    /// "çok büyük, yine de göster" seçeneği sunar.
    /// </remarks>
    public bool IsTooLarge { get; init; }

    /// <summary><c>--numstat</c>'tan gelen eklenen satır sayısı; yoksa <see langword="null"/>.</summary>
    public int? StatAdded { get; init; }

    /// <summary><c>--numstat</c>'tan gelen silinen satır sayısı; yoksa <see langword="null"/>.</summary>
    public int? StatRemoved { get; init; }

    /// <summary>
    /// Eklenen satır sayısı.
    /// </summary>
    /// <remarks>
    /// Öncelik <c>--numstat</c>'ta: içerik okunmamış olsa bile (çok büyük dosya) sayı doğru
    /// kalır. Yoksa hunk'lardan hesaplanır.
    /// </remarks>
    public int AddedLines => StatAdded ?? Hunks.Sum(h => h.AddedCount);

    public int RemovedLines => StatRemoved ?? Hunks.Sum(h => h.RemovedCount);

    /// <summary>Toplam değişen satır sayısı — boyut sınırı buna göre uygulanır.</summary>
    public int ChangedLines => AddedLines + RemovedLines;

    public override string ToString() =>
        OldPath is { } previous ? $"{Change}: {previous} → {Path}" : $"{Change}: {Path}";
}
