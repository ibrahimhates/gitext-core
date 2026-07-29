namespace GitExt.Core.Model;

/// <summary>Bir ref'in türü.</summary>
public enum GitRefKind
{
    /// <summary>Tanınmayan ref alanı (<c>refs/stash</c>, <c>refs/notes/…</c> vb.).</summary>
    Other,

    /// <summary><c>refs/heads/…</c></summary>
    LocalBranch,

    /// <summary><c>refs/remotes/…</c></summary>
    RemoteBranch,

    /// <summary><c>refs/tags/…</c></summary>
    Tag,
}

/// <summary>
/// Bir Git referansı (dal, tag, uzak dal).
/// </summary>
public sealed record GitRef
{
    /// <summary>Tam ad, örn. <c>refs/heads/main</c>.</summary>
    public required string FullName { get; init; }

    /// <summary>Kısa ad, örn. <c>main</c> veya <c>origin/main</c>.</summary>
    public required string ShortName { get; init; }

    public required GitRefKind Kind { get; init; }

    /// <summary>
    /// Ref'in doğrudan işaret ettiği nesne.
    /// </summary>
    /// <remarks>
    /// Annotated tag'de bu <b>tag nesnesidir</b>, commit değil. Commit için
    /// <see cref="TargetCommit"/> kullanılmalı.
    /// </remarks>
    public required CommitId ObjectId { get; init; }

    /// <summary>
    /// Ref'in nihai olarak işaret ettiği commit.
    /// </summary>
    /// <remarks>
    /// Annotated tag'lerde <c>%(*objectname)</c> ile çözülür; diğer ref'lerde
    /// <see cref="ObjectId"/> ile aynıdır.
    /// </remarks>
    public required CommitId TargetCommit { get; init; }

    /// <summary>
    /// Annotated (nesneli) tag mi? Hafif tag'ler doğrudan commit'e işaret eder.
    /// </summary>
    public bool IsAnnotatedTag { get; init; }

    /// <summary>
    /// Bu ref sembolikse işaret ettiği ref'in tam adı; değilse <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pratikteki tek yaygın örnek <c>refs/remotes/&lt;uzak&gt;/HEAD</c>: klonlanan her depoda
    /// bulunur ve uzağın varsayılan dalına (<c>refs/remotes/origin/main</c>) işaret eder.
    /// Ayrı bir dalmış gibi gösterilirse kullanıcı aynı commit'te iki özdeş rozet görür.
    /// </para>
    /// <para>
    /// <c>%(symref)</c> alanından okunur; sembolik olmayan ref'lerde <b>boş</b> gelir (ölçüldü).
    /// </para>
    /// </remarks>
    public string? SymbolicTarget { get; init; }

    /// <summary>Başka bir ref'e işaret eden sembolik ref mi?</summary>
    public bool IsSymbolic => SymbolicTarget is not null;

    public override string ToString() => ShortName;

    /// <summary>Tam ada bakarak türü belirler.</summary>
    internal static GitRefKind ClassifyKind(string fullName) => fullName switch
    {
        _ when fullName.StartsWith("refs/heads/", StringComparison.Ordinal) => GitRefKind.LocalBranch,
        _ when fullName.StartsWith("refs/remotes/", StringComparison.Ordinal) => GitRefKind.RemoteBranch,
        _ when fullName.StartsWith("refs/tags/", StringComparison.Ordinal) => GitRefKind.Tag,
        _ => GitRefKind.Other,
    };
}

/// <summary>
/// Bir dalın upstream'ine göre konumu.
/// </summary>
/// <remarks>
/// <c>%(upstream:track)</c> alanından ayrıştırılır. Ölçülen biçimler:
/// <c>[ahead 3, behind 2]</c>, <c>[ahead 1]</c>, <c>[behind 4]</c>, <c>[gone]</c>,
/// ve senkronsa <b>boş</b>.
/// </remarks>
public readonly record struct UpstreamTracking(int Ahead, int Behind, bool IsGone)
{
    /// <summary>Upstream yok veya takip bilgisi okunamadı.</summary>
    public static UpstreamTracking None { get; } = new(0, 0, false);

    /// <summary>Upstream ile aynı noktada mı?</summary>
    public bool IsUpToDate => !IsGone && Ahead == 0 && Behind == 0;

    /// <summary>Hem ileride hem geride — ayrışmış (diverged).</summary>
    public bool IsDiverged => Ahead > 0 && Behind > 0;
}

/// <summary>
/// Yerel veya uzak bir dal.
/// </summary>
public sealed record BranchInfo
{
    public required GitRef Ref { get; init; }

    /// <summary>Bu dal şu an checkout edilmiş mi?</summary>
    /// <remarks>
    /// Detached HEAD durumunda <b>hiçbir dal</b> geçerli değildir — ölçüldü:
    /// <c>%(HEAD)</c> tüm dallar için boşluk döner.
    /// </remarks>
    public bool IsCurrent { get; init; }

    /// <summary>Takip edilen uzak dal (<c>origin/main</c>); yoksa <see langword="null"/>.</summary>
    public string? Upstream { get; init; }

    public UpstreamTracking Tracking { get; init; } = UpstreamTracking.None;

    /// <summary>Uzak depodaki bir dal mı?</summary>
    public bool IsRemote => Ref.Kind == GitRefKind.RemoteBranch;

    public string Name => Ref.ShortName;

    public override string ToString() => Name;
}

/// <summary>Bir tag.</summary>
public sealed record TagInfo
{
    public required GitRef Ref { get; init; }

    /// <summary>Annotated tag'in mesaj başlığı; hafif tag'de commit başlığı.</summary>
    public string Subject { get; init; } = string.Empty;

    public string Name => Ref.ShortName;

    /// <summary>Annotated tag mi (kendi nesnesi, mesajı ve yazarı var)?</summary>
    public bool IsAnnotated => Ref.IsAnnotatedTag;

    public override string ToString() => Name;
}

/// <summary>Yapılandırılmış bir uzak depo.</summary>
public sealed record RemoteInfo
{
    public required string Name { get; init; }

    /// <summary>Veri çekilen URL.</summary>
    public required string FetchUrl { get; init; }

    /// <summary>
    /// Veri gönderilen URL.
    /// </summary>
    /// <remarks>
    /// Genellikle <see cref="FetchUrl"/> ile aynıdır ama <c>remote.&lt;ad&gt;.pushurl</c>
    /// ile farklı olabilir.
    /// </remarks>
    public required string PushUrl { get; init; }

    public override string ToString() => $"{Name} → {FetchUrl}";
}

/// <summary>
/// <c>HEAD</c>'in durumu.
/// </summary>
public sealed record HeadState
{
    /// <summary>
    /// Bir dala değil doğrudan bir commit'e işaret ediyor mu?
    /// </summary>
    public required bool IsDetached { get; init; }

    /// <summary>
    /// Henüz hiç commit yok mu (yeni <c>git init</c>)?
    /// </summary>
    /// <remarks>
    /// Bu durumda <c>HEAD</c> var olmayan bir dala işaret eder ve <c>rev-parse HEAD</c> başarısız
    /// olur. Kullanıcının ilk açtığı depo bu olabilir; çökmemeli.
    /// </remarks>
    public required bool IsUnborn { get; init; }

    /// <summary>Checkout edilmiş dal; detached ise <see langword="null"/>.</summary>
    public string? BranchName { get; init; }

    /// <summary>İşaret edilen commit; doğmamış depoda boş.</summary>
    public CommitId Commit { get; init; }

    public override string ToString() =>
        IsUnborn ? "(doğmamış)" : IsDetached ? $"(detached) {Commit.ToShortString()}" : BranchName!;
}

/// <summary>
/// Bir deponun tüm ref bilgisi — tek okumada.
/// </summary>
public sealed record RepositoryRefs
{
    public required HeadState Head { get; init; }

    public required IReadOnlyList<BranchInfo> LocalBranches { get; init; }

    public required IReadOnlyList<BranchInfo> RemoteBranches { get; init; }

    public required IReadOnlyList<TagInfo> Tags { get; init; }

    public required IReadOnlyList<RemoteInfo> Remotes { get; init; }

    /// <summary>Şu an checkout edilmiş dal; detached veya doğmamışsa <see langword="null"/>.</summary>
    public BranchInfo? CurrentBranch => LocalBranches.FirstOrDefault(b => b.IsCurrent);
}
