using System.Net;
using GitExt.UI.Updates;

namespace GitExt.UI.Tests.Updates;

/// <summary>
/// P13-T01 — the version notice.
/// </summary>
/// <remarks>
/// <b>There is no automatic update and these tests are part of saying so:</b> what is verified is
/// that a single public request is made, at most once a week, that it stays silent when it fails,
/// and that a switched-off setting means <b>no request at all</b>.
/// </remarks>
public class UpdateCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Answers the release request without a network.</summary>
    private sealed class FakeFeed : IReleaseFeed
    {
        private readonly ReleaseNote? _release;

        public FakeFeed(string? version = null, string url = "https://example.invalid/release")
        {
            _release = version is null ? null : new ReleaseNote(version, url);
        }

        public int CallCount { get; private set; }

        public Task<ReleaseNote?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_release);
        }
    }

    private static UpdateService Service(
        IReleaseFeed feed,
        InMemorySettingsStore settings,
        string current = "0.1.0",
        DateTimeOffset? now = null) =>
        new(feed, settings, current, () => now ?? DateTimeOffset.Parse("2026-08-20T12:00:00Z"));

    // ------------------------------------------------------------------ version comparison

    [Theory]
    [InlineData("v0.1.1", "0.1.0", true)]
    [InlineData("0.2.0", "0.1.9", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    [InlineData("0.1.0", "0.2.0-alpha.1", false)]
    public void Surum_karsilastirma_dogru(string published, string current, bool expectNewer)
    {
        ReleaseVersion.TryParse(published, out ReleaseVersion? left).ShouldBeTrue();
        ReleaseVersion.TryParse(current, out ReleaseVersion? right).ShouldBeTrue();

        (left!.CompareTo(right!) > 0).ShouldBe(expectNewer);
    }

    [Fact]
    public void On_surum_YAYINLANANDAN_eski_sayiliyor()
    {
        // 🔴 The rule the whole feature hangs on: someone running 0.1.2-alpha.3 IS behind 0.1.2,
        // and that build is exactly the one that most needs to hear about the release.
        ReleaseVersion.TryParse("0.1.2", out ReleaseVersion? release).ShouldBeTrue();
        ReleaseVersion.TryParse("0.1.2-alpha.3", out ReleaseVersion? pre).ShouldBeTrue();

        release!.CompareTo(pre!).ShouldBeGreaterThan(0);
        pre!.IsPreRelease.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sürüm yok")]
    [InlineData("1.2.3.4")]
    [InlineData("v")]
    public void Okunamayan_surum_SESSIZ_kaliyor(string text)
    {
        ReleaseVersion.TryParse(text, out ReleaseVersion? version).ShouldBeFalse();
        version.ShouldBeNull();
    }

    [Fact]
    public void Yapi_meta_verisi_yoksayiliyor()
    {
        // MinVer writes the commit into the metadata (`+sha`); semver ignores it for precedence
        // and so must we, or every build would look different from every other.
        ReleaseVersion.TryParse("0.1.1+abc1234", out ReleaseVersion? version).ShouldBeTrue();

        version!.ToString().ShouldBe("0.1.1");
    }

    // ------------------------------------------------------------------ the check itself

    [Fact]
    public async Task Yeni_surum_bildiriliyor()
    {
        FakeFeed feed = new("v0.1.1");
        InMemorySettingsStore settings = new();

        UpdateCheckResult result = await Service(feed, settings).CheckAsync(userRequested: false, Ct);

        result.Checked.ShouldBeTrue();
        result.HasUpdate.ShouldBeTrue();
        result.Update.ShouldNotBeNull().Version.ShouldBe("v0.1.1");
    }

    [Fact]
    public async Task Ayni_surumde_bildirim_YOK()
    {
        FakeFeed feed = new("v0.1.0");
        InMemorySettingsStore settings = new();

        UpdateCheckResult result = await Service(feed, settings).CheckAsync(userRequested: false, Ct);

        result.Checked.ShouldBeTrue();
        result.HasUpdate.ShouldBeFalse();
    }

    [Fact]
    public async Task Ayar_KAPALIYKEN_ag_istegi_hic_yapilmiyor()
    {
        // 🔴 "Off" has to mean off. A check that runs anyway and merely hides its answer would
        // still be a request leaving the machine — which is the whole point of the setting.
        FakeFeed feed = new("v9.9.9");
        InMemorySettingsStore settings = new();
        settings.Update(s => s.General.CheckForUpdates = false);

        UpdateCheckResult result = await Service(feed, settings).CheckAsync(userRequested: false, Ct);

        feed.CallCount.ShouldBe(0);
        result.Checked.ShouldBeFalse();
        result.HasUpdate.ShouldBeFalse();
    }

    [Fact]
    public async Task Kullanici_isterse_ayar_kapaliyken_de_deneniyor()
    {
        // Someone who clicked "check for updates" is owed an answer; that click IS the consent.
        FakeFeed feed = new("v9.9.9");
        InMemorySettingsStore settings = new();
        settings.Update(s => s.General.CheckForUpdates = false);

        UpdateCheckResult result = await Service(feed, settings).CheckAsync(userRequested: true, Ct);

        feed.CallCount.ShouldBe(1);
        result.HasUpdate.ShouldBeTrue();
    }

    [Fact]
    public async Task Hafta_dolmadan_TEKRAR_denetlenmiyor()
    {
        FakeFeed feed = new("v0.1.1");
        InMemorySettingsStore settings = new();

        DateTimeOffset now = DateTimeOffset.Parse("2026-08-20T12:00:00Z");

        await Service(feed, settings, now: now).CheckAsync(userRequested: false, Ct);
        feed.CallCount.ShouldBe(1);

        // Six days later: not yet.
        await Service(feed, settings, now: now.AddDays(6)).CheckAsync(userRequested: false, Ct);
        feed.CallCount.ShouldBe(1);

        // Eight days later: due again.
        await Service(feed, settings, now: now.AddDays(8)).CheckAsync(userRequested: false, Ct);
        feed.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task Ulasilamayan_ag_SESSIZ_ve_tekrar_tekrar_denenmiyor()
    {
        // 🔴 The stamp is written BEFORE the request. Otherwise a machine that is offline would
        // send a request on every single start — the one situation where the check is guaranteed
        // to be useless.
        FakeFeed feed = new(version: null);
        InMemorySettingsStore settings = new();

        UpdateCheckResult result = await Service(feed, settings).CheckAsync(userRequested: false, Ct);

        result.Checked.ShouldBeTrue();
        result.HasUpdate.ShouldBeFalse();
        settings.Current.General.LastUpdateCheck.ShouldNotBeNullOrWhiteSpace();

        await Service(feed, settings).CheckAsync(userRequested: false, Ct);
        feed.CallCount.ShouldBe(1);
    }

    // ------------------------------------------------------------------ reading the answer

    /// <summary>Serves a canned HTTP answer.</summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public CannedHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body),
            });
    }

    private static GitHubReleaseFeed Feed(HttpStatusCode status, string body) =>
        new(new HttpClient(new CannedHandler(status, body)), "https://example.invalid/latest");

    [Fact]
    public async Task Yayin_cevabindan_etiket_ve_adres_okunuyor()
    {
        // The shape is the real one (measured against the live endpoint); the fields we read are
        // `tag_name` and `html_url`, and everything else in the ~40-field answer is ignored.
        using GitHubReleaseFeed feed = Feed(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "v0.1.1",
              "html_url": "https://github.com/ibrahimhates/gitext-core/releases/tag/v0.1.1",
              "draft": false,
              "prerelease": false,
              "body": "notes"
            }
            """);

        ReleaseNote release = (await feed.GetLatestAsync(Ct)).ShouldNotBeNull();

        release.Version.ShouldBe("v0.1.1");
        release.Url.ShouldContain("releases/tag/v0.1.1");
    }

    [Fact]
    public async Task On_yayin_ve_taslak_ATLANIYOR()
    {
        // A pre-release is not what someone on a release build should be nudged towards, and a
        // draft is not published at all.
        using GitHubReleaseFeed pre = Feed(
            HttpStatusCode.OK,
            """{ "tag_name": "v0.2.0-rc.1", "prerelease": true }""");

        using GitHubReleaseFeed draft = Feed(
            HttpStatusCode.OK,
            """{ "tag_name": "v0.2.0", "draft": true }""");

        (await pre.GetLatestAsync(Ct)).ShouldBeNull();
        (await draft.GetLatestAsync(Ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.Forbidden, "rate limited")]
    [InlineData(HttpStatusCode.OK, "this is not json")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task Beklenmeyen_cevap_ISTISNA_atmiyor(HttpStatusCode status, string body)
    {
        // A repository with no releases answers 404; a rate limit answers 403. Neither is an
        // error the user has to be told about.
        using GitHubReleaseFeed feed = Feed(status, body);

        (await feed.GetLatestAsync(Ct)).ShouldBeNull();
    }
}
