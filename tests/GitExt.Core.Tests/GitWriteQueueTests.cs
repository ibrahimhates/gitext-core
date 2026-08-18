using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T01 — Serializing write operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED (before any code was written):</b> git does <b>not</b> wait on concurrent writes. When
/// 8 parallel <c>git add</c> runs were started on the same repository, 7 of them failed with
/// <c>fatal: Unable to create '…/index.lock': File exists</c>.
/// </para>
/// <para>
/// That is why the real test here is done <b>with real git</b>: a fake operation running in order does
/// not show that the queue is correct, git actually not complaining does.
/// </para>
/// </remarks>
public class GitWriteQueueTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const int Writers = 8;

    [Fact]
    public async Task Ayni_depoya_eszamanli_yazmalar_seri_calisir()
    {
        using GitWriteQueue queue = new();

        int active = 0;
        int maxActive = 0;
        object gate = new();

        async Task Work()
        {
            await queue.RunAsync("/tmp/depo/.git", async _ =>
            {
                lock (gate)
                {
                    active++;
                    maxActive = Math.Max(maxActive, active);
                }

                await Task.Delay(15, Ct);

                lock (gate)
                {
                    active--;
                }
            }, Ct);
        }

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => Work()));

        maxActive.ShouldBe(1);
    }

    [Fact]
    public async Task Farkli_depolar_birbirini_BEKLEMEZ()
    {
        // Worktrees have separate indexes (measured: concurrent adds in two worktrees do not collide).
        // Putting them all in a single queue would make the user wait for no reason.
        using GitWriteQueue queue = new();

        using SemaphoreSlim bothStarted = new(0, 2);
        using SemaphoreSlim release = new(0, 2);

        async Task Work(string gitDirectory)
        {
            await queue.RunAsync(gitDirectory, async _ =>
            {
                bothStarted.Release();
                await release.WaitAsync(Ct);
            }, Ct);
        }

        Task first = Work("/tmp/bir/.git");
        Task second = Work("/tmp/iki/.git");

        // Both must be able to start: were they in the same queue, the second would never start.
        await bothStarted.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await bothStarted.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        release.Release(2);

        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Ayni_depo_farkli_yazimla_verilse_de_ayni_kuyruga_duser()
    {
        // "/repo/.git" and "/repo/.git/" are the same repository; if they diverged, serialization would
        // SILENTLY be disabled.
        using GitWriteQueue queue = new();

        int active = 0;
        int maxActive = 0;
        object gate = new();

        async Task Work(string path)
        {
            await queue.RunAsync(path, async _ =>
            {
                lock (gate)
                {
                    active++;
                    maxActive = Math.Max(maxActive, active);
                }

                await Task.Delay(20, Ct);

                lock (gate)
                {
                    active--;
                }
            }, Ct);
        }

        await Task.WhenAll(
            Work("/tmp/depo/.git"),
            Work("/tmp/depo/.git/"),
            Work("/tmp/depo/alt/../.git"));

        maxActive.ShouldBe(1);
        queue.TrackedRepositories.ShouldBe(1);
    }

    [Fact]
    public async Task Islem_hata_verse_de_kuyruk_serbest_kalir()
    {
        // If it is not released the repository is locked permanently and the application looks frozen.
        using GitWriteQueue queue = new();

        await Should.ThrowAsync<InvalidOperationException>(
            queue.RunAsync("/tmp/depo/.git", _ => throw new InvalidOperationException("patladı"), Ct));

        bool ran = false;

        await queue.RunAsync("/tmp/depo/.git", _ =>
        {
            ran = true;
            return Task.CompletedTask;
        }, Ct);

        ran.ShouldBeTrue();
    }

    [Fact]
    public async Task Iptal_beklemeyi_sonlandirir()
    {
        using GitWriteQueue queue = new();
        using SemaphoreSlim started = new(0, 1);
        using SemaphoreSlim release = new(0, 1);

        Task holder = queue.RunAsync("/tmp/depo/.git", async _ =>
        {
            started.Release();
            await release.WaitAsync(Ct);
        }, Ct);

        await started.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        using CancellationTokenSource cancelled = new();
        Task waiting = queue.RunAsync("/tmp/depo/.git", _ => Task.CompletedTask, cancelled.Token);

        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(waiting);

        release.Release();
        await holder;
    }

    // ---- Real git ----

    [Fact]
    public void GERCEK_git_kilit_varken_BEKLEMEZ_hemen_duser()
    {
        // The DETERMINISTIC proof of why the queue exists: the race test below depends on timing
        // and can weaken on a slow CI machine; this test always gives the same
        // result.
        using TestRepository repository = CreateRepositoryWithChanges();

        string lockFile = Path.Combine(repository.Path, ".git", "index.lock");
        File.WriteAllText(lockFile, string.Empty);

        try
        {
            (int exitCode, string error) = repository.TryGit("add", "-A");

            exitCode.ShouldNotBe(0);
            error.ShouldContain("index.lock");
        }
        finally
        {
            File.Delete(lockFile);
        }
    }

    [Fact]
    public void Okumalar_kilit_varken_CALISIR()
    {
        // The basis for the "reads do not enter the queue" decision: when git cannot take the optional
        // lock it silently gives up, it does not error.
        using TestRepository repository = CreateRepositoryWithChanges();

        string lockFile = Path.Combine(repository.Path, ".git", "index.lock");
        File.WriteAllText(lockFile, string.Empty);

        try
        {
            repository.TryGit("status", "--porcelain=v2").ExitCode.ShouldBe(0);
            repository.TryGit("log", "--oneline", "-n", "1").ExitCode.ShouldBe(0);
            repository.TryGit("diff", "--stat").ExitCode.ShouldBe(0);
            repository.TryGit("diff", "--cached", "--stat").ExitCode.ShouldBe(0);
            repository.TryGit("for-each-ref").ExitCode.ShouldBe(0);
        }
        finally
        {
            File.Delete(lockFile);
        }
    }

    [Fact]
    public async Task GERCEK_git_ile_kuyruksuz_yazmalar_cakisiyor()
    {
        // Counter-evidence: to show that the queue really solves something, the problem is first shown
        // to exist. This test does NOT USE the queue.
        // ⚠️ RACE CONDITION — timing-dependent; on a slow CI machine parallelism drops and it may
        // come out as zero on the first try. Retries up to 3 times (max ~450 ms wall-clock) to
        // cover for that, but the test stops immediately when collisions are observed. For the
        // DETERMINISTIC proof of lock-file behavior see `GERCEK_git_kilit_varken_BEKLEMEZ_hemen_duser`.
        using TestRepository repository = CreateRepositoryWithChanges();

        const int maxAttempts = 3;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int failures = 0;

            await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => Task.Run(() =>
            {
                (int exitCode, string error) = repository.TryGit("add", "-A");

                if (exitCode != 0 && error.Contains("index.lock", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref failures);
                }
            }, Ct)));

            if (failures > 0)
            {
                // Collision observed — test passes.
                return;
            }

            // Retry: the repository was fully restored by the failed attempt's git add, so
            // subsequent parallel runs hit the same state again. A brief pause reduces the
            // chance that all processes enter within a single scheduling window.
            await Task.Delay(150, Ct);
        }

        Assert.Fail($"all {maxAttempts} attempts produced zero collisions (CI race condition)");
    }

    [Fact]
    public async Task GERCEK_git_ile_kuyruklu_yazmalar_cakismiyor()
    {
        using TestRepository repository = CreateRepositoryWithChanges();
        using GitWriteQueue queue = new();

        string gitDirectory = Path.Combine(repository.Path, ".git");

        int failures = 0;

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ =>
            queue.RunAsync(gitDirectory, _ => Task.Run(() =>
            {
                (int exitCode, string error) = repository.TryGit("add", "-A");

                if (exitCode != 0 && error.Contains("index.lock", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref failures);
                }
            }, Ct), Ct)));

        failures.ShouldBe(0);
    }

    private static TestRepository CreateRepositoryWithChanges()
    {
        TestRepository repository = TestRepository.CreateEmpty();

        for (int i = 0; i < 150; i++)
        {
            repository.WriteFile($"dosya{i}.txt", $"satir {i}\n");
        }

        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        for (int i = 0; i < 150; i++)
        {
            repository.WriteFile($"dosya{i}.txt", $"satir {i}\ndegisiklik\n");
        }

        return repository;
    }
}
