using Avalonia.Headless.XUnit;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T13 — Commit listesinin artımlı yüklenmesi.
/// </summary>
/// <remarks>
/// ViewModel gerçek <c>git</c> süreci başlatmadan test edilir; <c>git</c> davranışı
/// <c>GitExt.Core.Tests</c>'te doğrulanıyor. Testler <see cref="AvaloniaFactAttribute"/>
/// kullanıyor çünkü ViewModel <c>Dispatcher.UIThread</c> üzerinden toplu güncelleme yapıyor.
/// </remarks>
public class CommitListViewModelTests
{
    private static CommitListViewModel Create(
        IReadOnlyList<CommitInfo>? commits = null,
        Exception? locateFailure = null,
        Exception? logFailure = null,
        RepositoryRefs? refs = null,
        Exception? refFailure = null) =>
        new(
            new FakeRepositoryLocator(locateFailure),
            new FakeCommitLogReader(commits, logFailure),
            new FakeRefReader(refs, refFailure),
            new FakeCommitSignatureReader(),new FakeDiffReader());

    [AvaloniaFact]
    public async Task Depo_acilinca_satirlar_yuklenir()
    {
        CommitListViewModel viewModel = Create(FakeGitData.LinearHistory(5));

        await viewModel.OpenAsync("/tmp/depo");

        viewModel.Rows.Count.ShouldBe(5);
        viewModel.Repository.ShouldNotBeNull();
        viewModel.ErrorMessage.ShouldBeNull();
        viewModel.IsLoading.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Satirlar_grafik_yerlesimiyle_gelir()
    {
        CommitListViewModel viewModel = Create(FakeGitData.LinearHistory(3));

        await viewModel.OpenAsync("/tmp/depo");

        // Doğrusal geçmiş tek şeritte kalmalı (ADR-0007, düz şerit kuralı).
        viewModel.Rows.ShouldAllBe(r => r.GraphRow.Lane == 0);
        viewModel.Rows[0].GraphRow.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task Parti_boyutundan_buyuk_gecmis_tamamen_yuklenir()
    {
        // Toplu güncelleme sınırı 256; bunun üstünde birden fazla parti oluşur.
        // Partiler arasında satır kaybı olmamalı.
        CommitListViewModel viewModel = Create(FakeGitData.LinearHistory(600));

        await viewModel.OpenAsync("/tmp/depo");

        viewModel.Rows.Count.ShouldBe(600);

        // Sıra korunmalı: en yeni önce.
        viewModel.Rows[0].Subject.ShouldBe("commit 600");
        viewModel.Rows[^1].Subject.ShouldBe("commit 1");
    }

    [AvaloniaFact]
    public async Task Merge_iceren_gecmiste_serit_acilir()
    {
        IReadOnlyList<CommitInfo> commits =
        [
            FakeGitData.Commit(FakeGitData.Sha(4), [FakeGitData.Sha(2), FakeGitData.Sha(3)], "merge"),
            FakeGitData.Commit(FakeGitData.Sha(3), [FakeGitData.Sha(1)], "yan"),
            FakeGitData.Commit(FakeGitData.Sha(2), [FakeGitData.Sha(1)], "ana"),
            FakeGitData.Commit(FakeGitData.Sha(1), [], "kök"),
        ];

        CommitListViewModel viewModel = Create(commits);

        await viewModel.OpenAsync("/tmp/depo");

        viewModel.Rows[0].IsMerge.ShouldBeTrue();
        viewModel.Rows[0].GraphRow.Edges.ShouldContain(e => e.IsDiagonal);
        viewModel.Rows.Max(r => r.GraphRow.LaneCount).ShouldBeGreaterThan(1);
    }

    [AvaloniaFact]
    public async Task Bos_depo_hata_vermeden_bos_liste_dondurur()
    {
        // Doğmamış depo: `git log` hata veriyor ama bu kullanıcı için hata değil.
        CommitListViewModel viewModel = Create(
            logFailure: new GitException(
                GitFailureKind.UnknownRevision, "revizyon yok", "git log", 128, string.Empty));

        await viewModel.OpenAsync("/tmp/bos-depo");

        viewModel.Rows.ShouldBeEmpty();
        viewModel.ErrorMessage.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Depo_olmayan_yol_icin_hata_mesaji_gosterilir()
    {
        CommitListViewModel viewModel = Create(
            locateFailure: new GitException(
                GitFailureKind.NotARepository,
                "Bu klasör bir Git deposu değil.",
                "git rev-parse",
                128,
                "fatal: not a git repository"));

        await viewModel.OpenAsync("/tmp/duz-klasor");

        viewModel.ErrorMessage.ShouldNotBeNull();
        viewModel.ErrorMessage!.ShouldContain("Git deposu değil");
        viewModel.Rows.ShouldBeEmpty();
        viewModel.IsLoading.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Ikinci_acilis_onceki_satirlari_temizler()
    {
        CommitListViewModel viewModel = Create(FakeGitData.LinearHistory(4));

        await viewModel.OpenAsync("/tmp/depo");
        viewModel.Rows.Count.ShouldBe(4);

        await viewModel.OpenAsync("/tmp/baska-depo");

        // Eski satırlar kalmamalı — aksi halde iki deponun geçmişi karışır.
        viewModel.Rows.Count.ShouldBe(4);
    }

    [AvaloniaFact]
    public async Task Iptal_edilen_yukleme_hata_uretmez()
    {
        CommitListViewModel viewModel = Create(FakeGitData.LinearHistory(100));

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await viewModel.OpenAsync("/tmp/depo", cancellation.Token);

        // İptal kullanıcı eylemidir, hata değil.
        viewModel.ErrorMessage.ShouldBeNull();
        viewModel.IsLoading.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Rozetler_dogru_satira_yerlestirilir()
    {
        CommitListViewModel viewModel = Create(
            FakeGitData.LinearHistory(3),
            refs: FakeGitData.Refs(
                localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(3), isCurrent: true)],
                tags: [FakeGitData.Tag("v0.1.0", FakeGitData.Sha(1))]));

        await viewModel.OpenAsync("/tmp/depo");

        // LinearHistory en yeniden en eskiye üretir: satır 0 = commit 3, satır 2 = commit 1.
        viewModel.Rows[0].Badges.Single().Text.ShouldBe("main");
        viewModel.Rows[1].HasBadges.ShouldBeFalse();
        viewModel.Rows[2].Badges.Single().Kind.ShouldBe(RefBadgeKind.Tag);
    }

    [AvaloniaFact]
    public async Task Ref_okumasi_basarisiz_olursa_gecmis_yine_de_gosterilir()
    {
        // Rozetler yardımcı bilgidir; okunamaması geçmişi göstermemek için sebep değil.
        CommitListViewModel viewModel = Create(
            FakeGitData.LinearHistory(3),
            refFailure: new GitException(
                GitFailureKind.Unknown, "for-each-ref patladı", "git for-each-ref", 1, string.Empty));

        await viewModel.OpenAsync("/tmp/depo");

        viewModel.Rows.Count.ShouldBe(3);
        viewModel.ErrorMessage.ShouldBeNull();
        viewModel.Rows.ShouldAllBe(r => !r.HasBadges);
    }
}
