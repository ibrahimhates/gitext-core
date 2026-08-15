using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T06 · P07-T07 · P07-T08 · P07-T09 · P07-T10 — operations that rewrite history.
/// </summary>
/// <remarks>
/// The phase rule: before every operation the position is recorded and an undo path is offered. The
/// results are verified by looking at the repository's <b>state</b>, not at git's <b>text</b>.
/// </remarks>
public class HistoryRewriteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        ResetWriter Reset,
        SequencerWriter Sequencer,
        RebaseWriter Rebase,
        GitWriteQueue Queue) : IDisposable
    {
        public string Path => Repository.Path;

        public string Head => Repository.Git("rev-parse", "HEAD").Trim();

        public string[] Subjects => [.. Repository
            .Git("log", "--format=%s")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)];

        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        /// <summary>Produces consecutive commits: c1, c2, c3…</summary>
        public void Chain(int count)
        {
            for (int index = 1; index <= count; index++)
            {
                Repository.WriteFile($"f{index}.txt", $"{index}\n");
                Repository.Git("add", "-A");
                Repository.Git("commit", "-m", $"c{index}");
            }
        }
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("taban.txt", "taban\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "taban");

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
            queue);
    }

    // ==================================================== P07-T06 reset

    [Fact]
    public async Task SOFT_reset_degisikligi_STAGELI_birakiyor()
    {
        using Harness harness = await CreateAsync();
        harness.Chain(2);

        await harness.Reset.ResetAsync(
            harness.Path, new ResetOptions { Target = "HEAD~1", Mode = ResetMode.Soft }, Ct);

        harness.Subjects.ShouldBe(["c1", "taban"]);

        // MEASURED: the file stays on disk and shows up as staged in the index.
        harness.Repository.Git("status", "--porcelain=v2").ShouldContain("A.");
    }

    [Fact]
    public async Task MIXED_reset_degisikligi_STAGESIZ_birakiyor()
    {
        using Harness harness = await CreateAsync();
        harness.Chain(2);

        await harness.Reset.ResetAsync(
            harness.Path, new ResetOptions { Target = "HEAD~1", Mode = ResetMode.Mixed }, Ct);

        harness.Subjects.ShouldBe(["c1", "taban"]);
        File.Exists(Path.Combine(harness.Path, "f2.txt")).ShouldBeTrue("dosya diskte kalmalı");
        harness.Repository.Git("status", "--porcelain=v2").ShouldContain("?");
    }

    [Fact]
    public async Task HARD_reset_calisma_agacini_TEMIZLIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Chain(2);

        await harness.Reset.ResetAsync(
            harness.Path, new ResetOptions { Target = "HEAD~1", Mode = ResetMode.Hard }, Ct);

        harness.Subjects.ShouldBe(["c1", "taban"]);
        File.Exists(Path.Combine(harness.Path, "f2.txt")).ShouldBeFalse();
        harness.Repository.Git("status", "--porcelain=v2").Trim().ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_GERI_ALMA_noktasini_donduruyor()
    {
        using Harness harness = await CreateAsync();
        harness.Chain(2);
        string before = harness.Head;

        SafetyPoint point = await harness.Reset.ResetAsync(
            harness.Path, new ResetOptions { Target = "HEAD~1", Mode = ResetMode.Hard }, Ct);

        point.ObjectId.ShouldBe(before);
        point.RecoveryCommand.ShouldBe($"git reset --hard {before}");

        // Does the undo command REALLY work?
        harness.Repository.Git("reset", "--hard", point.ObjectId);
        harness.Head.ShouldBe(before);
    }

    [Fact]
    public async Task Reset_onizlemesi_DUSECEK_commitleri_sayiyor()
    {
        using Harness harness = await CreateAsync();
        harness.Chain(3);

        ResetPreview preview = await harness.Reset.PreviewAsync(harness.Path, "HEAD~2", Ct);

        preview.IsTargetValid.ShouldBeTrue();
        preview.DroppedCount.ShouldBe(2);
        preview.DroppedCommits.ShouldBe(["c3", "c2"]);
        preview.LosesUncommittedWork(ResetMode.Hard).ShouldBeFalse();
        preview.LosesUncommittedWork(ResetMode.Soft).ShouldBeFalse();
    }

    [Fact]
    public async Task Reset_onizlemesi_KIRLI_agacta_hard_icin_UYARIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Chain(2);
        harness.Repository.WriteFile("f1.txt", "elle degistirildi\n");

        ResetPreview preview = await harness.Reset.PreviewAsync(harness.Path, "HEAD~1", Ct);

        preview.HasUncommittedChanges.ShouldBeTrue();
        preview.LosesUncommittedWork(ResetMode.Hard).ShouldBeTrue();

        // --soft and --mixed do not touch the working tree; a warning would be wrong for them.
        preview.LosesUncommittedWork(ResetMode.Mixed).ShouldBeFalse();
    }

    [Fact]
    public async Task GECERSIZ_hedef_onizlemede_yakalaniyor()
    {
        using Harness harness = await CreateAsync();

        ResetPreview preview = await harness.Reset.PreviewAsync(harness.Path, "boyle-bir-sey-yok", Ct);

        preview.IsTargetValid.ShouldBeFalse();
    }

    [Fact]
    public void Reset_komutunda_ayrac_SONDA()
    {
        // 🔴 MEASURED: `git reset --hard -- <target>` dies with "Cannot do hard reset with paths" —
        // what follows `--` means a PATH to reset. The separator must go at the end; and it is not
        // unnecessary there either: if a file has the same name as a branch, a call without the
        // separator gives "ambiguous argument".
        ResetWriter.Describe(new ResetOptions { Target = "HEAD~1", Mode = ResetMode.Hard })
            .ShouldBe("git reset --hard HEAD~1 --");
    }

    // ============================================ P07-T07/T08 sequencer

    [Fact]
    public async Task CHERRY_PICK_commiti_buraya_uyguluyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Repository.WriteFile("yan.txt", "yan\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "yan commit");
        string picked = harness.Head;

        harness.Repository.Git("checkout", "-q", "main");

        SequencerResult result = await harness.Sequencer.RunAsync(
            harness.Path,
            new SequencerOptions
            {
                Operation = SequencerOperation.CherryPick,
                Commits = [picked],
            },
            Ct);

        result.HasConflicts.ShouldBeFalse();
        result.CommitsCreated.ShouldBe(1);
        harness.Subjects.ShouldContain("yan commit");
        File.Exists(Path.Combine(harness.Path, "yan.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task CHERRY_PICK_cakismasi_HATA_degil_DURUM()
    {
        // The conflict text is on stdout and the exit code is 1; the decision is made by looking at the index.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "a\nb\nc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ortak");

        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Repository.WriteFile("f.txt", "a\nYAN\nc\n");
        harness.Repository.Git("commit", "-am", "yan");
        string picked = harness.Head;

        harness.Repository.Git("checkout", "-q", "main");
        harness.Repository.WriteFile("f.txt", "a\nANA\nc\n");
        harness.Repository.Git("commit", "-am", "ana");

        SequencerResult result = await harness.Sequencer.RunAsync(
            harness.Path,
            new SequencerOptions { Operation = SequencerOperation.CherryPick, Commits = [picked] },
            Ct);

        result.HasConflicts.ShouldBeTrue();
        result.ConflictedPaths.ShouldContain(path => path.Value == "f.txt");
    }

    [Fact]
    public async Task CHERRY_PICK_BILINMEYEN_committe_FIRLATIYOR()
    {
        // Real errors must not be swallowed as a state.
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<GitException>(async () => await harness.Sequencer.RunAsync(
            harness.Path,
            new SequencerOptions
            {
                Operation = SequencerOperation.CherryPick,
                Commits = ["0000000000000000000000000000000000000000"],
            },
            Ct));
    }

    [Fact]
    public async Task NO_COMMIT_commit_ATMIYOR_ve_bunu_SOYLUYOR()
    {
        // The `--squash` lesson from P06-T11: exit code 0 but HEAD does not advance.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Repository.WriteFile("yan.txt", "yan\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "yan commit");
        string picked = harness.Head;
        harness.Repository.Git("checkout", "-q", "main");
        string before = harness.Head;

        SequencerResult result = await harness.Sequencer.RunAsync(
            harness.Path,
            new SequencerOptions
            {
                Operation = SequencerOperation.CherryPick,
                Commits = [picked],
                NoCommit = true,
            },
            Ct);

        harness.Head.ShouldBe(before, "HEAD ilerlememeli");
        result.CommitsCreated.ShouldBe(0);
        result.RequiresCommit.ShouldBeTrue("kullanıcıya hâlâ commit'lemesi gerektiği söylenmeli");
    }

    [Fact]
    public async Task REVERT_degisikligi_geri_aliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "ilk\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "eklendi");

        SequencerResult result = await harness.Sequencer.RunAsync(
            harness.Path,
            new SequencerOptions { Operation = SequencerOperation.Revert, Commits = ["HEAD"] },
            Ct);

        result.CommitsCreated.ShouldBe(1);
        File.Exists(Path.Combine(harness.Path, "f.txt")).ShouldBeFalse("revert dosyayı kaldırmalı");
    }

    [Fact]
    public async Task MERGE_commiti_revert_ederken_EBEVEYN_sayisi_biliniyor()
    {
        // 🔴 MEASURED: reverting a merge commit without `-m` gives rc=128
        // ("is a merge but no -m option was given"). The user has to choose the parent.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Repository.WriteFile("yan.txt", "yan\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "yan");
        harness.Repository.Git("checkout", "-q", "main");
        harness.Repository.WriteFile("ana.txt", "ana\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ana");
        harness.Repository.Git("merge", "--no-ff", "-m", "birlestirme", "yan");

        (await harness.Sequencer.CountParentsAsync(harness.Path, "HEAD", Ct)).ShouldBe(2);
        (await harness.Sequencer.CountParentsAsync(harness.Path, "HEAD~1", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task MERGE_commiti_EBEVEYN_secilerek_revert_ediliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Repository.WriteFile("yan.txt", "yan\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "yan");
        harness.Repository.Git("checkout", "-q", "main");
        harness.Repository.WriteFile("ana.txt", "ana\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ana");
        harness.Repository.Git("merge", "--no-ff", "-m", "birlestirme", "yan");

        SequencerResult result = await harness.Sequencer.RunAsync(
            harness.Path,
            new SequencerOptions
            {
                Operation = SequencerOperation.Revert,
                Commits = ["HEAD"],
                MainlineParent = 1,
            },
            Ct);

        result.CommitsCreated.ShouldBe(1);
        File.Exists(Path.Combine(harness.Path, "yan.txt")).ShouldBeFalse("yan tarafı geri alınmalı");
        File.Exists(Path.Combine(harness.Path, "ana.txt")).ShouldBeTrue("ana hat korunmalı");
    }

    [Fact]
    public void X_bayragi_yalnizca_CHERRY_PICKte()
    {
        SequencerWriter.Describe(new SequencerOptions
        {
            Operation = SequencerOperation.CherryPick,
            Commits = ["abc"],
            RecordOrigin = true,
        }).ShouldBe("git cherry-pick -x abc");

        // Revert already writes the source into its message; `-x` is meaningless there.
        SequencerWriter.Describe(new SequencerOptions
        {
            Operation = SequencerOperation.Revert,
            Commits = ["abc"],
            RecordOrigin = true,
        }).ShouldBe("git revert --no-edit abc");
    }

    // ========================================== P07-T09/T10 rebase

    [Fact]
    public async Task Basit_rebase_commitleri_yeniden_oynatiyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Repository.WriteFile("yan.txt", "yan\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "yan commit");

        harness.Repository.Git("checkout", "-q", "main");
        harness.Repository.WriteFile("ana.txt", "ana\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ana commit");

        harness.Repository.Git("checkout", "-q", "yan");

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path, new RebaseOptions { Upstream = "main" }, Ct);

        result.Outcome.ShouldBe(RebaseOutcome.Completed);

        // "yan commit" now sits on top of "ana commit".
        harness.Subjects.ShouldBe(["yan commit", "ana commit", "taban"]);
    }

    [Fact]
    public async Task Rebase_cakismasi_ILERLEMEYI_bildiriyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("f.txt", "a\nb\nc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ortak");

        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Repository.WriteFile("f.txt", "a\nYAN\nc\n");
        harness.Repository.Git("commit", "-am", "yan");

        harness.Repository.Git("checkout", "-q", "main");
        harness.Repository.WriteFile("f.txt", "a\nANA\nc\n");
        harness.Repository.Git("commit", "-am", "ana");

        harness.Repository.Git("checkout", "-q", "yan");

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path, new RebaseOptions { Upstream = "main" }, Ct);

        result.Outcome.ShouldBe(RebaseOutcome.Conflicted);
        result.ConflictedPaths.ShouldContain(path => path.Value == "f.txt");
        result.TotalSteps.ShouldBe(1);
        result.IsStopped.ShouldBeTrue();
    }

    [Fact]
    public async Task Adimlar_ekrani_doldurmak_icin_SIRAYLA_okunuyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Chain(3);

        IReadOnlyList<RebaseStep> steps =
            await harness.Rebase.ReadStepsAsync(harness.Path, "main", cancellationToken: Ct);

        // Oldest first: the todo list is applied top to bottom.
        steps.Select(step => step.Subject).ShouldBe(["c1", "c2", "c3"]);
        steps.ShouldAllBe(step => step.Action == RebaseAction.Pick);
    }

    [Fact]
    public async Task INTERACTIVE_rebase_ortadaki_commiti_DUSURUYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Chain(3);

        IReadOnlyList<RebaseStep> steps =
            await harness.Rebase.ReadStepsAsync(harness.Path, "main", cancellationToken: Ct);

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path,
            new RebaseOptions
            {
                Upstream = "main",
                Steps =
                [
                    steps[0],
                    steps[1] with { Action = RebaseAction.Drop },
                    steps[2],
                ],
            },
            Ct);

        result.Outcome.ShouldBe(RebaseOutcome.Completed);
        harness.Subjects.ShouldBe(["c3", "c1", "taban"]);
    }

    [Fact]
    public async Task INTERACTIVE_rebase_commitleri_KAYNASTIRIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Chain(3);

        IReadOnlyList<RebaseStep> steps =
            await harness.Rebase.ReadStepsAsync(harness.Path, "main", cancellationToken: Ct);

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path,
            new RebaseOptions
            {
                Upstream = "main",
                Steps =
                [
                    steps[0],
                    steps[1] with { Action = RebaseAction.Fixup },
                    steps[2],
                ],
            },
            Ct);

        result.Outcome.ShouldBe(RebaseOutcome.Completed);

        // `fixup` discards c2's message; its content is squashed into c1.
        harness.Subjects.ShouldBe(["c3", "c1", "taban"]);
        File.Exists(Path.Combine(harness.Path, "f2.txt")).ShouldBeTrue("içerik korunmalı");
    }

    [Fact]
    public async Task INTERACTIVE_rebase_SIRA_degistiriyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Chain(3);

        IReadOnlyList<RebaseStep> steps =
            await harness.Rebase.ReadStepsAsync(harness.Path, "main", cancellationToken: Ct);

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path,
            new RebaseOptions { Upstream = "main", Steps = [steps[2], steps[0], steps[1]] },
            Ct);

        result.Outcome.ShouldBe(RebaseOutcome.Completed);
        harness.Subjects.ShouldBe(["c2", "c1", "c3", "taban"]);
    }

    [Fact]
    public async Task INTERACTIVE_rebase_MESAJI_degistiriyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Chain(1);

        IReadOnlyList<RebaseStep> steps =
            await harness.Rebase.ReadStepsAsync(harness.Path, "main", cancellationToken: Ct);

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path,
            new RebaseOptions
            {
                Upstream = "main",
                Steps = [steps[0] with { Action = RebaseAction.Reword }],
                NewMessage = "yeniden yazılmış başlık\n",
            },
            Ct);

        result.Outcome.ShouldBe(RebaseOutcome.Completed);
        harness.Subjects.ShouldContain("yeniden yazılmış başlık");
    }

    [Fact]
    public async Task EDIT_adiminda_rebase_DURUYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("checkout", "-q", "-b", "yan");
        harness.Chain(2);

        IReadOnlyList<RebaseStep> steps =
            await harness.Rebase.ReadStepsAsync(harness.Path, "main", cancellationToken: Ct);

        RebaseResult result = await harness.Rebase.RebaseAsync(
            harness.Path,
            new RebaseOptions
            {
                Upstream = "main",
                Steps = [steps[0] with { Action = RebaseAction.Edit }, steps[1]],
            },
            Ct);

        // There is NO conflict but the rebase still stopped — even though the exit code is 0.
        result.Outcome.ShouldBe(RebaseOutcome.StoppedForEdit);
        result.ConflictedPaths.ShouldBeEmpty();
        result.IsStopped.ShouldBeTrue();

        harness.Repository.Git("rebase", "--abort");
    }

    // ------------------------------------------------------- todo list

    [Fact]
    public void Todo_listesi_git_in_bekledigi_bicimde_yaziliyor()
    {
        string todo = RebaseTodo.Render(
        [
            new RebaseStep { ObjectId = "aaa111", Subject = "ilk" },
            new RebaseStep { ObjectId = "bbb222", Subject = "ikinci", Action = RebaseAction.Squash },
            new RebaseStep { ObjectId = "ccc333", Subject = "ucuncu", Action = RebaseAction.Drop },
        ]);

        todo.ShouldBe("pick aaa111 # ilk\nsquash bbb222 # ikinci\ndrop ccc333 # ucuncu\n");
    }

    [Fact]
    public void Cok_satirli_konu_todo_yu_BOZMUYOR()
    {
        // A line ending is read as a new command line in the todo and would confuse git.
        string todo = RebaseTodo.Render(
            [new RebaseStep { ObjectId = "aaa111", Subject = "ilk\nsatir arasi" }]);

        todo.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1);
    }

    [Fact]
    public void HEPSI_dusurulurse_todo_REDDEDILIYOR()
    {
        // 🔴 MEASURED: an empty todo gives `error: nothing to do` with rc=1 and the rebase never
        // starts. Telling the user in advance is better than the "nothing happened" bewilderment.
        RebaseTodo.Validate(
        [
            new RebaseStep { ObjectId = "a", Action = RebaseAction.Drop },
            new RebaseStep { ObjectId = "b", Action = RebaseAction.Drop },
        ]).ShouldNotBeNull();
    }

    [Fact]
    public void ILK_adim_squash_olamaz()
    {
        RebaseTodo.Validate([new RebaseStep { ObjectId = "a", Action = RebaseAction.Squash }])
            .ShouldNotBeNull();

        RebaseTodo.Validate(
        [
            new RebaseStep { ObjectId = "a" },
            new RebaseStep { ObjectId = "b", Action = RebaseAction.Squash },
        ]).ShouldBeNull();
    }

    [Fact]
    public void Sequence_editor_betigi_dosyayi_KESIYOR()
    {
        // 🔴 MEASURED: the file passed to the script arrives FULL of git's own todo. Appending with `>>`
        // had caused git to see 6 commands when 3 were wanted, and the commits to be applied twice
        // and conflict.
        using RebaseTodoSession session = RebaseTodoSession.Create("pick abc\n");

        string scriptPath = session.Environment["GIT_SEQUENCE_EDITOR"];
        string script = File.ReadAllText(scriptPath);

        script.ShouldContain("> \"$1\"");
        script.ShouldNotContain(">> \"$1\"");
    }

    [Fact]
    public void Todo_METNI_komut_satirinda_ya_da_betikte_GORUNMUYOR()
    {
        // It takes the pattern from AskPassSession: the content is not embedded in the script, it is read
        // from a file named in the environment. That way quoting/newline escaping problems never arise.
        const string todo = "pick abc # ÇOK ÖZEL \"tırnaklı\" konu\n";

        using RebaseTodoSession session = RebaseTodoSession.Create(todo);

        File.ReadAllText(session.Environment["GIT_SEQUENCE_EDITOR"]).ShouldNotContain("ÖZEL");
        File.ReadAllText(session.Environment[RebaseTodoSession.TodoVariable]).ShouldBe(todo);
    }

    [Fact]
    public void Oturum_kapaninca_gecici_dosyalar_SILINIYOR()
    {
        string scriptPath;
        string todoPath;

        using (RebaseTodoSession session = RebaseTodoSession.Create("pick abc\n"))
        {
            scriptPath = session.Environment["GIT_SEQUENCE_EDITOR"];
            todoPath = session.Environment[RebaseTodoSession.TodoVariable];

            File.Exists(scriptPath).ShouldBeTrue();
        }

        File.Exists(scriptPath).ShouldBeFalse();
        File.Exists(todoPath).ShouldBeFalse();
    }
}
