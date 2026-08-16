using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T21 — Scrolling the graph window.
/// </summary>
/// <remarks>
/// <para>
/// <b>This behaviour exists because of a measurement.</b> Real repositories were measured in P03-T18:
/// in git/git and Linux the lane count is <b>around 120 at the median</b> and the commit nodes spread
/// across those lanes — a fixed limit of 16 lanes would show only 24% of the nodes in Linux. So a
/// "cut it off" approach does not work; the column stays a fixed width and shows a <b>sliding
/// window</b>, and the window follows the selected commit.
/// </para>
/// </remarks>
public class GraphWindowTests
{
    /// <summary>
    /// Produces a history containing <paramref name="width"/> parallel branches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every branch has <b>its own intermediate commit</b>: had all the tips joined the root directly,
    /// the layout engine would reuse the lane and they would collapse into one (that behaviour was
    /// added deliberately in P03-T13). The intermediate commits keep the lanes <b>open</b> while the
    /// tips are processed — a small-scale version of the wide graph in real repositories.
    /// </para>
    /// <para>
    /// The order is topological: all the tips → all the intermediate commits → the root.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<CommitInfo> WideHistory(int width)
    {
        List<CommitInfo> commits = [];

        // The tips.
        for (int i = width; i >= 1; i--)
        {
            commits.Add(FakeGitData.Commit(
                id: FakeGitData.Sha(1 + width + i),
                parents: [FakeGitData.Sha(1 + i)],
                subject: $"uç {i}"));
        }

        // The intermediate commits — all attached to the root.
        for (int i = width; i >= 1; i--)
        {
            commits.Add(FakeGitData.Commit(
                id: FakeGitData.Sha(1 + i),
                parents: [FakeGitData.Sha(1)],
                subject: $"ara {i}"));
        }

        commits.Add(FakeGitData.Commit(FakeGitData.Sha(1), [], "kök"));

        return commits;
    }

    private static async Task<CommitListViewModel> LoadedAsync(int width)
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(WideHistory(width)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader());

        await viewModel.OpenAsync("/tmp/depo");
        return viewModel;
    }

    [AvaloniaFact]
    public async Task Pencere_bastan_baslar()
    {
        CommitListViewModel viewModel = await LoadedAsync(30);

        viewModel.FirstVisibleLane.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Sagdaki_bir_serit_secilince_pencere_kayar()
    {
        CommitListViewModel viewModel = await LoadedAsync(30);

        // Find a row with a high lane that falls outside the window.
        int index = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= viewModel.VisibleLanes);

        int lane = viewModel.Rows[index].GraphRow.Lane;

        viewModel.SelectedIndex = index;

        // The selected commit's node must ALWAYS be visible; otherwise the user cannot see where the
        // row they picked sits in the graph.
        viewModel.FirstVisibleLane.ShouldBeLessThanOrEqualTo(lane);
        (viewModel.FirstVisibleLane + viewModel.VisibleLanes).ShouldBeGreaterThan(lane);
    }

    [AvaloniaFact]
    public async Task Pencere_icindeki_secim_pencereyi_oynatmaz()
    {
        // Centring on every selection would make the graph jump constantly; the window only slides when
        // it has to.
        CommitListViewModel viewModel = await LoadedAsync(30);

        int index = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= viewModel.VisibleLanes);

        viewModel.SelectedIndex = index;
        int after = viewModel.FirstVisibleLane;

        // Move to another lane within the same window.
        int inWindow = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= after
                && viewModel.Rows[i].GraphRow.Lane < after + viewModel.VisibleLanes);

        viewModel.SelectedIndex = inWindow;

        viewModel.FirstVisibleLane.ShouldBe(after);
    }

    [AvaloniaFact]
    public async Task Sola_donunce_pencere_geri_kayar()
    {
        CommitListViewModel viewModel = await LoadedAsync(30);

        int far = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= viewModel.VisibleLanes);

        viewModel.SelectedIndex = far;
        viewModel.FirstVisibleLane.ShouldBeGreaterThan(0);

        int near = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane == 0);

        viewModel.SelectedIndex = near;

        viewModel.FirstVisibleLane.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Yeni_depo_acilinca_pencere_sifirlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(30);

        int far = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= viewModel.VisibleLanes);

        viewModel.SelectedIndex = far;
        viewModel.FirstVisibleLane.ShouldBeGreaterThan(0);

        await viewModel.OpenAsync("/tmp/baska-depo");

        viewModel.FirstVisibleLane.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Sutun_dar_depoda_daralir_genis_depoda_sinirda_durur()
    {
        // In a narrow repository a fixed 12-lane column would take up space for nothing; in a wide one
        // the limit has to kick in. Because the value is shared across all rows, the columns still stay
        // aligned.
        CommitListViewModel narrow = await LoadedAsync(3);
        narrow.VisibleLanes.ShouldBe(narrow.Rows.Max(r => r.GraphRow.LaneCount));
        narrow.VisibleLanes.ShouldBeLessThan(CommitListViewModel.DefaultVisibleLanes);

        CommitListViewModel wide = await LoadedAsync(30);
        wide.VisibleLanes.ShouldBe(CommitListViewModel.DefaultVisibleLanes);
    }

    [AvaloniaFact]
    public async Task Dar_gecmiste_pencere_hic_kaymaz()
    {
        // Most repositories are narrow; the window mechanism must be invisible there.
        CommitListViewModel viewModel = await LoadedAsync(3);

        for (int i = 0; i < viewModel.Rows.Count; i++)
        {
            viewModel.SelectedIndex = i;
            viewModel.FirstVisibleLane.ShouldBe(0);
        }
    }
}
