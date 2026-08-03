

namespace GitExt.Core.Tests;

/// <summary>
/// Dosya sistemi olaylarının sınıflandırılması (P05-T14).
/// </summary>
/// <remarks>
/// Buradaki her kural <b>gerçek <c>git</c> ile ölçülmüş bir olay dizisinden</b> geliyor;
/// testler o ölçümlerin özeti. Sınıflandırıcı yanlış olduğunda belirti sessizdir: ya
/// hiç tazelenmez (bayat ekran) ya da durmadan tazelenir (sonsuz döngü).
/// </remarks>
public class RepositoryChangeClassifierTests
{
    [Fact]
    public void Calisma_agacindaki_dosya_calisma_agaci_degisimidir()
    {
        RepositoryChangeClassifier.ClassifyWorkingTreePath("src/Program.cs")
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Ref_guncellemesi_depo_degisimidir()
    {
        // ÖLÇÜLDÜ: harici `git commit` 64 olay üretti ve HEPSİ .git altındaydı; çalışma
        // ağacında sıfır olay. Bu yol elenirse dışarıdan yapılan commit hiç görülmez.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/refs/heads/main")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/HEAD")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/packed-refs")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Kilit_dosyalari_yok_sayilir()
    {
        // ÖLÇÜLDÜ: salt-okunur `git status` bile .git/index.lock oluşturup siliyor (2 olay).
        // Sonsuz tazeleme döngüsünü kapatan tek şey bu filtre.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/index.lock").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/refs/heads/main.lock").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/config.lock").ShouldBeNull();
    }

    [Fact]
    public void Kilidin_kaldirilmasiyla_gelen_gercek_ref_sinyali_YENMEZ()
    {
        // ÖLÇÜLDÜ: `git branch x` beş olay üretti; gerçek sinyal
        // `refs/heads/x.lock → refs/heads/x` YENİDEN ADLANDIRMASI. İzleyici yeniden
        // adlandırmada YENİ adı kullandığı için kilit filtresine takılmıyor.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/refs/heads/gecici-dal")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Index_calisma_agaci_degisimidir_depo_degisimi_degil()
    {
        // Stage durumu değişti; commit listesi aynı. Commit listesini de tazelemek
        // her `git add` sonrası gereksiz bir log okuması olurdu.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/index")
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Nesne_ve_reflog_yazimlari_yok_sayilir()
    {
        // ÖLÇÜLDÜ: tek bir `git commit` 19 Created olayının çoğunu objects/ altında üretti.
        // Nesne yazılmış olması ref güncellenmedikçe kullanıcı için görünür değil.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/objects/3c/1a2b").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/objects/pack/pack-abc.pack").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/logs/refs/heads/main").ShouldBeNull();
    }

    [Fact]
    public void Kendi_taslak_dosyamiz_yok_sayilir()
    {
        // P05-T13'ün taslağı yazarken sürekli yazılıyor; elenmezse kullanıcı commit mesajı
        // yazarken her tuş vuruşu tazeleme tetiklerdi.
        RepositoryChangeClassifier
            .ClassifyWorkingTreePath($".git/{CommitMessageStore.DraftFileName}")
            .ShouldBeNull();

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/COMMIT_EDITMSG").ShouldBeNull();
    }

    [Fact]
    public void Suregelen_islem_durumu_depo_degisimidir()
    {
        // Merge/rebase/cherry-pick başlaması veya bitmesi ekranı değiştirir.
        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/MERGE_HEAD")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/rebase-merge/done")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(".git/CHERRY_PICK_HEAD")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Ic_ice_depolarin_git_dizini_de_ayni_kurala_tabi()
    {
        // Alt modülün kendi .git'i çalışma ağacının altında; nesne yazımı yine gürültü.
        RepositoryChangeClassifier.ClassifyWorkingTreePath("alt/modul/.git/objects/ab/cd")
            .ShouldBeNull();

        RepositoryChangeClassifier.ClassifyWorkingTreePath("alt/modul/.git/refs/heads/main")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Alt_modulun_git_DOSYASI_depo_degisimidir()
    {
        // Alt modülde `.git` bir dizin değil dosyadır; oluşması/değişmesi depo yapısını
        // değiştirir.
        RepositoryChangeClassifier.ClassifyWorkingTreePath("alt/modul/.git")
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Git_dizinine_goreli_yollar_ayri_siniflandirilir()
    {
        // Bağlı çalışma ağacında git dizini çalışma ağacının DIŞINDA; oradaki izleyicinin
        // yolları `.git/` önekini içermez.
        RepositoryChangeClassifier.ClassifyGitDirectoryPath("HEAD")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyGitDirectoryPath("index")
            .ShouldBe(RepositoryChangeKind.WorkingTree);

        RepositoryChangeClassifier.ClassifyGitDirectoryPath("index.lock").ShouldBeNull();
        RepositoryChangeClassifier.ClassifyGitDirectoryPath("objects/ab/cd").ShouldBeNull();
    }

    [Fact]
    public void Windows_ayraci_da_kabul_edilir()
    {
        RepositoryChangeClassifier.ClassifyWorkingTreePath(@".git\refs\heads\main")
            .ShouldBe(RepositoryChangeKind.Repository);

        RepositoryChangeClassifier.ClassifyWorkingTreePath(@"src\Program.cs")
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }
}
