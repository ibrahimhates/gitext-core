using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A clickable parent link in the details panel.
/// </summary>
public sealed class ParentLink
{
    public ParentLink(CommitId id, ICommand command)
    {
        Id = id;
        Command = command;
        Text = id.ToShortString();
    }

    public CommitId Id { get; }

    public string Text { get; }

    public ICommand Command { get; }

    public override string ToString() => Text;
}

/// <summary>
/// The full information about the selected commit (P03-T15).
/// </summary>
/// <remarks>
/// <para>
/// The row in the list shows a summary; this is the "everything" panel: the full SHA, author and
/// committer <b>separately</b>, the dates both locally and <b>in the author's own time zone</b>, the
/// full message, clickable parents, ref badges and the signature status.
/// </para>
/// <para>
/// <b>The signature is read separately and with a delay.</b> Measured: adding the <c>%G?</c> field to
/// the bulk <c>git log</c> format slows the read by 72% over 2,000 unsigned commits. On top of that
/// the user can hold <c>↓</c> down and pass hundreds of rows; to avoid starting a <c>git</c> process
/// for every one of them, the read waits <see cref="_signatureDelay"/> and is cancelled when the
/// selection changes.
/// </para>
/// </remarks>
public sealed partial class CommitDetailsViewModel : ViewModelBase
{
    /// <summary>
    /// How long to wait before reading the signature.
    /// </summary>
    /// <remarks>
    /// Ensures no <c>git</c> process is started at all while scrolling quickly through the list.
    /// Short enough to go unnoticed by the human eye, long enough to filter out key repeat.
    /// </remarks>
    private static readonly TimeSpan _signatureDelay = TimeSpan.FromMilliseconds(150);

    private readonly ICommitSignatureReader _signatureReader;
    private readonly Func<CommitId, bool> _navigate;
    private readonly ICommand _goToParentCommand;

    private CancellationTokenSource? _signatureLoad;

    public CommitDetailsViewModel(ICommitSignatureReader signatureReader, Func<CommitId, bool> navigate)
    {
        ArgumentNullException.ThrowIfNull(signatureReader);
        ArgumentNullException.ThrowIfNull(navigate);

        _signatureReader = signatureReader;
        _navigate = navigate;

        // A single command instance is shared by all the parent links; producing a new command for
        // every row would be a needless allocation.
        _goToParentCommand = new RelayCommand<CommitId>(id => _navigate(id));
    }

    /// <summary>Is there a commit to show?</summary>
    [ObservableProperty]
    public partial bool HasCommit { get; private set; }

    [ObservableProperty]
    public partial string FullId { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Subject { get; private set; } = string.Empty;

    /// <summary>The body of the message, excluding the subject.</summary>
    [ObservableProperty]
    public partial string Body { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasBody { get; private set; }

    [ObservableProperty]
    public partial string AuthorText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string AuthorDate { get; private set; } = string.Empty;

    /// <summary>The date in the author's own time zone; empty when it matches the local one.</summary>
    [ObservableProperty]
    public partial string? AuthorOriginalDate { get; private set; }

    /// <summary>The translated text of the author's time zone note (P11-T05).</summary>
    public string? AuthorOriginalDateText => AuthorOriginalDate is null
        ? null
        : Loc.F("commit_details.in_the_authors_time_zone", AuthorOriginalDate);

    partial void OnAuthorOriginalDateChanged(string? value) =>
        OnPropertyChanged(nameof(AuthorOriginalDateText));

    [ObservableProperty]
    public partial string CommitterText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CommitterDate { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? CommitterOriginalDate { get; private set; }

    /// <summary>The translated text of the committer's time zone note (P11-T05).</summary>
    public string? CommitterOriginalDateText => CommitterOriginalDate is null
        ? null
        : Loc.F("commit_details.in_their_own_time_zone", CommitterOriginalDate);

    partial void OnCommitterOriginalDateChanged(string? value) =>
        OnPropertyChanged(nameof(CommitterOriginalDateText));

    /// <summary>
    /// Is the committer different from the author?
    /// </summary>
    /// <remarks>
    /// They diverge on rebases, cherry-picks and patches. Without highlighting it, the user sees two
    /// identical lines and cannot tell when the difference is meaningful.
    /// </remarks>
    [ObservableProperty]
    public partial bool CommitterDiffersFromAuthor { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ParentLink> Parents { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasParents { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<RefBadge> Badges { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasBadges { get; private set; }

    /// <summary>A short description of the signature status; empty while reading or when unsigned.</summary>
    [ObservableProperty]
    public partial string? SignatureText { get; private set; }

    /// <summary>The signer/key, or the reason it could not be verified.</summary>
    [ObservableProperty]
    public partial string? SignatureDetail { get; private set; }

    /// <summary>The signature was verified and is trusted.</summary>
    [ObservableProperty]
    public partial bool SignatureIsTrusted { get; private set; }

    /// <summary>There is a problem with the signature (bad, expired, revoked, unverifiable).</summary>
    [ObservableProperty]
    public partial bool SignatureIsProblem { get; private set; }

    /// <summary>
    /// Updates the panel for the given row.
    /// </summary>
    /// <param name="row">The selected row; <see langword="null"/> when there is no selection.</param>
    /// <param name="workingDirectory">The repository path used to read the signature; without it, no
    /// signature is read.</param>
    public void Show(CommitRowViewModel? row, string? workingDirectory)
    {
        CancelSignatureLoad();
        ClearSignature();

        if (row is null)
        {
            Clear();
            return;
        }

        CommitInfo commit = row.Commit;

        HasCommit = true;
        FullId = commit.Id.Value;
        Subject = commit.Subject;
        Body = commit.Body;
        HasBody = !string.IsNullOrWhiteSpace(commit.Body);

        AuthorText = Describe(commit.Author);
        AuthorDate = FormatLocal(commit.Author.When);
        AuthorOriginalDate = FormatOriginalIfDifferent(commit.Author.When);

        CommitterText = Describe(commit.Committer);
        CommitterDate = FormatLocal(commit.Committer.When);
        CommitterOriginalDate = FormatOriginalIfDifferent(commit.Committer.When);

        CommitterDiffersFromAuthor =
            commit.Committer.Name != commit.Author.Name
            || commit.Committer.Email != commit.Author.Email
            || commit.Committer.When != commit.Author.When;

        Parents = BuildParents(commit.Parents);
        HasParents = Parents.Count > 0;

        Badges = row.Badges;
        HasBadges = row.HasBadges;

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            _signatureLoad = new CancellationTokenSource();
            _ = LoadSignatureAsync(workingDirectory, commit.Id, _signatureLoad.Token);
        }
    }

    private IReadOnlyList<ParentLink> BuildParents(IReadOnlyList<CommitId> parents)
    {
        if (parents.Count == 0)
        {
            return [];
        }

        ParentLink[] links = new ParentLink[parents.Count];

        for (int i = 0; i < parents.Count; i++)
        {
            links[i] = new ParentLink(parents[i], _goToParentCommand);
        }

        return links;
    }

    private async Task LoadSignatureAsync(
        string workingDirectory,
        CommitId commit,
        CancellationToken token)
    {
        try
        {
            await Task.Delay(_signatureDelay, token).ConfigureAwait(true);

            CommitSignatureInfo signature = await _signatureReader
                .ReadAsync(workingDirectory, commit, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            ApplySignature(signature);
        }
        catch (OperationCanceledException)
        {
            // The user selected another commit; not an error.
        }
        catch (GitException)
        {
            // The signature is supplementary information. Failing to read it is no reason to hide the
            // rest of the commit — the panel is simply left without the signature line.
        }
    }

    private void ApplySignature(CommitSignatureInfo signature)
    {
        if (!signature.IsSigned)
        {
            ClearSignature();
            return;
        }

        SignatureIsTrusted = signature.IsTrusted;
        SignatureIsProblem = signature.Status is not (SignatureStatus.Valid or SignatureStatus.ValidUntrusted);

        SignatureText = signature.Status switch
        {
            SignatureStatus.Valid => Loc.T("commit_details.signature_verified"),
            SignatureStatus.ValidUntrusted => Loc.T("commit_details.signature_valid_key_not_marked_as_trusted"),
            SignatureStatus.Bad => Loc.T("commit_details.signature_invalid"),
            SignatureStatus.Expired => Loc.T("commit_details.the_signature_has_expired"),
            SignatureStatus.KeyExpired => Loc.T("commit_details.the_signing_key_has_expired"),
            SignatureStatus.KeyRevoked => Loc.T("commit_details.the_signing_key_was_revoked"),
            _ => Loc.T("commit_details.signature_could_not_be_verified"),
        };

        SignatureDetail = signature.CannotVerifyReason
            ?? string.Join(" · ", new[] { signature.Signer, signature.Key }.Where(s => !string.IsNullOrEmpty(s)));

        if (string.IsNullOrWhiteSpace(SignatureDetail))
        {
            SignatureDetail = null;
        }
    }

    /// <summary>
    /// Releases everything belonging to the commit being shown (P09-T10).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Turning <see cref="HasCommit"/> off was not enough on its own.</b> While the panel was
    /// hidden, <see cref="Badges"/> and <see cref="Parents"/> kept holding objects belonging to the
    /// closed repository; because the badge list belongs to the row itself, the row — and therefore
    /// its commit — stayed in memory too. Over a long session moving between repositories, every
    /// switch would accumulate the previous one.
    /// <para>
    /// An invisible panel holding on to old data cannot be spotted by eye — what found the leak was
    /// the weak-reference measurement in <c>MemoryStressTests</c>.
    /// </para>
    /// </remarks>
    private void Clear()
    {
        HasCommit = false;

        FullId = string.Empty;
        Subject = string.Empty;
        Body = string.Empty;
        HasBody = false;

        AuthorText = string.Empty;
        AuthorDate = string.Empty;
        AuthorOriginalDate = null;

        CommitterText = string.Empty;
        CommitterDate = string.Empty;
        CommitterOriginalDate = null;
        CommitterDiffersFromAuthor = false;

        Parents = [];
        HasParents = false;

        Badges = [];
        HasBadges = false;
    }

    private void ClearSignature()
    {
        SignatureText = null;
        SignatureDetail = null;
        SignatureIsTrusted = false;
        SignatureIsProblem = false;
    }

    private void CancelSignatureLoad()
    {
        _signatureLoad?.Cancel();
        _signatureLoad?.Dispose();
        _signatureLoad = null;
    }

    private static string Describe(Signature signature) =>
        string.IsNullOrEmpty(signature.Email)
            ? signature.Name
            : $"{signature.Name} <{signature.Email}>";

    private static string FormatLocal(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats the date in the author's own time zone; returns <see langword="null"/> when it matches
    /// the local offset.
    /// </summary>
    /// <remarks>
    /// Showing it when they match would be writing every line twice. The offset is specific to the
    /// commit: commits in the same repository may have been made in different zones, so the
    /// comparison is made against the local offset <b>at that moment</b> (daylight saving shifts
    /// included).
    /// </remarks>
    private static string? FormatOriginalIfDifferent(DateTimeOffset value)
    {
        TimeSpan localOffset = TimeZoneInfo.Local.GetUtcOffset(value);

        return value.Offset == localOffset
            ? null
            : value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture);
    }
}
