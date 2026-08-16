using System.Globalization;
using GitExt.Core.Model;
using GitExt.Graph;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A single row in the commit list: the commit data plus the graph layout (P03-T11).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately kept <b>light</b>. In a repository of 500 thousand rows, every extra field per row
/// turns into hundreds of megabytes — measured: <see cref="CommitInfo"/> is ~1.1 KB and
/// <see cref="GraphRow"/> ~330 bytes, that is ~700 MB at 500k. This type holds only references to the
/// two and makes no copies.
/// </para>
/// <para>
/// The display strings (<see cref="ShortId"/>, <see cref="DateText"/>) are produced <b>lazily</b>:
/// they are computed only for the rows that reach the screen. Producing them in the constructor would
/// mean hundreds of thousands of strings that are never seen.
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

    /// <summary>This row's lane/colour/edge layout.</summary>
    public GraphRow GraphRow { get; }

    public string ShortId => _shortId ??= Commit.Id.ToShortString();

    public string Subject => Commit.Subject;

    public string AuthorName => Commit.Author.Name;

    /// <summary>
    /// The author date, in local format.
    /// </summary>
    /// <remarks>
    /// The author date is shown, not the committer date — "when was this change written" is what the
    /// user expects. After a rebase the two diverge; the committer date is shown separately in the
    /// details panel (P03-T15).
    /// </remarks>
    public string DateText => _dateText ??=
        Commit.Author.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    /// <summary>
    /// The refs pointing at this commit, <b>with their kinds</b>.
    /// </summary>
    /// <remarks>
    /// <c>CommitInfo.Refs</c> (git's <c>%D</c> field) carries no kind information — a local branch
    /// cannot be told from a remote one. That is why the badges are produced from <c>for-each-ref</c>
    /// data (see <see cref="RefBadgeIndex"/>).
    /// </remarks>
    public IReadOnlyList<RefBadge> Badges { get; }

    public bool HasBadges => Badges.Count > 0;

    public bool IsMerge => Commit.IsMerge;

    public override string ToString() => $"{ShortId} {Subject}";
}
