using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T15 — drag-and-drop merging.
/// </summary>
/// <remarks>
/// The plan's item: <b>always with a confirmation dialog</b> — an accidental drag is a real risk.
/// </remarks>
public class MergeDropTests
{
    private static (MainWindowViewModel Model, FakeMergeWriter Merge, FakeMergeDropConfirmer Confirmer)
        Create()
    {
        FakeMergeWriter merge = new();
        FakeMergeDropConfirmer confirmer = new();

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(FakeGitData.Refs(
                    localBranches:
                    [
                        FakeGitData.LocalBranch("main", FakeGitData.Sha(2), isCurrent: true),
                        FakeGitData.LocalBranch("ozellik", FakeGitData.Sha(1)),
                    ])),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            mergeWriter: merge)
        {
            MergePrompt = new FakeMergePrompt(),
            MergeDropConfirmer = confirmer,
        };

        return (model, merge, confirmer);
    }

    [AvaloniaFact]
    public async Task ONAYSIZ_birlestirme_yapilmiyor()
    {
        (MainWindowViewModel model, FakeMergeWriter merge, FakeMergeDropConfirmer confirmer) = Create();

        confirmer.Answer = false;

        await model.OpenRepositoryAsync("/depo");
        await model.MergeDroppedAsync("ozellik", "main");

        confirmer.Asked.ShouldBeTrue("onay her zaman sorulmalı");
        merge.Merged.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Onaylaninca_birlestirme_CALISIYOR()
    {
        (MainWindowViewModel model, FakeMergeWriter merge, _) = Create();

        await model.OpenRepositoryAsync("/depo");
        await model.MergeDroppedAsync("ozellik", "main");

        merge.Merged.Single().Source.ShouldBe("ozellik");
        model.BranchNotice.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task Onay_ekraninda_calistirilacak_KOMUT_yazili()
    {
        // The "show the command" principle applies here too: the user must see what they are confirming.
        (MainWindowViewModel model, _, FakeMergeDropConfirmer confirmer) = Create();

        await model.OpenRepositoryAsync("/depo");
        await model.MergeDroppedAsync("ozellik", "main");

        confirmer.LastRequest.ShouldNotBeNull();
        confirmer.LastRequest!.Command.ShouldBe("git merge -- ozellik");
        confirmer.LastRequest.Source.ShouldBe("ozellik");
        confirmer.LastRequest.Target.ShouldBe("main");
    }

    [AvaloniaFact]
    public async Task MEVCUT_dal_disinda_bir_hedefe_birakmak_calismiyor()
    {
        // 🔑 GitExtensions allows dropping onto another branch as well, but there is a hidden checkout
        // behind it. There are no hidden operations in this project; the reason is written out.
        (MainWindowViewModel model, FakeMergeWriter merge, FakeMergeDropConfirmer confirmer) = Create();

        await model.OpenRepositoryAsync("/depo");
        await model.MergeDroppedAsync("main", "ozellik");

        merge.Merged.ShouldBeEmpty();
        confirmer.Asked.ShouldBeFalse("onay bile sorulmamalı");
        model.BranchNotice!.ShouldContain("ozellik");
        model.BranchNotice!.ShouldContain("Switch to branch");
    }

    [AvaloniaFact]
    public async Task Dali_KENDI_uzerine_birakmak_bir_sey_yapmiyor()
    {
        (MainWindowViewModel model, FakeMergeWriter merge, FakeMergeDropConfirmer confirmer) = Create();

        await model.OpenRepositoryAsync("/depo");
        await model.MergeDroppedAsync("main", "main");

        merge.Merged.ShouldBeEmpty();
        confirmer.Asked.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Cakisma_seride_bildiriliyor()
    {
        (MainWindowViewModel model, FakeMergeWriter merge, _) = Create();

        merge.Result = new MergeResult
        {
            Outcome = MergeOutcome.Conflicted,
            HeadBefore = "aaaa",
            HeadAfter = "aaaa",
            ConflictedPaths = ["a.txt"],
        };

        await model.OpenRepositoryAsync("/depo");
        await model.MergeDroppedAsync("ozellik", "main");

        model.BranchNotice!.ShouldContain("stopped with conflicts");
    }

    [AvaloniaFact]
    public async Task SQUASH_gibi_yarim_kalan_durum_da_bildiriliyor()
    {
        (MainWindowViewModel model, FakeMergeWriter merge, _) = Create();

        merge.Result = new MergeResult
        {
            Outcome = MergeOutcome.Staged,
            HeadBefore = "aaaa",
            HeadAfter = "aaaa",
        };

        await model.OpenRepositoryAsync("/depo");
        await model.MergeDroppedAsync("ozellik", "main");

        model.BranchNotice!.ShouldContain("NOT committed");
    }

    private sealed class FakeMergeDropConfirmer : IMergeDropConfirmer
    {
        public bool Asked { get; private set; }

        public bool Answer { get; set; } = true;

        public MergeDropRequest? LastRequest { get; private set; }

        public Task<bool> ConfirmAsync(MergeDropRequest request)
        {
            Asked = true;
            LastRequest = request;

            return Task.FromResult(Answer);
        }
    }
}
