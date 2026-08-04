using GitExt.Core;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Dala geçme diyaloğuna verilen bağlam (P06-T02).
/// </summary>
public sealed record CheckoutRequest
{
    /// <summary>Hedef: dal adı veya commit hash'i.</summary>
    public required string Target { get; init; }

    /// <summary>Kullanıcıya gösterilecek hedef açıklaması.</summary>
    public required string TargetLabel { get; init; }

    /// <summary>
    /// Hedef bir dal değil, doğrudan bir commit mi? (detached HEAD olacak)
    /// </summary>
    public bool IsDetached { get; init; }

    /// <summary>Çalışma ağacında kaydedilmemiş değişiklik var mı?</summary>
    /// <remarks>
    /// Yoksa "yerel değişiklikler" grubunun tamamı anlamsız; gösterilmiyor.
    /// </remarks>
    public bool HasLocalChanges { get; init; }
}

/// <summary>
/// Kullanıcının dala geçme diyaloğundaki kararı (P06-T02).
/// </summary>
public sealed record CheckoutDecision
{
    public bool Confirmed { get; init; }

    /// <summary>Yerel değişikliklere ne yapılacak?</summary>
    public LocalChangesAction LocalChanges { get; init; } = LocalChangesAction.Keep;

    /// <summary>İptal edilmiş karar.</summary>
    public static CheckoutDecision Cancelled { get; } = new();
}

/// <summary>
/// Dala geçme diyaloğunu gösteren taraf (P06-T02).
/// </summary>
public interface ICheckoutPrompt
{
    Task<CheckoutDecision> RequestAsync(CheckoutRequest request);
}
