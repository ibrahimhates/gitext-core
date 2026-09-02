using System.Diagnostics;
using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T14 — what the folder tree costs on a working directory with a lot of files.
/// </summary>
/// <remarks>
/// The tree is rebuilt on <b>every</b> refresh, and a refresh follows every save the user makes in
/// their editor. A build that is cheap for twenty files and quadratic for ten thousand would only
/// show up on a repository big enough for it to hurt — which is exactly where it must not.
/// </remarks>
public class WorkingTreeFileTreeCostTests
{
    private static FileStatus[] Files(int count)
    {
        FileStatus[] files = new FileStatus[count];

        for (int index = 0; index < count; index++)
        {
            files[index] = new FileStatus
            {
                Path = RepositoryPath.Parse(
                    $"src/module{index % 40}/area{index % 12}/deep{index % 5}/file{index}.cs"),
                UnstagedChange = FileChangeKind.Modified,
            };
        }

        return files;
    }

    private static async Task<(int Rows, double Milliseconds)> MeasureAsync(int count)
    {
        FakeStatusReader status = new(Files(count));

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()));

        await model.OpenAsync("/tmp/depo");

        Stopwatch clock = Stopwatch.StartNew();
        await model.RefreshAsync();
        clock.Stop();

        return (model.UnstagedRows.Count, clock.Elapsed.TotalMilliseconds);
    }

    [AvaloniaFact]
    public async Task Bin_dosyalik_calisma_dizini_agaca_donuyor()
    {
        (int rows, double milliseconds) = await MeasureAsync(1_000);

        // 1.000 file rows plus their folder rows.
        rows.ShouldBeGreaterThan(1_000);
        // Measured on this machine: 1.161 rows in 0,81 ms. The bound leaves room for a
        // loaded CI box; what it guards is the order of magnitude.
        milliseconds.ShouldBeLessThan(1_500);
    }

    [AvaloniaFact]
    public async Task On_bin_dosyada_maliyet_patlamiyor()
    {
        (_, double small) = await MeasureAsync(1_000);
        (int rows, double large) = await MeasureAsync(10_000);

        rows.ShouldBeGreaterThan(10_000);

        // Ten times the files must not cost anything like a hundred times the work. The bound is
        // deliberately loose — this catches a quadratic insert, it does not police milliseconds on
        // a loaded machine.
        // Measured: 1.000 files 0,78 ms, 10.000 files 7,49 ms — ten times the files, ten
        // times the work.
        large.ShouldBeLessThan(Math.Max(small * 40, 4_000));
    }
}
