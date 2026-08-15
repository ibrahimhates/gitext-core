using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Detay panelindeki tıklanabilir ebeveyn bağlantısı.
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
/// Seçili commit'in tam bilgisi (P03-T15).
/// </summary>
/// <remarks>
/// <para>
/// Listedeki satır özeti gösterir; burası "her şey" panelidir: tam SHA, yazar ve kaydeden
/// <b>ayrı ayrı</b>, tarihler hem yerel hem <b>yazarın kendi saat diliminde</b>, tam mesaj,
/// tıklanabilir ebeveynler, ref rozetleri ve imza durumu.
/// </para>
/// <para>
/// <b>İmza ayrıca ve gecikmeli okunur.</b> Ölçüldü: <c>%G?</c> alanını toplu <c>git log</c>
/// formatına eklemek 2.000 imzasız commit'te okumayı %72 yavaşlatıyor. Ayrıca kullanıcı
/// <c>↓</c> tuşuna basılı tutarak yüzlerce satır geçebilir; her satır için bir <c>git</c>
/// süreci başlatmamak adına okuma <see cref="_signatureDelay"/> kadar bekletiliyor ve seçim
/// değişince iptal ediliyor.
/// </para>
/// </remarks>
public sealed partial class CommitDetailsViewModel : ViewModelBase
{
    /// <summary>
    /// İmza okumadan önce beklenen süre.
    /// </summary>
    /// <remarks>
    /// Listede hızlıca gezinirken hiç <c>git</c> süreci başlatılmamasını sağlar. İnsan gözüyle
    /// fark edilmeyecek kadar kısa, tuş tekrarını elemeye yetecek kadar uzun.
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

        // Tek komut örneği tüm ebeveyn bağlantılarınca paylaşılıyor; her satır için yeni
        // komut üretmek gereksiz tahsis olurdu.
        _goToParentCommand = new RelayCommand<CommitId>(id => _navigate(id));
    }

    /// <summary>Gösterilecek bir commit var mı?</summary>
    [ObservableProperty]
    public partial bool HasCommit { get; private set; }

    [ObservableProperty]
    public partial string FullId { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Subject { get; private set; } = string.Empty;

    /// <summary>Mesajın başlık dışındaki gövdesi.</summary>
    [ObservableProperty]
    public partial string Body { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasBody { get; private set; }

    [ObservableProperty]
    public partial string AuthorText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string AuthorDate { get; private set; } = string.Empty;

    /// <summary>Yazarın kendi saat dilimindeki tarih; yerelle aynıysa boş.</summary>
    [ObservableProperty]
    public partial string? AuthorOriginalDate { get; private set; }

    [ObservableProperty]
    public partial string CommitterText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CommitterDate { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? CommitterOriginalDate { get; private set; }

    /// <summary>
    /// Kaydeden yazardan farklı mı?
    /// </summary>
    /// <remarks>
    /// Rebase, cherry-pick ve yamalarda ayrışır. Vurgulanmazsa kullanıcı iki özdeş satır
    /// görür ve farkın ne zaman anlamlı olduğunu bilemez.
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

    /// <summary>İmza durumunun kısa açıklaması; okunuyorsa veya imza yoksa boş.</summary>
    [ObservableProperty]
    public partial string? SignatureText { get; private set; }

    /// <summary>İmzalayan/anahtar ya da doğrulanamama sebebi.</summary>
    [ObservableProperty]
    public partial string? SignatureDetail { get; private set; }

    /// <summary>İmza doğrulandı ve güvenilir.</summary>
    [ObservableProperty]
    public partial bool SignatureIsTrusted { get; private set; }

    /// <summary>İmzada sorun var (hatalı, süresi dolmuş, iptal, doğrulanamadı).</summary>
    [ObservableProperty]
    public partial bool SignatureIsProblem { get; private set; }

    /// <summary>
    /// Paneli verilen satıra göre günceller.
    /// </summary>
    /// <param name="row">Seçili satır; seçim yoksa <see langword="null"/>.</param>
    /// <param name="workingDirectory">İmza okumak için depo yolu; yoksa imza okunmaz.</param>
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
            // Kullanıcı başka bir commit seçti; hata değil.
        }
        catch (GitException)
        {
            // İmza yardımcı bilgidir. Okunamaması commit'in geri kalanını göstermemek için
            // sebep değil — panel imza satırı olmadan kalır.
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
            SignatureStatus.Valid => "Signature verified",
            SignatureStatus.ValidUntrusted => "Signature valid, key not marked as trusted",
            SignatureStatus.Bad => "Signature INVALID",
            SignatureStatus.Expired => "The signature has expired",
            SignatureStatus.KeyExpired => "The signing key has expired",
            SignatureStatus.KeyRevoked => "The signing key was revoked",
            _ => "Signature could not be verified",
        };

        SignatureDetail = signature.CannotVerifyReason
            ?? string.Join(" · ", new[] { signature.Signer, signature.Key }.Where(s => !string.IsNullOrEmpty(s)));

        if (string.IsNullOrWhiteSpace(SignatureDetail))
        {
            SignatureDetail = null;
        }
    }

    /// <summary>
    /// Gösterilen commit'e ait her şeyi bırakır (P09-T10).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Yalnızca <see cref="HasCommit"/>'i kapatmak yetmiyordu.</b> Panel gizlenirken
    /// <see cref="Badges"/> ve <see cref="Parents"/> kapatılan deponun nesnelerini
    /// tutmaya devam ediyordu; rozet listesi satırın kendisine ait olduğu için satır da,
    /// dolayısıyla commit'i de bellekte kalıyordu. Depolar arasında geçen uzun bir
    /// oturumda her geçiş bir öncekini biriktirirdi.
    /// <para>
    /// Görünmeyen bir panelin eski veriyi tutması gözle fark edilmiyor — sızıntıyı
    /// bulan şey <c>MemoryStressTests</c>'in zayıf referans ölçümü oldu.
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
    /// Tarihi yazarın kendi saat diliminde biçimler; yerel ofsetle aynıysa
    /// <see langword="null"/> döner.
    /// </summary>
    /// <remarks>
    /// Aynı olduğunda göstermek her satırı iki kez yazmak olurdu. Ofset commit'e özeldir:
    /// aynı deponun commit'leri farklı dilimlerde atılmış olabilir, bu yüzden karşılaştırma
    /// <b>o anın</b> yerel ofsetiyle yapılıyor (yaz saati kayması dahil).
    /// </remarks>
    private static string? FormatOriginalIfDifferent(DateTimeOffset value)
    {
        TimeSpan localOffset = TimeZoneInfo.Local.GetUtcOffset(value);

        return value.Offset == localOffset
            ? null
            : value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture);
    }
}
