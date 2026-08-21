using GitExt.UI.Settings;

namespace GitExt.UI.Updates;

/// <summary>The outcome of a version check (P13-T01).</summary>
public sealed record UpdateCheckResult
{
    /// <summary>The newer release, or <see langword="null"/> when there is none.</summary>
    public ReleaseNote? Update { get; init; }

    /// <summary>Was the check actually performed?</summary>
    /// <remarks>
    /// <see langword="false"/> when the setting is off or the weekly interval has not elapsed —
    /// which is a different thing from "checked and found nothing", and the interface says so
    /// differently.
    /// </remarks>
    public bool Checked { get; init; }

    public bool HasUpdate => Update is not null;

    public static UpdateCheckResult Skipped { get; } = new();
}

/// <summary>
/// Tells the user when a newer version has been published (P13-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no automatic update.</b> Nothing is downloaded, nothing is run: what happens is a
/// single public GET to the release page and, at most, a line on screen. Whether to install is the
/// user's business, and on Linux it is usually their package manager's.
/// </para>
/// <para>
/// The cadence and the setting are GitExtensions': it checks <b>once a week</b>
/// (<c>LastUpdateCheck.AddDays(7)</c>) and can be switched off (<c>CheckForUpdates</c>, on by
/// default). Checking on every start would mean a request every time the application opens, for
/// news that changes a few times a year.
/// </para>
/// </remarks>
public sealed class UpdateService
{
    /// <summary>How long a check lasts before another one is due.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(7);

    private readonly IReleaseFeed _feed;
    private readonly ISettingsStore _settings;
    private readonly string _currentVersion;
    private readonly Func<DateTimeOffset> _now;

    public UpdateService(
        IReleaseFeed feed,
        ISettingsStore settings,
        string currentVersion,
        Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(settings);

        _feed = feed;
        _settings = settings;
        _currentVersion = currentVersion ?? string.Empty;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>
    /// Checks whether a newer version exists.
    /// </summary>
    /// <param name="userRequested">
    /// <see langword="true"/> when the user asked through the menu. Then neither the setting nor
    /// the weekly interval stands in the way: a person who clicked "check for updates" is owed an
    /// answer.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UpdateCheckResult> CheckAsync(
        bool userRequested,
        CancellationToken cancellationToken = default)
    {
        if (!userRequested && !IsDue())
        {
            return UpdateCheckResult.Skipped;
        }

        // The stamp is written BEFORE the request: an unreachable network must not turn into a
        // request on every single start.
        _settings.Update(s => s.General.LastUpdateCheck = _now().ToString("O"));

        ReleaseNote? latest = await _feed.GetLatestAsync(cancellationToken).ConfigureAwait(true);

        return new UpdateCheckResult
        {
            Checked = true,
            Update = IsNewer(latest) ? latest : null,
        };
    }

    /// <summary>Is a check due? (The setting is on and a week has passed.)</summary>
    public bool IsDue()
    {
        if (!_settings.Current.General.CheckForUpdates)
        {
            return false;
        }

        string stamp = _settings.Current.General.LastUpdateCheck;

        if (string.IsNullOrWhiteSpace(stamp)
            || !DateTimeOffset.TryParse(stamp, out DateTimeOffset last))
        {
            // Never checked, or the stamp is unreadable: check.
            return true;
        }

        return _now() - last >= CheckInterval;
    }

    /// <summary>
    /// Is the published release newer than what is running?
    /// </summary>
    /// <remarks>
    /// 🔴 Both sides have to be readable. A build whose version cannot be parsed says nothing:
    /// announcing an update that may not apply is worse than staying quiet. And because a
    /// pre-release ranks BELOW the release of the same number, someone on <c>0.1.2-alpha.3</c> is
    /// correctly told about <c>0.1.2</c>.
    /// </remarks>
    private bool IsNewer(ReleaseNote? latest)
    {
        if (latest is null)
        {
            return false;
        }

        return ReleaseVersion.TryParse(latest.Version, out ReleaseVersion? published)
            && ReleaseVersion.TryParse(_currentVersion, out ReleaseVersion? current)
            && published.CompareTo(current) > 0;
    }
}
