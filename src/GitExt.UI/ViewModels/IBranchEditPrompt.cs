namespace GitExt.UI.ViewModels;

/// <summary>
/// Dal yeniden adlandırma diyaloğuna verilen bağlam (P06-T03).
/// </summary>
public sealed record RenameBranchRequest
{
    public required string CurrentName { get; init; }
}

/// <summary>
/// Yeniden adlandırma kararı (P06-T03).
/// </summary>
public sealed record RenameBranchDecision
{
    public bool Confirmed { get; init; }

    public string NewName { get; init; } = string.Empty;

    public static RenameBranchDecision Cancelled { get; } = new();
}

/// <summary>
/// Dal silme diyaloğuna verilen bağlam (P06-T03).
/// </summary>
public sealed record DeleteBranchRequest
{
    public required string Name { get; init; }

    /// <summary>
    /// Dal birleştirilmemiş mi? Yalnızca ikinci turda (git reddettikten sonra) doğru olur.
    /// </summary>
    /// <remarks>
    /// 🔴 Bu bilgiyi <b>kendimiz hesaplamıyoruz</b>: ölçüldü, <c>git branch -d</c> dalı
    /// HEAD'e değil <b>upstream'ine</b> birleşmiş olsa da siliyor. Kendi hesabımız bu
    /// dallarda yanlış alarm üretirdi. Karar git'in.
    /// </remarks>
    public bool IsUnmerged { get; init; }

    /// <summary>Dalın ucu — kurtarma yolu olarak gösterilir.</summary>
    public string? LastCommitId { get; init; }
}

/// <summary>
/// Silme kararı (P06-T03).
/// </summary>
public sealed record DeleteBranchDecision
{
    public bool Confirmed { get; init; }

    /// <summary>Birleştirilmemiş dal da silinsin mi?</summary>
    public bool Force { get; init; }

    public static DeleteBranchDecision Cancelled { get; } = new();
}

/// <summary>
/// Dal düzenleme diyaloglarını gösteren taraf (P06-T03).
/// </summary>
public interface IBranchEditPrompt
{
    Task<RenameBranchDecision> RequestRenameAsync(RenameBranchRequest request);

    Task<DeleteBranchDecision> RequestDeleteAsync(DeleteBranchRequest request);
}
