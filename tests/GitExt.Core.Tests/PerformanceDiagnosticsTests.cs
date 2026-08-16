using GitExt.Core.Diagnostics;
using GitExt.Core.Git;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T03 — the performance diagnostics collector.
/// </summary>
/// <remarks>
/// The value of the diagnostics panel lies in showing the right numbers. A wrong statistic points
/// the "it is slow" complaint at the wrong place — that is worse than having no numbers at all.
/// </remarks>
public class PerformanceDiagnosticsTests
{
    // ---------------------------------------------------- command-name extraction

    /// <remarks>
    /// 🔴 <c>-c key=value</c> comes <b>before</b> the subcommand. Taking the first word would group
    /// all of these commands as <c>-c</c>; the "which command is expensive" question would become
    /// invisible in the statistics table. The value part must be skipped too, otherwise
    /// <c>core.editor=…</c> is mistaken for the command name.
    /// </remarks>
    [Theory]
    [InlineData("git log --oneline", "log")]
    [InlineData("git -c core.editor=false rebase --continue", "rebase")]
    [InlineData("git -c a=1 -c b=2 status --porcelain=v2", "status")]
    [InlineData("git --no-pager diff", "diff")]
    [InlineData("log -n 50", "log")]
    [InlineData("", "(bilinmiyor)")]
    [InlineData("   ", "(bilinmiyor)")]
    [InlineData("git", "(bilinmiyor)")]
    [InlineData("git -c only=option", "(bilinmiyor)")]
    public void ShouldExtractSubcommandName(string commandLine, string expected) =>
        PerformanceDiagnostics.ExtractCommandName(commandLine).ShouldBe(expected);

    // ------------------------------------------------------- istatistik toplama

    [Fact]
    public void ShouldGroupRunsBySubcommand()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        Record(log, TimeSpan.FromMilliseconds(10), success: true, "log", "--oneline");
        Record(log, TimeSpan.FromMilliseconds(30), success: true, "log", "-n", "50");
        Record(log, TimeSpan.FromMilliseconds(5), success: true, "status");

        IReadOnlyList<GitCommandStatistics> statistics = diagnostics.CommandStatistics;

        statistics.Count.ShouldBe(2);

        GitCommandStatistics logStatistics = statistics.Single(s => s.Name == "log");
        logStatistics.Count.ShouldBe(2);
        logStatistics.TotalDuration.ShouldBe(TimeSpan.FromMilliseconds(40));
        logStatistics.AverageDuration.ShouldBe(TimeSpan.FromMilliseconds(20));
        logStatistics.MaxDuration.ShouldBe(TimeSpan.FromMilliseconds(30));
    }

    /// <remarks>
    /// Ordering is by total duration: what is being looked for in diagnostics is "where the time
    /// goes". Ordering by count would put a fast but frequently called command at the top and hide
    /// the genuinely expensive one.
    /// </remarks>
    [Fact]
    public void ShouldOrderByTotalDurationDescending()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        // status is called more often but is cheaper in total.
        for (int i = 0; i < 10; i++)
        {
            Record(log, TimeSpan.FromMilliseconds(1), success: true, "status");
        }

        Record(log, TimeSpan.FromMilliseconds(500), success: true, "log");

        diagnostics.CommandStatistics[0].Name.ShouldBe("log");
    }

    [Fact]
    public void ShouldCountFailures()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        Record(log, TimeSpan.FromMilliseconds(10), success: true, "push");
        Record(log, TimeSpan.FromMilliseconds(10), success: false, "push");
        Record(log, TimeSpan.FromMilliseconds(10), success: false, "push");

        GitCommandStatistics statistics = diagnostics.CommandStatistics.Single();

        statistics.Count.ShouldBe(3);
        statistics.FailureCount.ShouldBe(2);
    }

    /// <remarks>
    /// Computing an average over zero runs would be a division by zero.
    /// </remarks>
    [Fact]
    public void ShouldReportZeroAverageForEmptyStatistics()
    {
        GitCommandStatistics empty = new()
        {
            Name = "log",
            Count = 0,
            TotalDuration = TimeSpan.Zero,
            MaxDuration = TimeSpan.Zero,
            FailureCount = 0,
        };

        empty.AverageDuration.ShouldBe(TimeSpan.Zero);
    }

    // ---------------------------------------------------------- active operations

    [Fact]
    public void ShouldTrackActiveOperationUntilDisposed()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        diagnostics.ActiveOperations.ShouldBeEmpty();

        using (diagnostics.TrackOperation("fetch: origin"))
        {
            diagnostics.ActiveOperations.ShouldBe(["fetch: origin"]);
        }

        diagnostics.ActiveOperations.ShouldBeEmpty();
    }

    /// <remarks>
    /// Two operations with the same name can run at once (a parallel fetch to two remotes).
    /// Keying by name would make one of them invisible; the identity comes from a counter.
    /// </remarks>
    [Fact]
    public void ShouldTrackOperationsWithIdenticalNamesSeparately()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        IDisposable first = diagnostics.TrackOperation("fetch");
        IDisposable second = diagnostics.TrackOperation("fetch");

        diagnostics.ActiveOperations.Count.ShouldBe(2);

        first.Dispose();
        diagnostics.ActiveOperations.Count.ShouldBe(1);

        second.Dispose();
        diagnostics.ActiveOperations.ShouldBeEmpty();
    }

    /// <remarks>
    /// Calling <c>Dispose</c> twice must not drop another operation from the list.
    /// </remarks>
    [Fact]
    public void ShouldIgnoreRepeatedDispose()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        IDisposable scope = diagnostics.TrackOperation("fetch");
        using IDisposable other = diagnostics.TrackOperation("pull");

        scope.Dispose();
        scope.Dispose();

        diagnostics.ActiveOperations.ShouldBe(["pull"]);
    }

    // ------------------------------------------------------------------- reset

    /// <remarks>
    /// 🔴 A reset must <b>not clear the active operations</b>: they are still running. If they drop
    /// off the list they are assumed to have finished, and the reason for a frozen interface becomes
    /// invisible at exactly that moment.
    /// </remarks>
    [Fact]
    public void ShouldClearStatisticsButKeepActiveOperations()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        Record(log, TimeSpan.FromMilliseconds(10), success: true, "log");
        using IDisposable running = diagnostics.TrackOperation("clone: büyük depo");

        diagnostics.Reset();

        diagnostics.CommandStatistics.ShouldBeEmpty();
        diagnostics.ActiveOperations.ShouldBe(["clone: büyük depo"]);
    }

    // --------------------------------------------------------------- lifecycle

    /// <remarks>
    /// <c>Dispose</c> must release the subscription; if it does not, the log keeps the diagnostics
    /// object alive indefinitely and a long session leaks.
    /// </remarks>
    [Fact]
    public void ShouldStopCollectingAfterDispose()
    {
        InMemoryGitCommandLog log = new();
        PerformanceDiagnostics diagnostics = new(log);

        Record(log, TimeSpan.FromMilliseconds(10), success: true, "log");
        diagnostics.CommandStatistics.Count.ShouldBe(1);

        diagnostics.Dispose();
        Record(log, TimeSpan.FromMilliseconds(10), success: true, "status");

        diagnostics.CommandStatistics.Count.ShouldBe(1);
    }

    [Fact]
    public void ShouldReportMemoryAndUptime()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        MemorySnapshot memory = diagnostics.Memory;

        memory.ManagedBytes.ShouldBeGreaterThan(0);
        memory.Gen0.ShouldBeGreaterThanOrEqualTo(0);

        diagnostics.Uptime.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    // --------------------------------------------------------- null implementation

    /// <remarks>
    /// With diagnostics off no call may blow up — in particular the object returned by
    /// <c>TrackOperation</c> cannot be <see langword="null"/>, it is used inside a <c>using</c>.
    /// </remarks>
    [Fact]
    public void NullDiagnosticsShouldBeSafe()
    {
        IPerformanceDiagnostics diagnostics = NullPerformanceDiagnostics.Instance;

        diagnostics.ActiveOperations.ShouldBeEmpty();
        diagnostics.CommandStatistics.ShouldBeEmpty();
        diagnostics.Uptime.ShouldBe(TimeSpan.Zero);
        diagnostics.Reset();

        using IDisposable scope = diagnostics.TrackOperation("iş");
        scope.ShouldNotBeNull();
    }

    /// <summary>
    /// Writes a single run record into the log.
    /// </summary>
    /// <remarks>
    /// <paramref name="arguments"/> is passed without <c>git</c>; the text that lands in the log is
    /// produced by <see cref="GitCommand.ToDisplayString"/> — so that the string the test produces
    /// and the string that is actually recorded go through the same path.
    /// </remarks>
    private static void Record(
        InMemoryGitCommandLog log,
        TimeSpan duration,
        bool success,
        params string[] arguments)
    {
        GitCommand command = GitCommand.Create("/tmp", arguments);

        log.Record(new GitResult(
            command,
            exitCode: success ? 0 : 1,
            standardOutput: [],
            standardError: success ? string.Empty : "hata",
            duration: duration));
    }
}
