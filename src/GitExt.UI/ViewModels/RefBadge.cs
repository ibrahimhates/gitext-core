using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Commit satırında gösterilen ref rozetinin türü (P03-T12).
/// </summary>
public enum RefBadgeKind
{
    /// <summary>Yerel dal.</summary>
    LocalBranch,

    /// <summary>Uzak takip dalı (<c>origin/main</c>).</summary>
    RemoteBranch,

    /// <summary>Tag (hafif veya annotated).</summary>
    Tag,

    /// <summary><c>HEAD</c> — detached durumda tek başına gösterilir.</summary>
    Head,
}

/// <summary>
/// Bir commit'e işaret eden, türü belli ref.
/// </summary>
/// <param name="Text">Gösterilecek kısa ad.</param>
/// <param name="Kind">Rozet türü — görsel stil buna göre seçilir.</param>
/// <param name="IsCurrent">Checkout edilmiş dal mı?</param>
public sealed record RefBadge(string Text, RefBadgeKind Kind, bool IsCurrent)
{
    public bool IsLocalBranch => Kind == RefBadgeKind.LocalBranch;

    public bool IsRemoteBranch => Kind == RefBadgeKind.RemoteBranch;

    public bool IsTag => Kind == RefBadgeKind.Tag;

    public bool IsHead => Kind == RefBadgeKind.Head;

    public override string ToString() => Text;
}

/// <summary>
/// Commit kimliğinden rozetlere eşleme.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden <c>%D</c> ayrıştırılmıyor?</b> <c>git log</c>'un <c>%D</c> alanı ölçüldü ve
/// tür bilgisi vermiyor: yerel dal <c>ikinci</c>, uzak dal <c>origin/main</c> olarak geliyor —
/// ikisi de çıplak isim. Bunları ayırmak için uzak adlarını bilip önek eşleştirmek gerekirdi,
/// yani tahmin yürütmek. (<c>tag:</c> öneki var, stash ise <c>refs/stash</c> olarak tam adla
/// geliyor — biçim tutarsız.)
/// </para>
/// <para>
/// <c>for-each-ref</c> ise <b>yetkili tam adı</b> veriyor (<c>refs/heads/…</c>,
/// <c>refs/remotes/…</c>, <c>refs/tags/…</c>) ve annotated tag'lerde hedef commit'i
/// çözüyor. Rozetler oradan üretiliyor.
/// </para>
/// </remarks>
public sealed class RefBadgeIndex
{
    private static readonly IReadOnlyList<RefBadge> _noBadges = [];

    private readonly Dictionary<CommitId, List<RefBadge>> _byCommit = [];

    /// <summary>Boş dizin — depo açılmadan veya ref okuması başarısız olduğunda kullanılır.</summary>
    public static RefBadgeIndex Empty { get; } = new();

    /// <summary>
    /// Ref okumasından rozet dizini üretir.
    /// </summary>
    public static RefBadgeIndex Build(RepositoryRefs refs)
    {
        ArgumentNullException.ThrowIfNull(refs);

        RefBadgeIndex index = new();

        // Detached HEAD: hiçbir dal geçerli değil, HEAD kendi rozetini alır.
        if (refs.Head is { IsDetached: true, Commit.IsEmpty: false })
        {
            index.Add(refs.Head.Commit, new RefBadge("HEAD", RefBadgeKind.Head, IsCurrent: true));
        }

        foreach (BranchInfo branch in refs.LocalBranches)
        {
            index.Add(
                branch.Ref.TargetCommit,
                new RefBadge(branch.Name, RefBadgeKind.LocalBranch, branch.IsCurrent));
        }

        foreach (BranchInfo branch in refs.RemoteBranches)
        {
            // origin/HEAD gibi sembolik ref'ler atlanır: klonlanan HER depoda bulunurlar,
            // işaret ettikleri dalla aynı commit'i gösterirler ve yan yana iki özdeş rozet
            // üretirler. İsim tahminiyle değil git'in %(symref) alanıyla ayırt ediliyor.
            if (branch.Ref.IsSymbolic)
            {
                continue;
            }

            index.Add(
                branch.Ref.TargetCommit,
                new RefBadge(branch.Name, RefBadgeKind.RemoteBranch, IsCurrent: false));
        }

        foreach (TagInfo tag in refs.Tags)
        {
            // Annotated tag'de TargetCommit çözülmüş commit'tir, tag nesnesi değil —
            // aksi halde rozet grafikte yanlış satıra düşerdi.
            index.Add(tag.Ref.TargetCommit, new RefBadge(tag.Name, RefBadgeKind.Tag, IsCurrent: false));
        }

        return index;
    }

    /// <summary>Bu commit'e işaret eden rozetler; yoksa boş liste.</summary>
    public IReadOnlyList<RefBadge> For(CommitId commit) =>
        _byCommit.TryGetValue(commit, out List<RefBadge>? badges) ? badges : _noBadges;

    public int Count => _byCommit.Count;

    private void Add(CommitId commit, RefBadge badge)
    {
        if (commit.IsEmpty)
        {
            return;
        }

        if (!_byCommit.TryGetValue(commit, out List<RefBadge>? badges))
        {
            badges = [];
            _byCommit[commit] = badges;
        }

        // Sıralama: geçerli dal önce, sonra HEAD, dal, uzak dal, tag.
        // Kullanıcının en çok umursadığı bilgi solda dursun.
        badges.Add(badge);
        badges.Sort(static (a, b) => Rank(a).CompareTo(Rank(b)));
    }

    private static int Rank(RefBadge badge) => badge switch
    {
        { IsCurrent: true } => 0,
        { Kind: RefBadgeKind.Head } => 1,
        { Kind: RefBadgeKind.LocalBranch } => 2,
        { Kind: RefBadgeKind.RemoteBranch } => 3,
        _ => 4,
    };
}
