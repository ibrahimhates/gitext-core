using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

public sealed class RevisionGridDisplayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static CommitInfo Dated(DateTimeOffset author, DateTimeOffset committer) =>
        FakeGitData.Commit(FakeGitData.Sha(1), [], "subject") with
        {
            Author = new Signature("A", "a@test.invalid", author),
            Committer = new Signature("C", "c@test.invalid", committer),
        };

    [Fact]
    public void Varsayilanlar_GitExtensions_ile_ayni()
    {
        RevisionGridDisplay display = new();

        display.ShowRemoteBranches.ShouldBeTrue();
        display.ShowTags.ShouldBeTrue();
        display.DrawNonRelativesGray.ShouldBeTrue();
        display.ShowAuthorDate.ShouldBeTrue();
        display.RelativeDate.ShouldBeTrue();
        display.ShowCommitBody.ShouldBeTrue();
        display.ShowGraphColumn.ShouldBeTrue();
        display.ShowAuthorNameColumn.ShouldBeTrue();
        display.ShowDateColumn.ShouldBeTrue();
        display.ShowIdColumn.ShouldBeTrue();
    }

    [Fact]
    public void Ayar_dosyasi_varsayilanlari_kayitla_ayni()
    {
        RevisionGridDisplay.From(new GitExt.UI.Settings.RevisionGridSettings()).ShouldBe(new RevisionGridDisplay());
    }

    [Theory]
    [MemberData(nameof(RevisionGridContextMenuTests.DisplaySwitches), MemberType = typeof(RevisionGridContextMenuTests))]
    public void Her_anahtar_yalniz_kendi_alanini_ceviriyor(RevisionGridMenuId id)
    {
        RevisionGridDisplay before = new();
        RevisionGridDisplay after = before.Toggle(id)!;

        after.IsChecked(id).ShouldBe(false);
        after.Toggle(id).ShouldBe(before);

        foreach (RevisionGridMenuId other in RevisionGridContextMenuTests.DisplaySwitches.Select(row => row.Data))
        {
            if (other != id)
            {
                after.IsChecked(other).ShouldBe(true);
            }
        }
    }

    [Fact]
    public void Ayara_yazilan_geri_okunuyor()
    {
        GitExt.UI.Settings.RevisionGridSettings settings = new();
        RevisionGridDisplay display = new RevisionGridDisplay()
            .Toggle(RevisionGridMenuId.ShowIdColumn)!
            .Toggle(RevisionGridMenuId.ShowRelativeDate)!;

        display.CopyTo(settings);

        RevisionGridDisplay.From(settings).ShouldBe(display);
    }

    [Fact]
    public void Yazar_tarihi_kapaliyken_commit_tarihi_gosteriliyor()
    {
        CommitInfo commit = Dated(Now.AddDays(-3), Now.AddHours(-2));

        new RevisionGridDisplay().FormatDate(commit, Now).ShouldBe("3 days ago");
        new RevisionGridDisplay { ShowAuthorDate = false }.FormatDate(commit, Now).ShouldBe("2 hours ago");
    }

    [Fact]
    public void Goreli_tarih_kapaliyken_mutlak_tarih()
    {
        CommitInfo commit = Dated(Now.AddDays(-3), Now);

        new RevisionGridDisplay { RelativeDate = false }.FormatDate(commit, Now)
            .ShouldBe(Now.AddDays(-3).ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture));
    }

    [Theory]
    [InlineData(0, "0 seconds ago")]
    [InlineData(1, "1 second ago")]
    [InlineData(59, "59 seconds ago")]
    [InlineData(60, "1 minute ago")]
    [InlineData(3599, "59 minutes ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(86399, "23 hours ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(6 * 86400, "6 days ago")]
    [InlineData(7 * 86400, "1 week ago")]
    [InlineData(29 * 86400, "4 weeks ago")]
    [InlineData(30 * 86400, "1 month ago")]
    [InlineData(364 * 86400, "12 months ago")]
    [InlineData(365 * 86400, "1 year ago")]
    [InlineData(800 * 86400, "2 years ago")]
    public void Goreli_tarih_birimleri(int seconds, string expected)
    {
        RevisionGridDisplay.Relative(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
    }

    [Fact]
    public void Gelecekteki_tarih_eksi_yazilmiyor()
    {
        RevisionGridDisplay.Relative(TimeSpan.FromMinutes(-5)).ShouldBe("0 seconds ago");
    }

    [Fact]
    public void Rozet_filtresi_yerel_dali_hic_gizlemiyor()
    {
        RevisionGridDisplay hidden = new() { ShowRemoteBranches = false, ShowTags = false };

        hidden.Shows(new RefBadge("main", RefBadgeKind.LocalBranch, false)).ShouldBeTrue();
        hidden.Shows(new RefBadge("origin/main", RefBadgeKind.RemoteBranch, false)).ShouldBeFalse();
        hidden.Shows(new RefBadge("v1", RefBadgeKind.Tag, false)).ShouldBeFalse();
        new RevisionGridDisplay().Shows(new RefBadge("origin/main", RefBadgeKind.RemoteBranch, false)).ShouldBeTrue();
    }

    [Fact]
    public void Gorunum_anahtari_ayara_yaziliyor()
    {
        InMemorySettingsStore store = new();
        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader([]),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        list.AttachSettings(store);

        list.ToggleDisplay(RevisionGridMenuId.ShowIdColumn).ShouldBeTrue();

        list.Display.ShowIdColumn.ShouldBeFalse();
        store.Current.RevisionGrid.ShowIdColumn.ShouldBeFalse();
        store.Current.RevisionGrid.ShowTags.ShouldBeTrue();
    }

    [Fact]
    public void Kayitli_ayar_acilista_okunuyor()
    {
        InMemorySettingsStore store = new();
        store.Current.RevisionGrid.RelativeDate = false;

        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader([]),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        list.AttachSettings(store);

        list.Display.RelativeDate.ShouldBeFalse();
    }

    [Fact]
    public void Gorunum_disi_oge_anahtar_sayilmiyor()
    {
        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader([]),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        list.ToggleDisplay(RevisionGridMenuId.CopyCommitHash).ShouldBeFalse();
        list.Display.ShouldBe(new RevisionGridDisplay());
    }

    [AvaloniaFact]
    public async Task Govde_tek_satira_iniyor()
    {
        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(
                [FakeGitData.Commit(FakeGitData.Sha(1), [], "subject") with { Body = "first line\n\n  second  \nthird\n" }]),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        await list.OpenAsync("/tmp/depo");

        list.Rows[0].BodyLine.ShouldBe("first line second third");
    }
}
