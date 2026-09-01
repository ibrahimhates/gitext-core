using System.Diagnostics;
using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests.Git;

/// <summary>
/// P14-T01 — does the output reach the caller <b>while the command is still running</b>?
/// </summary>
/// <remarks>
/// The process window stands or falls on this. If the pieces only arrive when the process exits,
/// the window sits empty for the whole operation and then fills up after it no longer matters —
/// which is worse than not having a window, because it looks frozen.
/// </remarks>
public class GitOutputStreamingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<GitProcessRunner> CreateRunnerAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new GitProcessRunner(executable);
    }

    private sealed class Collector : IProgress<GitOutputChunk>
    {
        private readonly Stopwatch _clock;
        private readonly Lock _gate = new();
        private readonly List<(TimeSpan At, GitOutputChunk Chunk)> _chunks = [];

        public Collector(Stopwatch clock) => _clock = clock;

        public IReadOnlyList<(TimeSpan At, GitOutputChunk Chunk)> Chunks
        {
            get
            {
                lock (_gate)
                {
                    return [.. _chunks];
                }
            }
        }

        public void Report(GitOutputChunk value)
        {
            lock (_gate)
            {
                _chunks.Add((_clock.Elapsed, value));
            }
        }
    }

    [Fact]
    public async Task Yavas_hookun_ciktisi_islem_bitmeden_gelir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        repository.InstallHook(
            "pre-commit",
            """
            #!/bin/sh
            i=1
            while [ $i -le 4 ]; do
              echo "hook adim $i"
              sleep 0.4
              i=$((i + 1))
            done
            exit 0
            """);

        repository.WriteFile("yeni.txt", "icerik\n");
        repository.Git("add", "yeni.txt");

        Stopwatch clock = Stopwatch.StartNew();
        Collector collector = new(clock);

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["commit", "-m", "hooklu commit"],
                OutputReceived = collector,
            },
            Ct);

        clock.Stop();

        result.IsSuccess.ShouldBeTrue();

        IReadOnlyList<(TimeSpan At, GitOutputChunk Chunk)> chunks = collector.Chunks;

        chunks.ShouldNotBeEmpty();

        TimeSpan total = clock.Elapsed;
        TimeSpan first = chunks[0].At;

        // The hook runs for ~1.6 s. The first piece has to arrive long before that; a buffered
        // reader would deliver every piece at the very end instead.
        total.ShouldBeGreaterThan(TimeSpan.FromSeconds(1));
        first.ShouldBeLessThan(total - TimeSpan.FromMilliseconds(700));

        string text = string.Concat(chunks.Select(entry => entry.Chunk.Text));
        text.ShouldContain("hook adim 1");
        text.ShouldContain("hook adim 4");
    }

    [Fact]
    public async Task Hata_ciktisi_da_akar_ve_stderr_olarak_isaretlenir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        repository.InstallHook(
            "pre-commit",
            """
            #!/bin/sh
            echo "hook: reddedildi" >&2
            exit 1
            """);

        repository.WriteFile("yeni.txt", "icerik\n");
        repository.Git("add", "yeni.txt");

        Collector collector = new(Stopwatch.StartNew());
        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["commit", "-m", "reddedilecek"],
                OutputReceived = collector,
            },
            Ct);

        result.IsSuccess.ShouldBeFalse();

        IReadOnlyList<(TimeSpan At, GitOutputChunk Chunk)> chunks = collector.Chunks;

        string errorText = string.Concat(
            chunks.Where(entry => entry.Chunk.IsError).Select(entry => entry.Chunk.Text));

        errorText.ShouldContain("hook: reddedildi");
    }

    [Fact]
    public async Task Akis_istenmediginde_calisma_bozulmaz()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["log", "--oneline"],
            },
            Ct);

        result.IsSuccess.ShouldBeTrue();
        result.GetStandardOutputText().ShouldContain("ilk commit");
    }

    [Fact]
    public async Task Akan_parcalar_tam_ciktiyla_ayni_metni_verir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        for (int index = 0; index < 40; index++)
        {
            repository.WriteFile($"dosya{index}.txt", new string('x', 900) + "\n");
            repository.Git("add", $"dosya{index}.txt");
            repository.Commit($"commit {index}");
        }

        Collector collector = new(Stopwatch.StartNew());
        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["log", "--stat"],
                OutputReceived = collector,
            },
            Ct);

        result.IsSuccess.ShouldBeTrue();

        string streamed = string.Concat(
            collector.Chunks.Where(entry => !entry.Chunk.IsError).Select(entry => entry.Chunk.Text));

        streamed.ShouldBe(result.GetStandardOutputText());
    }
}
