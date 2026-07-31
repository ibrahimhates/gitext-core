using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T13 — commit mesajı yardımcıları: geçmiş, şablon, amend mesajı, taslak koruma.
/// </summary>
/// <remarks>
/// 🔑 Bu testlerin çoğu <b>tek bir değişmezi</b> koruyor: <i>kullanıcının yazdığı metnin
/// üzerine hiçbir kaynak yazmaz.</i> Taslağın, şablonun ya da <c>MERGE_MSG</c>'in yarım
/// kalmış bir mesajı ezmesi, bu fazın en sinsi veri kaybı olurdu — hata mesajı yok, geri
/// alma yok.
/// </remarks>
public class CommitMessageHelperTests
{
    private const string Repository = "/tmp/depo";

    private static FileStatus Staged(string path) =>
        new() { Path = RepositoryPath.Parse(path), StagedChange = FileChangeKind.Modified };

    private sealed record Harness(
        WorkingTreeViewModel Model,
        FakeCommitMessageReader Messages,
        FakeCommitMessageStore Store,
        FakeCommitWriter Commits)
    {
        public CommitMessageViewModel Message => Model.Message;
    }

    private static async Task<Harness> CreateAsync(
        FakeCommitMessageReader? reader = null,
        FakeCommitMessageStore? store = null,
        params FileStatus[] entries)
    {
        reader ??= new FakeCommitMessageReader();
        store ??= new FakeCommitMessageStore();

        FakeStatusReader status = new(entries);
        FakeCommitWriter commits = new(status);

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            commits,
            new DiffViewModel(new FakeDiffReader()),
            reader,
            store);

        // Testler taslağın gecikmesini beklemesin; gecikme davranışı ayrıca test ediliyor.
        model.Message.DraftSaveDelay = TimeSpan.Zero;

        await model.OpenAsync(Repository);

        return new Harness(model, reader, store, commits);
    }

    // ---- Geçmiş ----

    [AvaloniaFact]
    public async Task Gecmis_depo_acilirken_OKUNMAZ()
    {
        // Menü açılırken okunuyor: her depo açılışında fazladan bir `git log` çalıştırmak,
        // kullanıcının hiç açmayacağı bir menünün bedeli olurdu.
        FakeCommitMessageReader reader = new();
        reader.Recent.Add("eski mesaj");

        Harness harness = await CreateAsync(reader);

        harness.Messages.RecentReadCount.ShouldBe(0);

        await harness.Message.LoadRecentAsync();

        harness.Messages.RecentReadCount.ShouldBe(1);
        harness.Message.RecentMessages.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Gecmis_etiketi_ILK_SATIR_ve_kisaltilmis()
    {
        // Menü öğesi tek satır olmak zorunda; çok satırlı bir mesaj menüyü ekran dışına
        // taşırırdı (GitExtensions'ta da sınır 72).
        FakeCommitMessageReader reader = new();
        reader.Recent.Add("konu satiri\n\ngövde burada ve menüde GÖRÜNMEMELİ");
        reader.Recent.Add(new string('u', 200));

        Harness harness = await CreateAsync(reader);
        await harness.Message.LoadRecentAsync();

        CommitMessageHistoryItem first = harness.Message.RecentMessages[0];
        first.Label.ShouldBe("konu satiri");
        first.Message.ShouldContain("gövde burada");

        harness.Message.RecentMessages[1].Label.Length
            .ShouldBe(CommitMessageHistoryItem.LabelLimit);
    }

    [AvaloniaFact]
    public async Task Gecmisten_secilen_mesaj_kutuya_KONUR()
    {
        // Var olan metnin üzerine yazılan TEK yer burası: kullanıcı menüden seçerek tam
        // olarak bunu istedi (GitExtensions'ın `ReplaceMessage`'ı da böyle).
        FakeCommitMessageReader reader = new();
        reader.Recent.Add("geri çağrılan mesaj\n\ngövdesiyle birlikte");

        Harness harness = await CreateAsync(reader);
        await harness.Message.LoadRecentAsync();

        harness.Message.Text = "yazmakta olduğum";

        CommitMessageHistoryItem item = harness.Message.RecentMessages[0];
        item.ApplyCommand.Execute(item);

        harness.Message.Text.ShouldBe("geri çağrılan mesaj\n\ngövdesiyle birlikte");
    }

    [AvaloniaFact]
    public async Task Yalnizca_benim_mesajlarim_filtresi_cekirdege_GECER()
    {
        FakeCommitMessageReader reader = new();
        reader.Recent.AddRange(["baskasinin", "benim"]);
        reader.Mine.Add("benim");

        Harness harness = await CreateAsync(reader);

        harness.Message.OnlyMyMessages = true;
        await harness.Message.LoadRecentAsync();

        harness.Messages.LastOnlyCurrentUser.ShouldBeTrue();
        harness.Message.RecentMessages.Select(item => item.Message).ShouldBe(["benim"]);
    }

    [AvaloniaFact]
    public async Task Gecmis_okunamazsa_ekran_CALISMAYA_devam_eder()
    {
        FakeCommitMessageReader reader = new()
        {
            Failure = new GitExt.Core.Git.GitException(
                GitExt.Core.Git.GitFailureKind.Unknown,
                "git log başarısız.",
                "git log",
                exitCode: 128,
                standardError: "fatal"),
        };

        Harness harness = await CreateAsync(reader, null, Staged("a.txt"));

        await harness.Message.LoadRecentAsync();

        harness.Message.RecentMessages.ShouldBeEmpty();

        harness.Message.Text = "yine de commit atabilmeliyim";
        harness.Model.CanCommit.ShouldBeTrue();
    }

    // ---- Şablon ----

    [AvaloniaFact]
    public async Task Sablon_YORUMLARI_TEMIZLENEREK_yuklenir()
    {
        // 🔴 Bizim commit yolumuz `--cleanup=whitespace` ve yorumları KORUYOR (P05-T06,
        // bilinçli). Şablon olduğu gibi yüklenseydi git'in editör yolunda commit'e
        // girmeyen `# …` satırları bizde commit gövdesine girerdi.
        FakeCommitMessageReader reader = new()
        {
            Template = new CommitTemplate
            {
                Path = "/tmp/depo/sablon.txt",
                Text = "\n# Konuyu buraya yaz\n# 50 karakteri geçme\nkonu taslağı\n",
            },
        };

        Harness harness = await CreateAsync(reader);

        harness.Message.CanApplyTemplate.ShouldBeTrue();

        await harness.Message.ApplyTemplateAsync();

        harness.Message.Text.ShouldBe("konu taslağı");
    }

    [AvaloniaFact]
    public async Task Sablonun_yorum_karakteri_DEPODAN_okunur()
    {
        // 🔴 ÖLÇÜLDÜ: `core.commentChar` `#` olmak zorunda değil. `;` olan bir depoda kör
        // bir `#` filtresi hem gerçek yorumları bırakır hem kullanıcının issue satırını
        // silerdi.
        FakeCommitMessageReader reader = new()
        {
            CommentCharacter = ";",
            Template = new CommitTemplate
            {
                Path = "/tmp/depo/sablon.txt",
                Text = "; bu yorum\nRefs #123\n",
            },
        };

        Harness harness = await CreateAsync(reader);
        await harness.Message.ApplyTemplateAsync();

        harness.Message.Text.ShouldBe("Refs #123");
    }

    [AvaloniaFact]
    public async Task Bulunamayan_sablon_menude_GORUNUR_ama_uygulanamaz()
    {
        // git'in kendisi bu durumda commit'i `fatal: could not read` ile reddediyor
        // (ölçüldü) — yapılandırma gerçekten bozuk. Menüyü boş göstermek onu saklamak olurdu.
        FakeCommitMessageReader reader = new()
        {
            Template = new CommitTemplate { Path = "/tmp/depo/yok.txt", Text = null },
        };

        Harness harness = await CreateAsync(reader);

        harness.Message.HasTemplate.ShouldBeTrue();
        harness.Message.CanApplyTemplate.ShouldBeFalse();
        harness.Message.TemplateLabel.ShouldContain("yok.txt");

        await harness.Message.ApplyTemplateAsync();

        harness.Message.Text.ShouldBeEmpty();
    }

    // ---- Amend ----

    [AvaloniaFact]
    public async Task Amend_isaretlenince_HEAD_mesaji_yuklenir()
    {
        FakeCommitMessageReader reader = new() { HeadMessage = "düzeltilecek konu\n\ngövde" };

        Harness harness = await CreateAsync(reader);

        harness.Model.Amend = true;

        harness.Message.Text.ShouldBe("düzeltilecek konu\n\ngövde");
    }

    [AvaloniaFact]
    public async Task Amend_DOLU_kutunun_uzerine_YAZMAZ()
    {
        // Kullanıcı yeni bir mesaj yazmaya başladıysa amend'i işaretlemek onu silmemeli.
        FakeCommitMessageReader reader = new() { HeadMessage = "eski commit mesajı" };

        Harness harness = await CreateAsync(reader);

        harness.Message.Text = "yazmakta olduğum yeni mesaj";
        harness.Model.Amend = true;

        harness.Message.Text.ShouldBe("yazmakta olduğum yeni mesaj");
    }

    [AvaloniaFact]
    public async Task Commitsiz_depoda_amend_kutuyu_BOZMAZ()
    {
        // Çekirdek burada null döndürüyor (gerçek git'te `git log` çıkış 128).
        Harness harness = await CreateAsync(new FakeCommitMessageReader { HeadMessage = null });

        harness.Model.Amend = true;

        harness.Message.Text.ShouldBeEmpty();
    }

    // ---- Taslak ----

    [AvaloniaFact]
    public async Task Yazilan_mesaj_TASLAGA_kaydedilir()
    {
        FakeCommitMessageStore store = new();

        Harness harness = await CreateAsync(null, store);

        harness.Message.Text = "yarım kalan iş";

        // Gecikme sıfır; kaydın kuyruğa alınan devamı çalışsın.
        await Task.Delay(20);

        store.Drafts[Repository].ShouldBe("yarım kalan iş");
    }

    [AvaloniaFact]
    public async Task Taslak_ekran_yeniden_acilinca_GERI_GELIR()
    {
        FakeCommitMessageStore store = new();
        store.Drafts[Repository] = "önceki oturumdan kalan";

        Harness harness = await CreateAsync(null, store);

        harness.Message.Text.ShouldBe("önceki oturumdan kalan");
        harness.Message.Source.ShouldBe(CommitMessageSource.Draft);
    }

    [AvaloniaFact]
    public async Task Taslak_DOLU_kutunun_uzerine_YAZMAZ()
    {
        FakeCommitMessageStore store = new();
        store.Drafts[Repository] = "diskteki eski taslak";

        Harness harness = await CreateAsync(null, store);

        harness.Message.Text = "kullanıcının yazdığı";

        // Aynı ekranda depo yeniden açılırsa (yenileme/depo değişimi) yazılan metin durmalı.
        await harness.Model.OpenAsync(Repository);

        harness.Message.Text.ShouldBe("kullanıcının yazdığı");
    }

    [AvaloniaFact]
    public async Task Basarili_commit_TASLAGI_da_siler()
    {
        // 🔑 Taslak silinmezse ekran bir daha açıldığında az önce commit'lenen metin geri
        // gelir ve kullanıcıyı ikinci bir commit'e davet eder.
        FakeCommitMessageStore store = new();

        Harness harness = await CreateAsync(null, store, Staged("a.txt"));

        harness.Message.Text = "commit edilecek";
        await Task.Delay(20);

        store.Drafts.ShouldContainKey(Repository);

        await harness.Model.CommitAsync();

        store.Drafts.ShouldNotContainKey(Repository);
        harness.Message.Text.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Basarisiz_commit_TASLAGI_KORUR()
    {
        // P05-T12'de mesajın korunduğu test edilmişti; taslağın da korunması gerekiyor,
        // yoksa hata anında uygulama kapanınca metin yine kaybolurdu.
        FakeCommitMessageStore store = new();

        Harness harness = await CreateAsync(null, store, Staged("a.txt"));

        harness.Commits.Failure = new GitExt.Core.Git.GitException(
            GitExt.Core.Git.GitFailureKind.Unknown,
            "Git komutu başarısız oldu.",
            "git commit -F -",
            exitCode: 1,
            standardError: "pre-commit: reddedildi");

        harness.Message.Text = "kaybolmamalı";
        await Task.Delay(20);

        await harness.Model.CommitAsync();

        harness.Message.Text.ShouldBe("kaybolmamalı");
        store.Drafts[Repository].ShouldBe("kaybolmamalı");
    }

    [AvaloniaFact]
    public async Task Kutu_bosaltilinca_taslak_SILINIR()
    {
        FakeCommitMessageStore store = new();

        Harness harness = await CreateAsync(null, store);

        harness.Message.Text = "bir şey";
        await Task.Delay(20);

        harness.Message.Text = "   ";
        await Task.Delay(20);

        store.Drafts.ShouldNotContainKey(Repository);
    }

    [AvaloniaFact]
    public async Task Kapanirken_bekleyen_taslak_HEMEN_yazilir()
    {
        // 🔑 Gecikmeli kayıt henüz çalışmamış olabilir; pencere kapanırken kullanıcının en
        // son yazdığı satır — bıraktığı yer — kaybolurdu.
        FakeCommitMessageStore store = new();

        Harness harness = await CreateAsync(null, store);

        harness.Message.DraftSaveDelay = TimeSpan.FromMinutes(5);
        harness.Message.Text = "gecikmeli kayıt henüz çalışmadı";

        store.Drafts.ShouldNotContainKey(Repository);

        await harness.Message.FlushDraftAsync();

        store.Drafts[Repository].ShouldBe("gecikmeli kayıt henüz çalışmadı");
    }

    [AvaloniaFact]
    public async Task Yuklenen_mesaj_taslak_kaydini_TETIKLEMEZ()
    {
        // Taslaktan yüklenen metni hemen geri yazmak boşuna disk yazması olurdu.
        FakeCommitMessageStore store = new();
        store.Drafts[Repository] = "diskten gelen";

        Harness harness = await CreateAsync(null, store);

        await Task.Delay(20);

        harness.Message.Text.ShouldBe("diskten gelen");
        store.SaveCount.ShouldBe(0);
    }

    // ---- git'in hazırladığı mesaj ----

    [AvaloniaFact]
    public async Task Merge_mesaji_ekran_acilinca_YUKLENIR()
    {
        FakeCommitMessageStore store = new() { PendingMessage = "Merge branch 'yan'" };

        Harness harness = await CreateAsync(null, store);

        harness.Message.Text.ShouldBe("Merge branch 'yan'");
        harness.Message.Source.ShouldBe(CommitMessageSource.Pending);
    }

    [AvaloniaFact]
    public async Task Merge_mesaji_taslaktan_ONCE_gelir()
    {
        FakeCommitMessageStore store = new() { PendingMessage = "Merge branch 'yan'" };
        store.Drafts[Repository] = "eski taslak";

        Harness harness = await CreateAsync(null, store);

        harness.Message.Text.ShouldBe("Merge branch 'yan'");
    }
}
