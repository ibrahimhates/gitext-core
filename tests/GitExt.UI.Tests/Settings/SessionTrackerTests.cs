using GitExt.UI.Settings;

namespace GitExt.UI.Tests.Settings;

/// <summary>
/// P08-T16 — session persistence.
/// </summary>
public class SessionTrackerTests
{
    private static (SessionTracker Tracker, InMemorySettingsStore Settings) Create()
    {
        InMemorySettingsStore settings = new();

        return (new SessionTracker(settings), settings);
    }

    [Fact]
    public void Acilan_depo_hatirlaniyor()
    {
        (SessionTracker tracker, _) = Create();

        tracker.RememberRepository("/depo");

        tracker.LastRepository.ShouldBe("/depo");
    }

    /// <summary>
    /// If the repository was closed deliberately it is <b>forgotten</b>.
    /// </summary>
    /// <remarks>
    /// When the user says "close" they mean the welcome screen on the next start; reopening the
    /// same repository would be ignoring that decision.
    /// </remarks>
    [Fact]
    public void Kapatilan_depo_unutuluyor()
    {
        (SessionTracker tracker, _) = Create();
        tracker.RememberRepository("/depo");

        tracker.ForgetRepository();

        tracker.LastRepository.ShouldBeEmpty();
    }

    [Fact]
    public void Secili_commit_depo_basina_saklaniyor()
    {
        (SessionTracker tracker, _) = Create();

        tracker.RememberSelectedCommit("/a", "aaa111");
        tracker.RememberSelectedCommit("/b", "bbb222");

        tracker.SelectedCommit("/a").ShouldBe("aaa111");
        tracker.SelectedCommit("/b").ShouldBe("bbb222");
        tracker.SelectedCommit("/c").ShouldBeNull();
    }

    [Fact]
    public void Bos_deger_kaydedilmiyor()
    {
        (SessionTracker tracker, InMemorySettingsStore settings) = Create();

        tracker.RememberSelectedCommit("/a", "");
        tracker.RememberSelectedCommit("", "aaa");

        settings.Current.Session.SelectedCommits.ShouldBeEmpty();
    }

    /// <summary>
    /// The number of records is capped and the <b>current repository is never dropped</b>.
    /// </summary>
    /// <remarks>
    /// Left uncapped, the settings file would grow over time with a record for every repository
    /// the user ever opened once.
    /// </remarks>
    [Fact]
    public void Kayit_sayisi_sinirli_ve_guncel_depo_korunuyor()
    {
        (SessionTracker tracker, InMemorySettingsStore settings) = Create();

        for (int i = 0; i < SessionTracker.MaximumTrackedRepositories + 5; i++)
        {
            tracker.RememberSelectedCommit($"/depo{i}", $"sha{i}");
        }

        settings.Current.Session.SelectedCommits.Count
            .ShouldBeLessThanOrEqualTo(SessionTracker.MaximumTrackedRepositories);

        string lastPath = $"/depo{SessionTracker.MaximumTrackedRepositories + 4}";
        tracker.SelectedCommit(lastPath).ShouldNotBeNull("en son yazılan kayıt korunmalı");
    }
}
