using GitExt.Core.Diagnostics;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P09-T03 — the diagnostics panel's ViewModel.
/// </summary>
public class DiagnosticsViewModelTests
{
    /// <summary>
    /// A diagnostics implementation that returns the numbers the test gives it.
    /// </summary>
    private sealed class FakeDiagnostics : IPerformanceDiagnostics
    {
        public List<string> Active { get; } = [];

        public List<GitCommandStatistics> Statistics { get; } = [];

        public MemorySnapshot MemoryValue { get; set; }

        public TimeSpan UptimeValue { get; set; }

        public int ResetCount { get; private set; }

        public IReadOnlyList<string> ActiveOperations => Active;

        public IReadOnlyList<GitCommandStatistics> CommandStatistics => Statistics;

        public MemorySnapshot Memory => MemoryValue;

        public TimeSpan Uptime => UptimeValue;

        public IDisposable TrackOperation(string name) => new Scope();

        public void Reset()
        {
            ResetCount++;
            Statistics.Clear();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private static GitCommandStatistics Stat(
        string name,
        int count = 1,
        int totalMs = 100,
        int maxMs = 100,
        int failures = 0) =>
        new()
        {
            Name = name,
            Count = count,
            TotalDuration = TimeSpan.FromMilliseconds(totalMs),
            MaxDuration = TimeSpan.FromMilliseconds(maxMs),
            FailureCount = failures,
        };

    // ------------------------------------------------------- display

    [Fact]
    public void ShouldProjectCommandStatistics()
    {
        FakeDiagnostics diagnostics = new();
        diagnostics.Statistics.Add(Stat("log", count: 4, totalMs: 800, maxMs: 500));

        using DiagnosticsViewModel model = new(diagnostics);

        model.Commands.Count.ShouldBe(1);
        model.Commands[0].Name.ShouldBe("log");
        model.Commands[0].Count.ShouldBe(4);
        model.Commands[0].Average.ShouldBe("200 ms");
        model.Commands[0].Max.ShouldBe("500 ms");
        model.IsEmpty.ShouldBeFalse();
    }

    /// <remarks>
    /// The total has to be the sum of the rows in the table: it is the first number the user looks at.
    /// </remarks>
    [Fact]
    public void ShouldSumTotalsAcrossCommands()
    {
        FakeDiagnostics diagnostics = new();
        diagnostics.Statistics.Add(Stat("log", count: 2, totalMs: 300));
        diagnostics.Statistics.Add(Stat("status", count: 3, totalMs: 200));

        using DiagnosticsViewModel model = new(diagnostics);

        model.TotalCommandCount.ShouldBe(5);
        model.TotalCommandDuration.ShouldBe("500 ms");
    }

    /// <remarks>
    /// Durations over a second are shown in seconds: "12483 ms" is not readable.
    /// </remarks>
    [Fact]
    public void ShouldSwitchToSecondsForLongDurations()
    {
        FakeDiagnostics diagnostics = new();
        diagnostics.Statistics.Add(Stat("clone", count: 1, totalMs: 12_483, maxMs: 12_483));

        using DiagnosticsViewModel model = new(diagnostics);

        model.Commands[0].Total.ShouldBe("12.48 sn");
    }

    [Fact]
    public void ShouldFlagCommandsWithFailures()
    {
        FakeDiagnostics diagnostics = new();
        diagnostics.Statistics.Add(Stat("push", count: 3, failures: 2));
        diagnostics.Statistics.Add(Stat("log", count: 1));

        using DiagnosticsViewModel model = new(diagnostics);

        model.Commands.Single(c => c.Name == "push").HasFailures.ShouldBeTrue();
        model.Commands.Single(c => c.Name == "log").HasFailures.ShouldBeFalse();
    }

    [Fact]
    public void ShouldReportEmptyWhenNothingRan()
    {
        using DiagnosticsViewModel model = new(new FakeDiagnostics());

        model.IsEmpty.ShouldBeTrue();
        model.TotalCommandCount.ShouldBe(0);
    }

    // ------------------------------------------------------- active operations

    [Fact]
    public void ShouldShowActiveOperations()
    {
        FakeDiagnostics diagnostics = new();
        diagnostics.Active.Add("git fetch origin");

        using DiagnosticsViewModel model = new(diagnostics);

        model.ActiveOperations.ShouldBe(["git fetch origin"]);
        model.HasActiveOperations.ShouldBeTrue();
    }

    [Fact]
    public void ShouldHideActiveSectionWhenIdle()
    {
        using DiagnosticsViewModel model = new(new FakeDiagnostics());

        model.HasActiveOperations.ShouldBeFalse();
    }

    /// <remarks>
    /// A refresh has to drop a finished operation from the list — otherwise the panel shows a finished
    /// one as "in progress".
    /// </remarks>
    [Fact]
    public void ShouldDropFinishedOperationsOnRefresh()
    {
        FakeDiagnostics diagnostics = new();
        diagnostics.Active.Add("git fetch origin");

        using DiagnosticsViewModel model = new(diagnostics);
        model.HasActiveOperations.ShouldBeTrue();

        diagnostics.Active.Clear();
        model.Refresh();

        model.HasActiveOperations.ShouldBeFalse();
        model.ActiveOperations.ShouldBeEmpty();
    }

    // ------------------------------------------------------- bellek

    [Fact]
    public void ShouldFormatMemory()
    {
        FakeDiagnostics diagnostics = new()
        {
            MemoryValue = new MemorySnapshot(
                ManagedBytes: 52_428_800,
                ProcessBytes: 209_715_200,
                Gen0: 12,
                Gen1: 4,
                Gen2: 1),
        };

        using DiagnosticsViewModel model = new(diagnostics);

        model.ManagedMemory.ShouldBe("50.0 MB");
        model.ProcessMemory.ShouldBe("200.0 MB");
        model.Collections.ShouldBe("Gen0 12 · Gen1 4 · Gen2 1");
    }

    /// <remarks>
    /// When the process memory cannot be read it comes back as zero; writing "0 B" would read as a wrong
    /// measurement — a dash means "unknown".
    /// </remarks>
    [Fact]
    public void ShouldShowDashWhenProcessMemoryUnavailable()
    {
        FakeDiagnostics diagnostics = new()
        {
            MemoryValue = new MemorySnapshot(1024, 0, 0, 0, 0),
        };

        using DiagnosticsViewModel model = new(diagnostics);

        model.ProcessMemory.ShouldBe("—");
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2 KB")]
    [InlineData(5_242_880L, "5.0 MB")]
    [InlineData(2_147_483_648L, "2.00 GB")]
    public void ShouldFormatByteSizes(long bytes, string expected) =>
        DiagnosticsViewModel.FormatBytes(bytes).ShouldBe(expected);

    [Theory]
    [InlineData(45, "45 sn")]
    [InlineData(90, "1 dk 30 sn")]
    [InlineData(3_930, "1 sa 5 dk")]
    public void ShouldFormatUptime(int seconds, string expected) =>
        DiagnosticsViewModel.FormatUptime(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);

    // ------------------------------------------------------- kare istatistikleri

    /// <remarks>
    /// The frame measurement is tied to a window; without one, the related rows are hidden. Showing the
    /// empty values as "0 ms" would read as a perfect 0 ms frame time.
    /// </remarks>
    [Fact]
    public void ShouldHideFrameSectionWithoutMonitor()
    {
        using DiagnosticsViewModel model = new(new FakeDiagnostics());

        model.HasFrameStatistics.ShouldBeFalse();
        model.AverageFrameTime.ShouldBe("—");
    }

    // ------------------------------------------------------- komutlar

    [Fact]
    public void ShouldForwardResetToDiagnostics()
    {
        FakeDiagnostics diagnostics = new();
        diagnostics.Statistics.Add(Stat("log"));

        using DiagnosticsViewModel model = new(diagnostics);
        model.Commands.Count.ShouldBe(1);

        model.ResetCommand.Execute(null);

        diagnostics.ResetCount.ShouldBe(1);
        model.Commands.ShouldBeEmpty();
        model.IsEmpty.ShouldBeTrue();
    }

    /// <remarks>
    /// A refresh has to show the new runs; if commands running while the panel is open are invisible, the
    /// diagnosis misses the freshest data available.
    /// </remarks>
    [Fact]
    public void ShouldPickUpNewStatisticsOnRefresh()
    {
        FakeDiagnostics diagnostics = new();

        using DiagnosticsViewModel model = new(diagnostics);
        model.IsEmpty.ShouldBeTrue();

        diagnostics.Statistics.Add(Stat("fetch"));
        model.RefreshCommand.Execute(null);

        model.Commands.Count.ShouldBe(1);
        model.Commands[0].Name.ShouldBe("fetch");
    }
}
