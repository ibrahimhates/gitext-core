using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core.Git;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T16 — the git output panel.
/// </summary>
/// <remarks>
/// The plan's clause: <i>"The user must always be able to see what happened."</i>
/// </remarks>
public class CommandLogTests
{
    private static GitCommandLogEntry Entry(
        string command = "git status",
        int? exitCode = 0,
        bool success = true,
        string details = "") =>
        new()
        {
            Timestamp = new DateTimeOffset(2026, 8, 6, 13, 45, 12, TimeSpan.Zero),
            CommandLine = command,
            WorkingDirectory = "/depo",
            Duration = TimeSpan.FromMilliseconds(42),
            ExitCode = exitCode,
            IsSuccess = success,
            Details = details,
        };

    private static InMemoryGitCommandLog Log(params GitCommandLogEntry[] seed)
    {
        InMemoryGitCommandLog log = new();

        foreach (GitCommandLogEntry entry in seed)
        {
            log.Record(new GitResult(
                GitCommand.Create(entry.WorkingDirectory, entry.CommandLine.Split(' ')[1..]),
                entry.ExitCode ?? 0,
                [],
                entry.Details,
                entry.Duration));
        }

        return log;
    }

    [AvaloniaFact]
    public void Panel_acilmadan_ONCE_calisan_komutlar_da_gorunuyor()
    {
        // An empty list at startup would read as "nothing ran"; whereas the problem may be in a
        // command that ran earlier.
        InMemoryGitCommandLog log = Log(Entry(), Entry("git log"));

        using CommandLogViewModel model = new(log);

        model.Rows.Count.ShouldBe(2);
        model.IsEmpty.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Yeni_komut_CANLI_ekleniyor_ve_en_uste_giriyor()
    {
        InMemoryGitCommandLog log = new();

        using CommandLogViewModel model = new(log);

        model.IsEmpty.ShouldBeTrue();

        log.Record(new GitResult(GitCommand.Create("/depo", "status"), 0, [], string.Empty, TimeSpan.Zero));
        Dispatcher.UIThread.RunJobs();

        model.Rows.Count.ShouldBe(1);

        log.Record(new GitResult(GitCommand.Create("/depo", "log"), 0, [], string.Empty, TimeSpan.Zero));
        Dispatcher.UIThread.RunJobs();

        // Newest on top: the user is looking at what just happened.
        model.Rows[0].CommandLine.ShouldBe("git log");
        model.Rows.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public void Sure_ve_cikis_kodu_gosteriliyor()
    {
        InMemoryGitCommandLog log = new();

        using CommandLogViewModel model = new(log);

        log.Record(new GitResult(
            GitCommand.Create("/depo", "status"),
            0,
            [],
            string.Empty,
            TimeSpan.FromMilliseconds(42)));

        Dispatcher.UIThread.RunJobs();

        model.Rows[0].Duration.ShouldBe("42 ms");
        model.Rows[0].ExitCode.ShouldBe("0");
    }

    [AvaloniaFact]
    public void Saniyeyi_gecen_sure_saniye_olarak_yaziliyor()
    {
        InMemoryGitCommandLog log = new();

        using CommandLogViewModel model = new(log);

        log.Record(new GitResult(
            GitCommand.Create("/depo", "fetch"),
            0,
            [],
            string.Empty,
            TimeSpan.FromMilliseconds(2500)));

        Dispatcher.UIThread.RunJobs();

        model.Rows[0].Duration.ShouldBe("2.50 sn");
    }

    [AvaloniaFact]
    public void TAMAMLANMAYAN_komutun_cikis_kodu_SIFIR_yazilmiyor()
    {
        // ⚠️ null and 0 are not the same thing: showing a cancelled command as "0" would make it
        // read as successful.
        InMemoryGitCommandLog log = new();

        using CommandLogViewModel model = new(log);

        log.RecordFailure(GitCommand.Create("/depo", "fetch"), TimeSpan.Zero, "iptal edildi");
        Dispatcher.UIThread.RunJobs();

        model.Rows[0].ExitCode.ShouldBe("—");
        model.Rows[0].IsSuccess.ShouldBeFalse();
        model.Rows[0].Details.ShouldBe("iptal edildi");
    }

    [AvaloniaFact]
    public void Yalnizca_basarisizlar_suzulebiliyor()
    {
        InMemoryGitCommandLog log = new();

        using CommandLogViewModel model = new(log);

        log.Record(new GitResult(GitCommand.Create("/depo", "status"), 0, [], string.Empty, TimeSpan.Zero));
        log.Record(new GitResult(GitCommand.Create("/depo", "push"), 1, [], "reddedildi", TimeSpan.Zero));
        Dispatcher.UIThread.RunJobs();

        model.Rows.Count.ShouldBe(2);
        model.FailureCount.ShouldBe(1);

        model.OnlyFailures = true;

        model.Rows.Count.ShouldBe(1);
        model.Rows[0].CommandLine.ShouldBe("git push");
    }

    [AvaloniaFact]
    public void Suzme_listeden_dusen_SECIMI_de_dusuruyor()
    {
        // The detail panel continuing to show a record no longer in the list would mislead.
        InMemoryGitCommandLog log = new();

        using CommandLogViewModel model = new(log);

        log.Record(new GitResult(GitCommand.Create("/depo", "status"), 0, [], "ayrinti", TimeSpan.Zero));
        Dispatcher.UIThread.RunJobs();

        model.Selected = model.Rows[0];
        model.HasSelectedDetails.ShouldBeTrue();

        model.OnlyFailures = true;

        model.Selected.ShouldBeNull();
        model.HasSelectedDetails.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Temizleme_listeyi_bosaltiyor()
    {
        InMemoryGitCommandLog log = Log(Entry());

        using CommandLogViewModel model = new(log);

        model.ClearCommand.Execute(null);

        model.IsEmpty.ShouldBeTrue();
        model.FailureCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public void Kapatildiktan_sonra_yeni_kayitlar_ISLENMIYOR()
    {
        // If the subscription is not released, the log would keep every closed panel alive forever.
        InMemoryGitCommandLog log = new();

        CommandLogViewModel model = new(log);
        model.Dispose();

        log.Record(new GitResult(GitCommand.Create("/depo", "status"), 0, [], string.Empty, TimeSpan.Zero));
        Dispatcher.UIThread.RunJobs();

        model.IsEmpty.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Kapasite_asilinca_EN_ESKI_dusuyor()
    {
        InMemoryGitCommandLog log = new();

        using CommandLogViewModel model = new(log, capacity: 2);

        foreach (string name in new[] { "bir", "iki", "uc" })
        {
            log.Record(new GitResult(GitCommand.Create("/depo", name), 0, [], string.Empty, TimeSpan.Zero));
        }

        Dispatcher.UIThread.RunJobs();

        model.Rows.Count.ShouldBe(2);
        model.Rows.Select(row => row.CommandLine).ShouldBe(["git uc", "git iki"]);
    }

    // ------------------------------------------------------- ana pencere

    [AvaloniaFact]
    public async Task Menu_ogesi_DEPO_ACIK_OLMADAN_da_etkin()
    {
        // 🔑 The log is not tied to a repository: the commands that run when no repository is
        // found are in there too, and the problem may be exactly in one of those.
        FakeCommandLogPrompt prompt = new();
        InMemoryGitCommandLog log = new();

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(1)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            commandLog: log)
        {
            CommandLogPrompt = prompt,
        };

        model.CanShowCommandLog.ShouldBeTrue("depo açılmadan da açılabilmeli");

        await model.ShowCommandLogCommand.ExecuteAsync(null);

        prompt.Shown.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void Pencere_sutunlari_ve_ayrinti_alani_YERINDE()
    {
        InMemoryGitCommandLog log = Log(Entry(details: "bir seyler"));

        using CommandLogViewModel model = new(log);

        CommandLogWindow window = new() { DataContext = model, Width = 880, Height = 520 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetControl<ListBox>("LogList").ShouldNotBeNull();
        window.GetControl<TextBox>("DetailsBox").ShouldNotBeNull();
        window.GetControl<CheckBox>("OnlyFailuresBox").ShouldNotBeNull();
        window.GetControl<Button>("ClearButton").ShouldNotBeNull();

        window.Close();
    }

    private sealed class FakeCommandLogPrompt : ICommandLogPrompt
    {
        public CommandLogViewModel? Shown { get; private set; }

        public Task ShowAsync(CommandLogViewModel model)
        {
            Shown = model;
            return Task.CompletedTask;
        }
    }
}
