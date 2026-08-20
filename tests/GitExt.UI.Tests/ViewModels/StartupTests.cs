using Avalonia.Headless.XUnit;
using GitExt.UI.Settings;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P12-T04 — where the application lands at startup.
/// </summary>
/// <remarks>
/// <para>
/// GitExtensions starts on the dashboard: <c>Program.cs</c> looks at the working directory only
/// when it was given an argument, and reopening the last repository is
/// <c>StartWithRecentWorkingDir</c> — <c>GetBool(…, false)</c>, off by default.
/// </para>
/// <para>
/// 🔴 Before this, the current working directory was tried silently. Launched from a terminal that
/// happened to be inside a repository — which is where a developer usually is — the application
/// went straight into it and the repository list was never seen. Nothing failed; the screen was
/// simply never reached.
/// </para>
/// </remarks>
public class StartupTests
{
    private static MainWindowViewModel Create(FakeRecentRepositoryStore? recent = null) =>
        new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            recent ?? new FakeRecentRepositoryStore());

    [AvaloniaFact]
    public async Task Yol_verilmeden_acilista_KONTROL_PANELI_geliyor()
    {
        // The locator here SUCCEEDS: were the working directory still being tried, a repository
        // would open and this test would fail. That is the point of it.
        MainWindowViewModel model = Create(new FakeRecentRepositoryStore("/r/bir"));

        await model.StartAsync(explicitPath: null);

        model.ShowWelcome.ShouldBeTrue();
        model.Commits.Repository.ShouldBeNull();

        // …and the list is on screen, ready to pick from.
        model.Dashboard.Groups.Single().Items.Single().Path.ShouldBe("/r/bir");
    }

    [AvaloniaFact]
    public async Task Son_depo_ayar_KAPALIYKEN_acilmiyor()
    {
        InMemorySettingsStore settings = new();
        MainWindowViewModel model = Create();

        model.Session = new SessionTracker(settings);
        model.Session.RememberRepository("/r/son");

        await model.StartAsync(explicitPath: null);

        model.Commits.Repository.ShouldBeNull();
        model.ShowWelcome.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Son_depo_ayar_ACIKKEN_aciliyor()
    {
        // The P08-T16 behaviour is not gone, it became a choice.
        InMemorySettingsStore settings = new();
        settings.Update(s => s.General.StartWithRecentWorkingDir = true);

        MainWindowViewModel model = Create();
        model.Session = new SessionTracker(settings);
        model.Session.RememberRepository("/r/son");

        await model.StartAsync(explicitPath: null);

        model.Commits.Repository.ShouldNotBeNull().WorkingDirectory.ShouldBe("/r/son");
        model.ShowWelcome.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Komut_satirindan_verilen_yol_dogrudan_aciliyor()
    {
        // `gitext-core .` must still go straight in — the dashboard is the default, not a toll gate.
        MainWindowViewModel model = Create();

        await model.StartAsync("/r/acik");

        model.Commits.Repository.ShouldNotBeNull().WorkingDirectory.ShouldBe("/r/acik");
        model.ShowWelcome.ShouldBeFalse();
    }
}
