namespace GitExt.Core.Model;

/// <summary>Bir Git nesnesinin türü.</summary>
public enum GitObjectType
{
    /// <summary>Nesne bulunamadı.</summary>
    Missing,

    /// <summary>Dosya içeriği.</summary>
    Blob,

    /// <summary>Dizin.</summary>
    Tree,

    Commit,

    /// <summary>Annotated tag nesnesi.</summary>
    Tag,
}

/// <summary>
/// Bir nesnenin içeriği okunmadan elde edilebilen bilgisi.
/// </summary>
/// <remarks>
/// <c>cat-file --batch-check</c> ile alınır — büyük bir dosyayı belleğe almadan boyutunu
/// öğrenmek için.
/// </remarks>
public sealed record GitObjectInfo
{
    public required CommitId Id { get; init; }

    public required GitObjectType Type { get; init; }

    /// <summary>Bayt cinsinden boyut; nesne yoksa 0.</summary>
    public required long Size { get; init; }

    public bool Exists => Type != GitObjectType.Missing;
}

/// <summary>
/// Bir ağaç (dizin) girdisi.
/// </summary>
public sealed record TreeEntry
{
    public required RepositoryPath Path { get; init; }

    /// <summary>Sekizlik dosya modu, örn. <c>100644</c>.</summary>
    public required string Mode { get; init; }

    public required GitObjectType Type { get; init; }

    public required CommitId Id { get; init; }

    /// <summary>Bayt cinsinden boyut; yalnızca <c>--long</c> ile istendiyse dolu.</summary>
    public long? Size { get; init; }

    public bool IsDirectory => Type == GitObjectType.Tree;

    /// <summary><c>120000</c> — sembolik bağ.</summary>
    public bool IsSymlink => Mode == "120000";

    /// <summary><c>160000</c> — gitlink, yani submodule.</summary>
    public bool IsSubmodule => Mode == "160000";

    /// <summary><c>100755</c> — çalıştırılabilir dosya.</summary>
    public bool IsExecutable => Mode == "100755";

    public string Name => Path.Name;

    public override string ToString() => Path.Value;
}

/// <summary>
/// Bir blob'un içeriği.
/// </summary>
public sealed record BlobContent
{
    public required CommitId Id { get; init; }

    /// <summary>Nesnenin depodaki gerçek boyutu (kırpılmış olsa bile).</summary>
    public required long Size { get; init; }

    /// <summary>Ham içerik. <see cref="IsTruncated"/> ise yalnızca ilk kısım.</summary>
    public required byte[] Content { get; init; }

    /// <summary>
    /// İçerik ikili (binary) mi?
    /// </summary>
    /// <remarks>
    /// <c>git</c>'in sezgisiyle aynı: ilk 8000 baytta NUL varsa ikili sayılır.
    /// Kusursuz değil ama git'le tutarlı olmak, kendi kuralımızı uydurmaktan iyi.
    /// </remarks>
    public required bool IsBinary { get; init; }

    /// <summary>Boyut sınırı aşıldığı için içerik kısaltıldı mı?</summary>
    public bool IsTruncated { get; init; }

    /// <summary>
    /// İçeriği UTF-8 metin olarak döndürür.
    /// </summary>
    /// <exception cref="InvalidOperationException">İçerik ikiliyse.</exception>
    public string GetText()
    {
        if (IsBinary)
        {
            throw new InvalidOperationException(
                "İkili içerik metin olarak okunamaz. Önce IsBinary kontrol edilmeli.");
        }

        return System.Text.Encoding.UTF8.GetString(Content);
    }
}
