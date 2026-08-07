using GitExt.UI.Settings;

namespace GitExt.UI.Tests.Settings;

/// <summary>
/// P08-T16 — oturum kalıcılığı.
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
    /// Depo bilerek kapatıldıysa <b>unutuluyor</b>.
    /// </summary>
    /// <remarks>
    /// Kullanıcı "kapat" derken sonraki açılışta karşılama ekranını kastediyor; aynı depoyu
    /// geri açmak o kararı yok saymak olurdu.
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
    /// Kayıt sayısı sınırlı ve <b>güncel depo hiçbir zaman atılmıyor</b>.
    /// </summary>
    /// <remarks>
    /// Sınırsız bırakılsaydı ayar dosyası, kullanıcının bir kez açtığı her deponun kaydıyla
    /// zamanla büyürdü.
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
