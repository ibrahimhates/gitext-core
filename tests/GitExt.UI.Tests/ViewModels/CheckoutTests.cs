using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T02 — the switch-to-branch / commit flow (ViewModel side).
/// </summary>
public class CheckoutTests
{
    private static MainWindowViewModel Create(
        FakeBranchWriter writer,
        FakeCheckoutPrompt prompt,
        FakeRefReader? refReader = null)
    {
        return new MainWindowViewModel(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                refReader ?? new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            branchWriter: writer)
        {
            CheckoutPrompt = prompt,
        };
    }

    [AvaloniaFact]
    public async Task Secim_yokken_kullaniciya_SOYLENIYOR()
    {
        FakeBranchWriter writer = new();
        FakeCheckoutPrompt prompt = new();
        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = -1;

        await model.CheckoutCommand.ExecuteAsync(null);

        // Silently doing nothing makes the user think the command is broken.
        model.BranchNotice.ShouldNotBeNull();
        prompt.AskCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Dalsiz_commit_te_DETACHED_geçis_yapiliyor()
    {
        FakeBranchWriter writer = new();
        FakeCheckoutPrompt prompt = new();
        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = 1;

        await model.CheckoutCommand.ExecuteAsync(null);

        // Which one it will be is written IN THE DIALOG; it is not chosen silently.
        prompt.LastRequest!.IsDetached.ShouldBeTrue();

        BranchSwitchOptions options = writer.Switched.ShouldHaveSingleItem();
        options.Detach.ShouldBeTrue();
        options.Target.ShouldBe(model.Commits.Rows[1].Commit.Id.Value);
    }

    [AvaloniaFact]
    public async Task Iptal_edilince_HICBIR_SEY_yapilmiyor()
    {
        FakeBranchWriter writer = new();
        FakeCheckoutPrompt prompt = new(CheckoutDecision.Cancelled);
        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = 0;

        await model.CheckoutCommand.ExecuteAsync(null);

        prompt.AskCount.ShouldBe(1);
        writer.Switched.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Atma_secilince_ONAY_bayragi_geciyor()
    {
        // The Core side refuses to discard without `UserConfirmed`; not carrying the dialog's
        // choice over there would mean the feature silently never works at all.
        FakeBranchWriter writer = new();
        FakeCheckoutPrompt prompt = new(new CheckoutDecision
        {
            Confirmed = true,
            LocalChanges = LocalChangesAction.Discard,
        });

        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = 0;

        await model.CheckoutCommand.ExecuteAsync(null);

        BranchSwitchOptions options = writer.Switched.ShouldHaveSingleItem();
        options.LocalChanges.ShouldBe(LocalChangesAction.Discard);
        options.UserConfirmed.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Atma_SECILMEYINCE_onay_bayragi_GECMIYOR()
    {
        FakeBranchWriter writer = new();
        FakeCheckoutPrompt prompt = new(new CheckoutDecision
        {
            Confirmed = true,
            LocalChanges = LocalChangesAction.Stash,
        });

        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = 0;

        await model.CheckoutCommand.ExecuteAsync(null);

        writer.Switched.ShouldHaveSingleItem().UserConfirmed.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Cakisma_CIKIS_KODU_0_olsa_bile_kullaniciya_bildiriliyor()
    {
        // 🔴 The most important measurement of P06-T02: `switch --merge` returns exit code 0 on a
        // conflict. If the notification just says "switched" and goes quiet, the user carries on
        // working with unresolved files.
        FakeBranchWriter writer = new()
        {
            SwitchResult = new BranchSwitchResult { Target = "ozellik", HasConflicts = true },
        };

        MainWindowViewModel model = Create(writer, new FakeCheckoutPrompt());

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = 0;

        await model.CheckoutCommand.ExecuteAsync(null);

        model.BranchNotice.ShouldNotBeNull();
        model.BranchNotice!.ShouldContain("UNRESOLVED");
    }

    [AvaloniaFact]
    public async Task Stash_olusunca_GERI_ALMA_yolu_soyleniyor()
    {
        FakeBranchWriter writer = new()
        {
            SwitchResult = new BranchSwitchResult { Target = "ozellik", StashCreated = true },
        };

        MainWindowViewModel model = Create(writer, new FakeCheckoutPrompt());

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = 0;

        await model.CheckoutCommand.ExecuteAsync(null);

        model.BranchNotice!.ShouldContain("stash");
    }

    [AvaloniaFact]
    public async Task Hata_SINIFLANDIRILMIS_mesajla_bildiriliyor()
    {
        FakeBranchWriter writer = new()
        {
            Failure = new GitException(
                GitFailureKind.DirtyWorkingTree,
                "Çalışma dizininde kaydedilmemiş değişiklikler var; işlem devam edemedi.",
                "git switch ozellik",
                exitCode: 1,
                standardError: "error: Your local changes … would be overwritten"),
        };

        MainWindowViewModel model = Create(writer, new FakeCheckoutPrompt());

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = 0;

        await model.CheckoutCommand.ExecuteAsync(null);

        model.BranchNotice.ShouldNotBeNull();
        model.BranchNotice!.ShouldNotContain("error:");
    }
}
