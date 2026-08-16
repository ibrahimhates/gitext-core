using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T03 — the branch rename and delete flow (ViewModel side).
/// </summary>
public class BranchEditTests
{
    /// <summary>The selected commit must carry a branch badge; the fake ref reader sets that up.</summary>
    private const string BranchName = "ozellik";

    private static MainWindowViewModel Create(
        FakeBranchWriter writer,
        FakeBranchEditPrompt prompt)
    {
        // The branch is attached to the THIRD commit (the top of the list).
        RepositoryRefs refs = FakeGitData.Refs(
            localBranches: [FakeGitData.LocalBranch(BranchName, FakeGitData.Sha(3), isCurrent: true)]);

        return new MainWindowViewModel(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(refs),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            branchWriter: writer)
        {
            BranchEditPrompt = prompt,
        };
    }

    /// <summary>Index of the row carrying a local branch badge; -1 if there is none.</summary>
    private static int RowWithLocalBranch(MainWindowViewModel model)
    {
        for (int i = 0; i < model.Commits.Rows.Count; i++)
        {
            if (model.Commits.Rows[i].Badges.Any(badge => badge.IsLocalBranch))
            {
                return i;
            }
        }

        return -1;
    }

    [AvaloniaFact]
    public async Task Dalsiz_commit_te_yeniden_adlandirma_SOYLENIYOR()
    {
        FakeBranchWriter writer = new();
        FakeBranchEditPrompt prompt = new();
        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = -1;

        await model.RenameBranchCommand.ExecuteAsync(null);

        // Silently doing nothing would make the command look broken.
        model.BranchNotice.ShouldNotBeNull();
        writer.Renamed.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Dal_yeniden_adlandiriliyor()
    {
        FakeBranchWriter writer = new();
        FakeBranchEditPrompt prompt = new(
            rename: new RenameBranchDecision { Confirmed = true, NewName = "yeniad" });

        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");

        int row = RowWithLocalBranch(model);
        row.ShouldBeGreaterThanOrEqualTo(0, "sahte veri yerel dal rozeti üretmiyor");
        model.Commits.SelectedIndex = row;

        await model.RenameBranchCommand.ExecuteAsync(null);

        writer.Renamed.ShouldHaveSingleItem().New.ShouldBe("yeniad");
    }

    [AvaloniaFact]
    public async Task Ayni_ad_verilirse_YAZILMIYOR()
    {
        FakeBranchWriter writer = new();
        MainWindowViewModel model = Create(writer, new FakeBranchEditPrompt());

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = RowWithLocalBranch(model);

        // The dialog opens pre-filled with the current name; if the user confirms without
        // changing it, there is no point in making a git call.
        MainWindowViewModel same = Create(
            writer,
            new FakeBranchEditPrompt(
                rename: new RenameBranchDecision { Confirmed = true, NewName = BranchName }));

        await same.OpenRepositoryAsync("/tmp/depo");
        same.Commits.SelectedIndex = RowWithLocalBranch(same);

        await same.RenameBranchCommand.ExecuteAsync(null);

        writer.Renamed.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Iptal_edilince_YAZILMIYOR()
    {
        FakeBranchWriter writer = new();
        FakeBranchEditPrompt prompt = new(
            rename: RenameBranchDecision.Cancelled,
            delete: DeleteBranchDecision.Cancelled);

        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = RowWithLocalBranch(model);

        await model.RenameBranchCommand.ExecuteAsync(null);
        await model.DeleteBranchCommand.ExecuteAsync(null);

        writer.Renamed.ShouldBeEmpty();
        writer.Deleted.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Merge_edilmis_dal_TEK_TURDA_siliniyor()
    {
        FakeBranchWriter writer = new();
        FakeBranchEditPrompt prompt = new();
        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = RowWithLocalBranch(model);

        await model.DeleteBranchCommand.ExecuteAsync(null);

        // The second round must NOT open: a force warning on a merged branch would be a false alarm.
        prompt.DeleteRequests.Count.ShouldBe(1);
        prompt.DeleteRequests[0].IsUnmerged.ShouldBeFalse();
        writer.Deleted.ShouldHaveSingleItem().Force.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Birlestirilmemis_dalda_IKINCI_TUR_kurtarma_hash_i_ile_aciliyor()
    {
        // 🔴 The central flow of P06-T03. We do not compute merged-ness up front (measured: `-d`
        // also deletes a branch merged into upstream); git makes the decision, the second round
        // opens on its refusal, and that is when we have the RECOVERY HASH in hand.
        FakeBranchWriter writer = new()
        {
            UnmergedFailure = new BranchNotMergedException("ozellik", "1234567890abcdef"),
        };

        FakeBranchEditPrompt prompt = new()
        {
            ForcedDecision = new DeleteBranchDecision { Confirmed = true, Force = true },
        };

        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = RowWithLocalBranch(model);

        await model.DeleteBranchCommand.ExecuteAsync(null);

        prompt.DeleteRequests.Count.ShouldBe(2);
        prompt.DeleteRequests[1].IsUnmerged.ShouldBeTrue();
        prompt.DeleteRequests[1].LastCommitId.ShouldBe("1234567890abcdef");

        writer.Deleted.ShouldHaveSingleItem().Force.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Ikinci_turda_vazgecilince_SILINMIYOR()
    {
        FakeBranchWriter writer = new()
        {
            UnmergedFailure = new BranchNotMergedException("ozellik", "1234567890abcdef"),
        };

        FakeBranchEditPrompt prompt = new()
        {
            ForcedDecision = DeleteBranchDecision.Cancelled,
        };

        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = RowWithLocalBranch(model);

        await model.DeleteBranchCommand.ExecuteAsync(null);

        writer.Deleted.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Zorlanan_silmede_KURTARMA_KOMUTU_bildirimde_kaliyor()
    {
        // 🔴 MEASURED: the deleted branch's OWN reflog is deleted too; if the branch was never
        // checked out in this worktree there is no trace in the HEAD reflog either. If the hash
        // does not stay in the notification, the user has no way back.
        FakeBranchWriter writer = new()
        {
            UnmergedFailure = new BranchNotMergedException("ozellik", "1234567890abcdef"),
            DeletedCommitId = "1234567890abcdef",
        };

        FakeBranchEditPrompt prompt = new()
        {
            ForcedDecision = new DeleteBranchDecision { Confirmed = true, Force = true },
        };

        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = RowWithLocalBranch(model);

        await model.DeleteBranchCommand.ExecuteAsync(null);

        model.BranchNotice.ShouldNotBeNull();
        model.BranchNotice!.ShouldContain("1234567890abcdef");
        model.BranchNotice.ShouldContain("git branch");
    }

    [AvaloniaFact]
    public async Task Hata_SINIFLANDIRILMIS_mesajla_bildiriliyor()
    {
        FakeBranchWriter writer = new()
        {
            Failure = new GitException(
                GitFailureKind.Unknown,
                "Git komutu başarısız oldu.",
                "git branch -d ozellik",
                exitCode: 1,
                standardError: "error: cannot delete branch 'x' used by worktree"),
        };

        MainWindowViewModel model = Create(writer, new FakeBranchEditPrompt());

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = RowWithLocalBranch(model);

        await model.DeleteBranchCommand.ExecuteAsync(null);

        model.BranchNotice.ShouldNotBeNull();
        model.BranchNotice!.ShouldNotContain("error:");
    }
}
