using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P04-T08 — Değişen dosyalar listesi.
/// </summary>
/// <remarks>
/// Bu bileşen <b>bağımsız</b>: ana pencereyi veya commit listesini tanımıyor. Aynı bileşen
/// <c>P04-T16</c>'daki karşılaştırma penceresinde de kullanılacak, bu yüzden testleri de
/// yalnızca kendi API'sine karşı yazılıyor.
/// </remarks>
public class DiffViewModelTests
{
    private static readonly CommitId _someCommit = CommitId.Parse(FakeGitData.Sha(7));

    private static DiffViewModel Create(params FileDiff[] diffs) =>
        new(new FakeDiffReader(diffs));

    private static async Task<DiffViewModel> LoadedAsync(params FileDiff[] diffs)
    {
        DiffViewModel viewModel = Create(diffs);
        await viewModel.ShowCommitAsync("/tmp/depo", _someCommit);
        return viewModel;
    }

    [AvaloniaFact]
    public async Task Dosyalar_listelenir()
    {
        DiffViewModel viewModel = await LoadedAsync(
            FakeGitData.Diff("src/a.cs"),
            FakeGitData.Diff("README.md", FileChangeKind.Added, added: 10, removed: 0));

        viewModel.Files.Count.ShouldBe(2);
        viewModel.HasFiles.ShouldBeTrue();
        viewModel.IsEmpty.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Durum_harfleri_gitin_harfleriyle_ayni()
    {
        // Kullanıcı komut satırında gördüğü gösterimi burada da tanımalı.
        DiffViewModel viewModel = await LoadedAsync(
            FakeGitData.Diff("a", FileChangeKind.Added),
            FakeGitData.Diff("m", FileChangeKind.Modified),
            FakeGitData.Diff("d", FileChangeKind.Deleted),
            FakeGitData.Diff("r", FileChangeKind.Renamed, oldPath: "eski"));

        viewModel.Files.Select(f => f.StatusLetter).ShouldBe(["A", "M", "D", "R"]);
    }

    [AvaloniaFact]
    public async Task Binary_dosyada_satir_sayisi_gosterilmez()
    {
        // ÖLÇÜLDÜ: binary dosyada --numstat sayı vermiyor. "0 / 0" göstermek
        // "hiç değişmedi" demek olurdu.
        DiffViewModel viewModel = await LoadedAsync(
            FakeGitData.Diff("resim.png", binary: true),
            FakeGitData.Diff("kod.cs", added: 3, removed: 1));

        viewModel.Files.Single(f => f.IsBinary).HasLineCounts.ShouldBeFalse();

        FileChangeRow text = viewModel.Files.Single(f => !f.IsBinary);
        text.HasLineCounts.ShouldBeTrue();
        text.AddedLines.ShouldBe(3);
        text.RemovedLines.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Cok_buyuk_dosya_isaretlenir()
    {
        DiffViewModel viewModel = await LoadedAsync(
            FakeGitData.Diff("dev.txt", added: 500_000, removed: 500_000, tooLarge: true));

        FileChangeRow row = viewModel.Files.Single();

        row.IsTooLarge.ShouldBeTrue();

        // İçerik okunmamış olsa da sayılar doğru: --numstat'tan geliyor.
        row.AddedLines.ShouldBe(500_000);
    }

    [AvaloniaFact]
    public async Task Filtre_yola_gore_suzer()
    {
        DiffViewModel viewModel = await LoadedAsync(
            FakeGitData.Diff("src/kod.cs"),
            FakeGitData.Diff("src/test/kod-test.cs"),
            FakeGitData.Diff("README.md"));

        viewModel.Filter = "kod";
        viewModel.Files.Count.ShouldBe(2);

        viewModel.Filter = "readme";
        viewModel.Files.Single().Path.Value.ShouldBe("README.md");

        viewModel.Filter = "";
        viewModel.Files.Count.ShouldBe(3);
    }

    [AvaloniaFact]
    public async Task Filtre_secili_dosyayi_elerse_secim_basa_doner()
    {
        // Seçimi elenen bir satırda bırakmak kullanıcıyı beklemediği bir dosyada tutardı.
        DiffViewModel viewModel = await LoadedAsync(
            FakeGitData.Diff("bir.cs"),
            FakeGitData.Diff("iki.cs"),
            FakeGitData.Diff("uc.cs"));

        viewModel.SelectedIndex = 2;
        viewModel.SelectedFile!.Path.Value.ShouldBe("uc.cs");

        viewModel.Filter = "bir";

        viewModel.SelectedIndex.ShouldBe(0);
        viewModel.SelectedFile!.Path.Value.ShouldBe("bir.cs");
    }

    [AvaloniaFact]
    public async Task Hicbir_dosya_eslesmezse_secim_dusar()
    {
        DiffViewModel viewModel = await LoadedAsync(FakeGitData.Diff("bir.cs"));

        viewModel.Filter = "boyle-bir-sey-yok";

        viewModel.Files.ShouldBeEmpty();
        viewModel.SelectedIndex.ShouldBe(-1);
        viewModel.SelectedFile.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Agac_klasorlere_gore_gruplanir()
    {
        DiffViewModel viewModel = await LoadedAsync(
            FakeGitData.Diff("src/app/kod.cs"),
            FakeGitData.Diff("src/app/digeri.cs"),
            FakeGitData.Diff("README.md"));

        // Kökte: "src" klasörü + README.md
        viewModel.Tree.Count.ShouldBe(2);

        FileTreeNode src = viewModel.Tree.Single(n => n.Name == "src");
        src.IsFolder.ShouldBeTrue();

        FileTreeNode app = src.Children.Single();
        app.Name.ShouldBe("app");
        app.Children.Count.ShouldBe(2);
        app.Children.ShouldAllBe(n => !n.IsFolder);

        viewModel.Tree.Single(n => n.Name == "README.md").IsFolder.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Agac_filtreyi_takip_eder()
    {
        DiffViewModel viewModel = await LoadedAsync(
            FakeGitData.Diff("src/kod.cs"),
            FakeGitData.Diff("belge/oku.md"));

        viewModel.Filter = "src";

        viewModel.Tree.Single().Name.ShouldBe("src");
    }

    [AvaloniaFact]
    public async Task Degisiklik_yoksa_bos_olarak_isaretlenir()
    {
        DiffViewModel viewModel = await LoadedAsync();

        viewModel.IsEmpty.ShouldBeTrue();
        viewModel.HasFiles.ShouldBeFalse();
        viewModel.ErrorMessage.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Okuma_hatasi_mesaja_donusur()
    {
        DiffViewModel viewModel = new(new FakeDiffReader(
            failure: new GitException(GitFailureKind.Unknown, "diff patladı", "git diff", 1, string.Empty)));

        await viewModel.ShowCommitAsync("/tmp/depo", _someCommit);

        viewModel.ErrorMessage.ShouldNotBeNullOrEmpty();
        viewModel.Files.ShouldBeEmpty();
        viewModel.IsLoading.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Bos_commit_kimligi_temizler()
    {
        DiffViewModel viewModel = await LoadedAsync(FakeGitData.Diff("a.cs"));
        viewModel.Files.ShouldNotBeEmpty();

        await viewModel.ShowCommitAsync("/tmp/depo", default);

        viewModel.Files.ShouldBeEmpty();
        viewModel.Tree.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Hizli_gezinmede_onceki_okuma_iptal_edilir()
    {
        // Kullanıcı commit listesinde ↓ tuşuna basılı tutabilir; her satır için bir git
        // süreci başlatmamak adına okuma gecikmeli ve seçim değişince iptal ediliyor.
        FakeDiffReader reader = new([FakeGitData.Diff("a.cs")]);
        DiffViewModel viewModel = new(reader);

        for (int i = 0; i < 20; i++)
        {
            _ = viewModel.ShowCommitAsync("/tmp/depo", _someCommit);
        }

        await viewModel.ShowCommitAsync("/tmp/depo", _someCommit);

        // Anlamlı özellik: 21 hızlı seçim tek bir okuma üretmeli.
        // ("Şu anda tam olarak 0 okuma var" demek zamanlamaya bağlı ve kırılgan olurdu —
        // yük altında bir gecikme tamamlanmış olabilir.)
        reader.ReadCallCount.ShouldBe(1);
    }

    // ---- P04-T09: unified diff görünümü ----

    private static FileDiff WithHunk(string path, params DiffLine[] lines) =>
        FakeGitData.Diff(path) with
        {
            Hunks =
            [
                new DiffHunk
                {
                    Header = "@@ -1,2 +1,2 @@",
                    OldStart = 1,
                    OldLength = 2,
                    NewStart = 1,
                    NewLength = 2,
                    Lines = lines,
                },
            ],
        };

    [AvaloniaFact]
    public async Task Secili_dosyanin_satirlari_hunk_basligiyla_akar()
    {
        // Hunk başlıkları ve içerik satırları TEK düz listede: sanallaştırma böyle çalışıyor.
        DiffViewModel viewModel = await LoadedAsync(WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Context, "bir") { OldLineNumber = 1, NewLineNumber = 1 },
            new DiffLine(DiffLineKind.Removed, "iki") { OldLineNumber = 2 },
            new DiffLine(DiffLineKind.Added, "IKI") { NewLineNumber = 2 }));

        viewModel.HasLines.ShouldBeTrue();
        viewModel.Lines.Count.ShouldBe(4);

        viewModel.Lines[0].IsHunkHeader.ShouldBeTrue();
        viewModel.Lines[0].Text.ShouldBe("@@ -1,2 +1,2 @@");

        viewModel.Lines[1].OldLineNumber.ShouldBe("1");
        viewModel.Lines[1].NewLineNumber.ShouldBe("1");

        viewModel.Lines[2].IsRemoved.ShouldBeTrue();
        viewModel.Lines[2].NewLineNumber.ShouldBe("");

        viewModel.Lines[3].IsAdded.ShouldBeTrue();
        viewModel.Lines[3].OldLineNumber.ShouldBe("");
    }

    [AvaloniaFact]
    public async Task Hunk_basligi_da_parca_olarak_gelir()
    {
        // Gerçek depo render'ında yakalandı: görünüm satırı HER ZAMAN parçalar üzerinden
        // çiziyor, dolayısıyla parçasız bir hunk başlığı ekranda boş gri şerit oluyordu.
        DiffViewModel viewModel = await LoadedAsync(WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Added, "kod") { NewLineNumber = 1 }));

        DiffLineRow header = viewModel.Lines[0];

        header.IsHunkHeader.ShouldBeTrue();
        header.Segments.Count.ShouldBe(1);
        header.Segments[0].Text.ShouldBe("@@ -1,2 +1,2 @@");
    }

    // ---- P04-T10: yan yana görünüm ----

    [AvaloniaFact]
    public async Task Yan_yana_moda_gecince_ayni_diff_yeniden_yerlesir()
    {
        // Mod değişimi `git`'i YENİDEN ÇALIŞTIRMAMALI: iki liste de aynı FileDiff'ten üretiliyor.
        FakeDiffReader reader = new([WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Context, "bir") { OldLineNumber = 1, NewLineNumber = 1 },
            new DiffLine(DiffLineKind.Removed, "iki eski") { OldLineNumber = 2 },
            new DiffLine(DiffLineKind.Added, "iki yeni") { NewLineNumber = 2 })]);

        DiffViewModel viewModel = new(reader);
        await viewModel.ShowCommitAsync("/tmp/depo", _someCommit);

        viewModel.ShowUnifiedLines.ShouldBeTrue();
        viewModel.ShowSideLines.ShouldBeFalse();

        viewModel.ShowSideBySide = true;

        viewModel.ShowUnifiedLines.ShouldBeFalse();
        viewModel.ShowSideLines.ShouldBeTrue();
        viewModel.Lines.ShouldBeEmpty();

        // Başlık + bağlam + değişiklik çifti.
        viewModel.SideLines.Count.ShouldBe(3);
        viewModel.SideLines[0].IsHunkHeader.ShouldBeTrue();
        viewModel.SideLines[2].Left.Text.ShouldBe("iki eski");
        viewModel.SideLines[2].Right.Text.ShouldBe("iki yeni");

        reader.ReadCallCount.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Karsiligi_olmayan_tarafa_dolgu_konur()
    {
        // Dolgu BOŞ SATIR DEĞİL: "burada satır yok" demek ve ayrı boyanıyor. Boş bir bağlam
        // satırıyla karıştırılırsa kullanıcı olmayan bir satırı var sanar.
        DiffViewModel viewModel = Create(WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Added, "yeni satir") { NewLineNumber = 1 }));

        viewModel.ShowSideBySide = true;
        await viewModel.ShowCommitAsync("/tmp/depo", _someCommit);

        SideBySideLineRow row = viewModel.SideLines[1];

        row.Left.IsFiller.ShouldBeTrue();
        row.Right.IsFiller.ShouldBeFalse();
        row.Right.Text.ShouldBe("yeni satir");
    }

    [AvaloniaFact]
    public async Task Yan_yana_gorunumde_satir_ici_parcalar_korunur()
    {
        DiffViewModel viewModel = Create(WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Removed, "bir iki uc")
            {
                OldLineNumber = 1,
                Segments =
                [
                    new DiffSegment(DiffLineKind.Context, "bir "),
                    new DiffSegment(DiffLineKind.Removed, "iki"),
                    new DiffSegment(DiffLineKind.Context, " uc"),
                ],
            },
            new DiffLine(DiffLineKind.Added, "bir IKI uc")
            {
                NewLineNumber = 1,
                Segments =
                [
                    new DiffSegment(DiffLineKind.Context, "bir "),
                    new DiffSegment(DiffLineKind.Added, "IKI"),
                    new DiffSegment(DiffLineKind.Context, " uc"),
                ],
            }));

        viewModel.ShowSideBySide = true;
        await viewModel.ShowCommitAsync("/tmp/depo", _someCommit);

        SideBySideLineRow row = viewModel.SideLines[1];

        row.Left.Segments[1].IsRemoved.ShouldBeTrue();
        row.Right.Segments[1].IsAdded.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_baska_dosya_secilince_liste_yenilenir()
    {
        DiffViewModel viewModel = await LoadedAsync(
            WithHunk("a.cs", new DiffLine(DiffLineKind.Added, "a") { NewLineNumber = 1 }),
            WithHunk("b.cs", new DiffLine(DiffLineKind.Added, "b") { NewLineNumber = 1 }));

        viewModel.ShowSideBySide = true;
        viewModel.SelectedIndex = 1;

        viewModel.SideLines[1].Right.Text.ShouldBe("b");
        viewModel.Lines.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Gosterimde_sondaki_CR_kirpilir()
    {
        // ÖLÇÜLDÜ (P04-T07): CRLF dosyada içerik `\r` ile bitiyor ve model bunu BİLEREK
        // koruyor (Faz 05'te `git apply` için gerekli). Ekranda kutu karakteri görünürdü.
        DiffViewModel viewModel = await LoadedAsync(WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Added, "kod\r") { NewLineNumber = 1 }));

        viewModel.Lines[1].Text.ShouldBe("kod");

        // Modeldeki içerik değişmemeli.
        viewModel.Files.Single().Diff.Hunks.Single().Lines.Single().Content.ShouldBe("kod\r");
    }

    [AvaloniaFact]
    public async Task Parcasiz_satir_da_tek_parcayla_gelir()
    {
        // Görünüm her zaman parçalar üzerinden çiziyor; iki ayrı şablon bakımı olmasın diye.
        DiffViewModel viewModel = await LoadedAsync(WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Added, "kod") { NewLineNumber = 1 }));

        DiffLineRow row = viewModel.Lines[1];

        row.Segments.Count.ShouldBe(1);
        row.Segments[0].Text.ShouldBe("kod");
    }

    [AvaloniaFact]
    public async Task Satir_ici_parcalar_korunur()
    {
        DiffViewModel viewModel = await LoadedAsync(WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Added, "bir IKI uc")
            {
                NewLineNumber = 1,
                Segments =
                [
                    new DiffSegment(DiffLineKind.Context, "bir "),
                    new DiffSegment(DiffLineKind.Added, "IKI"),
                    new DiffSegment(DiffLineKind.Context, " uc"),
                ],
            }));

        viewModel.Lines[1].Segments.Count.ShouldBe(3);
        viewModel.Lines[1].Segments[1].IsAdded.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Icerik_yoksa_NEDEN_oldugu_soylenir()
    {
        // Hunk'sız diff türleri normaldir (P04-T02'de ölçüldü); boş alan bırakmak
        // kullanıcıya hata gibi görünürdü.
        DiffViewModel binary = await LoadedAsync(FakeGitData.Diff("resim.png", binary: true));
        binary.HasLines.ShouldBeFalse();
        binary.ContentNotice!.ShouldContain("Binary");

        DiffViewModel large = await LoadedAsync(FakeGitData.Diff("dev.txt", tooLarge: true));
        large.ContentNotice!.ShouldContain("too large");

        DiffViewModel renamed = await LoadedAsync(
            FakeGitData.Diff("yeni", FileChangeKind.Renamed, oldPath: "eski"));
        renamed.ContentNotice!.ShouldContain("was moved");
    }

    [AvaloniaFact]
    public async Task Dosya_degisince_satirlar_yenilenir()
    {
        DiffViewModel viewModel = await LoadedAsync(
            WithHunk("a.cs", new DiffLine(DiffLineKind.Added, "a") { NewLineNumber = 1 }),
            WithHunk("b.cs", new DiffLine(DiffLineKind.Added, "b") { NewLineNumber = 1 }));

        viewModel.SelectedIndex = 0;
        viewModel.Lines[1].Text.ShouldBe("a");

        viewModel.SelectedIndex = 1;
        viewModel.Lines[1].Text.ShouldBe("b");
    }

    // ---- P04-T12: diff içinde gezinme ----

    private static FileDiff Sample() => WithHunk(
        "a.cs",
        new DiffLine(DiffLineKind.Context, "bir") { OldLineNumber = 1, NewLineNumber = 1 },
        new DiffLine(DiffLineKind.Removed, "iki eski") { OldLineNumber = 2 },
        new DiffLine(DiffLineKind.Removed, "uc eski") { OldLineNumber = 3 },
        new DiffLine(DiffLineKind.Added, "iki yeni") { NewLineNumber = 2 },
        new DiffLine(DiffLineKind.Context, "dort") { OldLineNumber = 4, NewLineNumber = 3 },
        new DiffLine(DiffLineKind.Added, "bes yeni") { NewLineNumber = 4 });

    [AvaloniaFact]
    public async Task Sonraki_degisiklik_blok_basina_gider_baslığa_degil()
    {
        // GitExtensions'ın `GoToNextChange`'i de böyle: kullanıcı "sonraki değişiklik"
        // derken bir sonraki FARKI kastediyor. Ardışık değişiklikler tek blok.
        DiffViewModel viewModel = await LoadedAsync(Sample());

        viewModel.GoToNextChange().ShouldBeTrue();
        viewModel.Lines[viewModel.CurrentLineIndex].Text.ShouldBe("iki eski");

        // "uc eski" ve "iki yeni" AYNI bloğun devamı — atlanmalı.
        viewModel.GoToNextChange().ShouldBeTrue();
        viewModel.Lines[viewModel.CurrentLineIndex].Text.ShouldBe("bes yeni");

        viewModel.GoToNextChange().ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Onceki_degisiklik_geri_gider()
    {
        DiffViewModel viewModel = await LoadedAsync(Sample());

        viewModel.GoToNextChange();
        viewModel.GoToNextChange();

        viewModel.GoToPreviousChange().ShouldBeTrue();
        viewModel.Lines[viewModel.CurrentLineIndex].Text.ShouldBe("iki eski");

        viewModel.GoToPreviousChange().ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Hunk_gezinmesi_baslıklara_gider()
    {
        DiffViewModel viewModel = await LoadedAsync(Sample());

        viewModel.GoToNextHunk().ShouldBeTrue();
        viewModel.Lines[viewModel.CurrentLineIndex].IsHunkHeader.ShouldBeTrue();

        viewModel.GoToNextHunk().ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Dosyalar_arasinda_gezinilir()
    {
        DiffViewModel viewModel = await LoadedAsync(
            WithHunk("a.cs", new DiffLine(DiffLineKind.Added, "a") { NewLineNumber = 1 }),
            WithHunk("b.cs", new DiffLine(DiffLineKind.Added, "b") { NewLineNumber = 1 }));

        viewModel.SelectedIndex.ShouldBe(0);
        viewModel.GoToPreviousFile().ShouldBeFalse();

        viewModel.GoToNextFile().ShouldBeTrue();
        viewModel.SelectedFile!.Name.ShouldBe("b.cs");

        viewModel.GoToNextFile().ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Arama_eslesen_satira_gider_ve_basa_sarar()
    {
        DiffViewModel viewModel = await LoadedAsync(Sample());

        viewModel.LineSearchText = "yeni";

        viewModel.FindNext().ShouldBeTrue();
        viewModel.Lines[viewModel.CurrentLineIndex].Text.ShouldBe("iki yeni");

        viewModel.FindNext().ShouldBeTrue();
        viewModel.Lines[viewModel.CurrentLineIndex].Text.ShouldBe("bes yeni");

        // Sona gelince başa sarmalı: aranan şey imlecin üstündeyse "not found" yanıltıcı olur.
        viewModel.FindNext().ShouldBeTrue();
        viewModel.Lines[viewModel.CurrentLineIndex].Text.ShouldBe("iki yeni");

        viewModel.LineSearchStatus.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Arama_bulamazsa_sessiz_kalinmaz()
    {
        DiffViewModel viewModel = await LoadedAsync(Sample());

        viewModel.LineSearchText = "boyle bir sey yok";

        viewModel.FindNext().ShouldBeFalse();
        viewModel.LineSearchStatus.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_arama_iki_tarafi_da_tarar()
    {
        DiffViewModel viewModel = await LoadedAsync(Sample());
        viewModel.ShowSideBySide = true;

        viewModel.LineSearchText = "uc eski";
        viewModel.FindNext().ShouldBeTrue();

        viewModel.SideLines[viewModel.CurrentLineIndex].Left.Text.ShouldBe("uc eski");
    }

    [AvaloniaFact]
    public async Task Kopyalama_varsayilan_olarak_ONEKSIZ_koddur()
    {
        // Kullanıcı diff'ten kopyalarken çoğunlukla kodu başka yere yapıştırıyor;
        // +/- önekleri orada gürültü. GitExtensions'ın varsayılan "Copy"si de böyle.
        DiffViewModel viewModel = await LoadedAsync(Sample());

        string text = viewModel.CopyText();

        text.ShouldNotContain("@@");
        text.ShouldNotContain("+");
        text.ShouldNotContain("-");
        text.ShouldContain("iki yeni");
    }

    [AvaloniaFact]
    public async Task Yama_olarak_kopyalama_onekleri_ve_basligi_icerir()
    {
        DiffViewModel viewModel = await LoadedAsync(Sample());

        string text = viewModel.CopyText(DiffCopyMode.Patch);

        text.ShouldContain("@@ -1,2 +1,2 @@");
        text.ShouldContain("-iki eski");
        text.ShouldContain("+iki yeni");
        text.ShouldContain(" bir");
    }

    [AvaloniaFact]
    public async Task Eski_ve_yeni_surum_kopyalama_karsi_tarafi_atlar()
    {
        DiffViewModel viewModel = await LoadedAsync(Sample());

        string old = viewModel.CopyText(DiffCopyMode.OldVersion);
        old.ShouldContain("iki eski");
        old.ShouldNotContain("iki yeni");

        string current = viewModel.CopyText(DiffCopyMode.NewVersion);
        current.ShouldContain("iki yeni");
        current.ShouldNotContain("iki eski");
    }

    [AvaloniaFact]
    public async Task Secim_verilirse_yalnizca_o_satirlar_kopyalanir()
    {
        DiffViewModel viewModel = await LoadedAsync(Sample());

        string text = viewModel.CopyText(DiffCopyMode.Code, [4]);

        text.ShouldBe("iki yeni");
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_yama_kopyalamasi_baglami_iki_kez_yazmaz()
    {
        DiffViewModel viewModel = await LoadedAsync(Sample());
        viewModel.ShowSideBySide = true;

        string text = viewModel.CopyText(DiffCopyMode.Patch);

        // Bağlam satırı iki tarafta da var; yamada tek kez görünmeli.
        text.Split('\n').Count(l => l == " bir").ShouldBe(1);
        text.ShouldContain("-iki eski");
        text.ShouldContain("+iki yeni");
    }

    [AvaloniaFact]
    public async Task Dosya_degisince_duraklanan_satir_sifirlanir()
    {
        // İki listenin indeksleri farklı; eski indeks başka dosyada başka satırı gösterirdi.
        DiffViewModel viewModel = await LoadedAsync(
            WithHunk("a.cs", new DiffLine(DiffLineKind.Added, "a") { NewLineNumber = 1 }),
            WithHunk("b.cs", new DiffLine(DiffLineKind.Added, "b") { NewLineNumber = 1 }));

        viewModel.GoToNextChange();
        viewModel.CurrentLineIndex.ShouldBeGreaterThanOrEqualTo(0);

        viewModel.SelectedIndex = 1;
        viewModel.CurrentLineIndex.ShouldBe(-1);
    }

    // ---- P04-T13: görsel ayarlar ----

    private static FileDiff Tabbed() => WithHunk(
        "a.cs",
        new DiffLine(DiffLineKind.Context, "ab\tc") { OldLineNumber = 1, NewLineNumber = 1 },
        new DiffLine(DiffLineKind.Added, "x y") { NewLineNumber = 2 });

    [AvaloniaFact]
    public async Task Sekmeler_tab_stop_a_acilir()
    {
        // ÖLÇÜLDÜ: Avalonia sekmeyi tab-stop olarak değil sabit dört boşluk genişliğinde
        // çiziyor ve ayarlanamıyor; dönüşüm bizde yapılıyor.
        DiffViewModel viewModel = await LoadedAsync(Tabbed());

        viewModel.Lines[1].Text.ShouldBe("ab  c");

        viewModel.TabWidth = 8;
        viewModel.Lines[1].Text.ShouldBe("ab      c");
    }

    [AvaloniaFact]
    public async Task Bosluk_gosterimi_acilip_kapanabilir()
    {
        DiffViewModel viewModel = await LoadedAsync(Tabbed());

        viewModel.Lines[2].Text.ShouldBe("x y");

        viewModel.ShowWhitespace = true;
        viewModel.Lines[2].Text.ShouldBe($"x{DiffTextFormatter.SpaceMarker}y");

        viewModel.ShowWhitespace = false;
        viewModel.Lines[2].Text.ShouldBe("x y");
    }

    [AvaloniaFact]
    public async Task Gosterim_ayarlari_MODELI_degistirmez()
    {
        // Faz 05'te yamayı `git apply`'a birebir geri vereceğiz; model içeriği dokunulmaz.
        DiffViewModel viewModel = await LoadedAsync(Tabbed());

        viewModel.ShowWhitespace = true;
        viewModel.TabWidth = 8;

        viewModel.Files.Single().Diff.Hunks.Single().Lines[0].Content.ShouldBe("ab\tc");
    }

    [AvaloniaFact]
    public async Task Kopyalama_gosterim_karakterlerini_ICERMEZ()
    {
        // ⚠️ Gerçek tuzak: gösterim metni · ve » içeriyor ve sekmeler boşluğa açılmış.
        // Kopyalama onu kullansaydı kullanıcı panoya BOZUK KOD alırdı.
        DiffViewModel viewModel = await LoadedAsync(Tabbed());

        viewModel.ShowWhitespace = true;

        string text = viewModel.CopyText();

        text.ShouldContain("ab\tc");
        text.ShouldNotContain(DiffTextFormatter.SpaceMarker);
        text.ShouldNotContain(DiffTextFormatter.TabMarker);
    }

    [AvaloniaFact]
    public async Task Arama_ham_metinde_yapilir()
    {
        // Kullanıcı "ab\tc" içindeki sekmeyi göstergeyle aramaz.
        DiffViewModel viewModel = await LoadedAsync(Tabbed());

        viewModel.ShowWhitespace = true;
        viewModel.LineSearchText = "x y";

        viewModel.FindNext().ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_da_gosterim_ayarlari_gecerli()
    {
        DiffViewModel viewModel = await LoadedAsync(Tabbed());
        viewModel.ShowSideBySide = true;
        viewModel.ShowWhitespace = true;

        SideBySideLineRow row = viewModel.SideLines[1];

        row.Left.Text.ShouldContain(DiffTextFormatter.TabMarker);
        row.Left.RawText.ShouldBe("ab\tc");
    }

    [AvaloniaFact]
    public async Task Sekme_genisligi_makul_araliga_sikistirilir()
    {
        DiffViewModel viewModel = await LoadedAsync(Tabbed());

        viewModel.TabWidth = 0;
        viewModel.Lines[1].Text.ShouldBe("ab c");

        viewModel.TabWidth = 500;
        viewModel.Lines[1].Text.Length.ShouldBeLessThan(40);
    }
}
