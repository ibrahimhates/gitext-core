using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T01 — dal oluşturma.
/// </summary>
/// <remarks>
/// Testlerin ağırlığı ölçümde çıkan <b>sessiz</b> davranışlarda: git'in bir şeyi hata
/// saymadan yanlış yapması (iç içe ref adı) ya da hatayı yanlış anlatması (boş depo).
/// </remarks>
public class BranchWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        BranchWriter Writer,
        WorkingTreeWriter WorkingTree,
        GitWriteQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        public string Path => Repository.Path;

        public string Read(string name) =>
            File.ReadAllText(System.IO.Path.Combine(Repository.Path, name));

        public string CurrentBranch => Repository.Git("symbolic-ref", "--short", "HEAD").Trim();

        public IReadOnlyList<string> Branches =>
        [
            .. Repository
                .Git("for-each-ref", "--format=%(refname)", "refs/heads")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()),
        ];
    }

    private static async Task<Harness> CreateAsync(bool withCommit = true)
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        if (withCommit)
        {
            repository.WriteFile("a.txt", "a\n");
            repository.Git("add", "-A");
            repository.Git("commit", "-m", "ilk");
        }

        GitWriter writer = new(runner, queue);
        WorkingTreeWriter workingTree = new(writer, runner);

        return new Harness(
            repository, new BranchWriter(writer, runner, workingTree), workingTree, queue);
    }

    [Fact]
    public async Task Dal_olusturulup_gecilir()
    {
        using Harness harness = await CreateAsync();

        BranchCreateResult result = await harness.Writer.CreateAsync(
            harness.Path, new BranchCreateOptions { Name = "ozellik" }, Ct);

        result.Name.ShouldBe("ozellik");
        result.CheckedOut.ShouldBeTrue();
        harness.CurrentBranch.ShouldBe("ozellik");
    }

    [Fact]
    public async Task Checkout_kapaliyken_DAL_DEGISMIYOR()
    {
        using Harness harness = await CreateAsync();
        string before = harness.CurrentBranch;

        await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions { Name = "ozellik", Checkout = false },
            Ct);

        harness.CurrentBranch.ShouldBe(before);
        harness.Branches.ShouldContain("refs/heads/ozellik");
    }

    [Fact]
    public async Task Baslangic_noktasi_verilince_ORADAN_olusuyor()
    {
        using Harness harness = await CreateAsync();
        string ilk = harness.Repository.Git("rev-parse", "HEAD").Trim();

        harness.Repository.WriteFile("b.txt", "b\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ikinci");

        await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions { Name = "gecmisten", StartPoint = ilk, Checkout = false },
            Ct);

        harness.Repository.Git("rev-parse", "gecmisten").Trim().ShouldBe(ilk);
    }

    [Fact]
    public async Task Kirli_agacta_checkout_SUZ_olusturma_her_zaman_calisir()
    {
        // ÖLÇÜLDÜ: `git branch` çalışma ağacına hiç dokunmuyor.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "KIRLI\n");

        await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions { Name = "temiz", Checkout = false },
            Ct);

        harness.Branches.ShouldContain("refs/heads/temiz");
        harness.Repository.Git("status", "--porcelain").ShouldContain("a.txt");
    }

    [Fact]
    public async Task Cakisan_kirli_dosya_varken_switch_REDDEDILIR_ve_dal_OLUSMAZ()
    {
        // 🔴 Asıl güvence bu: reddedilen bir işlemin YARIM sonucu olmamalı. Dal oluşup
        // checkout başarısız olsaydı kullanıcı, adı "kullanılmış" ama beklediği yerde
        // olmayan bir dalla kalırdı.
        using Harness harness = await CreateAsync();
        string ilk = harness.Repository.Git("rev-parse", "HEAD").Trim();

        harness.Repository.WriteFile("b.txt", "b\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ikinci");

        // b.txt yalnızca ikinci commit'te var; onu kirletip ilk commit'e geçmeye çalışmak
        // çakışma üretir.
        harness.Repository.WriteFile("b.txt", "YEREL DEGISIKLIK\n");

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "olmamali", StartPoint = ilk },
                Ct));

        error.Kind.ShouldBe(GitFailureKind.DirtyWorkingTree);
        harness.Branches.ShouldNotContain("refs/heads/olmamali");
        harness.Repository.Git("cat-file", "-p", ":b.txt").ShouldBe("b\n");
    }

    [Fact]
    public async Task Var_olan_dal_ANLAMLI_hatayla_reddediliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "ozellik");

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "ozellik", Checkout = false },
                Ct));

        error.Kind.ShouldBe(GitFailureKind.BranchAlreadyExists);
    }

    [Theory]
    [InlineData("ust", "ust/alt")]
    [InlineData("ust/alt", "ust")]
    public async Task Dizin_dosya_cakismasi_ANLAMLI_hatayla_reddediliyor(string first, string second)
    {
        // 🔴 ÖLÇÜLDÜ, iki yönlü: ad kurallarına TAMAMEN uygun olduğu için doğrulamadan
        // geçiyor; git dalları dosya gibi sakladığı için yalnızca o söyleyebiliyor.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", first);

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = second, Checkout = false },
                Ct));

        error.Kind.ShouldBe(GitFailureKind.RefNameConflict);
    }

    [Fact]
    public async Task Bos_depoda_hata_DEPONUN_BOS_oldugunu_soyluyor()
    {
        // 🔴 ÖLÇÜLDÜ: git'in kendi mesajı "not a valid object name: 'main'" — bu
        // sınıflandırmada UnknownRevision'a düşüyor ve kullanıcıya "dal bulunamadı" derdi.
        // Oysa kullanıcı bir dal ADI yazmadı; depo boş.
        using Harness harness = await CreateAsync(withCommit: false);

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "ilk-dal", Checkout = false },
                Ct));

        error.Kind.ShouldBe(GitFailureKind.UnbornHead);
    }

    [Fact]
    public async Task Gecersiz_ad_GIT_CAGRILMADAN_reddediliyor()
    {
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<ArgumentException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "gecersiz ad" },
                Ct));

        harness.Branches.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Tam_ref_adi_yapistirmak_IC_ICE_dal_olusturmuyor()
    {
        // 🔴 ÖLÇÜLDÜ: `git branch refs/heads/x` hata VERMİYOR, `refs/heads/refs/heads/x`
        // oluşturuyor. Kullanıcı `git branch -a` çıktısından bir ad kopyaladığında
        // sessizce iç içe bir dal elde ederdi.
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<ArgumentException>(
            harness.Writer.CreateAsync(
                harness.Path,
                new BranchCreateOptions { Name = "refs/heads/x", Checkout = false },
                Ct));

        harness.Branches.ShouldNotContain("refs/heads/refs/heads/x");
    }

    [Fact]
    public async Task Uzak_daldan_olusturulunca_upstream_BILDIRILIYOR()
    {
        // ÖLÇÜLDÜ: upstream'i git kendisi kuruyor (`branch.autoSetupMerge` varsayılanı).
        // Biz taklit etmiyoruz, sonucu OKUYUP bildiriyoruz — kullanıcının ayarı bunu
        // değiştirebilir ve o zaman uydurmuş oluruz.
        using TestRepository upstream = TestRepository.CreateEmpty();
        upstream.WriteFile("a.txt", "a\n");
        upstream.Git("add", "-A");
        upstream.Git("commit", "-m", "ilk");
        upstream.Git("branch", "ozellik");

        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", upstream.Path);
        harness.Repository.Git("fetch", "-q", "origin");

        BranchCreateResult fromRemote = await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions
            {
                Name = "ozellik",
                StartPoint = "origin/ozellik",
                Checkout = false,
            },
            Ct);

        fromRemote.Upstream.ShouldBe("origin/ozellik");

        // …yerel bir daldan oluşturulunca kurulmuyor.
        BranchCreateResult fromLocal = await harness.Writer.CreateAsync(
            harness.Path,
            new BranchCreateOptions { Name = "yerelden", Checkout = false },
            Ct);

        fromLocal.Upstream.ShouldBeNull();
    }

    // ---- Dal değiştirme (P06-T02) ----

    /// <summary>İki dal kurar: `main` (main.txt var) ve `ozellik` (main.txt silinmiş).</summary>
    private static void SetupTwoBranches(Harness harness)
    {
        harness.Repository.WriteFile("ortak.txt", "ortak\n");
        harness.Repository.WriteFile("main.txt", "mainde\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("temel");

        harness.Repository.Git("switch", "-c", "ozellik");
        harness.Repository.Git("rm", "-q", "main.txt");
        harness.Repository.Commit("main.txt silindi");
        harness.Repository.Git("switch", "main");
    }

    [Fact]
    public async Task Temiz_agacta_dal_degistiriliyor()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path, new BranchSwitchOptions { Target = "ozellik" }, Ct);

        harness.CurrentBranch.ShouldBe("ozellik");
        result.HasConflicts.ShouldBeFalse();
        result.StashCreated.ShouldBeFalse();
    }

    [Fact]
    public async Task Ilgisiz_kirli_dosya_YENI_DALA_tasiniyor()
    {
        // ÖLÇÜLDÜ: git taşıyabildiğini taşıyor; bu bir hata değil beklenen davranış.
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("ortak.txt", "KIRLI\n");

        await harness.Writer.SwitchAsync(
            harness.Path, new BranchSwitchOptions { Target = "ozellik" }, Ct);

        harness.CurrentBranch.ShouldBe("ozellik");
        harness.Read("ortak.txt").ShouldBe("KIRLI\n");
    }

    [Fact]
    public async Task Cakisan_kirli_dosyada_gecis_REDDEDILIYOR()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("main.txt", "YEREL\n");

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.SwitchAsync(
                harness.Path, new BranchSwitchOptions { Target = "ozellik" }, Ct));

        error.Kind.ShouldBe(GitFailureKind.DirtyWorkingTree);
        harness.CurrentBranch.ShouldBe("main");
        harness.Read("main.txt").ShouldBe("YEREL\n");
    }

    [Fact]
    public async Task Stash_yolu_cakismayi_cozuyor_ve_icerik_KAYBOLMUYOR()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("main.txt", "YEREL\n");

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = "ozellik", LocalChanges = LocalChangesAction.Stash },
            Ct);

        harness.CurrentBranch.ShouldBe("ozellik");
        result.StashCreated.ShouldBeTrue();

        // İçerik stash'te duruyor: geri dönülüp alınabiliyor.
        harness.Repository.Git("switch", "main");
        harness.Repository.Git("stash", "pop");
        harness.Read("main.txt").ShouldBe("YEREL\n");
    }

    [Fact]
    public async Task Stash_TAKIP_EDILMEYEN_dosya_cakismasini_da_cozuyor()
    {
        // 🔴 ÖLÇÜLDÜ: `--discard-changes` bu durumu ÇÖZMÜYOR (reddediyor ve dosyaya
        // dokunmuyor). Yani "zorla" evrensel bir kaçış yolu değil; stash daha yetenekli.
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);

        // `ozellik` dalında `main.txt` yok; `main` dalında takip ediliyor. Ters yönde
        // çakışma kurmak için `ozellik`e geçip aynı adı takipsiz dosyayla dolduruyoruz.
        harness.Repository.Git("switch", "ozellik");
        harness.Repository.WriteFile("main.txt", "BENIM YEREL DOSYAM\n");

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = "main", LocalChanges = LocalChangesAction.Stash },
            Ct);

        harness.CurrentBranch.ShouldBe("main");
        result.StashCreated.ShouldBeTrue();
    }

    [Fact]
    public async Task Onaysiz_ATMA_reddediliyor()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("main.txt", "YEREL\n");

        await Should.ThrowAsync<InvalidOperationException>(
            harness.Writer.SwitchAsync(
                harness.Path,
                new BranchSwitchOptions
                {
                    Target = "ozellik",
                    LocalChanges = LocalChangesAction.Discard,
                },
                Ct));

        harness.CurrentBranch.ShouldBe("main");
        harness.Read("main.txt").ShouldBe("YEREL\n");
    }

    [Fact]
    public async Task Atilan_icerik_YEDEKTEN_geri_okunabiliyor()
    {
        // 🔴 P06-T02'nin en önemli güvencesi. ÖLÇÜLDÜ: `--discard-changes` sonrası
        // STAGE'LENMEMİŞ içeriğin nesne veritabanında hiçbir izi kalmıyor —
        // `fsck --lost-found` bile bulmuyor. Yedek olmasa geri dönüş yolu YOK.
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("ortak.txt", "KAYBOLMAMASI GEREKEN\n");

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions
            {
                Target = "ozellik",
                LocalChanges = LocalChangesAction.Discard,
                UserConfirmed = true,
            },
            Ct);

        harness.CurrentBranch.ShouldBe("ozellik");
        harness.Read("ortak.txt").ShouldBe("ortak\n");

        DiscardBackup backup = result.Backups.ShouldHaveSingleItem();
        backup.Path.Value.ShouldBe("ortak.txt");
        harness.Repository.Git("cat-file", "-p", backup.BlobId)
            .ShouldBe("KAYBOLMAMASI GEREKEN\n");
    }

    [Fact]
    public async Task Merge_yolunda_CIKIS_KODU_0_olsa_bile_cakisma_bildiriliyor()
    {
        // 🔴 ÖLÇÜLDÜ: `switch --merge` çakışmada çıkış kodu **0** veriyor, ağacı
        // birleşmemiş bırakıyor ve gizli bir autostash oluşturuyor. Çıkış koduna bakan
        // bir arayüz "başarıyla geçildi" derdi.
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        harness.Repository.WriteFile("main.txt", "YEREL SATIR\n");

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = "ozellik", LocalChanges = LocalChangesAction.Merge },
            Ct);

        result.HasConflicts.ShouldBeTrue();
    }

    [Fact]
    public async Task Temiz_agacta_merge_yolu_cakisma_BILDIRMIYOR()
    {
        // Yanlış alarm da en az sessiz hata kadar zararlı: her geçişte "çakışma var"
        // diyen bir arayüzün uyarısı okunmaz olur.
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);

        BranchSwitchResult result = await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = "ozellik", LocalChanges = LocalChangesAction.Merge },
            Ct);

        result.HasConflicts.ShouldBeFalse();
        harness.CurrentBranch.ShouldBe("ozellik");
    }

    [Fact]
    public async Task Detached_HEAD_e_gecilebiliyor()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);
        string ilk = harness.Repository.Git("rev-parse", "HEAD").Trim();

        await harness.Writer.SwitchAsync(
            harness.Path,
            new BranchSwitchOptions { Target = ilk, Detach = true },
            Ct);

        harness.Repository.Git("rev-parse", "HEAD").Trim().ShouldBe(ilk);
        harness.Repository.TryGit("symbolic-ref", "--short", "HEAD").ExitCode.ShouldNotBe(0);
    }

    [Fact]
    public async Task Cozumlenemeyen_hedef_ANLAMLI_hatayla_reddediliyor()
    {
        using Harness harness = await CreateAsync();
        SetupTwoBranches(harness);

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.SwitchAsync(
                harness.Path, new BranchSwitchOptions { Target = "boyle-bir-dal-yok" }, Ct));

        error.Kind.ShouldBe(GitFailureKind.UnknownRevision);
        harness.CurrentBranch.ShouldBe("main");
    }

    // ---- Yeniden adlandırma ve silme (P06-T03) ----

    [Fact]
    public async Task Dal_yeniden_adlandiriliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "eski");

        await harness.Writer.RenameAsync(harness.Path, "eski", "yeni", Ct);

        harness.Branches.ShouldContain("refs/heads/yeni");
        harness.Branches.ShouldNotContain("refs/heads/eski");
    }

    [Fact]
    public async Task Yeniden_adlandirma_UPSTREAM_ve_reflog_u_koruyor()
    {
        // Upstream kaybolsaydı sonraki `push` sessizce başka bir yere giderdi.
        using TestRepository upstream = TestRepository.CreateEmpty();
        upstream.WriteFile("a.txt", "a\n");
        upstream.Git("add", "-A");
        upstream.Git("commit", "-m", "ilk");
        upstream.Git("branch", "ozellik");

        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", upstream.Path);
        harness.Repository.Git("fetch", "-q", "origin");
        harness.Repository.Git("branch", "ozellik", "origin/ozellik");

        await harness.Writer.RenameAsync(harness.Path, "ozellik", "yeniad", Ct);

        harness.Repository
            .Git("for-each-ref", "--format=%(upstream:short)", "refs/heads/yeniad")
            .Trim()
            .ShouldBe("origin/ozellik");

        harness.Repository.Git("reflog", "show", "yeniad").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Var_olan_ada_yeniden_adlandirma_HEDEFI_EZMIYOR()
    {
        // 🔴 ÖLÇÜLDÜ: `git branch -M <var-olan>` hedef dalı hiçbir uyarı olmadan yok
        // ediyor. Zorlama sunulmuyor; çakışma hata olarak bildiriliyor.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "kaynak");
        harness.Repository.WriteFile("b.txt", "b\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("hedefin ucu");
        harness.Repository.Git("branch", "hedef");

        string hedefOnce = harness.Repository.Git("rev-parse", "hedef").Trim();

        GitException error = await Should.ThrowAsync<GitException>(
            harness.Writer.RenameAsync(harness.Path, "kaynak", "hedef", Ct));

        error.Kind.ShouldBe(GitFailureKind.BranchAlreadyExists);

        harness.Branches.ShouldContain("refs/heads/kaynak");
        harness.Repository.Git("rev-parse", "hedef").Trim().ShouldBe(hedefOnce);
    }

    [Fact]
    public async Task Gecersiz_yeni_ad_GIT_CAGRILMADAN_reddediliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "eski");

        await Should.ThrowAsync<ArgumentException>(
            harness.Writer.RenameAsync(harness.Path, "eski", "gecersiz ad", Ct));

        harness.Branches.ShouldContain("refs/heads/eski");
    }

    [Fact]
    public async Task Merge_edilmis_dal_siliniyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("branch", "birlesmis");

        BranchDeleteResult result = await harness.Writer.DeleteAsync(
            harness.Path, "birlesmis", cancellationToken: Ct);

        harness.Branches.ShouldNotContain("refs/heads/birlesmis");
        result.WasUnmerged.ShouldBeFalse();
        result.LastCommitId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Merge_EDILMEMIS_dal_zorlama_olmadan_SILINMIYOR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("switch", "-c", "birlesmemis");
        harness.Repository.WriteFile("yeni.txt", "iş\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("KAYBOLABILIR IS");
        harness.Repository.Git("switch", "-");

        BranchNotMergedException error = await Should.ThrowAsync<BranchNotMergedException>(
            harness.Writer.DeleteAsync(harness.Path, "birlesmemis", cancellationToken: Ct));

        // Hata, kurtarma için gereken hash'i TAŞIMALI: kullanıcı zorlamayı seçmeden önce
        // neyin gideceğini görebilmeli.
        error.LastCommitId.ShouldNotBeNullOrWhiteSpace();
        harness.Branches.ShouldContain("refs/heads/birlesmemis");
    }

    [Fact]
    public async Task Zorlanan_silmede_SON_COMMIT_kullaniciya_veriliyor()
    {
        // 🔴 P06-T03'ün en önemli güvencesi. ÖLÇÜLDÜ: silinen dalın KENDİ reflog'u da
        // siliniyor; HEAD reflog'unda iz olması yalnızca o dalda BU çalışma ağacında
        // çalışılmışsa geçerli. Bağlı worktree'de üretilmiş bir dal silindiğinde hiçbir
        // reflog izi kalmıyor. Hash, kurtarmanın tek güvenilir yolu.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("switch", "-c", "birlesmemis");
        harness.Repository.WriteFile("yeni.txt", "iş\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("KAYBOLABILIR IS");
        string beklenen = harness.Repository.Git("rev-parse", "HEAD").Trim();
        harness.Repository.Git("switch", "-");

        BranchDeleteResult result = await harness.Writer.DeleteAsync(
            harness.Path, "birlesmemis", force: true, cancellationToken: Ct);

        result.LastCommitId.ShouldBe(beklenen);
        result.WasUnmerged.ShouldBeTrue();

        // Verilen hash gerçekten kurtarıyor mu?
        harness.Repository.Git("branch", "kurtarilan", result.LastCommitId);
        harness.Repository.Git("rev-parse", "kurtarilan").Trim().ShouldBe(beklenen);
    }

    [Fact]
    public async Task Uzerinde_olunan_dal_SILINMIYOR()
    {
        using Harness harness = await CreateAsync();
        string current = harness.CurrentBranch;

        await Should.ThrowAsync<GitException>(
            harness.Writer.DeleteAsync(harness.Path, current, force: true, cancellationToken: Ct));

        harness.Branches.ShouldContain($"refs/heads/{current}");
    }

    [Fact]
    public async Task Upstream_e_merge_edilmis_dal_YANLIS_ALARM_uretmiyor()
    {
        // 🔴 ÖLÇÜLDÜ: `-d`, dalı HEAD'e değil UPSTREAM'ine birleşmiş olsa da siliyor.
        // Birleşmişliği `merge-base --is-ancestor … HEAD` ile kendimiz hesaplasaydık
        // bu dal için "birleştirilmemiş" alarmı verirdik — oysa git sorunsuz siliyor.
        using TestRepository upstream = TestRepository.CreateEmpty();
        upstream.WriteFile("a.txt", "a\n");
        upstream.Git("add", "-A");
        upstream.Git("commit", "-m", "ilk");

        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", upstream.Path);
        harness.Repository.Git("push", "-q", "origin", "HEAD:refs/heads/ust");
        harness.Repository.Git("fetch", "-q", "origin");
        harness.Repository.Git("switch", "-c", "ust");
        harness.Repository.WriteFile("z.txt", "z\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("upstream'e gidecek");
        harness.Repository.Git("push", "-q", "origin", "ust");
        harness.Repository.Git("branch", "--set-upstream-to=origin/ust", "ust");
        harness.Repository.Git("switch", "-");

        // HEAD'e birleşmemiş, ama upstream'inde var → git siliyor, biz de sormuyoruz.
        BranchDeleteResult result = await harness.Writer.DeleteAsync(
            harness.Path, "ust", cancellationToken: Ct);

        result.WasUnmerged.ShouldBeFalse();
        harness.Branches.ShouldNotContain("refs/heads/ust");
    }
}
