namespace GitExt.UI.ViewModels;

/// <summary>
/// Dal oluşturma diyaloğuna verilen bağlam (P06-T01).
/// </summary>
public sealed record CreateBranchRequest
{
    /// <summary>
    /// Başlangıç noktası (commit hash'i veya ref adı). <see langword="null"/> ise <c>HEAD</c>.
    /// </summary>
    public string? StartPoint { get; init; }

    /// <summary>Kullanıcıya gösterilecek başlangıç noktası açıklaması.</summary>
    public required string StartPointLabel { get; init; }

    /// <summary>
    /// Çalışma ağacında kaydedilmemiş değişiklik var mı?
    /// </summary>
    /// <remarks>
    /// Yalnızca <b>uyarı</b> için: ölçüldü, <c>git switch -c</c> kirli ağaçta değişiklikleri
    /// çoğu zaman taşıyor. Engel değil bilgi.
    /// </remarks>
    public bool HasLocalChanges { get; init; }
}

/// <summary>
/// Kullanıcının dal oluşturma diyaloğundaki kararı (P06-T01).
/// </summary>
public sealed record CreateBranchDecision
{
    /// <summary>Kullanıcı onayladı mı?</summary>
    public bool Confirmed { get; init; }

    /// <summary>Girilen dal adı.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Oluşturduktan sonra dala geçilsin mi?</summary>
    public bool Checkout { get; init; } = true;

    /// <summary>İptal edilmiş karar.</summary>
    public static CreateBranchDecision Cancelled { get; } = new();
}

/// <summary>
/// Dal oluşturma diyaloğunu gösteren taraf (P06-T01).
/// </summary>
/// <remarks>
/// <see cref="IDestructiveActionConfirmer"/> ile aynı gerekçe: diyalog bir sahip pencere
/// istiyor, o da ancak açılış anında biliniyor; ViewModel'in <c>Window</c> tanıması ise
/// katman kuralını kırardı.
/// </remarks>
public interface ICreateBranchPrompt
{
    /// <summary>Diyaloğu gösterir ve kararı döndürür.</summary>
    Task<CreateBranchDecision> RequestAsync(CreateBranchRequest request);
}
