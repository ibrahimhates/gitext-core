using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T13 — commit mesajı yardımcıları: geçmiş, şablon, <c>HEAD</c> mesajı, taslak.
/// </summary>
/// <remarks>
/// Testlerin ağırlığı <b>ölçümle bulunan üç tuzakta</b>: <c>~</c> yalnızca <c>--path</c> ile
/// genişliyor, yorum karakteri <c>#</c> olmak zorunda değil, ve git'in hazırladığı mesaj
/// dosyası ham baytlarla yazılıyor.
/// </remarks>
public class CommitMessageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        CommitMessageReader Reader,
        CommitMessageStore Store,
        GitConfigReader Config) : IDisposable
    {
        public string Path => Repository.Path;

        public void Dispose() => Repository.Dispose();
    }

    private static async Task<Harness> CreateAsync(bool withCommit = true)
    {
        TestRepository repository = withCommit
            ? TestRepository.CreateWithSingleCommit()
            : TestRepository.CreateEmpty();

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitConfigReader config = new(runner);

        return new Harness(
            repository,
            new CommitMessageReader(runner, config),
            new CommitMessageStore(runner, config),
            config);
    }

    // ---- Yapılandırma okuma ----

    [Fact]
    public async Task Ayarsiz_anahtar_null_doner_hata_DEGIL()
    {
        // Yapılandırılmamış her depo istisna atsaydı commit ekranı hiç açılmazdı.
        using Harness harness = await CreateAsync();

        (await harness.Config.GetAsync(harness.Path, "commit.template", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Tilde_YALNIZCA_path_ile_genisliyor()
    {
        // 🔴 ÖLÇÜLDÜ: düz `--get` `~/…` değerini ham döndürüyor. Ham değerle File.Exists
        // çağırmak, `~` ile başlayan her şablonu sessizce "bulunamadı" yapardı.
        // (TestRepository HOME'u deponun köküne ayarlıyor.)
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "--local", "commit.template", "~/sablon.txt");

        string? raw = await harness.Config.GetAsync(harness.Path, "commit.template", Ct);
        string? expanded = await harness.Config.GetPathAsync(harness.Path, "commit.template", Ct);

        raw.ShouldBe("~/sablon.txt");
        expanded.ShouldNotBeNull().ShouldNotStartWith("~");
        expanded.ShouldEndWith("sablon.txt");
    }

    [Fact]
    public async Task Ayni_anahtarin_SON_degeri_kazanir()
    {
        // git'in kendi kuralı "son yazan kazanır"; ilk satırı almak sessizce yanlış olurdu.
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "--local", "--add", "commit.template", "birinci.txt");
        harness.Repository.Git("config", "--local", "--add", "commit.template", "ikinci.txt");

        (await harness.Config.GetAsync(harness.Path, "commit.template", Ct)).ShouldBe("ikinci.txt");
    }

    // ---- Mesaj geçmişi ----

    [Fact]
    public async Task Son_mesajlar_YENIDEN_ESKIYE_okunur()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Commit("ikinci konu\n\nikinci gövde");
        harness.Repository.Commit("üçüncü konu");

        IReadOnlyList<string> messages = await harness.Reader.ReadRecentAsync(harness.Path, 10, false, Ct);

        messages[0].ShouldBe("üçüncü konu");
        messages[1].ShouldBe("ikinci konu\n\nikinci gövde");
        messages[2].ShouldBe("ilk commit");
    }

    [Fact]
    public async Task Cok_satirli_mesajlar_birbirine_KARISMAZ()
    {
        // `-z` olmadan kayıt ayracı satır sonu olurdu ve çok satırlı bir mesajın nerede
        // bittiği belirlenemezdi (ölçüldü).
        using Harness harness = await CreateAsync();

        harness.Repository.Commit("konu\n\nsatir bir\nsatir iki\nsatir üç");
        harness.Repository.Commit("sonraki");

        IReadOnlyList<string> messages = await harness.Reader.ReadRecentAsync(harness.Path, 2, false, Ct);

        messages.Count.ShouldBe(2);
        messages[1].ShouldBe("konu\n\nsatir bir\nsatir iki\nsatir üç");
    }

    [Fact]
    public async Task Bos_mesajli_commit_listeyi_KAYDIRMAZ()
    {
        // Boş mesajlı commit gerçek (P02-T04): `--allow-empty-message` ile oluşuyor ve
        // rebase/import araçları üretiyor. `-z` çıktısında boş bir alan olarak geliyor.
        using Harness harness = await CreateAsync();

        harness.Repository.Git(
            "commit", "--allow-empty", "--allow-empty-message", "-m", string.Empty);
        harness.Repository.Commit("bostan sonraki");

        IReadOnlyList<string> messages = await harness.Reader.ReadRecentAsync(harness.Path, 5, false, Ct);

        messages.ShouldBe(["bostan sonraki", "ilk commit"]);
    }

    [Fact]
    public async Task Yalnizca_kendi_commitlerim_filtrelenir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Git(
            "commit", "--allow-empty", "--author=Baskasi <baska@example.invalid>",
            "-m", "baskasinin mesaji");
        harness.Repository.Commit("benim mesajim");

        IReadOnlyList<string> mine =
            await harness.Reader.ReadRecentAsync(harness.Path, 10, onlyCurrentUser: true, Ct);

        IReadOnlyList<string> all =
            await harness.Reader.ReadRecentAsync(harness.Path, 10, onlyCurrentUser: false, Ct);

        mine.ShouldNotContain("baskasinin mesaji");
        mine.ShouldContain("benim mesajim");
        all.ShouldContain("baskasinin mesaji");
    }

    [Fact]
    public async Task Yazar_deseni_ALT_DIZE_eslesmesi_yapmaz()
    {
        // ÖLÇÜLDÜ: `--author` düzenli ifade olarak eşleşiyor. Çapasız bir desen, adı başka
        // bir adın içinde geçen herkesin commit'ini "benim" sayardı.
        using Harness harness = await CreateAsync();

        harness.Repository.Git(
            "commit", "--allow-empty",
            "--author=gitext-core tests uzatilmis <tests@gitext-core.invalid>",
            "-m", "benzeyen ad");
        harness.Repository.Commit("gercek benim");

        IReadOnlyList<string> mine =
            await harness.Reader.ReadRecentAsync(harness.Path, 10, onlyCurrentUser: true, Ct);

        mine.ShouldContain("gercek benim");
        mine.ShouldNotContain("benzeyen ad");
    }

    [Fact]
    public async Task Commitsiz_depoda_gecmis_BOS_liste()
    {
        // `git log` burada çıkış 128 veriyor; ilk commit'ini atan kullanıcıya istisna
        // göstermek olurdu.
        using Harness harness = await CreateAsync(withCommit: false);

        (await harness.Reader.ReadRecentAsync(harness.Path, 5, false, Ct)).ShouldBeEmpty();
        (await harness.Reader.ReadHeadMessageAsync(harness.Path, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task HEAD_mesaji_amend_icin_okunur()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Commit("düzeltilecek konu\n\ngövde");

        (await harness.Reader.ReadHeadMessageAsync(harness.Path, Ct))
            .ShouldBe("düzeltilecek konu\n\ngövde");
    }

    // ---- Şablon ----

    [Fact]
    public async Task Sablon_ayarsizsa_null()
    {
        using Harness harness = await CreateAsync();

        (await harness.Reader.ReadTemplateAsync(harness.Path, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Sablon_okunur()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("sablon.txt", "konu\n\n# yardim satiri\ngövde\n");
        harness.Repository.Git("config", "--local", "commit.template", "sablon.txt");

        CommitTemplate template = (await harness.Reader.ReadTemplateAsync(harness.Path, Ct)).ShouldNotBeNull();

        template.IsMissing.ShouldBeFalse();
        template.Text.ShouldNotBeNull().ShouldContain("gövde");
    }

    [Fact]
    public async Task Goreli_sablon_yolu_KOKE_gore_cozulur()
    {
        // 🔴 ÖLÇÜLDÜ: git göreli yolu çalışma ağacının köküne göre çözüyor, komutun
        // çalıştığı dizine göre değil — alt dizinde aynı adlı dosya varken bile kökteki
        // okundu. Aksi hâlde kullanıcıya terminalde gördüğünden başka bir şablon gösterirdik.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("sablon.txt", "KOK SABLONU\n");
        harness.Repository.WriteFile("alt/sablon.txt", "ALT SABLONU\n");
        harness.Repository.Git("config", "--local", "commit.template", "sablon.txt");

        string subdirectory = Path.Combine(harness.Path, "alt");

        CommitTemplate template =
            (await harness.Reader.ReadTemplateAsync(subdirectory, Ct)).ShouldNotBeNull();

        template.Text.ShouldNotBeNull().ShouldContain("KOK SABLONU");
    }

    [Fact]
    public async Task Var_olmayan_sablon_SESSIZCE_bos_gecmez()
    {
        // git'in kendisi bu durumda `fatal: could not read` ile çıkış 128 veriyor, yani
        // kullanıcının terminaldeki commit'i de çalışmıyor. "Şablon boş" göstermek bozuk
        // yapılandırmayı gizlemek olurdu.
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "--local", "commit.template", "yok-boyle-dosya.txt");

        CommitTemplate template = (await harness.Reader.ReadTemplateAsync(harness.Path, Ct)).ShouldNotBeNull();

        template.IsMissing.ShouldBeTrue();
        template.Path.ShouldContain("yok-boyle-dosya.txt");
    }

    // ---- Yorum karakteri ----

    [Fact]
    public async Task Yorum_karakteri_ayardan_okunur()
    {
        // 🔴 ÖLÇÜLDÜ: `core.commentChar=';'` olan depoda git `;` satırlarını siliyor ve
        // `#` satırlarını KORUYOR. Kör bir `#` filtresi burada hem gerçek yorumları bırakır
        // hem kullanıcının issue satırını silerdi.
        using Harness harness = await CreateAsync();

        (await harness.Reader.ReadCommentCharacterAsync(harness.Path, Ct)).ShouldBe("#");

        harness.Repository.Git("config", "--local", "core.commentChar", ";");

        (await harness.Reader.ReadCommentCharacterAsync(harness.Path, Ct)).ShouldBe(";");
    }

    [Fact]
    public void Yorum_temizleme_yalnizca_SATIR_BASINDAKI_oneki_alir()
    {
        // ÖLÇÜLDÜ: git de girintili yorum satırını silmiyor.
        string text = "konu\n\n# yorum\n  # girintili\nson";

        CommitMessageText.RemoveComments(text).ShouldBe("konu\n\n  # girintili\nson");
    }

    [Fact]
    public void Auto_yorum_karakterinde_VARSAYILANA_donulur()
    {
        // `auto` git'in mesaja göre seçtiği karakter demek; sabit bir cevabı yok.
        // Yanlış tahminle kullanıcının satırını silmektense yorumu bırakmak yeğdir.
        CommitMessageText.ResolveCommentCharacter("auto").ShouldBe("#");
        CommitMessageText.ResolveCommentCharacter(null).ShouldBe("#");
        CommitMessageText.ResolveCommentCharacter("//").ShouldBe("//");
    }

    // ---- Taslak ----

    [Fact]
    public async Task Taslak_yazilir_ve_geri_okunur()
    {
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "yarim kalan mesaj", Ct);

        PendingCommitMessage pending = await harness.Store.ReadAsync(harness.Path, Ct);

        pending.Text.ShouldBe("yarim kalan mesaj");
        pending.Source.ShouldBe(CommitMessageSource.Draft);
    }

    [Fact]
    public async Task Taslak_GIT_DIZININE_yazilir_ve_calisma_agacini_kirletmez()
    {
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "taslak", Ct);

        File.Exists(Path.Combine(harness.Path, ".git", CommitMessageStore.DraftFileName))
            .ShouldBeTrue();

        // `.git` altındaki yabancı dosya git'i rahatsız etmiyor (ölçüldü) — ama testin
        // doğrulaması gereken şey çalışma ağacının temiz kalması.
        harness.Repository.Git("status", "--porcelain").ShouldBeEmpty();
    }

    [Fact]
    public async Task Bos_taslak_dosyayi_SILER()
    {
        // Yarım mesajı silen kullanıcı, ekranı bir daha açtığında onu geri görmemeli.
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "bir sey", Ct);
        await harness.Store.SaveDraftAsync(harness.Path, "   \n  ", Ct);

        File.Exists(Path.Combine(harness.Path, ".git", CommitMessageStore.DraftFileName))
            .ShouldBeFalse();

        (await harness.Store.ReadAsync(harness.Path, Ct)).Source.ShouldBe(CommitMessageSource.None);
    }

    [Fact]
    public async Task Taslak_worktree_BASINA_ayri()
    {
        // MERGE_MSG ve index worktree başına ayrı (P02-T06). Taslağı ortak dizine koymak,
        // iki worktree'de çalışan kullanıcının mesajlarını birbirine karıştırırdı.
        using Harness harness = await CreateAsync();
        using TestRepository worktree = harness.Repository.AddWorkTree("yan-dal");

        await harness.Store.SaveDraftAsync(harness.Path, "ana mesaj", Ct);
        await harness.Store.SaveDraftAsync(worktree.Path, "worktree mesaji", Ct);

        (await harness.Store.ReadAsync(harness.Path, Ct)).Text.ShouldBe("ana mesaj");
        (await harness.Store.ReadAsync(worktree.Path, Ct)).Text.ShouldBe("worktree mesaji");
    }

    [Fact]
    public async Task Merge_mesaji_taslaktan_ONCE_gelir()
    {
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "eski taslak", Ct);

        CreateConflictingMerge(harness.Repository);

        PendingCommitMessage pending = await harness.Store.ReadAsync(harness.Path, Ct);

        pending.Source.ShouldBe(CommitMessageSource.Pending);
        pending.Text.ShouldStartWith("Merge branch");
    }

    [Fact]
    public async Task Merge_mesajindaki_YORUMLAR_temizlenir()
    {
        // 🔴 Fazın en sessiz hatası burada olurdu: git'in editör yolu `# Conflicts:`
        // satırlarını commit'e sokmuyor, bizim `--cleanup=whitespace` yolumuz sokardı.
        // Kutuda görünen = commit'lenen.
        using Harness harness = await CreateAsync();

        CreateConflictingMerge(harness.Repository);

        string raw = File.ReadAllText(Path.Combine(harness.Path, ".git", "MERGE_MSG"));
        raw.ShouldContain("# Conflicts:");

        PendingCommitMessage pending = await harness.Store.ReadAsync(harness.Path, Ct);

        pending.Text.ShouldNotContain("#");
        pending.Text.ShouldBe("Merge branch 'yan'");
    }

    [Fact]
    public async Task Merge_mesaji_DOSYANIN_KODLAMASIYLA_okunur()
    {
        // 🔴 ÖLÇÜLDÜ: git bu dosyayı ham baytlarla yazıyor — `i18n.commitEncoding` Latin-5
        // olan bir depoda cherry-pick edilen mesaj Latin-5 baytlarıyla düşüyor. UTF-8
        // varsayılsaydı Türkçe mesaj değiştirme karakterine dönerdi (P04-T07'nin aynısı).
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "--local", "i18n.commitEncoding", "ISO-8859-9");

        string gitDirectory = Path.Combine(harness.Path, ".git");
        File.WriteAllBytes(
            Path.Combine(gitDirectory, "MERGE_MSG"),
            Encoding.GetEncoding("ISO-8859-9").GetBytes("Türkçe merge mesajı\n"));

        PendingCommitMessage pending = await harness.Store.ReadAsync(harness.Path, Ct);

        pending.Text.ShouldBe("Türkçe merge mesajı");
    }

    [Fact]
    public async Task Taslak_temizlenince_geri_gelmez()
    {
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "commit edilecek", Ct);
        await harness.Store.ClearDraftAsync(harness.Path, Ct);

        (await harness.Store.ReadAsync(harness.Path, Ct)).Source.ShouldBe(CommitMessageSource.None);
    }

    /// <summary>Çakışmalı bir merge başlatır; <c>MERGE_MSG</c> geride kalır.</summary>
    private static void CreateConflictingMerge(TestRepository repository)
    {
        repository.WriteFile("çakışan.txt", "taban\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "taban");

        repository.Git("checkout", "-b", "yan");
        repository.WriteFile("çakışan.txt", "yan dal\n");
        repository.Git("commit", "-am", "yan dal degisikligi");

        repository.Git("checkout", "main");
        repository.WriteFile("çakışan.txt", "ana dal\n");
        repository.Git("commit", "-am", "ana dal degisikligi");

        // Çakışma bekleniyor: `Git` başarısızlıkta fırlatıyor, bu yüzden TryGit.
        repository.TryGit("merge", "yan");
    }
}
