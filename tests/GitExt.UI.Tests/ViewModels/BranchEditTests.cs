using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T03 — dal yeniden adlandırma ve silme akışı (ViewModel tarafı).
/// </summary>
public class BranchEditTests
{
    /// <summary>Seçili commit'in dal rozeti taşıması şart; sahte ref okuyucu onu kuruyor.</summary>
    private const string BranchName = "ozellik";

    private static MainWindowViewModel Create(
        FakeBranchWriter writer,
        FakeBranchEditPrompt prompt)
    {
        // Dal, ÜÇÜNCÜ commit'e (listenin başı) bağlanıyor.
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

    /// <summary>Yerel dal rozeti taşıyan satırın indeksi; yoksa -1.</summary>
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

        // Sessizce hiçbir şey yapmamak komutu bozuk gösterirdi.
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

        // Diyalog mevcut adla dolu açılıyor; kullanıcı değiştirmeden onaylarsa gereksiz
        // bir git çağrısı yapmanın anlamı yok.
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

        // İkinci tur AÇILMAMALI: birleştirilmiş dalda zorlama uyarısı yanlış alarm olurdu.
        prompt.DeleteRequests.Count.ShouldBe(1);
        prompt.DeleteRequests[0].IsUnmerged.ShouldBeFalse();
        writer.Deleted.ShouldHaveSingleItem().Force.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Birlestirilmemis_dalda_IKINCI_TUR_kurtarma_hash_i_ile_aciliyor()
    {
        // 🔴 P06-T03'ün merkezi akışı. Birleşmişliği önden hesaplamıyoruz (ölçüldü: `-d`
        // upstream'e birleşmiş dalı da siliyor); kararı git veriyor, ikinci tur onun
        // reddi üzerine açılıyor ve KURTARMA HASH'İ o zaman elimizde oluyor.
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
        // 🔴 ÖLÇÜLDÜ: silinen dalın KENDİ reflog'u da siliniyor; dal bu çalışma ağacında
        // hiç checkout edilmemişse HEAD reflog'unda da iz yok. Hash bildirimde kalmazsa
        // kullanıcının geri dönüş yolu kalmaz.
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
