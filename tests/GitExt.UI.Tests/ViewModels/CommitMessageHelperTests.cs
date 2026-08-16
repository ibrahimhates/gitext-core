using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T13 — the commit message helpers: history, template, the amend message, draft preservation.
/// </summary>
/// <remarks>
/// 🔑 Most of these tests protect <b>a single invariant</b>: <i>no source overwrites the text the
/// user wrote.</i> A draft, a template or <c>MERGE_MSG</c> wiping out a half-finished message would
/// be this phase's most insidious data loss — no error message, no undo.
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

        // The tests must not wait on the draft's delay; the delay behaviour is tested separately.
        model.Message.DraftSaveDelay = TimeSpan.Zero;

        await model.OpenAsync(Repository);

        return new Harness(model, reader, store, commits);
    }

    // ---- History ----

    [AvaloniaFact]
    public async Task Gecmis_depo_acilirken_OKUNMAZ()
    {
        // It is read as the menu opens: running an extra `git log` on every repository open would be
        // the price of a menu the user may never open.
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
        // A menu item has to be a single line; a multi-line message would push the menu off screen
        // (GitExtensions caps it at 72 too).
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
        // This is the ONE place existing text is overwritten: the user asked for exactly this by
        // picking from the menu (GitExtensions' `ReplaceMessage` does the same).
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

    // ---- Template ----

    [AvaloniaFact]
    public async Task Sablon_YORUMLARI_TEMIZLENEREK_yuklenir()
    {
        // 🔴 Our commit path is `--cleanup=whitespace` and KEEPS comments (P05-T06, deliberately).
        // Had the template been loaded as is, the `# …` lines that never reach the commit on git's
        // editor path would end up in our commit body.
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
        // 🔴 MEASURED: `core.commentChar` does not have to be `#`. In a repository where it is `;`, a
        // blind `#` filter would both leave the real comments in and delete the user's issue line.
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
        // git itself rejects the commit in this case with `fatal: could not read` (measured) — the
        // configuration really is broken. Showing an empty menu would be hiding it.
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
        // If the user has started writing a new message, ticking amend must not delete it.
        FakeCommitMessageReader reader = new() { HeadMessage = "eski commit mesajı" };

        Harness harness = await CreateAsync(reader);

        harness.Message.Text = "yazmakta olduğum yeni mesaj";
        harness.Model.Amend = true;

        harness.Message.Text.ShouldBe("yazmakta olduğum yeni mesaj");
    }

    [AvaloniaFact]
    public async Task Commitsiz_depoda_amend_kutuyu_BOZMAZ()
    {
        // The core returns null here (with real git, `git log` exits 128).
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

        // Zero delay; let the queued continuation of the save run.
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

        // If the repository is reopened on the same screen (a refresh or a repository switch), the text
        // typed must stay.
        await harness.Model.OpenAsync(Repository);

        harness.Message.Text.ShouldBe("kullanıcının yazdığı");
    }

    [AvaloniaFact]
    public async Task Basarili_commit_TASLAGI_da_siler()
    {
        // 🔑 Unless the draft is deleted, the text just committed comes back the next time the screen
        // opens and invites the user into a second commit.
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
        // P05-T12 tested that the message is preserved; the draft has to be preserved as well, or the
        // text would still be lost if the application closed at the moment of an error.
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
        // 🔑 The delayed save may not have run yet; the last line the user typed — where they left off
        // — would be lost as the window closed.
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
        // Writing the text loaded from the draft straight back would be a pointless disk write.
        FakeCommitMessageStore store = new();
        store.Drafts[Repository] = "diskten gelen";

        Harness harness = await CreateAsync(null, store);

        await Task.Delay(20);

        harness.Message.Text.ShouldBe("diskten gelen");
        store.SaveCount.ShouldBe(0);
    }

    // ---- The message git prepared ----

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
