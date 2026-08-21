namespace GitExt.UI.Settings;

/// <summary>
/// Writes and reads what is remembered between sessions (P08-T16).
/// </summary>
/// <remarks>
/// <para>
/// A separate class because <b>what is remembered</b> is a product decision, not a matter of the
/// settings file's format. Three things are kept here: the last repository opened, the last selected
/// commit in it, and the window's size (the last of those in <c>MainWindow.Layout</c>, since that is
/// where the window is known).
/// </para>
/// <para>
/// <b>The selected commit is stored per repository.</b> Keeping a single "last selected commit" is
/// meaningless for a user who switches repositories — and the SHA would never be found in another
/// repository, so restoring it would silently do nothing.
/// </para>
/// </remarks>
public sealed class SessionTracker
{
    /// <summary>
    /// The maximum number of selection records stored per repository.
    /// </summary>
    /// <remarks>
    /// Left unbounded, the settings file would grow over time with a record for every repository the user
    /// ever opened. It is kept the same size as the recent-repositories list: there is no way to reach an
    /// older repository's selection anyway.
    /// </remarks>
    public const int MaximumTrackedRepositories = 12;

    private readonly ISettingsStore _settings;

    public SessionTracker(ISettingsStore settings) => _settings = settings;

    /// <summary>The repository open at shutdown; empty when there is none.</summary>
    public string LastRepository => _settings.Current.Session.LastRepository;

    /// <summary>
    /// Should that repository be reopened at startup (P12-T04)?
    /// </summary>
    /// <remarks>
    /// Off by default: the application starts on the dashboard, like GitExtensions. The repository
    /// is still remembered while the setting is off — turning it on later brings back the last one
    /// rather than starting from nothing.
    /// </remarks>
    public bool StartWithLastRepository => _settings.Current.General.StartWithRecentWorkingDir;

    public void RememberRepository(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _settings.Update(s => s.Session.LastRepository = workingDirectory);
    }

    /// <summary>Called when a repository is closed; the welcome screen appears on the next start.</summary>
    public void ForgetRepository() =>
        _settings.Update(s => s.Session.LastRepository = "");

    public string? SelectedCommit(string workingDirectory) =>
        _settings.Current.Session.SelectedCommits.GetValueOrDefault(workingDirectory);

    public void RememberSelectedCommit(string workingDirectory, string sha)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(sha))
        {
            return;
        }

        _settings.Update(s =>
        {
            s.Session.SelectedCommits[workingDirectory] = sha;

            if (s.Session.SelectedCommits.Count <= MaximumTrackedRepositories)
            {
                return;
            }

            // There is no ordering information; to preserve the most recently written one, an arbitrary
            // record other than the current repository is dropped. Keeping a proper LRU would mean adding
            // a timestamp to the settings file — too high a price for a remembered selection.
            string? victim = s.Session.SelectedCommits.Keys
                .FirstOrDefault(k => !string.Equals(k, workingDirectory, StringComparison.Ordinal));

            if (victim is not null)
            {
                s.Session.SelectedCommits.Remove(victim);
            }
        });
    }
}
