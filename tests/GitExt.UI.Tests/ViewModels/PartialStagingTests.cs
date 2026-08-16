using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T10 — staging/unstaging by selecting hunks and lines in the diff panel.
/// </summary>
/// <remarks>
/// Producing the patch itself lives in the core and was tested against real <c>git</c> in
/// P05-T04/T05. The question here is a different one: <b>is the selection on screen translated to the
/// right lines?</b> That was the one class of bug P05-T04 measured as going uncaught — because the
/// patch is valid, git accepts it and the content is <b>silently</b> wrong.
/// </remarks>
public class PartialStagingTests
{
    /// <summary>A fake host that records the selection.</summary>
    private sealed class RecordingHost : IPartialStagingHost
    {
        public bool CanStage { get; set; } = true;

        public bool CanUnstage { get; set; }

        public PatchSelection? LastSelection { get; private set; }

        public bool? LastWasStage { get; private set; }

        public int CallCount { get; private set; }

        public Exception? Failure { get; set; }

        public Task ApplyAsync(FileDiff diff, PatchSelection selection, bool stage)
        {
            CallCount++;
            LastSelection = selection;
            LastWasStage = stage;

            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        /// <summary>How many times a destructive undo was requested (P05-T15)?</summary>
        public int DiscardCount { get; private set; }

        public Task DiscardAsync(FileDiff diff, PatchSelection selection)
        {
            DiscardCount++;
            LastSelection = selection;

            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private static FileDiff TwoHunks()
    {
        DiffHunk first = new()
        {
            Header = "@@ -1,3 +1,3 @@",
            OldStart = 1,
            OldLength = 3,
            NewStart = 1,
            NewLength = 3,
            Lines =
            [
                new DiffLine(DiffLineKind.Context, "bir") { OldLineNumber = 1, NewLineNumber = 1 },
                new DiffLine(DiffLineKind.Removed, "iki eski") { OldLineNumber = 2 },
                new DiffLine(DiffLineKind.Added, "iki yeni") { NewLineNumber = 2 },
            ],
        };

        DiffHunk second = new()
        {
            Header = "@@ -10,2 +10,2 @@",
            OldStart = 10,
            OldLength = 2,
            NewStart = 10,
            NewLength = 2,
            Lines =
            [
                new DiffLine(DiffLineKind.Context, "on") { OldLineNumber = 10, NewLineNumber = 10 },
                new DiffLine(DiffLineKind.Added, "on bir") { NewLineNumber = 11 },
            ],
        };

        return new FileDiff
        {
            Path = RepositoryPath.Parse("a.cs"),
            Change = FileChangeKind.Modified,
            Hunks = [first, second],
        };
    }

    private static async Task<(DiffViewModel Model, RecordingHost Host)> LoadedAsync()
    {
        DiffViewModel model = new(new FakeDiffReader([TwoHunks()]));
        RecordingHost host = new();

        model.StagingHost = host;

        await model.ShowWorkingTreeAsync("/tmp/depo", staged: false);

        return (model, host);
    }

    // The line layout: 0 = @@ first, 1 = "bir", 2 = "iki eski", 3 = "iki yeni",
    //                  4 = @@ second, 5 = "on",  6 = "on bir"

    [AvaloniaFact]
    public async Task Hunk_basligi_secilince_o_hunkun_TAMAMI_secilir()
    {
        // There is NO separate "stage this hunk" command — GitExtensions does not have one either. The
        // header row is already the natural way of saying "this hunk".
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([0]);

        host.CallCount.ShouldBe(1);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(2);
        selection.IsSelected(0, 1).ShouldBeTrue();
        selection.IsSelected(0, 2).ShouldBeTrue();

        // The second hunk MUST NOT BE TOUCHED.
        selection.IsSelected(1, 1).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Tek_satir_secilince_yalnizca_o_satir_secilir()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([3]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(1);
        selection.IsSelected(0, 2).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task BAGLAM_satiri_secilse_bile_secime_girmez()
    {
        // 🔴 The class of silent bug measured in P05-T04: a context line goes into the patch by itself.
        // Counting it as "selected" would mean taking a change the user did not select.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([1]);

        host.CallCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Iki_hunktan_secilen_satirlar_birlikte_gonderilir()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([2, 6]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(2);
        selection.IsSelected(0, 1).ShouldBeTrue();
        selection.IsSelected(1, 1).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Hic_secim_yoksa_ve_duraklanan_satir_da_yoksa_HICBIR_SEY_yapilmaz()
    {
        // ⚠️ "Stage with nothing selected" would silently stage the WHOLE file.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([]);

        host.CallCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Secim_yoksa_DURAKLANAN_satir_kullanilir()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.GoToNextChange().ShouldBeTrue();

        await model.StageSelectionAsync([]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(1);
        selection.IsSelected(0, 1).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Stage_ve_unstage_yonu_hosta_dogru_gecer()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([2]);
        host.LastWasStage.ShouldBe(true);

        await model.UnstageSelectionAsync([2]);
        host.LastWasStage.ShouldBe(false);
    }

    [AvaloniaFact]
    public async Task Host_yoksa_komutlar_KAPALI()
    {
        // Staging makes no sense in commit history or in the comparison window.
        DiffViewModel model = new(new FakeDiffReader([TwoHunks()]));

        await model.ShowCommitAsync("/tmp/depo", CommitId.Parse(new string('a', 40)));

        model.CanStageSelection.ShouldBeFalse();
        model.CanUnstageSelection.ShouldBeFalse();

        await model.StageSelectionAsync([0]);
    }

    [AvaloniaFact]
    public async Task Stage_ve_unstage_BIRBIRINI_dislar()
    {
        // The same as in GitExtensions: stage appears only on the working tree side, unstage only on
        // the index side.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.CanStageSelection.ShouldBeTrue();
        model.CanUnstageSelection.ShouldBeFalse();

        host.CanStage = false;
        host.CanUnstage = true;
        model.NotifyStagingAvailabilityChanged();

        model.CanStageSelection.ShouldBeFalse();
        model.CanUnstageSelection.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_secili_CIFTIN_IKI_TARAFI_da_secime_girer()
    {
        // 🔴 One side-by-side row carries TWO different unified lines: the removal on the left and the
        // addition that replaces it on the right. Taking only one of them would stage HALF of the pair
        // the user sees — the patch comes out valid and the content is silently wrong.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.ShowSideBySide = true;
        model.CanStageSelection.ShouldBeTrue();

        // The side-by-side layout: 0 = @@, 1 = "bir" (context), 2 = "iki eski" ↔ "iki yeni".
        model.SideLines[2].Left.RawText.ShouldBe("iki eski");
        model.SideLines[2].Right.RawText.ShouldBe("iki yeni");

        await model.StageSelectionAsync([2]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(2);
        selection.IsSelected(0, 1).ShouldBeTrue();
        selection.IsSelected(0, 2).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_hunk_basligi_TUM_hunku_secer()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.ShowSideBySide = true;

        await model.StageSelectionAsync([0]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(2);
        selection.IsSelected(1, 1).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_DOLGU_satiri_secime_girmez()
    {
        // A filler means "there is no line here"; staging something with no counterpart is meaningless.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.ShowSideBySide = true;

        // The second hunk: a single added line with no counterpart on the left.
        int filler = model.SideLines
            .Select((row, index) => (row, index))
            .First(pair => pair.row.Left.IsFiller && !pair.row.IsHunkHeader)
            .index;

        await model.StageSelectionAsync([filler]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(1);
        selection.IsSelected(1, 1).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Git_reddederse_mesaj_kullaniciya_ULASIR()
    {
        // `git apply` rejects count/context errors (P05-T04). Staying silent would produce the
        // "I clicked and nothing happened" situation.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        host.Failure = new GitExt.Core.Git.GitException(
            GitExt.Core.Git.GitFailureKind.Unknown,
            "Git komutu başarısız oldu.",
            "git apply --cached -",
            exitCode: 1,
            standardError: "error: corrupt patch at line 5");

        await model.StageSelectionAsync([2]);

        model.ErrorMessage.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void Sifirlama_YALNIZCA_calisma_agaci_tarafinda_kullanilabilir()
    {
        // P05-T15. On the index side "reset" would already mean *unstage*; two commands doing the same
        // thing would leave the user asking which one does what.
        DiffViewModel model = new(new FakeDiffReader());

        // With no host, none of the actions is available.
        model.CanDiscardSelection.ShouldBeFalse();

        RecordingHost worktreeSide = new() { CanStage = true, CanUnstage = false };
        model.StagingHost = worktreeSide;
        model.CanDiscardSelection.ShouldBeTrue();

        RecordingHost indexSide = new() { CanStage = false, CanUnstage = true };
        model.StagingHost = indexSide;
        model.CanDiscardSelection.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Kodlama_okuma_ve_yazma_arasinda_TASINIYOR()
    {
        // 🔴 The defect measured in a real repository in P05-T16: when the diff is read with the UTF-8
        // default and the patch written as UTF-8, `git apply` REJECTS the patch on a Latin-5 file
        // (`patch does not apply`). Break one link in the chain and the feature does not work.
        FakeStatusReader status = new([
            new FileStatus
            {
                Path = RepositoryPath.Parse("tr.txt"),
                UnstagedChange = FileChangeKind.Modified,
            },
        ]);

        FakeStagingWriter staging = new(status);
        System.Text.Encoding latin5 = System.Text.Encoding.Latin1;

        WorkingTreeViewModel model = new(
            status,
            staging,
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()));

        model.Diff.ContentEncoding = latin5;

        await model.OpenAsync("/tmp/depo");

        FileDiff diff = TwoHunks();

        await ((IPartialStagingHost)model).ApplyAsync(
            diff, PatchSelection.Hunks(diff, 0), stage: true);

        staging.LastPartialEncoding.ShouldBeSameAs(latin5);
    }

    [AvaloniaFact]
    public async Task Ikili_dosyada_kismi_stage_COKMEZ_ve_hicbir_sey_yapmaz()
    {
        // In P05-T16 the measurement program crashed at exactly this point: a binary file has no hunks
        // and `Hunks[0]` blows up. Because line selection is impossible in the UI either, the command
        // must silently do nothing — but it must not crash.
        FileDiff binary = new()
        {
            Path = RepositoryPath.Parse("resim.png"),
            Change = FileChangeKind.Modified,
            IsBinary = true,
            Hunks = [],
        };

        RecordingHost host = new();
        DiffViewModel model = new(new FakeDiffReader([binary]))
        {
            StagingHost = host,
        };

        await model.ShowWorkingTreeAsync("/tmp/depo", staged: false);

        await Should.NotThrowAsync(() => model.StageSelectionAsync());
        await Should.NotThrowAsync(() => model.DiscardSelectionAsync());

        host.CallCount.ShouldBe(0);
        host.DiscardCount.ShouldBe(0);
    }
}
