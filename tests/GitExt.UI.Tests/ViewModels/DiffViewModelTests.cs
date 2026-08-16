using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P04-T08 — The changed files list.
/// </summary>
/// <remarks>
/// This component is <b>standalone</b>: it knows nothing about the main window or the commit list.
/// The same component will be used in the comparison window of <c>P04-T16</c>, so its tests too are
/// written only against its own API.
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
        // The user must recognise here the same presentation they see on the command line.
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
        // MEASURED: --numstat gives no counts for a binary file. Showing "0 / 0" would mean
        // "nothing changed".
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

        // Even though the content has not been read, the counts are right: they come from --numstat.
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
        // Leaving the selection on a filtered-out row would keep the user in a file they did not expect.
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

        // At the root: the "src" folder plus README.md
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
        // The user can hold ↓ down on the commit list; to avoid starting a git process for every row,
        // the read is delayed and cancelled when the selection changes.
        FakeDiffReader reader = new([FakeGitData.Diff("a.cs")]);
        DiffViewModel viewModel = new(reader);

        for (int i = 0; i < 20; i++)
        {
            _ = viewModel.ShowCommitAsync("/tmp/depo", _someCommit);
        }

        await viewModel.ShowCommitAsync("/tmp/depo", _someCommit);

        // The meaningful property: 21 rapid selections must produce a single read.
        // (Saying "there are exactly 0 reads right now" would depend on timing and be fragile — under
        // load a delay may already have elapsed.)
        reader.ReadCallCount.ShouldBe(1);
    }

    // ---- P04-T09: the unified diff view ----

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
        // Hunk headers and content lines in ONE flat list: that is how virtualisation works.
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
        // Caught in a render of a real repository: the view ALWAYS draws a line from segments, so a
        // hunk header with no segments came out as an empty grey strip on screen.
        DiffViewModel viewModel = await LoadedAsync(WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Added, "kod") { NewLineNumber = 1 }));

        DiffLineRow header = viewModel.Lines[0];

        header.IsHunkHeader.ShouldBeTrue();
        header.Segments.Count.ShouldBe(1);
        header.Segments[0].Text.ShouldBe("@@ -1,2 +1,2 @@");
    }

    // ---- P04-T10: the side-by-side view ----

    [AvaloniaFact]
    public async Task Yan_yana_moda_gecince_ayni_diff_yeniden_yerlesir()
    {
        // Switching mode MUST NOT RUN `git` AGAIN: both lists are produced from the same FileDiff.
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

        // Header + context + a change pair.
        viewModel.SideLines.Count.ShouldBe(3);
        viewModel.SideLines[0].IsHunkHeader.ShouldBeTrue();
        viewModel.SideLines[2].Left.Text.ShouldBe("iki eski");
        viewModel.SideLines[2].Right.Text.ShouldBe("iki yeni");

        reader.ReadCallCount.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Karsiligi_olmayan_tarafa_dolgu_konur()
    {
        // A filler IS NOT A BLANK LINE: it means "there is no line here" and is painted differently.
        // Confused with an empty context line, the user takes a line that does not exist for one that does.
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
        // MEASURED (P04-T07): in a CRLF file the content ends with `\r` and the model preserves that
        // DELIBERATELY (it is needed for `git apply` in Phase 05). On screen it showed as a box character.
        DiffViewModel viewModel = await LoadedAsync(WithHunk(
            "a.cs",
            new DiffLine(DiffLineKind.Added, "kod\r") { NewLineNumber = 1 }));

        viewModel.Lines[1].Text.ShouldBe("kod");

        // The content in the model must not change.
        viewModel.Files.Single().Diff.Hunks.Single().Lines.Single().Content.ShouldBe("kod\r");
    }

    [AvaloniaFact]
    public async Task Parcasiz_satir_da_tek_parcayla_gelir()
    {
        // The view always draws from segments, so there is not a second template to maintain.
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
        // Diff kinds without hunks are normal (measured in P04-T02); leaving a blank area would look
        // like an error to the user.
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

    // ---- P04-T12: navigating within the diff ----

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
        // GitExtensions' `GoToNextChange` works this way too: when the user says "next change" they
        // mean the next DIFFERENCE. Consecutive changes are one block.
        DiffViewModel viewModel = await LoadedAsync(Sample());

        viewModel.GoToNextChange().ShouldBeTrue();
        viewModel.Lines[viewModel.CurrentLineIndex].Text.ShouldBe("iki eski");

        // "uc eski" and "iki yeni" are the continuation of the SAME block — they must be skipped.
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

        // On reaching the end it must wrap around: if what is being looked for is above the cursor,
        // "not found" is misleading.
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
        // When copying from a diff the user is mostly pasting the code somewhere else; the +/- prefixes
        // are noise there. GitExtensions' default "Copy" behaves the same way.
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

        // A context line exists on both sides; it must appear only once in the patch.
        text.Split('\n').Count(l => l == " bir").ShouldBe(1);
        text.ShouldContain("-iki eski");
        text.ShouldContain("+iki yeni");
    }

    [AvaloniaFact]
    public async Task Dosya_degisince_duraklanan_satir_sifirlanir()
    {
        // The two lists have different indices; the old index would point at another line in another file.
        DiffViewModel viewModel = await LoadedAsync(
            WithHunk("a.cs", new DiffLine(DiffLineKind.Added, "a") { NewLineNumber = 1 }),
            WithHunk("b.cs", new DiffLine(DiffLineKind.Added, "b") { NewLineNumber = 1 }));

        viewModel.GoToNextChange();
        viewModel.CurrentLineIndex.ShouldBeGreaterThanOrEqualTo(0);

        viewModel.SelectedIndex = 1;
        viewModel.CurrentLineIndex.ShouldBe(-1);
    }

    // ---- P04-T13: display settings ----

    private static FileDiff Tabbed() => WithHunk(
        "a.cs",
        new DiffLine(DiffLineKind.Context, "ab\tc") { OldLineNumber = 1, NewLineNumber = 1 },
        new DiffLine(DiffLineKind.Added, "x y") { NewLineNumber = 2 });

    [AvaloniaFact]
    public async Task Sekmeler_tab_stop_a_acilir()
    {
        // MEASURED: Avalonia draws a tab not as a tab stop but at a fixed width of four spaces, and it
        // cannot be configured; the conversion is done on our side.
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
        // In Phase 05 we will hand the patch back to `git apply` verbatim; the model content is untouchable.
        DiffViewModel viewModel = await LoadedAsync(Tabbed());

        viewModel.ShowWhitespace = true;
        viewModel.TabWidth = 8;

        viewModel.Files.Single().Diff.Hunks.Single().Lines[0].Content.ShouldBe("ab\tc");
    }

    [AvaloniaFact]
    public async Task Kopyalama_gosterim_karakterlerini_ICERMEZ()
    {
        // ⚠️ The real trap: the display text contains · and » and the tabs have been expanded to spaces.
        // Had copying used it, the user would get BROKEN CODE on the clipboard.
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
        // The user does not search for the tab inside "ab\tc" using its display marker.
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
