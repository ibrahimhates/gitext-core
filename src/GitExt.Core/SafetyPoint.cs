using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// Geçmişi değiştiren bir işlemden <b>önceki</b> depo konumu (P07-T15).
/// </summary>
/// <remarks>
/// <para>
/// Faz kuralı: <i>"Geçmişi değiştiren her işlem öncesinde reflog konumu kaydedilir ve
/// kullanıcıya 'nasıl geri alırım' bilgisi her zaman sunulur."</i> Bu tip o bilgiyi
/// taşıyor.
/// </para>
/// </remarks>
public sealed record SafetyPoint
{
    /// <summary>İşlem öncesi <c>HEAD</c> (tam SHA).</summary>
    public required string ObjectId { get; init; }

    /// <summary>Üzerinde bulunulan dal; ayrık <c>HEAD</c> ise <see langword="null"/>.</summary>
    public string? BranchName { get; init; }

    /// <summary>Güvenlik noktasını alan işlemin adı — "rebase", "reset" gibi.</summary>
    public required string Operation { get; init; }

    /// <summary>
    /// Güvenlik noktası alınırken çalışma ağacında commit'lenmemiş değişiklik var mıydı?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — <c>git reset --hard</c> commit'lenmemiş işi de siliyor.</b>
    /// Stage'lenmiş yeni bir dosya (<c>yeni.txt</c>) reset sonrası diskten <b>kayboldu</b>.
    /// Yani ağaç kirliyken "geri almak için: <c>git reset --hard &lt;sha&gt;</c>" demek
    /// <b>eksik</b> bir söz: commit geri gelir, kullanıcının o anki işi gelmez.
    /// <see cref="IsFullyRecoverable"/> bunu ayırıyor.
    /// </remarks>
    public bool HasUncommittedChanges { get; init; }

    public bool IsDetached => BranchName is null;

    /// <summary>Kısaltılmış SHA — ekranda gösterilen.</summary>
    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;

    /// <summary>
    /// Bu noktaya dönmek için çalıştırılacak komut.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>ÖLÇÜLDÜ — ayrık <c>HEAD</c>'de <c>reset --hard</c> hiçbir dalı oynatmıyor</b>,
    /// dal üzerindeyken ise <b>dalı</b> oynatıyor. İkisi de doğru davranış ama farklı; ayrık
    /// durumda kullanıcının istediği şey genelde o commit'e dönmek olduğu için
    /// <c>checkout</c> öneriliyor.
    /// </para>
    /// <para>
    /// ⚠️ <c>ORIG_HEAD</c> ya da <c>HEAD@{1}</c> gibi <b>kayan</b> bir referans değil,
    /// <b>SHA</b> yazılıyor: kullanıcı komutu kopyalayıp sonra çalıştırırsa kayan referans
    /// bambaşka bir yeri gösterirdi.
    /// </para>
    /// </remarks>
    public string RecoveryCommand => IsDetached
        ? $"git checkout {ObjectId}"
        : $"git reset --hard {ObjectId}";

    /// <summary>
    /// Geri alma komutu <b>her şeyi</b> geri getirir mi?
    /// </summary>
    /// <remarks>
    /// Ağaç kirliyken <see langword="false"/>: commit'lenmemiş iş geri alınamaz.
    /// Ekran bunu ayrı bir uyarı olarak gösteriyor, komutu gizlemiyor.
    /// </remarks>
    public bool IsFullyRecoverable => !HasUncommittedChanges;
}

/// <summary>Güvenlik noktası alma (P07-T15).</summary>
public interface ISafetyPointRecorder
{
    /// <summary>
    /// Geçmişi değiştiren bir işlemden hemen önce çağrılır.
    /// </summary>
    Task<SafetyPoint> CaptureAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>HEAD</c>'i ve çalışma ağacının temizliğini kaydeder (P07-T15).
/// </summary>
public sealed class SafetyPointRecorder : ISafetyPointRecorder
{
    private readonly IGitProcessRunner _runner;

    public SafetyPointRecorder(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<SafetyPoint> CaptureAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        GitResult head = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "HEAD"),
            cancellationToken).ConfigureAwait(false);

        // Doğmamış depoda `rev-parse HEAD` başarısız — geri dönülecek bir nokta da yok.
        string objectId = head.IsSuccess ? head.GetStandardOutputText().Trim() : string.Empty;

        GitResult branch = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "symbolic-ref", "--quiet", "--short", "HEAD"),
            cancellationToken).ConfigureAwait(false);

        // Ayrık HEAD'de `symbolic-ref` çıkış kodu 1 veriyor; bu bir hata değil, bir durum.
        string? branchName = branch.IsSuccess
            ? branch.GetStandardOutputText().Trim() is { Length: > 0 } name ? name : null
            : null;

        return new SafetyPoint
        {
            ObjectId = objectId,
            BranchName = branchName,
            Operation = operation,
            HasUncommittedChanges =
                await IsDirtyAsync(workingDirectory, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Çalışma ağacında commit'lenmemiş bir değişiklik var mı?
    /// </summary>
    /// <remarks>
    /// Takip edilmeyen dosyalar <b>sayılmıyor</b>: <c>reset --hard</c> onlara dokunmuyor
    /// (ölçüldü — silinen yalnızca stage'lenmiş/izlenen değişikliklerdi), dolayısıyla
    /// geri alınabilirliği etkilemiyorlar.
    /// </remarks>
    private async Task<bool> IsDirtyAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["status", "--porcelain=v2", "-z", "--untracked-files=no"],
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess && result.GetStandardOutputText().Length > 0;
    }
}
