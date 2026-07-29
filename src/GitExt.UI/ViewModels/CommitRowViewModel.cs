using System.Globalization;
using GitExt.Core.Model;
using GitExt.Graph;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Commit listesindeki tek bir satır: commit verisi + grafik yerleşimi (P03-T11).
/// </summary>
/// <remarks>
/// <para>
/// Bilinçli olarak <b>hafif</b> tutuldu. 500 bin satırlık bir depoda satır başına her ek alan
/// yüzlerce megabayta dönüşür — ölçüldü: <see cref="CommitInfo"/> ~1,1 KB, <see cref="GraphRow"/>
/// ~330 bayt, yani 500k'da ~700 MB. Bu tip yalnızca ikisine referans tutar, kopya çıkarmaz.
/// </para>
/// <para>
/// Gösterim dizeleri (<see cref="ShortId"/>, <see cref="DateText"/>) <b>tembel</b> üretilir:
/// yalnızca ekrana gelen satırlar için hesaplanır. Yapıcıda üretmek, hiç görülmeyecek
/// yüz binlerce dize demek olurdu.
/// </para>
/// </remarks>
public sealed class CommitRowViewModel
{
    private string? _shortId;
    private string? _dateText;

    public CommitRowViewModel(CommitInfo commit, GraphRow graphRow, IReadOnlyList<RefBadge> badges)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(graphRow);
        ArgumentNullException.ThrowIfNull(badges);

        Commit = commit;
        GraphRow = graphRow;
        Badges = badges;
    }

    public CommitInfo Commit { get; }

    /// <summary>Bu satırın şerit/renk/kenar yerleşimi.</summary>
    public GraphRow GraphRow { get; }

    public string ShortId => _shortId ??= Commit.Id.ToShortString();

    public string Subject => Commit.Subject;

    public string AuthorName => Commit.Author.Name;

    /// <summary>
    /// Yazar tarihi, yerel biçimde.
    /// </summary>
    /// <remarks>
    /// Yazar tarihi gösteriliyor, kaydeden tarihi değil — kullanıcının beklediği "bu değişiklik
    /// ne zaman yazıldı" bilgisi budur. Rebase sonrası ikisi ayrışır; kaydeden tarihi detay
    /// panelinde ayrıca gösterilecek (P03-T15).
    /// </remarks>
    public string DateText => _dateText ??=
        Commit.Author.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    /// <summary>
    /// Bu commit'e işaret eden ref'ler, <b>türleriyle birlikte</b>.
    /// </summary>
    /// <remarks>
    /// <c>CommitInfo.Refs</c> (git'in <c>%D</c> alanı) tür bilgisi taşımıyor — yerel ve uzak
    /// dal ayırt edilemiyor. Rozetler bu yüzden <c>for-each-ref</c> verisinden üretiliyor
    /// (bkz. <see cref="RefBadgeIndex"/>).
    /// </remarks>
    public IReadOnlyList<RefBadge> Badges { get; }

    public bool HasBadges => Badges.Count > 0;

    public bool IsMerge => Commit.IsMerge;

    public override string ToString() => $"{ShortId} {Subject}";
}
