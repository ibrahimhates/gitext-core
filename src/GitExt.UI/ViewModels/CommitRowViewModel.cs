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

    public CommitRowViewModel(
        CommitInfo commit,
        GraphRow graphRow,
        IReadOnlyList<RefBadge> badges,
        GraphRow? previousGraphRow = null,
        bool isHead = false,
        bool isRelative = true,
        bool previousIsRelative = true)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(graphRow);
        ArgumentNullException.ThrowIfNull(badges);

        Commit = commit;
        GraphRow = graphRow;
        Badges = badges;
        PreviousGraphRow = previousGraphRow;
        IsHead = isHead;
        IsRelative = isRelative;
        PreviousIsRelative = previousIsRelative;
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

    /// <summary>
    /// The row above this one — the lines entering from above belong to it (P12-T09).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>This is what fixed the broken graph.</b> A row used to draw only the lines LEAVING it
    /// downwards, and the drawing is clipped to the row's own box, so the half between the previous
    /// row's node and this one was never drawn by anybody: every lane appeared cut off above every
    /// commit. GitExtensions draws each segment through three points — previous centre, this
    /// centre, next centre — so both halves exist and the clipped ends meet exactly.
    /// </remarks>
    public GraphRow? PreviousGraphRow { get; }

    /// <summary>Is this the commit <c>HEAD</c> points at?</summary>
    /// <remarks>
    /// Its node is drawn with an outline — the same mark GitExtensions puts on it. Without it the
    /// question "which branch am I on" has no answer in the graph itself.
    /// </remarks>
    public bool IsHead { get; }

    /// <summary>
    /// Is this commit an ancestor of <c>HEAD</c> (or <c>HEAD</c> itself)?
    /// </summary>
    /// <remarks>
    /// Commits that are not are drawn in grey, as GitExtensions does with
    /// <c>DrawNonRelativesGray</c>: the history you are actually on stays in colour and the other
    /// branches step back.
    /// </remarks>
    public bool IsRelative { get; }

    /// <summary>Whether the row above is relative — it colours the lines coming down from it.</summary>
    public bool PreviousIsRelative { get; }

    public override string ToString() => $"{ShortId} {Subject}";
}
