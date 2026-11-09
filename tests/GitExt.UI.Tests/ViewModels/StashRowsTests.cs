using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

public sealed class StashRowsTests
{
    private sealed class FakeStashes(IReadOnlyList<CommitInfo> stashes) : IStashCommitReader
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<CommitInfo>> ReadStashesAsync(string workingDirectory, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(stashes);
        }
    }

    private static CommitInfo Stash(int id, int baseCommit, string subject) =>
        FakeGitData.Commit(FakeGitData.Sha(id), [FakeGitData.Sha(baseCommit), FakeGitData.Sha(id + 50)], subject);

    private static async Task<(CommitListViewModel List, FakeCommitLogReader Log, FakeStashes Stashes)> LoadAsync(
        IReadOnlyList<CommitInfo>? history = null,
        InMemorySettingsStore? store = null)
    {
        FakeCommitLogReader log = new(history ?? FakeGitData.LinearHistory(3));
        FakeStashes stashes = new([Stash(90, 2, "On main: newer"), Stash(91, 2, "On main: older"), Stash(92, 3, "On main: top")]);
        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            log,
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader(),
            stashes: stashes);

        if (store is not null)
        {
            list.AttachSettings(store);
        }

        await list.OpenAsync("/tmp/depo");

        return (list, log, stashes);
    }

    private static string[] Subjects(CommitListViewModel list) => [.. list.Rows.Select(r => r.Subject)];

    [AvaloniaFact]
    public async Task Zulalar_taban_commitinin_hemen_ustunde_yeniden_eskiye()
    {
        (CommitListViewModel list, _, _) = await LoadAsync();

        Subjects(list).ShouldBe(["On main: top", "commit 3", "On main: newer", "On main: older", "commit 2", "commit 1"]);
    }

    [AvaloniaFact]
    public async Task Zula_satiri_secicisiyle_etiketli()
    {
        (CommitListViewModel list, _, _) = await LoadAsync();

        list.Rows.Single(r => r.Subject == "On main: newer").Badges.Single().Text.ShouldBe("stash@{0}");
        list.Rows.Single(r => r.Subject == "On main: older").Badges.Single().Text.ShouldBe("stash@{1}");
        list.Rows.Single(r => r.Subject == "On main: top").Badges.Single().Kind.ShouldBe(RefBadgeKind.Stash);
        list.Rows.Single(r => r.Subject == "commit 2").Badges.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Grafige_yalniz_taban_veriliyor()
    {
        (CommitListViewModel list, _, _) = await LoadAsync();

        CommitRowViewModel stash = list.Rows.Single(r => r.Subject == "On main: newer");

        stash.GraphRow.Commit.Parents.ShouldBe([FakeGitData.Sha(2)]);
        stash.Commit.Parents.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task Sorgu_zula_refini_haric_tutuyor()
    {
        (_, FakeCommitLogReader log, _) = await LoadAsync();

        log.LastQuery!.ExcludeStash.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Gecmiste_zaten_gelen_zula_iki_kez_gorunmuyor()
    {
        List<CommitInfo> history =
        [
            Stash(90, 2, "On main: newer"),
            .. FakeGitData.LinearHistory(3),
        ];

        (CommitListViewModel list, _, _) = await LoadAsync(history);

        list.Rows.Count(r => r.Subject == "On main: newer").ShouldBe(1);
        list.Rows.Single(r => r.Subject == "On main: newer").Badges.Single().Text.ShouldBe("stash@{0}");
    }

    [AvaloniaFact]
    public async Task Kapatilinca_zula_okunmuyor_ve_ayara_yaziliyor()
    {
        InMemorySettingsStore store = new();
        (CommitListViewModel list, _, FakeStashes stashes) = await LoadAsync(store: store);
        int reads = stashes.Reads;

        await list.ToggleShowStashesAsync();

        store.Current.RevisionGrid.ShowStashes.ShouldBeFalse();
        stashes.Reads.ShouldBe(reads);
        Subjects(list).ShouldBe(["commit 3", "commit 2", "commit 1"]);
    }

    [AvaloniaFact]
    public async Task Kayitli_kapali_ayarla_acilinca_zula_yok()
    {
        InMemorySettingsStore store = new();
        store.Current.RevisionGrid.ShowStashes = false;

        (CommitListViewModel list, _, FakeStashes stashes) = await LoadAsync(store: store);

        stashes.Reads.ShouldBe(0);
        list.Rows.Count.ShouldBe(3);
    }

    [Fact]
    public void Menu_zula_isaretini_ve_zula_ogelerini_gosteriyor()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(new RevisionGridMenuContext
        {
            CommitHash = FakeGitData.Sha(90),
            IsStash = true,
            ShowStashes = true,
        });

        RevisionGridMenuEntry toggle = menu.First(e => e.Id == RevisionGridMenuId.View).Children
            .First(e => e.Id == RevisionGridMenuId.ShowStashes);

        toggle.IsEnabled.ShouldBeTrue();
        toggle.IsChecked.ShouldBeTrue();
        menu.First(e => e.Id == RevisionGridMenuId.ApplyStash).IsVisible.ShouldBeTrue();
        menu.First(e => e.Id == RevisionGridMenuId.PopStash).IsVisible.ShouldBeTrue();
        menu.First(e => e.Id == RevisionGridMenuId.DropStash).IsVisible.ShouldBeTrue();
    }
}
