using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T14 — Seçim ve gezinme mantığı.
/// </summary>
/// <remarks>
/// Gezinme <b>kararı</b> ViewModel'da olduğu için burada test edilebiliyor; tuş eşlemesi ve
/// sayfa boyutu görünüm işi (bkz. <c>CommitListView.axaml.cs</c>) ve
/// <c>CommitListKeyboardTests</c>'te gerçek tuş olaylarıyla doğrulanıyor.
/// </remarks>
public class CommitListNavigationTests
{
    private static async Task<CommitListViewModel> LoadedAsync(int commitCount = 10)
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(commitCount)),
            new FakeRefReader(),
            new FakeCommitSignatureReader());

        await viewModel.OpenAsync("/tmp/depo");
        return viewModel;
    }

    [AvaloniaFact]
    public async Task Depo_acilinca_en_yeni_commit_secilir()
    {
        // Boş bir detay paneliyle karşılaşmak yerine kullanıcı doğrudan bir şey görsün.
        CommitListViewModel viewModel = await LoadedAsync();

        viewModel.SelectedIndex.ShouldBe(0);
        viewModel.SelectedRow.ShouldBeSameAs(viewModel.Rows[0]);
    }

    [AvaloniaFact]
    public async Task Bos_depoda_secim_olusmaz()
    {
        CommitListViewModel viewModel = await LoadedAsync(0);

        viewModel.SelectedIndex.ShouldBe(-1);
        viewModel.SelectedRow.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Secili_satir_indeksten_turetilir()
    {
        CommitListViewModel viewModel = await LoadedAsync();

        viewModel.SelectedIndex = 3;

        viewModel.SelectedRow.ShouldBeSameAs(viewModel.Rows[3]);
    }

    [AvaloniaFact]
    public async Task Seciliyken_sayfa_kadar_ilerlenir()
    {
        CommitListViewModel viewModel = await LoadedAsync(50);
        viewModel.SelectedIndex = 10;

        viewModel.MoveSelection(20).ShouldBeTrue();

        viewModel.SelectedIndex.ShouldBe(30);
    }

    [AvaloniaFact]
    public async Task Liste_sinirlarinda_durulur_sarmalanmaz()
    {
        // Sarmalamak, uzun bir listenin sonunda kullanıcının yerini kaybetmesi demek.
        CommitListViewModel viewModel = await LoadedAsync(10);

        viewModel.SelectedIndex = 8;
        viewModel.MoveSelection(100).ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(9);

        viewModel.MoveSelection(100).ShouldBeFalse();
        viewModel.SelectedIndex.ShouldBe(9);

        viewModel.MoveSelection(-100).ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Secim_yokken_ilk_hareket_listenin_ucundan_baslar()
    {
        // Depo açılışında ilk satır seçiliyor; seçimsiz durum kullanıcının seçimi
        // temizlemesiyle oluşur (örn. çoklu seçimde her şeyi bırakmak).
        CommitListViewModel viewModel = await LoadedAsync(10);

        viewModel.SelectedIndex = -1;
        viewModel.MoveSelection(1).ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(0);

        viewModel.SelectedIndex = -1;
        viewModel.MoveSelection(-1).ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(9);
    }

    [AvaloniaFact]
    public async Task Bos_listede_gezinme_cokmez()
    {
        CommitListViewModel viewModel = await LoadedAsync(0);

        viewModel.MoveSelection(5).ShouldBeFalse();
        viewModel.GoToParent().ShouldBeFalse();
        viewModel.GoToChild().ShouldBeFalse();
        viewModel.SelectedIndex.ShouldBe(-1);
    }

    [AvaloniaFact]
    public async Task Ebeveyne_atlanir()
    {
        // LinearHistory en yeniden en eskiye: satır 0 = commit 10, ebeveyni satır 1.
        CommitListViewModel viewModel = await LoadedAsync(10);
        viewModel.SelectedIndex = 0;

        viewModel.GoToParent().ShouldBeTrue();

        viewModel.SelectedIndex.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Cocuga_atlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(10);
        viewModel.SelectedIndex = 5;

        viewModel.GoToChild().ShouldBeTrue();

        viewModel.SelectedIndex.ShouldBe(4);
    }

    [AvaloniaFact]
    public async Task Kok_commitin_ebeveyni_yok()
    {
        CommitListViewModel viewModel = await LoadedAsync(10);
        viewModel.SelectedIndex = 9;

        viewModel.GoToParent().ShouldBeFalse();
        viewModel.SelectedIndex.ShouldBe(9);
    }

    [AvaloniaFact]
    public async Task Dal_ucunun_cocugu_yok()
    {
        CommitListViewModel viewModel = await LoadedAsync(10);
        viewModel.SelectedIndex = 0;

        viewModel.GoToChild().ShouldBeFalse();
        viewModel.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Birlesme_commitinde_ilk_ebeveyne_gidilir()
    {
        // Birleşmenin ikinci ebeveyni listede daha aşağıda; "ana hat" ilk ebeveyndir.
        //   satır 0: merge (ebeveynler: 3, 2)
        //   satır 1: commit 3   ← ilk ebeveyn
        //   satır 2: commit 2
        //   satır 3: commit 1
        CommitInfo[] commits =
        [
            FakeGitData.Commit(FakeGitData.Sha(4), [FakeGitData.Sha(3), FakeGitData.Sha(2)], "merge"),
            FakeGitData.Commit(FakeGitData.Sha(3), [FakeGitData.Sha(1)], "ust dal"),
            FakeGitData.Commit(FakeGitData.Sha(2), [FakeGitData.Sha(1)], "yan dal"),
            FakeGitData.Commit(FakeGitData.Sha(1), [], "kok"),
        ];

        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(commits),
            new FakeRefReader(),
            new FakeCommitSignatureReader());

        await viewModel.OpenAsync("/tmp/depo");

        viewModel.SelectedIndex = 0;
        viewModel.GoToParent().ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(1);

        // Çocuk taraması, ebeveyni birden fazla çocuğu olan bir commit'te de çalışmalı.
        viewModel.SelectedIndex = 3;
        viewModel.GoToChild().ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task Tam_sha_ile_atlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);

        viewModel.TryGoToCommit(FakeGitData.Sha(15)).ShouldBeTrue();

        viewModel.SelectedRow!.Commit.Id.Value.ShouldBe(FakeGitData.Sha(15));
    }

    [AvaloniaFact]
    public async Task Kisa_sha_onekiyle_atlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);

        // Sha(7) = 39 sıfır + "7"; öneki 4 sıfır — 20 commit'in hepsi sıfırla başlıyor,
        // yani ilk eşleşen satır seçilmeli (satır 0 = commit 20).
        viewModel.TryGoToCommit("0000").ShouldBeTrue();
        viewModel.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Cok_kisa_veya_bulunamayan_onek_secimi_bozmaz()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);
        viewModel.SelectedIndex = 5;

        // git'in alt sınırı 4 karakter.
        viewModel.TryGoToCommit("00").ShouldBeFalse();

        // Geçerli uzunlukta ama hiçbir commit'le eşleşmiyor.
        viewModel.TryGoToCommit("dead").ShouldBeFalse();

        viewModel.SelectedIndex.ShouldBe(5);
    }

    [AvaloniaFact]
    public async Task Arama_bulunamayinca_durum_bildirir()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);

        viewModel.SearchText = "dead";
        viewModel.ApplySearch();

        viewModel.SearchStatus.ShouldNotBeNullOrEmpty();

        // Yeniden yazmaya başlayınca uyarı kaybolmalı; eski hata yeni aramaya yapışmasın.
        viewModel.SearchText = "0000";
        viewModel.SearchStatus.ShouldBeNull();

        viewModel.ApplySearch();
        viewModel.SearchStatus.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Yeni_depo_acilinca_secim_ve_indeks_sifirlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(20);
        viewModel.SelectedIndex = 7;

        await viewModel.OpenAsync("/tmp/baska-depo");

        // Yeni depo kendi en yeni commit'iyle açılır; eski satır 7'de kalınmaz.
        viewModel.SelectedIndex.ShouldBe(0);

        // Eski deponun indeksi kalsaydı, aynı SHA'lar yanlış satırlara götürürdü.
        viewModel.TryGoToCommit(FakeGitData.Sha(15)).ShouldBeTrue();
        viewModel.SelectedRow!.Commit.Id.Value.ShouldBe(FakeGitData.Sha(15));
    }
}
