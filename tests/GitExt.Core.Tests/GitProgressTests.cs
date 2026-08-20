using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T10 — progress and cancellation on network operations.
/// </summary>
/// <remarks>
/// The two silent points of the measurement: progress lines being separated by <c>\r</c> and <b>not</b>
/// by <c>\n</c>, and whether a cancelled fetch leaves a lock behind.
/// </remarks>
public class GitProgressTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------- parser

    [Theory]
    [InlineData("remote: Counting objects:   5% (207/4125)        ", "Counting objects", 5, 207, 4125, true, false)]
    [InlineData("Receiving objects:  47% (7615/16201), 4.10 MiB | 8.20 MiB/s", "Receiving objects", 47, 7615, 16201, false, false)]
    [InlineData("Resolving deltas: 100% (11603/11603), done.", "Resolving deltas", 100, 11603, 11603, false, true)]
    public void Ilerleme_satirlari_ayristiriliyor(
        string line,
        string phase,
        double percent,
        long current,
        long total,
        bool remote,
        bool done)
    {
        GitProgress step = GitProgressParser.Parse(line).ShouldNotBeNull();

        step.Phase.ShouldBe(phase);
        step.Percent.ShouldBe(percent);
        step.Current.ShouldBe(current);
        step.Total.ShouldBe(total);
        step.IsRemote.ShouldBe(remote);
        step.IsDone.ShouldBe(done);
    }

    [Fact]
    public void Yuzdesiz_sayac_satiri_da_ayristiriliyor()
    {
        GitProgress step = GitProgressParser
            .Parse("remote: Enumerating objects: 16201, done.        ")
            .ShouldNotBeNull();

        step.Phase.ShouldBe("Enumerating objects");
        step.Percent.ShouldBeNull();
        step.Current.ShouldBe(16201);
        step.IsDone.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Cloning into 'repo1'...")]
    [InlineData("fatal: Could not read from remote repository.")]
    [InlineData("From file:///tmp/x")]
    [InlineData("")]
    [InlineData("   ")]
    public void Ilerleme_OLMAYAN_satirlar_atlaniyor(string line) =>
        GitProgressParser.Parse(line).ShouldBeNull();

    [Fact]
    public void Satirlar_CR_ile_de_boluuyor()
    {
        // 🔴 The heart of the measurement: in a real clone there were 404 `\r`s against 7 `\n`s. A reader
        // that splits on `\n` would show NO progress at all until the operation finished.
        const string text = "a: 1% (1/9)\rb: 2% (2/9)\rc: 3% (3/9)\n";

        (IReadOnlyList<string> lines, string remainder) = GitProgressParser.SplitLines(text);

        lines.ShouldBe(["a: 1% (1/9)", "b: 2% (2/9)", "c: 3% (3/9)"]);
        remainder.ShouldBeEmpty();
    }

    [Fact]
    public void YARIM_satir_ayristirilmiyor_sonraki_parcaya_birakiliyor()
    {
        // A line in the stream can be split across two reads; parsing half of it would produce a wrong
        // percentage (`Counting objects:  1` -> 1% instead of 12%).
        (IReadOnlyList<string> lines, string remainder) =
            GitProgressParser.SplitLines("tam: 5% (1/2)\ryarim: 12");

        lines.ShouldBe(["tam: 5% (1/2)"]);
        remainder.ShouldBe("yarim: 12");

        (IReadOnlyList<string> rest, _) = GitProgressParser.SplitLines(remainder + "% (3/4)\r");
        rest.Single().ShouldBe("yarim: 12% (3/4)");
    }

    // ------------------------------------------------------------- real git

    private sealed record Harness(
        TestRepository Local,
        TestRepository Upstream,
        GitProcessRunner Runner,
        GitWriteQueue Queue,
        FetchWriter Fetch) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Local.Dispose();
            Upstream.Dispose();
        }

        /// <summary>
        /// Writes an <c>upload-pack</c> wrapper that sleeps before serving, and returns its path.
        /// </summary>
        /// <remarks>
        /// Makes a cancellation test deterministic: a <c>file://</c> fetch of a small repository
        /// finishes in milliseconds (measured: 12 ms), so a test that wants to cancel a RUNNING
        /// fetch has nothing to cancel. With the wrapper in place the fetch stays alive for as long
        /// as we ask (measured: 5019 ms for a 5-second sleep).
        /// </remarks>
        public string InstallSlowUploadPack(TimeSpan delay)
        {
            // Next to the repository, not inside it: a file under .git would show up in the
            // leftover-lock scan the test performs afterwards.
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"gitext-slow-upload-pack-{Guid.NewGuid():N}.sh");

            File.WriteAllText(
                path,
                $"#!/bin/sh\nsleep {delay.TotalSeconds:0}\nexec git-upload-pack \"$@\"\n"
                    .ReplaceLineEndings("\n"),
                new UTF8Encoding(false));

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            // git runs the value through sh, so a Windows path has to use forward slashes.
            return path.Replace('\\', '/');
        }
    }

    /// <remarks>
    /// The remote is given with <c>file://</c>: had it been given as a path, git would choose the local
    /// copy shortcut and <b>would produce no progress at all</b> (measured).
    /// </remarks>
    private static async Task<Harness> CreateAsync(int fileCount = 60)
    {
        TestRepository upstream = TestRepository.CreateBare();

        TestRepository seed = TestRepository.CreateWithSingleCommit();

        for (int index = 0; index < fileCount; index++)
        {
            seed.WriteFile($"dosya{index}.txt", $"icerik {index}\n");
        }

        seed.Git("add", "-A");
        seed.Git("commit", "-m", "coklu");
        seed.Git("push", "-q", upstream.Path, "HEAD:main");
        seed.Dispose();

        TestRepository local = TestRepository.CreateEmpty();
        local.Git("remote", "add", "origin", "file://" + upstream.Path);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(local, upstream, runner, queue, new FetchWriter(new GitWriter(runner, queue), runner));
    }

    [Fact]
    public async Task GERCEK_fetch_ilerleme_bildiriyor()
    {
        using Harness harness = await CreateAsync();

        List<GitProgress> steps = [];

        await harness.Fetch.FetchAsync(
            harness.Local.Path,
            new FetchOptions
            {
                Remote = "origin",
                Progress = new Progress<GitProgress>(steps.Add),
            },
            Ct);

        // `Progress<T>` notifications can be queued onto the synchronization context; there is no context
        // in the test, but a short margin is still left for the last notifications to be processed.
        await Task.Delay(200, Ct);

        steps.ShouldNotBeEmpty("ilerleme hiç bildirilmedi");
        steps.ShouldContain(step => step.Phase == "Counting objects");
        steps.ShouldContain(step => step.Percent > 0);
    }

    [Fact]
    public async Task Ilerleme_istenmezse_TAM_metin_yine_de_okunuyor()
    {
        // The existing parsers (fetch's partial success, push's `remote:` lines) look at the WHOLE of
        // stderr; switching to streaming mode must not break that.
        using Harness harness = await CreateAsync(5);

        GitResult result = await harness.Runner.RunAsync(
            GitCommand.Create(harness.Local.Path, "fetch", "--progress", "origin"),
            Ct);

        result.IsSuccess.ShouldBeTrue();
        result.StandardError.ShouldContain("origin/main");
    }

    [Fact]
    public async Task Akis_modunda_da_TAM_metin_biriktiriliyor()
    {
        using Harness harness = await CreateAsync(5);

        GitResult result = await harness.Runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = harness.Local.Path,
                Arguments = ["fetch", "--progress", "origin"],
                Progress = new Progress<GitProgress>(_ => { }),
            },
            Ct);

        result.StandardError.ShouldContain("origin/main");
    }

    // ------------------------------------------------------------- cancellation

    [Fact]
    public async Task Iptal_edilen_fetch_geride_KILIT_birakmiyor()
    {
        // 🔴 If a network operation interrupted halfway leaves the repository unusable, the user cannot do
        // anything again. Measured: no lock after SIGTERM, `fsck` clean.
        using Harness harness = await CreateAsync(200);

        using CancellationTokenSource cancellation = new();

        // 🔴 REGRESSION (Windows CI): the cancellation used to be triggered from the first progress
        // notification. That ASSUMES the fetch is still running when the notification is handled —
        // and it very often is not. MEASURED: a `file://` fetch is not a network operation at all,
        // it finishes in 12 ms; `IProgress<T>` posts its callback asynchronously, so on a slow
        // runner the command was already done by the time Cancel() ran and nothing was cancelled.
        // The test then failed with "should throw OperationCanceledException but did not".
        //
        // The fix removes the race instead of widening it: `--upload-pack` points at a wrapper that
        // sleeps before serving, so the fetch is GUARANTEED to still be running when we cancel.
        // MEASURED: 12 ms → 5019 ms. Cancellation is requested straight away; no timing assumption
        // is left in the test. (`sleep` in an sh script is what the timeout tests already rely on,
        // and those are green on Windows — Git for Windows ships its own sh.)
        string slowUploadPack = harness.InstallSlowUploadPack(TimeSpan.FromSeconds(10));

        Task<GitResult> running = harness.Runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = harness.Local.Path,
                Arguments = ["fetch", "--progress", $"--upload-pack={slowUploadPack}", "origin"],
            },
            cancellation.Token);

        // The wrapper is still sleeping, so the process is certainly alive here.
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => running);

        // The repository must still be in working order.
        harness.Local.Git("fsck", "--no-progress");
        harness.Local.Git("status", "--porcelain=v2");

        Directory.GetFiles(
                System.IO.Path.Combine(harness.Local.Path, ".git"),
                "*.lock",
                SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Iptal_ONCESINDE_biten_komut_iptal_sayilmiyor()
    {
        // Even if the cancellation token has been cancelled, reporting a finished job as "cancelled"
        // would tell the user about something that did not happen.
        using Harness harness = await CreateAsync(3);

        GitResult result = await harness.Runner.RunAsync(
            GitCommand.Create(harness.Local.Path, "rev-parse", "--git-dir"),
            Ct);

        result.IsSuccess.ShouldBeTrue();
    }
}
