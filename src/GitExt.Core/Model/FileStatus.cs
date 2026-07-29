namespace GitExt.Core.Model;

/// <summary>
/// Bir dosyanın tek bir alandaki (index veya çalışma ağacı) değişim türü.
/// </summary>
/// <remarks>
/// <c>--porcelain=v2</c> çıktısındaki <c>XY</c> alanının tek karakterine karşılık gelir.
/// Değişmemiş olan boşlukla değil <b><c>.</c></b> ile gösterilir — v1'den farkı budur.
/// </remarks>
public enum FileChangeKind
{
    /// <summary><c>.</c> — bu alanda değişiklik yok.</summary>
    Unmodified,

    /// <summary><c>M</c></summary>
    Modified,

    /// <summary><c>A</c></summary>
    Added,

    /// <summary><c>D</c></summary>
    Deleted,

    /// <summary><c>R</c></summary>
    Renamed,

    /// <summary><c>C</c></summary>
    Copied,

    /// <summary><c>T</c> — dosya türü değişti (örn. normal dosya → sembolik bağ).</summary>
    TypeChanged,

    /// <summary><c>U</c> — çakışma nedeniyle birleştirilmemiş.</summary>
    Unmerged,
}

/// <summary>
/// Birleştirilmemiş (unmerged) bir dosyanın çakışma türü.
/// </summary>
/// <remarks>
/// <c>XY</c> çiftinin anlamı; "us" mevcut dal (<c>HEAD</c>), "them" birleştirilen dal.
/// Bu ayrım conflict çözüm arayüzünde doğrudan kullanıcıya gösterilecek (Faz 07).
/// </remarks>
public enum ConflictKind
{
    None,

    /// <summary><c>UU</c> — her iki taraf da değiştirdi.</summary>
    BothModified,

    /// <summary><c>AA</c> — her iki taraf da ekledi.</summary>
    BothAdded,

    /// <summary><c>DD</c> — her iki taraf da sildi.</summary>
    BothDeleted,

    /// <summary><c>AU</c> — biz ekledik, onlar dokunmadı.</summary>
    AddedByUs,

    /// <summary><c>UA</c> — onlar ekledi, biz dokunmadık.</summary>
    AddedByThem,

    /// <summary><c>DU</c> — biz sildik, onlar değiştirdi.</summary>
    DeletedByUs,

    /// <summary><c>UD</c> — onlar sildi, biz değiştirdik.</summary>
    DeletedByThem,
}

/// <summary>
/// Bir submodule girdisinin durumu (<c>S&lt;c&gt;&lt;m&gt;&lt;u&gt;</c> alanı).
/// </summary>
public readonly record struct SubmoduleState(
    bool CommitChanged,
    bool HasTrackedChanges,
    bool HasUntrackedChanges)
{
    public bool HasAnyChange => CommitChanged || HasTrackedChanges || HasUntrackedChanges;
}

/// <summary>
/// Çalışma dizinindeki tek bir dosyanın durumu.
/// </summary>
public sealed record FileStatus
{
    public required RepositoryPath Path { get; init; }

    /// <summary>
    /// Index'in <c>HEAD</c>'e göre durumu — yani <b>stage'lenmiş</b> değişiklik.
    /// </summary>
    public FileChangeKind StagedChange { get; init; } = FileChangeKind.Unmodified;

    /// <summary>
    /// Çalışma ağacının index'e göre durumu — yani <b>stage'lenmemiş</b> değişiklik.
    /// </summary>
    public FileChangeKind UnstagedChange { get; init; } = FileChangeKind.Unmodified;

    /// <summary>Takip edilmeyen dosya mı?</summary>
    public bool IsUntracked { get; init; }

    /// <summary><c>.gitignore</c> tarafından yok sayılıyor mu?</summary>
    public bool IsIgnored { get; init; }

    /// <summary>Çakışma türü; çakışma yoksa <see cref="ConflictKind.None"/>.</summary>
    public ConflictKind Conflict { get; init; } = ConflictKind.None;

    /// <summary>
    /// Yeniden adlandırma veya kopyalamada kaynak yol.
    /// </summary>
    /// <remarks>
    /// <c>-z</c> modunda bu değer <b>ayrı bir NUL kaydında</b> gelir (ölçüldü);
    /// ayrıştırıcı <c>2</c> satırından sonra bir sonraki kaydı tüketmelidir.
    /// </remarks>
    public RepositoryPath? OriginalPath { get; init; }

    /// <summary>Yeniden adlandırma/kopyalama benzerlik yüzdesi (<c>R100</c> → 100).</summary>
    public int? SimilarityScore { get; init; }

    /// <summary>Girdi bir submodule ise durumu.</summary>
    public SubmoduleState? Submodule { get; init; }

    public bool IsConflicted => Conflict != ConflictKind.None;

    /// <summary>Stage'lenmiş bir değişiklik var mı?</summary>
    public bool IsStaged =>
        StagedChange is not (FileChangeKind.Unmodified or FileChangeKind.Unmerged);

    /// <summary>Stage'lenmemiş bir değişiklik var mı?</summary>
    public bool IsUnstaged =>
        IsUntracked
        || UnstagedChange is not (FileChangeKind.Unmodified or FileChangeKind.Unmerged);

    public override string ToString() => Path.Value;
}

/// <summary>
/// Çalışma dizininin bütün durumu.
/// </summary>
public sealed record WorkingTreeStatus
{
    /// <summary>Mevcut commit; doğmamış depoda boş.</summary>
    public CommitId Head { get; init; }

    /// <summary>Mevcut dal; detached ise <see langword="null"/>.</summary>
    public string? BranchName { get; init; }

    public bool IsDetached { get; init; }

    /// <summary>Henüz hiç commit yok mu (<c># branch.oid (initial)</c>)?</summary>
    public bool IsUnborn { get; init; }

    public string? Upstream { get; init; }

    /// <summary>Upstream'e göre konum; <c># branch.ab</c> başlığından.</summary>
    public UpstreamTracking Tracking { get; init; } = UpstreamTracking.None;

    public required IReadOnlyList<FileStatus> Entries { get; init; }

    public IEnumerable<FileStatus> Staged => Entries.Where(e => e.IsStaged);

    public IEnumerable<FileStatus> Unstaged => Entries.Where(e => e.IsUnstaged && !e.IsUntracked);

    public IEnumerable<FileStatus> Untracked => Entries.Where(e => e.IsUntracked);

    public IEnumerable<FileStatus> Conflicted => Entries.Where(e => e.IsConflicted);

    public IEnumerable<FileStatus> Ignored => Entries.Where(e => e.IsIgnored);

    /// <summary>Commit edilmemiş hiçbir değişiklik yok mu?</summary>
    public bool IsClean => !Entries.Any(e => !e.IsIgnored);
}
