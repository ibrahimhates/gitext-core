using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T06 — Commit oluşturma.
/// </summary>
/// <remarks>
/// Mesaj <b>stdin</b> ile geçiriliyor: argüman olarak vermek uzunluk sınırına takılır ve
/// kullanıcı metnini kabuk yorumlamasına açardı (ADR-0002).
/// </remarks>
public class CommitWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        CommitWriter Writer,
        CommitWriter Impatient,
        GitWriteQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        public string Message() => Repository.Git("log", "-1", "--format=%B");

        public string Subject() => Repository.Git("log", "-1", "--format=%s").Trim();

        public int CommitCount() =>
            int.Parse(Repository.Git("rev-list", "--count", "HEAD").Trim());
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(
            repository,
            new CommitWriter(new GitWriter(runner, queue), runner),

            // Aynı depo, kısa yazma sınırı: yavaş hook testi için (varsayılan 10 dakika).
            new CommitWriter(
                new GitWriter(runner, queue, writeTimeout: TimeSpan.FromSeconds(2)), runner),
            queue);
    }

    private static void Stage(Harness harness, string name, string content)
    {
        harness.Repository.WriteFile(name, content);
        harness.Repository.Git("add", "-A");
    }

    [Fact]
    public async Task Commit_olusturulur_ve_kimligi_donulur()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "ilk commit", cancellationToken: Ct);

        // Kimlik `git commit` çıktısından ayrıştırılmıyor (o insan-okunur); ayrı okunuyor.
        result.Id.Value.ShouldBe(harness.Repository.Git("rev-parse", "HEAD").Trim());
        harness.Subject().ShouldBe("ilk commit");
    }

    [Fact]
    public async Task Coksatirli_mesaj_govdesiyle_korunur()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "Konu satiri\n\nGovde birinci.\nGovde ikinci.", cancellationToken: Ct);

        // `%B` çıktısı mesajın sonuna kendi ayracını ekliyor; karşılaştırma kırpılarak
        // yapılıyor (mesajın kendisi doğru, fark okuma biçiminden geliyor).
        harness.Message().Trim().ShouldBe("Konu satiri\n\nGovde birinci.\nGovde ikinci.");
    }

    [Fact]
    public async Task Diyez_ile_baslayan_satirlar_SILINMEZ()
    {
        // 🔴 Klasik tuzak: git bazı modlarda `#` satırlarını yorum sayıp siler. O durumda
        // "#123 numaralı hatayı düzeltir" gibi bir satır sessizce kaybolurdu.
        // `--cleanup=whitespace` açıkça veriliyor (kullanıcının `commit.cleanup` ayarından
        // bağımsız olmak için).
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.Git("config", "commit.cleanup", "scissors");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "Konu\n\n#123 numarali issue\nGovde", cancellationToken: Ct);

        harness.Message().ShouldContain("#123 numarali issue");
    }

    [Fact]
    public async Task ASCII_disi_mesaj_bozulmaz()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "Türkçe konu: şğüıöç İĞÜŞÖÇ", cancellationToken: Ct);

        harness.Subject().ShouldBe("Türkçe konu: şğüıöç İĞÜŞÖÇ");
    }

    [Fact]
    public async Task Bos_mesaj_REDDEDILIR()
    {
        // ÖLÇÜLDÜ: git çıkış 1 veriyor ("Aborting commit due to empty commit message").
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await Should.ThrowAsync<GitException>(
            harness.Writer.CommitAsync(harness.Repository.Path, "", cancellationToken: Ct));
    }

    [Fact]
    public async Task Bos_mesaja_ACIKCA_izin_verilebilir()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path,
            "",
            new CommitOptions { AllowEmptyMessage = true },
            Ct);

        harness.CommitCount().ShouldBe(1);
    }

    [Fact]
    public async Task Amend_son_commiti_degistirir_yenisini_eklemez()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "ilk\n");
        await harness.Writer.CommitAsync(harness.Repository.Path, "ilk mesaj", cancellationToken: Ct);

        Stage(harness, "a.txt", "ikinci\n");
        await harness.Writer.CommitAsync(
            harness.Repository.Path, "duzeltilmis mesaj", new CommitOptions { Amend = true }, Ct);

        harness.CommitCount().ShouldBe(1);
        harness.Subject().ShouldBe("duzeltilmis mesaj");
    }

    [Fact]
    public async Task Signoff_satiri_eklenir()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", new CommitOptions { SignOff = true }, Ct);

        harness.Message().ShouldContain("Signed-off-by:");
    }

    [Fact]
    public async Task Yazar_degistirilebilir_committer_kendimiz_kalir()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path,
            "konu",
            new CommitOptions { Author = "Baska Kisi <baska@ornek.com>" },
            Ct);

        harness.Repository.Git("log", "-1", "--format=%an <%ae>").Trim()
            .ShouldBe("Baska Kisi <baska@ornek.com>");

        // Committer DEĞİŞMEMELİ: kimin commit ettiği ayrı bir gerçektir.
        harness.Repository.Git("log", "-1", "--format=%cn").Trim().ShouldNotBe("Baska Kisi");
    }

    [Fact]
    public async Task Degisiklik_yokken_commit_REDDEDILIR()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");
        await harness.Writer.CommitAsync(harness.Repository.Path, "ilk", cancellationToken: Ct);

        await Should.ThrowAsync<GitException>(
            harness.Writer.CommitAsync(harness.Repository.Path, "bos", cancellationToken: Ct));
    }

    [Fact]
    public async Task Bos_commite_ACIKCA_izin_verilebilir()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");
        await harness.Writer.CommitAsync(harness.Repository.Path, "ilk", cancellationToken: Ct);

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "bos commit", new CommitOptions { AllowEmpty = true }, Ct);

        harness.CommitCount().ShouldBe(2);
    }

    // ---- Hook'lar ----

    [Fact]
    public async Task Basarisiz_pre_commit_hooku_commiti_DURDURUR()
    {
        // ADR-0002'de hook desteği CLI seçiminin ana gerekçesiydi; burada karşılığı.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("pre-commit", "echo 'hook reddetti' >&2\nexit 1\n");

        GitException exception = await Should.ThrowAsync<GitException>(
            harness.Writer.CommitAsync(harness.Repository.Path, "konu", cancellationToken: Ct));

        // Hook'un çıktısı kullanıcıya ulaşabilmeli (P05-T07 bunu arayüze taşıyacak).
        exception.StandardError.ShouldContain("hook reddetti");
    }

    [Fact]
    public async Task Hooklar_ACIKCA_atlanabilir()
    {
        // `--no-verify` varsayılan KAPALI; açıkken arayüz görünür uyarı gösterecek (P05-T15).
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("pre-commit", "exit 1\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", new CommitOptions { SkipHooks = true }, Ct);

        harness.CommitCount().ShouldBe(1);
    }

    [Fact]
    public async Task Commit_msg_hookunun_mesaj_degisikligi_yansir()
    {
        // `commit-msg` hook'u mesaj dosyasını yerinde düzenleyebilir; sonuç commit'e girmeli.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("commit-msg", "echo '\\nEk-Satir: hook' >> \"$1\"\n");

        await harness.Writer.CommitAsync(harness.Repository.Path, "konu", cancellationToken: Ct);

        harness.Message().ShouldContain("Ek-Satir: hook");
    }

    // ---- P05-T07: hook çıktısının yakalanması ----

    [Fact]
    public async Task Basarili_committe_bile_hook_ciktisi_TASINIR()
    {
        // 🔴 Asıl boşluk buydu: commit başarılı olduğunda `git commit`'in çıktısı hiç
        // döndürülmüyordu; başarılı bir `pre-commit`'in uyarıları sessizce kayboluyordu.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("pre-commit", "echo 'UYARI: iki TODO satiri var'\nexit 0\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        result.HasOutput.ShouldBeTrue();
        result.Output.ShouldContain("UYARI: iki TODO satiri var");
        harness.CommitCount().ShouldBe(1);
    }

    [Fact]
    public async Task Hookun_STDOUTU_da_yakalanir()
    {
        // ÖLÇÜLDÜ: git hook'un stdout'unu stderr'e yönlendiriyor (stdout_to_stderr).
        // Yalnızca stderr'e bakmak bu yüzden YETERLİ — ama bu bir varsayım değil, ölçüm;
        // burada sabitleniyor. Bir gün değişirse `echo` ile yazan hook'lar sessizce kaybolur.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook(
            "pre-commit",
            "echo 'SADECE-STDOUT'\necho 'SADECE-STDERR' >&2\nexit 1\n");

        GitException exception = await Should.ThrowAsync<GitException>(
            harness.Writer.CommitAsync(harness.Repository.Path, "konu", cancellationToken: Ct));

        exception.StandardError.ShouldContain("SADECE-STDOUT");
        exception.StandardError.ShouldContain("SADECE-STDERR");
    }

    [Fact]
    public async Task Hooksuz_basarili_committe_cikti_YOKTUR()
    {
        // Karşı kanıt: çıktı her zaman doluysa "hook konuştu" göstergesi anlamsız olurdu.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        result.HasOutput.ShouldBeFalse();
    }

    [Fact]
    public async Task Mesaji_degistiren_hook_sonucta_BILDIRILIR()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("commit-msg", "echo 'Change-Id: I0001' >> \"$1\"\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        result.MessageChanged.ShouldBeTrue();
        result.Message.ShouldContain("Change-Id: I0001");
        result.RequestedMessage.ShouldBe("konu");
    }

    [Fact]
    public async Task Prepare_commit_msg_hooku_da_mesaji_degistirebilir()
    {
        // ÖLÇÜLDÜ: `-F -` ile mesaj verildiğinde bile `prepare-commit-msg` çalışıyor
        // (source=message) ve dosyayı düzenleyebiliyor.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("prepare-commit-msg", "echo 'Hazirlik-Satiri' >> \"$1\"\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        result.MessageChanged.ShouldBeTrue();
        result.Message.ShouldContain("Hazirlik-Satiri");
    }

    [Fact]
    public async Task No_verify_prepare_commit_msgi_ATLAMAZ()
    {
        // 🔴 ÖLÇÜLDÜ: `--no-verify` yalnızca `pre-commit` ve `commit-msg`'i atlıyor.
        // "Hook'ları atla" diye anlaşılırsa, mesajın yine değişebildiği gözden kaçar.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("prepare-commit-msg", "echo 'Hazirlik-Satiri' >> \"$1\"\n");
        harness.Repository.InstallHook("pre-commit", "exit 1\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", new CommitOptions { SkipHooks = true }, Ct);

        result.Message.ShouldContain("Hazirlik-Satiri");
        result.MessageChanged.ShouldBeTrue();
    }

    [Fact]
    public async Task Hook_mesaja_dokunmazsa_degisiklik_BILDIRILMEZ()
    {
        // Karşı kanıt: `--cleanup=whitespace`'in kendi normalleştirmesi "değişiklik"
        // sayılmamalı, yoksa gösterge her commit'te yanıp anlamını yitirir.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "Konu satiri   \n\nGovde.\n\n\n", cancellationToken: Ct);

        result.MessageChanged.ShouldBeFalse();
    }

    [Fact]
    public async Task Post_commit_hooku_commiti_BOZMAZ_ama_ciktisi_gorunur()
    {
        // ÖLÇÜLDÜ: `post-commit` çıkış 9 verse bile git 0 dönüyor — commit zaten oluşmuş.
        // Kullanıcı yine de hook'un ne dediğini görebilmeli.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("post-commit", "echo 'POST: bildirim gonderilemedi' >&2\nexit 9\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        harness.CommitCount().ShouldBe(1);
        result.Output.ShouldContain("POST: bildirim gonderilemedi");
    }

    [Fact]
    public async Task Yavas_hook_zaman_asimina_takilirsa_commit_OLUSMAZ()
    {
        // ÖLÇÜLDÜ: süreç öldürüldüğünde commit oluşmuyor ve geride `index.lock` kalmıyor
        // (git kilidi hook'tan SONRA alıyor). Yani zaman aşımı veri kaybettirmiyor.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("pre-commit", "sleep 30\n");

        GitException exception = await Should.ThrowAsync<GitException>(
            harness.Impatient.CommitAsync(harness.Repository.Path, "konu", cancellationToken: Ct));

        exception.Kind.ShouldBe(GitFailureKind.Timeout);

        // `--all`: henüz HEAD yok, `rev-list --count HEAD` bu durumda çöker.
        harness.Repository.Git("rev-list", "--count", "--all").Trim().ShouldBe("0");
        File.Exists(Path.Combine(harness.Repository.Path, ".git", "index.lock")).ShouldBeFalse();
    }
}
