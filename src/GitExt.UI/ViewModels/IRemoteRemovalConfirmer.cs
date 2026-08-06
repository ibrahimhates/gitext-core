namespace GitExt.UI.ViewModels;

/// <summary>
/// Uzak depo silme onayına verilen bağlam (P06-T05).
/// </summary>
/// <remarks>
/// 🔴 Bu bilgilerin tamamı <b>silmeden önce</b> okunuyor; silme sonrası hiçbiri okunamıyor
/// (config anahtarları siliniyor, uzak izleme dalları ve reflog'ları gidiyor).
/// </remarks>
public sealed record RemoteRemovalRequest
{
    /// <summary>Silinecek uzak deponun adı.</summary>
    public required string Name { get; init; }

    /// <summary>Birlikte silinecek uzak izleme dallarının sayısı.</summary>
    public int TrackingBranchCount { get; init; }

    /// <summary>Upstream'i bu remote'a bakan ve bağlantısını kaybedecek yerel dallar.</summary>
    public IReadOnlyList<string> AffectedBranches { get; init; } = [];

    /// <summary><c>remote.pushDefault</c> bu remote'u gösteriyor mu?</summary>
    public bool IsPushDefault { get; init; }

    /// <summary>
    /// Kullanıcının çalıştırabileceği kurtarma komutları.
    /// </summary>
    /// <remarks>
    /// ⚠️ Bu komutlar <b>nesneleri geri getirmiyor</b>: yalnızca uzak izleme dallarında
    /// duran commit'ler <c>fetch</c> ile geri gelir, yani <b>uzak depo hâlâ erişilebilir
    /// olmalı</b>. Dal silmeden (P06-T03) farkı budur ve diyalog bunu yazıyor.
    /// </remarks>
    public IReadOnlyList<string> RecoveryCommands { get; init; } = [];
}

/// <summary>
/// Uzak depo silme onayını soran taraf (P06-T05).
/// </summary>
public interface IRemoteRemovalConfirmer
{
    /// <summary>Kullanıcı silmeyi onayladı mı?</summary>
    Task<bool> ConfirmAsync(RemoteRemovalRequest request);
}
