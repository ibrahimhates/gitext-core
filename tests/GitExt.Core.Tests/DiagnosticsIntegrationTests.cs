using GitExt.Core.Diagnostics;
using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T03 — end-to-end verification of the diagnostics with real git calls.
/// </summary>
/// <remarks>
/// The unit tests verify the collector itself; the question here is different: <b>is it really
/// wired up?</b> If a diagnostics panel is not attached to the path it thinks it measures, it silently
/// stays empty and that emptiness reads as "no problem".
/// </remarks>
public class DiagnosticsIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <remarks>
    /// The measurement point is attached to <see cref="GitProcessRunner"/>: per ADR-0002 every git
    /// call goes through there. Adding it to the writer/reader classes one by one would produce
    /// paths where someone forgot to add it.
    /// </remarks>
    [Fact]
    public async Task ShouldRecordStatisticsForRealGitCalls()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct).ConfigureAwait(true);
        GitProcessRunner runner = new(executable, log, logger: null, diagnostics: diagnostics);

        await runner.RunAsync(GitCommand.Create(repo.Path, "status", "--porcelain=v2"), Ct)
            .ConfigureAwait(true);
        await runner.RunAsync(GitCommand.Create(repo.Path, "log", "--oneline"), Ct)
            .ConfigureAwait(true);
        await runner.RunAsync(GitCommand.Create(repo.Path, "log", "-n", "1"), Ct)
            .ConfigureAwait(true);

        IReadOnlyList<GitCommandStatistics> statistics = diagnostics.CommandStatistics;

        statistics.Select(s => s.Name).ShouldBe(["log", "status"], ignoreOrder: true);
        statistics.Single(s => s.Name == "log").Count.ShouldBe(2);
        statistics.Single(s => s.Name == "status").Count.ShouldBe(1);

        // A real process takes a measurable amount of time; getting zero would mean the measurement
        // was never wired up at all.
        statistics.Single(s => s.Name == "status").TotalDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    /// <remarks>
    /// 🔴 A failed command must be counted too. Counting only the successful ones would let
    /// the diagnostics panel show "everything is fine" while the user's UI is stuck in an
    /// error loop.
    /// </remarks>
    [Fact]
    public async Task ShouldCountFailedRealGitCall()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct).ConfigureAwait(true);
        GitProcessRunner runner = new(executable, log, logger: null, diagnostics: diagnostics);

        // A ref that does not exist: git exits with a non-zero code.
        await runner.RunAsync(
                GitCommand.Create(repo.Path, "rev-parse", "--verify", "yok-boyle-bir-ref"),
                Ct)
            .ConfigureAwait(true);

        GitCommandStatistics statistics = diagnostics.CommandStatistics.Single();

        statistics.Name.ShouldBe("rev-parse");
        statistics.FailureCount.ShouldBe(1);
    }

    /// <remarks>
    /// It must drop off the active list when the command finishes. If it does not, the panel shows
    /// finished work as "in progress" — turning the diagnosis on its head.
    /// </remarks>
    [Fact]
    public async Task ShouldNotLeaveFinishedCallsActive()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct).ConfigureAwait(true);
        GitProcessRunner runner = new(executable, log, logger: null, diagnostics: diagnostics);

        await runner.RunAsync(GitCommand.Create(repo.Path, "status"), Ct).ConfigureAwait(true);

        diagnostics.ActiveOperations.ShouldBeEmpty();
    }

    /// <remarks>
    /// The streaming path (<see cref="GitProcessRunner.StreamNulSeparatedAsync"/>) is a separate code
    /// path; the commit list comes from exactly there and it is also the duration people care about most
    /// in the diagnostics. If it were not tracked, the panel would never show the app's most expensive call.
    /// </remarks>
    [Fact]
    public async Task ShouldNotLeaveStreamingCallsActive()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct).ConfigureAwait(true);
        GitProcessRunner runner = new(executable, log, logger: null, diagnostics: diagnostics);

        GitCommand command = GitCommand.Create(repo.Path, "log", "-z", "--format=%H");

        await foreach (string _ in runner.StreamNulSeparatedAsync(command, Ct).ConfigureAwait(true))
        {
            // The stream is consumed; what we care about is that the list empties out when it finishes.
        }

        diagnostics.ActiveOperations.ShouldBeEmpty();
    }
}
