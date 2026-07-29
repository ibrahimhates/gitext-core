namespace GitExt.Graph;

/// <summary>
/// Yerleşim algoritmasının tek bir satır için ürettiği sonuç.
/// </summary>
/// <remarks>
/// <b>Saf veridir</b> (ADR-0003): piksel, renk kodu, çizim nesnesi içermez. Şerit indeksi
/// ve renk indeksi soyut sayılardır; bunları koordinata ve renge çevirmek çizim katmanının
/// işidir. Bu ayrım sayesinde algoritma hiçbir şey çizmeden test edilebiliyor.
/// </remarks>
public sealed record GraphRow
{
    /// <summary>Bu satırdaki commit.</summary>
    public required DagCommit Commit { get; init; }

    /// <summary>
    /// Commit düğümünün bulunduğu dikey şerit; 0 en soldaki.
    /// </summary>
    public required int Lane { get; init; }

    /// <summary>
    /// Şeride atanmış renk indeksi.
    /// </summary>
    /// <remarks>
    /// Gerçek renge çevirmek tema katmanının işi (Faz 08). Burada yalnızca "komşu şeritler
    /// farklı indeks alsın" garantisi var.
    /// </remarks>
    public required int ColorIndex { get; init; }

    /// <summary>
    /// Bu satırdan aşağıya (daha eski commit'lere) uzanan kenarlar.
    /// </summary>
    public required IReadOnlyList<GraphEdge> Edges { get; init; }

    /// <summary>
    /// Bu satırda kullanımda olan toplam şerit sayısı.
    /// </summary>
    /// <remarks>
    /// Çizim katmanının satır genişliğini bilmesi için; grafiğin en geniş yerini bulmak
    /// üzere tüm satırları taramak zorunda kalmaz.
    /// </remarks>
    public required int LaneCount { get; init; }

    public override string ToString() => $"{Commit.Id}@{Lane}";
}

/// <summary>
/// İki satır arasındaki bağlantının geometrisi.
/// </summary>
/// <remarks>
/// Kenar, <b>bu satırdan başlayıp aşağı doğru</b> uzanır. Dikey bir kenarda
/// <see cref="FromLane"/> ve <see cref="ToLane"/> aynıdır; şerit değiştiren bir kenarda
/// (dallanma veya merge) farklıdır.
/// </remarks>
public sealed record GraphEdge
{
    /// <summary>Kenarın bu satırdaki başlangıç şeridi.</summary>
    public required int FromLane { get; init; }

    /// <summary>Kenarın bir sonraki satırdaki bitiş şeridi.</summary>
    public required int ToLane { get; init; }

    /// <summary>Kenarın ulaştığı commit (ebeveyn).</summary>
    public required string Target { get; init; }

    /// <summary>Şeride atanmış renk indeksi.</summary>
    public required int ColorIndex { get; init; }

    /// <summary>
    /// Kenar bu satırda bir düğümden mi çıkıyor, yoksa sadece geçiyor mu?
    /// </summary>
    /// <remarks>
    /// Geçiş (pass-through) kenarları, o satırdaki commit'le ilgisi olmayan ama şeridi
    /// meşgul eden bağlantılardır. Çizim katmanı bunları düğümsüz düz çizgi olarak çizer.
    /// </remarks>
    public bool IsPassThrough { get; init; }

    /// <summary>Şerit değiştiren (diyagonal) bir kenar mı?</summary>
    public bool IsDiagonal => FromLane != ToLane;

    public override string ToString() => $"{FromLane}→{ToLane} ({Target})";
}
