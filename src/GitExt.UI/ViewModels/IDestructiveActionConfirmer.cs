using GitExt.Core;
using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Sıfırlama onayı için kullanıcıya sorulan soru (P05-T15).
/// </summary>
/// <remarks>
/// Sayılar diyalogda gösteriliyor: "emin misiniz?" sorusu, neyin gideceğini söylemeden
/// sorulduğunda kullanıcıyı düşünmeye değil tıklamaya yönlendirir.
/// </remarks>
public sealed record ResetChangesRequest
{
    /// <summary>Değişiklikleri atılacak izlenen dosyalar.</summary>
    public required IReadOnlyList<RepositoryPath> ModifiedPaths { get; init; }

    /// <summary>Silinebilecek takip edilmeyen dosyalar.</summary>
    public required IReadOnlyList<RepositoryPath> UntrackedPaths { get; init; }

    /// <summary>Stage'lenmiş içerik de atılacak mı (<see cref="DiscardScope.All"/>)?</summary>
    public required bool IncludesStaged { get; init; }

    /// <summary>
    /// Bu işlem için "bir daha sorma" sunulabilir mi?
    /// </summary>
    /// <remarks>
    /// Yalnızca <b>yedeklenebilen</b> işlemlerde <see langword="true"/>. Yedeği olmayan bir
    /// işlemde bu seçeneği sunmak, kullanıcının bir daha asla uyarılmayacağı bir veri kaybı
    /// yolunu açmak olurdu.
    /// </remarks>
    public required bool CanSuppress { get; init; }
}

/// <summary>Kullanıcının sıfırlama onayına verdiği cevap.</summary>
public sealed record ResetChangesDecision
{
    /// <summary>Kullanıcı iptal etti.</summary>
    public static ResetChangesDecision Cancelled { get; } = new();

    /// <summary>İşlem onaylandı mı?</summary>
    public bool Confirmed { get; init; }

    /// <summary>Takip edilmeyen dosyalar da silinsin mi?</summary>
    public bool DeleteUntracked { get; init; }

    /// <summary>Bu işlem bir daha sorulmasın.</summary>
    public bool DoNotAskAgain { get; init; }
}

/// <summary>
/// Yıkıcı işlemler için kullanıcı onayı alır (P05-T15).
/// </summary>
/// <remarks>
/// Arayüz olarak ayrıldı: ViewModel testleri gerçek bir pencere açmadan "kullanıcı iptal
/// etti" / "onayladı" senaryolarını çalıştırabilsin. Aynı desen P05-T09'daki
/// <see cref="IPartialStagingHost"/> ile kurulmuştu.
/// </remarks>
public interface IDestructiveActionConfirmer
{
    /// <summary>Sıfırlama onayı ister.</summary>
    Task<ResetChangesDecision> ConfirmResetAsync(ResetChangesRequest request);
}
