using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P12-T09…T11 — what each row knows about the graph beyond its own lane.
/// </summary>
/// <remarks>
/// The drawing itself is verified pixel by pixel in <c>CommitGraphRenderTests</c>; what is checked
/// here is the information the drawing is given — the row above, which commit is <c>HEAD</c>, and
/// which commits are part of <c>HEAD</c>'s history.
/// </remarks>
public class CommitGraphStateTests
{
    /// <summary>
    /// A history with a side branch: HEAD is on <c>main</c>, and <c>feature</c> forked earlier.
    /// </summary>
    /// <remarks>
    /// The feature tip is listed FIRST, before HEAD — that is what a real <c>--topo-order --all</c>
    /// looks like when another branch is ahead in date, and it is the case the one-pass
    /// reachability has to survive.
    /// </remarks>
    private static IReadOnlyList<CommitInfo> SideBranchHistory() =>
    [
        FakeGitData.Commit(FakeGitData.Sha(9), [FakeGitData.Sha(3)], "feature tip"),
        FakeGitData.Commit(FakeGitData.Sha(5), [FakeGitData.Sha(4)], "head commit"),
        FakeGitData.Commit(FakeGitData.Sha(4), [FakeGitData.Sha(3)], "fourth"),
        FakeGitData.Commit(FakeGitData.Sha(3), [FakeGitData.Sha(2)], "third"),
        FakeGitData.Commit(FakeGitData.Sha(2), [FakeGitData.Sha(1)], "second"),
        FakeGitData.Commit(FakeGitData.Sha(1), [], "first"),
    ];

    private static RepositoryRefs RefsWithHead(string headSha) =>
        FakeGitData.Refs(
            localBranches:
            [
                FakeGitData.LocalBranch("main", headSha, isCurrent: true),
                FakeGitData.LocalBranch("feature", FakeGitData.Sha(9)),
            ],
            head: new HeadState
            {
                IsDetached = false,
                IsUnborn = false,
                BranchName = "main",
                Commit = CommitId.Parse(headSha),
            });

    private static async Task<CommitListViewModel> OpenAsync(
        IReadOnlyList<CommitInfo> commits,
        RepositoryRefs refs)
    {
        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(commits),
            new FakeRefReader(refs),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        await list.OpenAsync("/tmp/depo");

        return list;
    }

    [AvaloniaFact]
    public async Task Her_satir_USTUNDEKI_satirin_grafigini_taşiyor()
    {
        // 🔴 This is what the broken graph came down to: without the row above, the upper half of
        // every connection belonged to nobody and the lanes looked cut off above every commit.
        CommitListViewModel list = await OpenAsync(
            FakeGitData.LinearHistory(4),
            FakeGitData.NoRefs());

        list.Rows[0].PreviousGraphRow.ShouldBeNull("the first row has nothing above it");

        for (int i = 1; i < list.Rows.Count; i++)
        {
            list.Rows[i].PreviousGraphRow.ShouldBeSameAs(list.Rows[i - 1].GraphRow);
        }
    }

    [AvaloniaFact]
    public async Task HEAD_satiri_isaretleniyor()
    {
        CommitListViewModel list = await OpenAsync(SideBranchHistory(), RefsWithHead(FakeGitData.Sha(5)));

        list.Rows.Count(r => r.IsHead).ShouldBe(1);
        list.Rows.Single(r => r.IsHead).Commit.Id.ShouldBe(CommitId.Parse(FakeGitData.Sha(5)));
    }

    [AvaloniaFact]
    public async Task HEAD_gecmisi_ILGILI_diger_dal_DEGIL()
    {
        // The answer to "which branch am I on", drawn: HEAD's ancestors keep their colour and the
        // other branches go grey.
        CommitListViewModel list = await OpenAsync(SideBranchHistory(), RefsWithHead(FakeGitData.Sha(5)));

        Dictionary<string, bool> relative = list.Rows.ToDictionary(
            r => r.Commit.Subject,
            r => r.IsRelative);

        relative["head commit"].ShouldBeTrue();
        relative["fourth"].ShouldBeTrue();

        // The fork point is an ancestor of HEAD as well — it is shared, not foreign.
        relative["third"].ShouldBeTrue();
        relative["second"].ShouldBeTrue();
        relative["first"].ShouldBeTrue();

        // 🔴 …and the side branch is NOT, even though it was read BEFORE HEAD. A pass that only
        // looked backwards would have marked it relative.
        relative["feature tip"].ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Onceki_satirin_ilgililigi_de_tasiniyor()
    {
        // The lines coming down into a row belong to the row above, so they take ITS colour.
        CommitListViewModel list = await OpenAsync(SideBranchHistory(), RefsWithHead(FakeGitData.Sha(5)));

        for (int i = 1; i < list.Rows.Count; i++)
        {
            list.Rows[i].PreviousIsRelative.ShouldBe(list.Rows[i - 1].IsRelative);
        }
    }

    [AvaloniaFact]
    public async Task HEAD_bilinmiyorsa_HICBIR_SEY_grilesmiyor()
    {
        // A fresh `git init` has no HEAD commit. Graying everything out there would be a claim
        // about the history that nobody made.
        CommitListViewModel list = await OpenAsync(SideBranchHistory(), FakeGitData.NoRefs());

        list.Rows.ShouldAllBe(r => r.IsRelative);
        list.Rows.ShouldAllBe(r => !r.IsHead);
    }

    [AvaloniaFact]
    public async Task Filtre_degisince_grafik_bilgisi_YENIDEN_hesaplaniyor()
    {
        // The rows are rebuilt on every read; if the reachability were computed once and kept, a
        // filtered list would carry the previous list's colours.
        CommitListViewModel list = await OpenAsync(SideBranchHistory(), RefsWithHead(FakeGitData.Sha(5)));

        await list.ApplyFilterAsync();

        list.Rows.Single(r => r.Commit.Subject == "feature tip").IsRelative.ShouldBeFalse();
        list.Rows.Single(r => r.Commit.Subject == "head commit").IsHead.ShouldBeTrue();
    }
}
