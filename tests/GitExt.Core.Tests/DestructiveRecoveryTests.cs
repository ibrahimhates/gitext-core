using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T22 — end-to-end safety verification.
/// </summary>
/// <remarks>
/// <para>
/// The phase rule: <i>"Before every operation that rewrites history the reflog position is recorded
/// and the 'how do I undo this' information is always offered to the user."</i> This class proves
/// that the promise is <b>kept</b>: for every destructive operation the chain <b>apply → undo → did
/// the repository return exactly to its old state</b> is run.
/// </para>
/// <para>
/// The comparison criterion is not the commit count or <c>HEAD</c>, but the repository's <b>full tree
/// object</b>: <c>rev-parse HEAD^{tree}</c>. If the content differs by a single byte the tree SHA
/// changes. "HEAD is the same" does not show that the files are the same either — the index or the
/// working tree may be corrupted.
/// </para>
/// </remarks>
public class DestructiveRecoveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        ResetWriter Reset,
        SequencerWriter Sequencer,
        RebaseWriter Rebase,
        StashWriter Stash,
        SafetyPointRecorder Safety,
        GitWriteQueue Queue) : IDisposable
    {
        public string Path => Repository.Path;

        public string Head => Repository.Git("rev-parse", "HEAD").Trim();

        /// <summary>Fingerprint of the repository's content.</summary>
        public string TreeId => Repository.Git("rev-parse", "HEAD^{tree}").Trim();

        /// <summary>State of the working tree and the index.</summary>
        public string Status => Repository.Git("status", "--porcelain=v2").Trim();

        /// <summary>
        /// Is there <b>corruption</b> in the object database?
        /// </summary>
        /// <remarks>
        /// ⚠️ <c>dangling</c> lines are filtered out: they are NOT corruption, only unreachable objects
        /// that have not been collected yet, and <c>fsck</c> exits with code <b>0</b> even with them
        /// present (measured — they normally show up after a stash pop and after rebase --abort).
        /// Counting all of them as errors had turned two correctly working tests red.
        /// <para>
        /// That the exit code is 0 is guaranteed separately: <see cref="TestRepository.Git"/>
        /// throws on a non-zero exit.
        /// </para>
        /// </remarks>
        public string Fsck => string.Join(
            '\n',
            Repository.Git("fsck", "--no-progress")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith("dangling", StringComparison.Ordinal)))
            .Trim();

        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();

        // A three-commit history + one side branch: enough for all of the destructive operations.
        foreach (int index in Enumerable.Range(1, 3))
        {
            repository.WriteFile($"f{index}.txt", $"icerik {index}\n");
            repository.Git("add", "-A");
            repository.Git("commit", "-m", $"c{index}");
        }

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();
        GitWriter writer = new(runner, queue);
        SafetyPointRecorder safety = new(runner);

        return new Harness(
            repository,
            new ResetWriter(writer, runner, safety),
            new SequencerWriter(writer, runner, safety),
            new RebaseWriter(writer, runner, safety),
            new StashWriter(writer, runner),
            safety,
            queue);
    }

    /// <summary>
    /// Actually runs the command a safety point <b>shows</b>.
    /// </summary>
    /// <remarks>
    /// The critical point: it is not a command the test knows itself, it is the command <b>shown to the
    /// user</b> that gets run. Otherwise the test would still pass while a wrong command was on screen.
    /// </remarks>
    private static void RunRecovery(Harness harness, SafetyPoint point)
    {
        string[] parts = point.RecoveryCommand.Split(' ');

        parts[0].ShouldBe("git", "geri alma komutu bir git komutu olmalı");
        harness.Repository.Git(parts[1..]);
    }

    // ==================================================== reset

    [Theory]
    [InlineData(ResetMode.Soft)]
    [InlineData(ResetMode.Mixed)]
    [InlineData(ResetMode.Hard)]
    public async Task RESET_geri_alinabiliyor(ResetMode mode)
    {
        using Harness harness = await CreateAsync();

        string head = harness.Head;
        string tree = harness.TreeId;

        SafetyPoint point = await harness.Reset.ResetAsync(
            harness.Path, new ResetOptions { Target = "HEAD~2", Mode = mode }, Ct);

        harness.Head.ShouldNotBe(head, "işlem gerçekten bir şey yapmalı");

        RunRecovery(harness, point);

        harness.Head.ShouldBe(head);
        harness.TreeId.ShouldBe(tree, "içerik birebir dönmeli");
        harness.Fsck.ShouldBeEmpty();
    }

    [Fact]
    public async Task RESET_sonrasi_calisma_agaci_da_TEMIZ_donuyor()
    {
        // `--soft`/`--mixed` leave traces in the working tree; the undo command (`--hard`)
        // must clean those up too, otherwise "returned to its old state" would be a half-truth.
        using Harness harness = await CreateAsync();
        harness.Status.ShouldBeEmpty();

        SafetyPoint point = await harness.Reset.ResetAsync(
            harness.Path, new ResetOptions { Target = "HEAD~2", Mode = ResetMode.Mixed }, Ct);

        harness.Status.ShouldNotBeEmpty("mixed reset dosyaları stage'siz bırakır");

        RunRecovery(harness, point);

        harness.Status.ShouldBeEmpty();
    }

    // ==================================================== cherry-pick

    [Fact]
    public async Task CHERRY_PICK_geri_alinabiliyor()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Git("checkout", "-q", "-b", "yan", "HEAD~2");
        harness.Repository.WriteFile("yan.txt", "yan\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "yan commit");
        string picked = harness.Head;

        harness.Repository.Git("checkout", "-q", "main");

        string head = harness.Head;
        string tree = harness.TreeId;

        SequencerResult result = await harness.Sequencer.RunAsync(
            harness.Path,
            new SequencerOptions
            {
                Operation = SequencerOperation.CherryPick,
                Commits = [picked],
            },
            Ct);

        harness.Head.ShouldNotBe(head);

        RunRecovery(harness, result.SafetyPoint);

        harness.Head.ShouldBe(head);
        harness.TreeId.ShouldBe(tree);
        File.Exists(Path.Combine(harness.Path, "yan.txt")).ShouldBeFalse();
        harness.Fsck.ShouldBeEmpty();
    }

    // ==================================================== revert

    [Fact]
    public async Task REVERT_geri_alinabiliyor()
    {
        using Harness harness = await CreateAsync();

        string head = harness.Head;
        string tree = harness.TreeId;

        SequencerResult result = await harness.Sequencer.RunAsync(
            harness.Path,
            new SequencerOptions { Operation = SequencerOperation.Revert, Commits = ["HEAD"] },
            Ct);

        harness.Head.ShouldNotBe(head);
        File.Exists(Path.Combine(harness.Path, "f3.txt")).ShouldBeFalse("revert dosyayı kaldırmalı");

        RunRecovery(harness, result.SafetyPoint);

        harness.Head.ShouldBe(head);
        harness.TreeId.ShouldBe(tree);
        File.Exists(Path.Combine(harness.Path, "f3.txt")).ShouldBeTrue();
        harness.Fsck.ShouldBeEmpty();
    }

    // ==================================================== rebase

    [Fact]
    public async Task REBASE_geri_alinabiliyor()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Git("checkout", "-q", "-b", "yan", "HEAD~2");
        harness.Repository.WriteFile("yan.txt", "yan\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "yan commit");

        string head = harness.Head;
        string tree = harness.TreeId;

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path, new RebaseOptions { Upstream = "main" }, Ct);

        result.Outcome.ShouldBe(RebaseOutcome.Completed);
        harness.Head.ShouldNotBe(head, "rebase commit'leri yeniden yazmalı");

        RunRecovery(harness, result.SafetyPoint);

        harness.Head.ShouldBe(head);
        harness.TreeId.ShouldBe(tree);
        harness.Fsck.ShouldBeEmpty();
    }

    [Fact]
    public async Task INTERACTIVE_rebase_geri_alinabiliyor()
    {
        // The most dangerous operation of the phase: commits are deleted, squashed, reordered.
        using Harness harness = await CreateAsync();

        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Repository.WriteFile("a.txt", "a\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "a");
        harness.Repository.WriteFile("b.txt", "b\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "b");

        string head = harness.Head;
        string tree = harness.TreeId;

        IReadOnlyList<RebaseStep> steps =
            await harness.Rebase.ReadStepsAsync(harness.Path, "main", cancellationToken: Ct);

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path,
            new RebaseOptions
            {
                Upstream = "main",
                Steps = [steps[0], steps[1] with { Action = RebaseAction.Drop }],
            },
            Ct);

        result.Outcome.ShouldBe(RebaseOutcome.Completed);
        File.Exists(Path.Combine(harness.Path, "b.txt")).ShouldBeFalse("commit düşürüldü");

        RunRecovery(harness, result.SafetyPoint);

        harness.Head.ShouldBe(head);
        harness.TreeId.ShouldBe(tree);
        File.Exists(Path.Combine(harness.Path, "b.txt")).ShouldBeTrue("düşürülen commit geri geldi");
        harness.Fsck.ShouldBeEmpty();
    }

    [Fact]
    public async Task Yarim_kalmis_rebase_ABORT_ile_geri_donuyor()
    {
        // The second form of the undo path: aborting before the operation completes.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("f1.txt", "ortak\n");
        harness.Repository.Git("commit", "-am", "ortak");

        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Repository.WriteFile("f1.txt", "yan\n");
        harness.Repository.Git("commit", "-am", "yan");

        harness.Repository.Git("checkout", "-q", "main");
        harness.Repository.WriteFile("f1.txt", "ana\n");
        harness.Repository.Git("commit", "-am", "ana");

        harness.Repository.Git("checkout", "-q", "yan");

        string head = harness.Head;
        string tree = harness.TreeId;

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path, new RebaseOptions { Upstream = "main" }, Ct);

        result.Outcome.ShouldBe(RebaseOutcome.Conflicted);

        harness.Repository.Git("rebase", "--abort");

        harness.Head.ShouldBe(head);
        harness.TreeId.ShouldBe(tree);
        harness.Status.ShouldBeEmpty("iptal sonrası çakışma izi kalmamalı");
        harness.Fsck.ShouldBeEmpty();
    }

    // ==================================================== stash

    [Fact]
    public async Task STASH_pop_calisma_agacini_BIREBIR_geri_getiriyor()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("f1.txt", "stageli degisiklik\n");
        harness.Repository.Git("add", "f1.txt");
        harness.Repository.WriteFile("f2.txt", "stagesiz degisiklik\n");

        string before = harness.Status;

        await harness.Stash.PushAsync(harness.Path, new StashPushOptions(), Ct);

        harness.Status.ShouldBeEmpty("stash sonrası ağaç temiz olmalı");

        StashApplyResult result =
            await harness.Stash.ApplyAsync(harness.Path, "stash@{0}", drop: true, Ct);

        result.IndexRestored.ShouldBeTrue();

        // 🔴 The real exam: EXACTLY the same state, including the staged/unstaged distinction.
        harness.Status.ShouldBe(before);
        harness.Fsck.ShouldBeEmpty();
    }

    // ==================================================== reflog

    [Fact]
    public async Task Reflog_HER_yikici_islemden_sonra_geri_donus_sunuyor()
    {
        // Even if the user loses the undo command, the reflog browser must find it —
        // the phase's last safety net.
        using Harness harness = await CreateAsync();

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        ReflogReader reflog = new(new GitProcessRunner(executable));

        string lost = harness.Head;
        string tree = harness.TreeId;

        await harness.Reset.ResetAsync(
            harness.Path, new ResetOptions { Target = "HEAD~2", Mode = ResetMode.Hard }, Ct);

        IReadOnlyList<ReflogEntry> entries =
            await reflog.ReadAsync(harness.Path, "HEAD", cancellationToken: Ct);

        ReflogEntry? found = entries.FirstOrDefault(entry => entry.ObjectId == lost);

        found.ShouldNotBeNull("kaybolan commit reflog'da bulunmalı");
        found.IsUnreachable.ShouldBeTrue();

        // Does the command the browser shows actually work?
        string[] parts = found.RecoveryCommand.Split(' ');
        harness.Repository.Git(parts[1..]);

        harness.Head.ShouldBe(lost);
        harness.TreeId.ShouldBe(tree);
        harness.Fsck.ShouldBeEmpty();
    }

    // ==================================================== safety point

    [Fact]
    public async Task Her_yikici_yazici_GUVENLIK_NOKTASI_aliyor()
    {
        // If a writer skips this, the user never sees the undo path. Do all three make the
        // same promise?
        using Harness harness = await CreateAsync();
        string head = harness.Head;

        SafetyPoint reset = await harness.Reset.ResetAsync(
            harness.Path, new ResetOptions { Target = "HEAD", Mode = ResetMode.Soft }, Ct);

        SequencerResult revert = await harness.Sequencer.RunAsync(
            harness.Path,
            new SequencerOptions { Operation = SequencerOperation.Revert, Commits = ["HEAD"] },
            Ct);

        RebaseResult rebase = await harness.Rebase.RebaseAsync(
            harness.Path, new RebaseOptions { Upstream = "HEAD~1" }, Ct);

        reset.ObjectId.ShouldBe(head);
        reset.RecoveryCommand.ShouldNotBeEmpty();
        revert.SafetyPoint.RecoveryCommand.ShouldNotBeEmpty();
        rebase.SafetyPoint.RecoveryCommand.ShouldNotBeEmpty();

        // None of them uses a moving reference.
        foreach (string command in new[]
                 {
                     reset.RecoveryCommand,
                     revert.SafetyPoint.RecoveryCommand,
                     rebase.SafetyPoint.RecoveryCommand,
                 })
        {
            command.ShouldNotContain("ORIG_HEAD");
            command.ShouldNotContain("@{");
        }
    }
}
