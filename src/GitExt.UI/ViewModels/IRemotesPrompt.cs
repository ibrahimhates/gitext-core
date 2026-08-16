namespace GitExt.UI.ViewModels;

/// <summary>
/// The side that shows the remote management screen (P06-T05).
/// </summary>
/// <remarks>
/// The same reasoning as <see cref="ICreateBranchPrompt"/>: the window needs an owner and that is only
/// known on the view side. The deletion confirmation comes from here too, because the same window is
/// what shows it.
/// </remarks>
public interface IRemotesPrompt
{
    /// <summary>The side that asks for the deletion confirmation.</summary>
    IRemoteRemovalConfirmer RemovalConfirmer { get; }

    /// <summary>Shows the management screen modally and waits for it to close.</summary>
    Task ShowAsync(RemotesViewModel model);
}

/// <summary>
/// The side that shows the Pull / Fetch screen (P06-T06 + P06-T07).
/// </summary>
public interface IPullPrompt
{
    /// <summary>Ekranı modal gösterir ve kapanmasını bekler.</summary>
    Task ShowAsync(PullViewModel model);
}

/// <summary>
/// The side that shows the push screen (P06-T08).
/// </summary>
public interface IPushPrompt
{
    /// <summary>Ekranı modal gösterir ve kapanmasını bekler.</summary>
    Task ShowAsync(PushViewModel model);
}
