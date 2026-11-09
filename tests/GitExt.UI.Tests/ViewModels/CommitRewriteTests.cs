using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

public sealed class CommitRewriteTests
{
    private sealed class CapturingRebasePrompt : IRebasePrompt
    {
        public RebaseViewModel? Shown { get; private set; }

        public Task ShowAsync(RebaseViewModel model)
        {
            Shown = model;
            return Task.CompletedTask;
        }
    }

    private static CommitInfo Target(string subject = "the subject", string body = "the body") =>
        FakeGitData.Commit(FakeGitData.Sha(2), [FakeGitData.Sha(1)], subject) with { Body = body };

    [Theory]
    [InlineData(CommitKind.Fixup, "fixup! the subject")]
    [InlineData(CommitKind.Squash, "squash! the subject")]
    [InlineData(CommitKind.Amend, "amend! the subject\n\nthe body")]
    public void Ozel_commit_mesaji_GitExtensions_bicimi(CommitKind kind, string expected)
    {
        CommitKindMessage.For(kind, Target()).ShouldBe(expected);
    }

    [Fact]
    public void Onek_zaten_varsa_ikinci_kez_eklenmiyor()
    {
        CommitKindMessage.For(CommitKind.Fixup, Target("fixup! the subject")).ShouldBe("fixup! the subject");
    }

    [Fact]
    public void Yalniz_amend_mesaji_duzenlenebilir()
    {
        CommitKindMessage.LocksMessage(CommitKind.Fixup).ShouldBeTrue();
        CommitKindMessage.LocksMessage(CommitKind.Squash).ShouldBeTrue();
        CommitKindMessage.LocksMessage(CommitKind.Amend).ShouldBeFalse();
    }

    [Fact]
    public async Task On_doldurulan_mesaj_kullanicinin_taslagini_ezmiyor()
    {
        FakeCommitMessageStore store = new();
        store.Drafts["/depo"] = "my draft";

        CommitMessageViewModel message = new(store: store) { DraftSaveDelay = TimeSpan.Zero };
        await message.OpenAsync("/depo", TestContext.Current.CancellationToken);

        message.Prefill("fixup! x", readOnly: true);
        message.Text = "fixup! x edited";
        await message.FlushDraftAsync(TestContext.Current.CancellationToken);

        message.IsReadOnly.ShouldBeTrue();
        store.Drafts["/depo"].ShouldBe("my draft");

        await message.OnCommittedAsync(TestContext.Current.CancellationToken);

        store.Drafts["/depo"].ShouldBe("my draft");
        message.IsReadOnly.ShouldBeFalse();
    }

    private static async Task<(MainWindowViewModel Model, CapturingRebasePrompt Prompt)> OpenAsync(
        IReadOnlyList<RebaseStep> steps,
        int selectedIndex)
    {
        CapturingRebasePrompt prompt = new();
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            advanced: new AdvancedOperationServices { Rebase = new FakeRebaseWriter(steps) })
        {
            RebasePrompt = prompt,
        };

        await model.OpenRepositoryAsync("/depo");
        model.Commits.SelectedIndex = selectedIndex;

        return (model, prompt);
    }

    private static RebaseStep Step(int index) => new() { ObjectId = FakeGitData.Sha(index), Subject = $"commit {index}" };

    [AvaloniaTheory]
    [InlineData(RebaseAction.Edit)]
    [InlineData(RebaseAction.Reword)]
    public async Task Duzenle_ve_yeniden_adlandir_ebeveynden_isaretli_rebase_aciyor(RebaseAction action)
    {
        (MainWindowViewModel model, CapturingRebasePrompt prompt) = await OpenAsync([Step(2), Step(3)], selectedIndex: 1);

        await model.ChangeCommitAsync(action);

        RebaseViewModel shown = prompt.Shown.ShouldNotBeNull();
        shown.Upstream.ShouldBe(FakeGitData.Sha(1));
        shown.IsInteractive.ShouldBeTrue();
        shown.Steps.Single(step => step.Step.ObjectId == FakeGitData.Sha(2)).Action.ShouldBe(action);
        shown.Steps.Single(step => step.Step.ObjectId == FakeGitData.Sha(3)).Action.ShouldBe(RebaseAction.Pick);
    }

    [AvaloniaFact]
    public async Task Yeniden_adlandirmada_eski_mesaj_hazir_geliyor()
    {
        (MainWindowViewModel model, CapturingRebasePrompt prompt) = await OpenAsync([Step(2), Step(3)], selectedIndex: 1);

        await model.ChangeCommitAsync(RebaseAction.Reword);

        prompt.Shown!.Steps.Single(step => step.Step.ObjectId == FakeGitData.Sha(2)).NewMessage.ShouldBe("commit 2");
    }

    [AvaloniaFact]
    public async Task Ilk_commit_icin_rebase_acilmiyor()
    {
        (MainWindowViewModel model, CapturingRebasePrompt prompt) = await OpenAsync([Step(2)], selectedIndex: 2);

        await model.ChangeCommitAsync(RebaseAction.Edit);

        prompt.Shown.ShouldBeNull();
        model.BranchNotice.ShouldBe("The first commit of the history has no parent to rebase from.");
    }

    [AvaloniaFact]
    public async Task Dalda_olmayan_commit_icin_rebase_acilmiyor()
    {
        (MainWindowViewModel model, CapturingRebasePrompt prompt) = await OpenAsync([Step(3)], selectedIndex: 1);

        await model.ChangeCommitAsync(RebaseAction.Edit);

        prompt.Shown.ShouldBeNull();
        model.BranchNotice.ShouldBe("The selected commit is not on the current branch.");
    }

    [AvaloniaFact]
    public async Task Etkilesimli_rebase_secili_committen_etkilesimli_aciyor()
    {
        (MainWindowViewModel model, CapturingRebasePrompt prompt) = await OpenAsync([Step(3)], selectedIndex: 1);

        await model.RebaseInteractivelyAsync();

        prompt.Shown!.Upstream.ShouldBe(FakeGitData.Sha(2));
        prompt.Shown.IsInteractive.ShouldBeTrue();
    }

    [Fact]
    public void Gelismis_menu_ogeleri_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(new RevisionGridMenuContext
        {
            CommitHash = FakeGitData.Sha(1),
        });

        IReadOnlyList<RevisionGridMenuEntry> advanced = menu.First(e => e.Id == RevisionGridMenuId.ManipulateCommit).Children;
        IReadOnlyList<RevisionGridMenuEntry> rebase = menu.First(e => e.Id == RevisionGridMenuId.RebaseOn).Children;

        foreach (RevisionGridMenuId id in new[]
                 {
                     RevisionGridMenuId.EditCommit,
                     RevisionGridMenuId.RewordCommit,
                     RevisionGridMenuId.FixupCommit,
                     RevisionGridMenuId.SquashCommit,
                     RevisionGridMenuId.AmendCommit,
                     RevisionGridMenuId.GetHelpOnHowToUseTheseFeatures,
                 })
        {
            advanced.First(e => e.Id == id).IsEnabled.ShouldBeTrue(id.ToString());
        }

        rebase.First(e => e.Id == RevisionGridMenuId.RebaseSelectedCommitInteractively).IsEnabled.ShouldBeTrue();
        rebase.First(e => e.Id == RevisionGridMenuId.RebaseSelectedCommitWithAdvancedOptions).IsEnabled.ShouldBeTrue();
    }
}
