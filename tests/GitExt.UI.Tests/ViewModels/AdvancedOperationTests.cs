using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P07-T03 · T06 · T07 · T08 · T09 · T10 · T11 · T13 · T14 — advanced operation screens.
/// </summary>
public class AdvancedOperationTests
{
    // =========================================== P07-T03 conflict screen

    private static ConflictedFile Conflict(
        string path = "f.txt",
        ConflictKind kind = ConflictKind.BothModified,
        bool hasBase = true,
        bool hasOurs = true,
        bool hasTheirs = true) =>
        new()
        {
            Path = RepositoryPath.Parse(path),
            Kind = kind,
            HasBase = hasBase,
            HasOurs = hasOurs,
            HasTheirs = hasTheirs,
        };

    [AvaloniaFact]
    public async Task Cakisan_dosyalar_ve_kalan_sayi_gosteriliyor()
    {
        FakeConflictReader reader = new([Conflict(), Conflict("g.txt")]);
        FakeConflictResolver resolver = new(InProgressOperation.Merge, ["f.txt", "g.txt"]);

        ConflictViewModel model = new("/depo", reader, resolver);
        await model.RefreshAsync();

        model.Files.Count.ShouldBe(2);
        model.RemainingText.ShouldContain("2");
        model.IsEmpty.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task COZULMEDEN_devam_dugmesi_ETKIN_DEGIL()
    {
        // 🔴 In the measurement, `--continue` returned rc=128 without a resolution.
        FakeConflictReader reader = new([Conflict()]);
        FakeConflictResolver resolver = new(InProgressOperation.Merge, ["f.txt"]);

        ConflictViewModel model = new("/depo", reader, resolver);
        await model.RefreshAsync();

        model.ContinueCommand.CanExecute(null).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Hepsi_cozulunce_devam_ETKIN_ve_komut_ISLEME_gore()
    {
        FakeConflictReader reader = new([]);
        FakeConflictResolver resolver = new(InProgressOperation.Rebase, []);

        ConflictViewModel model = new("/depo", reader, resolver);
        await model.RefreshAsync();

        model.ContinueCommand.CanExecute(null).ShouldBeTrue();
        model.ContinueCommandText.ShouldBe("git rebase --continue");
    }

    [AvaloniaFact]
    public async Task VARLIK_cakismasinda_uc_yollu_gorunum_GIZLENIYOR()
    {
        // There are not two texts to merge; an empty "theirs" panel would read as though the file
        // were empty (the UI counterpart of the null/empty distinction from P07-T02).
        FakeConflictReader reader = new(
            [Conflict(kind: ConflictKind.DeletedByUs, hasOurs: false)]);
        FakeConflictResolver resolver = new(InProgressOperation.Merge, ["f.txt"]);

        ConflictViewModel model = new("/depo", reader, resolver);
        await model.RefreshAsync();

        model.Selected.ShouldNotBeNull();
        model.ShowThreeWay.ShouldBeFalse();
        model.TakeBothCommand.CanExecute(null).ShouldBeFalse("kaynaştıracak iki metin yok");
    }

    [AvaloniaFact]
    public async Task ICERIK_cakismasinda_uc_yollu_gorunum_ACIK()
    {
        FakeConflictReader reader = new([Conflict()]);
        FakeConflictResolver resolver = new(InProgressOperation.Merge, ["f.txt"]);

        ConflictViewModel model = new("/depo", reader, resolver);
        await model.RefreshAsync();

        model.ShowThreeWay.ShouldBeTrue();
        model.BaseText.ShouldBe("ATA");
        model.OursText.ShouldBe("BIZ");
        model.TheirsText.ShouldBe("ONLAR");
    }

    [AvaloniaFact]
    public async Task EKSIK_asama_icin_git_show_CAGRILMIYOR()
    {
        // 🔴 `git show :2:<path>` is fatal on a missing stage; we ask before reading.
        FakeConflictReader reader = new(
            [Conflict(kind: ConflictKind.DeletedByUs, hasOurs: false)]);
        FakeConflictResolver resolver = new(InProgressOperation.Merge, ["f.txt"]);

        ConflictViewModel model = new("/depo", reader, resolver);
        await model.RefreshAsync();

        reader.RequestedStages.ShouldNotContain(ConflictStage.Ours);
        reader.RequestedStages.ShouldContain(ConflictStage.Theirs);
    }

    [AvaloniaFact]
    public async Task Taraf_alinca_liste_YENILENIYOR()
    {
        FakeConflictReader reader = new([Conflict()]);
        FakeConflictResolver resolver = new(InProgressOperation.Merge, ["f.txt"]);

        ConflictViewModel model = new("/depo", reader, resolver);
        await model.RefreshAsync();

        resolver.Remaining = [];
        reader.Files = [];

        await model.TakeOursCommand.ExecuteAsync(null);

        resolver.TakenSide.ShouldBe(ResolutionSide.Ours);
        model.IsEmpty.ShouldBeTrue();
        model.ContinueCommand.CanExecute(null).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Pencere_ogeleri_YERINDE()
    {
        FakeConflictReader reader = new([Conflict()]);
        FakeConflictResolver resolver = new(InProgressOperation.Merge, ["f.txt"]);

        ConflictViewModel model = new("/depo", reader, resolver);
        await model.RefreshAsync();

        ConflictWindow window = new() { DataContext = model, Width = 1000, Height = 640 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The layout follows GitExtensions FormResolveConflicts (§ 9).
        window.GetControl<ListBox>("ConflictList").ShouldNotBeNull();
        window.GetControl<TextBox>("BaseBox").ShouldNotBeNull();
        window.GetControl<TextBox>("OursBox").ShouldNotBeNull();
        window.GetControl<TextBox>("TheirsBox").ShouldNotBeNull();
        window.GetControl<TextBox>("MergedBox").ShouldNotBeNull();
        window.GetControl<Button>("ContinueButton").ShouldNotBeNull();
        window.GetControl<Button>("AbortButton").ShouldNotBeNull();

        window.Close();
    }

    // ================================================= P07-T06 reset

    [AvaloniaFact]
    public async Task Reset_modu_degisince_ACIKLAMA_degisiyor()
    {
        FakeResetWriter writer = new(new ResetPreview { IsTargetValid = true });
        ResetViewModel model = new("/depo", writer, "abc123");
        await model.LoadAsync();

        model.Mode = ResetMode.Soft;
        string soft = model.ModeDescription;

        model.Mode = ResetMode.Hard;

        model.ModeDescription.ShouldNotBe(soft);
        model.ModeDescription.ShouldContain("discarded");
    }

    [AvaloniaFact]
    public async Task HARD_ve_KIRLI_agacta_ek_ONAY_isteniyor()
    {
        // Dropped commits are in the reflog; the truly unrecoverable thing is uncommitted work.
        FakeResetWriter writer = new(new ResetPreview
        {
            IsTargetValid = true,
            HasUncommittedChanges = true,
            DroppedCommits = ["c2"],
        });

        ResetViewModel model = new("/depo", writer, "abc123") { Mode = ResetMode.Hard };
        await model.LoadAsync();

        model.RequiresConfirmation.ShouldBeTrue();
        model.ResetCommand.CanExecute(null).ShouldBeFalse();

        model.Confirmed = true;
        model.ResetCommand.CanExecute(null).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task SOFT_modda_kirli_agacta_bile_ONAY_ISTENMIYOR()
    {
        // --soft does not touch the working tree; asking for confirmation there is needless friction.
        FakeResetWriter writer = new(new ResetPreview
        {
            IsTargetValid = true,
            HasUncommittedChanges = true,
        });

        ResetViewModel model = new("/depo", writer, "abc123") { Mode = ResetMode.Soft };
        await model.LoadAsync();

        model.RequiresConfirmation.ShouldBeFalse();
        model.ResetCommand.CanExecute(null).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Reset_sonrasi_GERI_ALMA_komutu_gosteriliyor()
    {
        // Phase rule: the undo information is ALWAYS offered.
        FakeResetWriter writer = new(new ResetPreview { IsTargetValid = true });
        ResetViewModel model = new("/depo", writer, "abc123");
        await model.LoadAsync();

        await model.ResetCommand.ExecuteAsync(null);

        model.Result.ShouldNotBeNull();
        model.Result.ShouldNotBeNull().ShouldContain("git reset --hard");
    }

    [AvaloniaFact]
    public async Task KIRLI_agacta_geri_almanin_EKSIK_oldugu_yaziliyor()
    {
        FakeResetWriter writer = new(
            new ResetPreview { IsTargetValid = true, HasUncommittedChanges = true },
            new SafetyPoint
            {
                ObjectId = "aaa",
                BranchName = "main",
                Operation = "reset",
                HasUncommittedChanges = true,
            });

        ResetViewModel model = new("/depo", writer, "abc123") { Mode = ResetMode.Hard, Confirmed = true };
        await model.LoadAsync();

        await model.ResetCommand.ExecuteAsync(null);

        model.Result.ShouldNotBeNull().ShouldContain("not restored");
    }

    [AvaloniaFact]
    public async Task GECERSIZ_hedefte_reset_ETKIN_DEGIL()
    {
        FakeResetWriter writer = new(new ResetPreview { IsTargetValid = false });
        ResetViewModel model = new("/depo", writer, "yok-boyle");
        await model.LoadAsync();

        model.IsTargetValid.ShouldBeFalse();
        model.ResetCommand.CanExecute(null).ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Reset_diyalogu_ogeleri_YERINDE()
    {
        FakeResetWriter writer = new(new ResetPreview { IsTargetValid = true });
        ResetViewModel model = new("/depo", writer, "abc123");

        ResetDialog dialog = new() { DataContext = model, Width = 580 };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        // GitExtensions FormResetCurrentBranch (§ 9): three radio buttons.
        dialog.GetControl<RadioButton>("SoftRadio").ShouldNotBeNull();
        dialog.GetControl<RadioButton>("MixedRadio").ShouldNotBeNull();
        dialog.GetControl<RadioButton>("HardRadio").ShouldNotBeNull();
        dialog.GetControl<CheckBox>("ConfirmBox").ShouldNotBeNull();

        dialog.Close();
    }

    // ======================================== P07-T07/T08 sequencer

    [AvaloniaFact]
    public async Task MERGE_commitinde_EBEVEYN_secimi_gorunuyor()
    {
        // 🔴 In the measurement, rc=128 without -m.
        FakeSequencerWriter writer = new(parentCount: 2);
        SequencerViewModel model = new("/depo", writer, SequencerOperation.Revert, ["abc"]);
        await model.LoadAsync();

        model.IsMergeCommit.ShouldBeTrue();
        model.ParentCount.ShouldBe(2);
        model.CommandLine.ShouldContain("-m 1");
    }

    [AvaloniaFact]
    public async Task TEK_ebeveynli_committe_m_bayragi_VERILMIYOR()
    {
        // git tek ebeveynli bir commit'te `-m`'i reddediyor.
        FakeSequencerWriter writer = new(parentCount: 1);
        SequencerViewModel model = new("/depo", writer, SequencerOperation.Revert, ["abc"]);
        await model.LoadAsync();

        model.IsMergeCommit.ShouldBeFalse();
        model.CommandLine.ShouldNotContain("-m ");
    }

    [AvaloniaFact]
    public async Task NO_COMMIT_sonucu_KULLANICIYA_soyleniyor()
    {
        // The `--squash` lesson from P06-T11: exit code 0 but HEAD does not move.
        FakeSequencerWriter writer = new(requiresCommit: true);
        SequencerViewModel model = new("/depo", writer, SequencerOperation.CherryPick, ["abc"])
        {
            NoCommit = true,
        };

        await model.LoadAsync();
        await model.RunCommand.ExecuteAsync(null);

        model.Result.ShouldNotBeNull().ShouldContain("not committed");
    }

    [AvaloniaFact]
    public async Task Cakisma_sonucu_bildiriliyor()
    {
        FakeSequencerWriter writer = new(conflicts: ["f.txt"]);
        SequencerViewModel model = new("/depo", writer, SequencerOperation.CherryPick, ["abc"]);

        await model.LoadAsync();
        await model.RunCommand.ExecuteAsync(null);

        model.HasConflicts.ShouldBeTrue();
        model.Result.ShouldNotBeNull().ShouldContain("conflicted");
    }

    [AvaloniaFact]
    public async Task Basarili_cherry_pick_GERI_ALMA_yolunu_gosteriyor()
    {
        FakeSequencerWriter writer = new(commitsCreated: 1);
        SequencerViewModel model = new("/depo", writer, SequencerOperation.CherryPick, ["abc"]);

        await model.LoadAsync();
        await model.RunCommand.ExecuteAsync(null);

        model.Result.ShouldNotBeNull().ShouldContain("git reset --hard");
    }

    [AvaloniaFact]
    public void REVERTte_x_secenegi_GORUNMUYOR()
    {
        // Revert already writes its source into the message.
        FakeSequencerWriter writer = new();

        new SequencerViewModel("/depo", writer, SequencerOperation.Revert, ["abc"])
            .SupportsRecordOrigin.ShouldBeFalse();

        new SequencerViewModel("/depo", writer, SequencerOperation.CherryPick, ["abc"])
            .SupportsRecordOrigin.ShouldBeTrue();
    }

    // ======================================== P07-T09/T10 rebase

    [AvaloniaFact]
    public async Task Rebase_adimlari_SIRAYLA_dolduruluyor()
    {
        FakeRebaseWriter writer = new(
        [
            new RebaseStep { ObjectId = "aaa1111", Subject = "c1" },
            new RebaseStep { ObjectId = "bbb2222", Subject = "c2" },
        ]);

        RebaseViewModel model = new("/depo", writer, "main") { IsInteractive = true };
        await model.LoadAsync();

        model.Steps.Select(step => step.Subject).ShouldBe(["c1", "c2"]);
        model.Steps.ShouldAllBe(step => step.Action == RebaseAction.Pick);
    }

    [AvaloniaFact]
    public async Task Adimlar_TASINABILIYOR()
    {
        FakeRebaseWriter writer = new(
        [
            new RebaseStep { ObjectId = "aaa1111", Subject = "c1" },
            new RebaseStep { ObjectId = "bbb2222", Subject = "c2" },
        ]);

        RebaseViewModel model = new("/depo", writer, "main") { IsInteractive = true };
        await model.LoadAsync();

        model.Move(0, 1);

        model.Steps.Select(step => step.Subject).ShouldBe(["c2", "c1"]);
    }

    [AvaloniaFact]
    public async Task KLAVYEYLE_de_tasinabiliyor()
    {
        // If drag-and-drop were the only way, it would be unusable from the keyboard.
        FakeRebaseWriter writer = new(
        [
            new RebaseStep { ObjectId = "aaa1111", Subject = "c1" },
            new RebaseStep { ObjectId = "bbb2222", Subject = "c2" },
        ]);

        RebaseViewModel model = new("/depo", writer, "main") { IsInteractive = true };
        await model.LoadAsync();

        model.Selected = model.Steps[1];
        model.MoveUpCommand.Execute(null);

        model.Steps[0].Subject.ShouldBe("c2");
        model.MoveUpCommand.CanExecute(null).ShouldBeFalse("en üstteki daha yukarı çıkamaz");
    }

    [AvaloniaFact]
    public async Task HEPSI_dusurulurse_rebase_ETKIN_DEGIL()
    {
        // 🔴 In the measurement an empty todo gave `error: nothing to do`; we say so up front.
        FakeRebaseWriter writer = new([new RebaseStep { ObjectId = "aaa1111", Subject = "c1" }]);

        RebaseViewModel model = new("/depo", writer, "main") { IsInteractive = true };
        await model.LoadAsync();

        model.Steps[0].Action = RebaseAction.Drop;

        model.ValidationError.ShouldNotBeNull();
        model.RunCommand.CanExecute(null).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task ILK_adim_squash_olunca_UYARILIYOR()
    {
        FakeRebaseWriter writer = new([new RebaseStep { ObjectId = "aaa1111", Subject = "c1" }]);

        RebaseViewModel model = new("/depo", writer, "main") { IsInteractive = true };
        await model.LoadAsync();

        model.Steps[0].Action = RebaseAction.Squash;

        model.ValidationError.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task EDIT_adiminda_durus_CAKISMADAN_ayirt_ediliyor()
    {
        // No conflict but the rebase stopped anyway; showing the two the same would mislead.
        FakeRebaseWriter writer = new([], RebaseOutcome.StoppedForEdit);

        RebaseViewModel model = new("/depo", writer, "main");
        await model.RunCommand.ExecuteAsync(null);

        model.HasConflicts.ShouldBeFalse();
        model.Result.ShouldNotBeNull().ShouldContain("amend");
    }

    [AvaloniaFact]
    public async Task Rebase_penceresi_ogeleri_YERINDE()
    {
        FakeRebaseWriter writer = new([new RebaseStep { ObjectId = "aaa1111", Subject = "c1" }]);
        RebaseViewModel model = new("/depo", writer, "main") { IsInteractive = true };
        await model.LoadAsync();

        RebaseWindow window = new() { DataContext = model, Width = 820, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetControl<TextBox>("UpstreamBox").ShouldNotBeNull();
        window.GetControl<CheckBox>("InteractiveBox").ShouldNotBeNull();
        window.GetControl<ListBox>("StepList").ShouldNotBeNull();
        window.GetControl<Button>("MoveUpButton").ShouldNotBeNull();

        window.Close();
    }

    // ================================================= P07-T13 stash

    [AvaloniaFact]
    public async Task Stash_listesi_ve_diff_dolduruluyor()
    {
        FakeStashWriter writer = new([Stash(0, "ilk"), Stash(1, "ikinci")]);
        StashViewModel model = new("/depo", writer);
        await model.RefreshAsync();

        model.Rows.Count.ShouldBe(2);
        model.Selected.ShouldNotBeNull();
        model.Diff.ShouldContain("diff");
    }

    [AvaloniaFact]
    public async Task Stashlenecek_sey_yoksa_SOYLENIYOR()
    {
        FakeStashWriter writer = new([]) { PushSucceeds = false };
        StashViewModel model = new("/depo", writer);
        await model.RefreshAsync();

        await model.PushCommand.ExecuteAsync(null);

        model.Notice.ShouldNotBeNull().ShouldContain("Nothing to stash");
    }

    [AvaloniaFact]
    public async Task POP_cakisirsa_girdinin_KALDIGI_soyleniyor()
    {
        // 🔴 Measured: git does not drop the entry. Unsaid, the user applies it twice.
        FakeStashWriter writer = new([Stash(0, "ilk")])
        {
            ApplyResult = new StashApplyResult
            {
                HasConflicts = true,
                ConflictedPaths = [RepositoryPath.Parse("f.txt")],
                EntryKept = true,
                IndexRestored = false,
            },
        };

        StashViewModel model = new("/depo", writer);
        await model.RefreshAsync();

        await model.PopCommand.ExecuteAsync(null);

        model.Notice.ShouldNotBeNull().ShouldContain("kept");
    }

    [AvaloniaFact]
    public async Task INDEX_geri_yuklenemediyse_UYARILIYOR()
    {
        // 🔴 Measured: `pop` silently loses the staged/unstaged distinction.
        FakeStashWriter writer = new([Stash(0, "ilk")])
        {
            ApplyResult = new StashApplyResult
            {
                HasConflicts = false,
                EntryKept = false,
                IndexRestored = false,
            },
        };

        StashViewModel model = new("/depo", writer);
        await model.RefreshAsync();

        await model.PopCommand.ExecuteAsync(null);

        model.Notice.ShouldNotBeNull().ShouldContain("unstaged");
    }

    [AvaloniaFact]
    public async Task Dal_adi_bos_iken_BRANCH_etkin_degil()
    {
        FakeStashWriter writer = new([Stash(0, "ilk")]);
        StashViewModel model = new("/depo", writer);
        await model.RefreshAsync();

        model.BranchCommand.CanExecute(null).ShouldBeFalse();

        model.BranchName = "yeni-dal";
        model.BranchCommand.NotifyCanExecuteChanged();

        model.BranchCommand.CanExecute(null).ShouldBeTrue();
    }

    // ================================================ P07-T14 reflog

    [AvaloniaFact]
    public async Task KAYIP_commitler_isaretleniyor_ve_suzulebiliyor()
    {
        FakeReflogReader reader = new(
        [
            Reflog("aaa1111", "commit: c1", unreachable: false),
            Reflog("bbb2222", "commit: c2", unreachable: true),
        ]);

        ReflogViewModel model = new("/depo", reader);
        await model.RefreshAsync();

        model.Rows.Count.ShouldBe(2);
        model.UnreachableCount.ShouldBe(1);

        model.OnlyUnreachable = true;

        model.Rows.Count.ShouldBe(1);
        model.Rows[0].ShortId.ShouldBe("bbb2222");
    }

    [AvaloniaFact]
    public async Task Geri_donus_komutu_SECICI_degil_SHA()
    {
        // ⚠️ `HEAD@{3}` kayan bir referans.
        FakeReflogReader reader = new([Reflog("aaa1111222233334444", "commit: c1")]);

        ReflogViewModel model = new("/depo", reader);
        await model.RefreshAsync();

        model.Selected = model.Rows[0];

        model.SelectedRecoveryCommand.ShouldBe("git reset --hard aaa1111222233334444");
        model.SelectedRecoveryCommand.ShouldNotContain("@{");
    }

    [AvaloniaFact]
    public async Task Reset_yazicisi_YOKSA_geri_donus_ETKIN_DEGIL()
    {
        FakeReflogReader reader = new([Reflog("aaa1111", "commit: c1")]);

        ReflogViewModel model = new("/depo", reader);
        await model.RefreshAsync();

        model.Selected = model.Rows[0];

        model.ReturnCommand.CanExecute(null).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Geri_donunce_GERI_ALMA_bilgisi_veriliyor()
    {
        FakeReflogReader reader = new([Reflog("aaa1111", "commit: c1")]);
        FakeResetWriter reset = new(new ResetPreview { IsTargetValid = true });

        ReflogViewModel model = new("/depo", reader, reset);
        await model.RefreshAsync();

        model.Selected = model.Rows[0];
        await model.ReturnCommand.ExecuteAsync(null);

        model.Notice.ShouldNotBeNull().ShouldContain("To undo");
    }

    [AvaloniaFact]
    public async Task Reflog_penceresi_ogeleri_YERINDE()
    {
        FakeReflogReader reader = new([Reflog("aaa1111", "commit: c1")]);
        ReflogViewModel model = new("/depo", reader);
        await model.RefreshAsync();

        ReflogWindow window = new() { DataContext = model, Width = 900, Height = 560 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetControl<ListBox>("ReflogList").ShouldNotBeNull();
        window.GetControl<CheckBox>("OnlyUnreachableBox").ShouldNotBeNull();
        window.GetControl<Button>("ReturnButton").ShouldNotBeNull();

        window.Close();
    }

    private static StashEntry Stash(int index, string message) => new()
    {
        Selector = $"refs/stash@{{{index}}}",
        ObjectId = $"sha{index}",
        Message = $"On main: {message}",
        Index = index,
    };

    private static ReflogEntry Reflog(string id, string message, bool unreachable = false) => new()
    {
        ObjectId = id,
        Selector = "HEAD@{0}",
        Message = message,
        Subject = "konu",
        IsUnreachable = unreachable,
    };
}
