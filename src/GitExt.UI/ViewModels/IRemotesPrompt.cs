namespace GitExt.UI.ViewModels;

/// <summary>
/// Uzak depo yönetimi ekranını gösteren taraf (P06-T05).
/// </summary>
/// <remarks>
/// <see cref="ICreateBranchPrompt"/> ile aynı gerekçe: pencere bir sahip istiyor ve o
/// yalnızca görünüm tarafında biliniyor. Silme onayı da buradan geliyor, çünkü onu gösteren
/// de aynı pencere.
/// </remarks>
public interface IRemotesPrompt
{
    /// <summary>Silme onayını soran taraf.</summary>
    IRemoteRemovalConfirmer RemovalConfirmer { get; }

    /// <summary>Yönetim ekranını modal gösterir ve kapanmasını bekler.</summary>
    Task ShowAsync(RemotesViewModel model);
}

/// <summary>
/// Pull / Fetch ekranını gösteren taraf (P06-T06 + P06-T07).
/// </summary>
public interface IPullPrompt
{
    /// <summary>Ekranı modal gösterir ve kapanmasını bekler.</summary>
    Task ShowAsync(PullViewModel model);
}
