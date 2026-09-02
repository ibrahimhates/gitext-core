using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Settings;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14 — the commit screen's file lists as a folder tree.
/// </summary>
/// <remarks>
/// <para>
/// The reference is GitExtensions' <c>FileStatusList.StatusSorter.CreateTreeSortedByPath</c> and the
/// snapshots beside it (<c>FileStatusListSorterTests.*.verified.txt</c>). The shape those pin down:
/// folders before files at every level, a folder that holds files of its own is never merged away,
/// and a chain of folders holding nothing but one subfolder becomes a single row.
/// </para>
/// <para>
/// Folder selection is <b>not</b> a separate command there either: <c>FormCommit.StageClick</c>
/// hands over <c>Unstaged.SelectedItems</c>, and that collects the selected node's tag <b>and every
/// descendant's</b>.
/// </para>
/// </remarks>
public class WorkingTreeFileTreeTests
{
    private static FileStatus Unstaged(string path) =>
        new() { Path = RepositoryPath.Parse(path), UnstagedChange = FileChangeKind.Modified };

    private static FileStatus Staged(string path) =>
        new() { Path = RepositoryPath.Parse(path), StagedChange = FileChangeKind.Modified };

    private static async Task<WorkingTreeViewModel> CreateAsync(params FileStatus[] entries)
    {
        FakeStatusReader status = new(entries);

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()));

        await model.OpenAsync("/tmp/depo");

        return model;
    }

    /// <summary>Renders the tree the way the list shows it: indent, then the row's own text.</summary>
    private static IReadOnlyList<string> Render(IEnumerable<WorkingTreeRow> rows) =>
        [.. rows.Select(row =>
            new string(' ', row.Depth * 2) + row.DisplayName + (row.IsFolder ? "/" : string.Empty))];

    // ---------------------------------------------------------------- building the tree

    [AvaloniaFact]
    public async Task Kok_seviyesindeki_dosya_klasor_dugumu_almaz()
    {
        WorkingTreeViewModel model = await CreateAsync(Unstaged("README.md"));

        Render(model.UnstagedRows).ShouldBe(["README.md"]);
    }

    [AvaloniaFact]
    public async Task Ayni_klasordeki_dosyalar_tek_klasor_altinda_toplanir()
    {
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("src/b.cs"),
            Unstaged("src/a.cs"),
            Unstaged("src/c.cs"));

        Render(model.UnstagedRows).ShouldBe(["src/", "  a.cs", "  b.cs", "  c.cs"]);
    }

    [AvaloniaFact]
    public async Task Tek_cocuklu_klasor_zinciri_TEK_satira_iner()
    {
        // GitExtensions never creates a node for a folder that neither holds files nor splits:
        // `GetOrCreateParent` only ever makes a node for a path that actually carries files, plus
        // the common prefix where two paths diverge.
        WorkingTreeViewModel model = await CreateAsync(Unstaged("a/b/c/derin.cs"));

        Render(model.UnstagedRows).ShouldBe(["a/b/c/", "  derin.cs"]);
    }

    [AvaloniaFact]
    public async Task Zincir_ayrildigi_yerde_bolunur()
    {
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("a/b/c/bir.cs"),
            Unstaged("a/b/d/iki.cs"));

        Render(model.UnstagedRows).ShouldBe(["a/b/", "  c/", "    bir.cs", "  d/", "    iki.cs"]);
    }

    [AvaloniaFact]
    public async Task Kendi_dosyasi_olan_klasor_ustune_KATLANMAZ()
    {
        // `core` holds a file of its own, so it cannot become part of `core/api`. GitExtensions'
        // `Sort_should_not_split_folders` snapshot pins exactly this.
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("core/c.1"),
            Unstaged("core/api/c_a.0"));

        Render(model.UnstagedRows).ShouldBe(["core/", "  api/", "    c_a.0", "  c.1"]);
    }

    [AvaloniaFact]
    public async Task Klasorler_dosyalardan_ONCE_gelir()
    {
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("zzz.txt"),
            Unstaged("aaa/dosya.txt"));

        Render(model.UnstagedRows).ShouldBe(["aaa/", "  dosya.txt", "zzz.txt"]);
    }

    [AvaloniaFact]
    public async Task Turkce_ve_bosluklu_klasor_adlari_bozulmaz()
    {
        // 🔴 The trimming is done by LENGTH (`path[parentPathLength..]`), so a name whose byte and
        // char counts differ must not be cut in the wrong place; and a space in the name must not
        // be taken for a separator.
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("belgeler/şükran çağrı/İstanbul notları.txt"),
            Unstaged("belgeler/şükran çağrı/ölçüm.txt"));

        Render(model.UnstagedRows).ShouldBe(
        [
            "belgeler/şükran çağrı/",
            "  İstanbul notları.txt",
            "  ölçüm.txt",
        ]);
    }

    [AvaloniaFact]
    public async Task Klasor_satiri_kendi_altindaki_TUM_dosyalari_temsil_eder()
    {
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("src/ui/a.cs"),
            Unstaged("src/ui/alt/b.cs"),
            Unstaged("baska.txt"));

        WorkingTreeFolderRow folder = model.UnstagedRows.OfType<WorkingTreeFolderRow>()
            .Single(row => row.Path == "src/ui");

        folder.EnumerateFiles().Select(file => file.Path.Value)
            .ShouldBe(["src/ui/alt/b.cs", "src/ui/a.cs"]);
    }

    // ---------------------------------------------------------------- staging a folder

    [AvaloniaFact]
    public async Task Klasor_secilince_stage_ALT_AGACIN_TAMAMINI_alir()
    {
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("src/ui/a.cs"),
            Unstaged("src/ui/alt/b.cs"),
            Unstaged("src/core/c.cs"),
            Unstaged("disarida.txt"));

        int index = model.UnstagedRows.ToList()
            .FindIndex(row => row is WorkingTreeFolderRow { Path: "src/ui" });

        model.SelectedUnstagedIndex = index;
        await model.StageSelectedAsync();

        model.Staged.Select(row => row.Path.Value).ShouldBe(["src/ui/a.cs", "src/ui/alt/b.cs"]);
        model.Unstaged.Select(row => row.Path.Value).ShouldBe(["disarida.txt", "src/core/c.cs"]);
    }

    [AvaloniaFact]
    public async Task Klasor_secilince_unstage_ALT_AGACIN_TAMAMINI_geri_alir()
    {
        WorkingTreeViewModel model = await CreateAsync(
            Staged("src/ui/a.cs"),
            Staged("src/ui/alt/b.cs"),
            Staged("src/core/c.cs"));

        int index = model.StagedRows.ToList()
            .FindIndex(row => row is WorkingTreeFolderRow { Path: "src/ui" });

        model.IsStagedListActive = true;
        model.SelectedStagedIndex = index;
        await model.UnstageSelectedAsync();

        model.Staged.Select(row => row.Path.Value).ShouldBe(["src/core/c.cs"]);
        model.Unstaged.Select(row => row.Path.Value).ShouldBe(["src/ui/a.cs", "src/ui/alt/b.cs"]);
    }

    [AvaloniaFact]
    public async Task Karisik_durumda_klasor_yalnizca_KENDI_TARAFINDAKI_dosyalari_alir()
    {
        // The same folder appears on both sides. Staging it from the unstaged list must not touch
        // the file that is already staged — the two lists are two different trees.
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("src/degisen.cs"),
            Staged("src/hazir.cs"));

        int index = model.UnstagedRows.ToList()
            .FindIndex(row => row is WorkingTreeFolderRow { Path: "src" });

        model.SelectedUnstagedIndex = index;
        await model.StageSelectedAsync();

        model.Unstaged.ShouldBeEmpty();
        model.Staged.Select(row => row.Path.Value).ShouldBe(["src/degisen.cs", "src/hazir.cs"]);
    }

    [AvaloniaFact]
    public async Task Klasor_secili_iken_diff_YOK()
    {
        // A folder is a valid selection, but there is no single file to show a diff for; the
        // partial-staging commands must go away with it.
        WorkingTreeViewModel model = await CreateAsync(Unstaged("src/a.cs"));

        model.SelectedUnstagedIndex = 0;

        model.SelectedRow.ShouldBeOfType<WorkingTreeFolderRow>();
        model.SelectedFile.ShouldBeNull();
        ((IPartialStagingHost)model).CanStage.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- open / close

    [AvaloniaFact]
    public async Task Kapali_klasorun_icerigi_satir_listesinde_YOK()
    {
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("src/a.cs"),
            Unstaged("src/b.cs"),
            Unstaged("kok.txt"));

        WorkingTreeFolderRow folder = model.UnstagedRows.OfType<WorkingTreeFolderRow>().Single();

        model.ToggleFolder(folder);

        Render(model.UnstagedRows).ShouldBe(["src/", "kok.txt"]);

        model.ToggleFolder(model.UnstagedRows.OfType<WorkingTreeFolderRow>().Single());

        Render(model.UnstagedRows).ShouldBe(["src/", "  a.cs", "  b.cs", "kok.txt"]);
    }

    [AvaloniaFact]
    public async Task Kapatilan_klasor_YENILEMEDEN_SONRA_da_kapali_kalir()
    {
        // 🔴 The watcher refreshes on every file save. If the closed/open state lived on the row
        // objects, every save in the editor would reopen every folder the user had closed.
        WorkingTreeViewModel model = await CreateAsync(Unstaged("src/a.cs"), Unstaged("kok.txt"));

        model.ToggleFolder(model.UnstagedRows.OfType<WorkingTreeFolderRow>().Single());

        await model.RefreshAsync();

        Render(model.UnstagedRows).ShouldBe(["src/", "kok.txt"]);
    }

    [AvaloniaFact]
    public async Task Tumunu_ac_ve_tumunu_kapat_tek_tarafa_uygulanir()
    {
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("src/ui/a.cs"),
            Staged("src/ui/b.cs"));

        model.SetAllFoldersExpanded(staged: false, expanded: false);

        Render(model.UnstagedRows).ShouldBe(["src/ui/"]);
        Render(model.StagedRows).ShouldBe(["src/ui/", "  b.cs"]);

        model.SetAllFoldersExpanded(staged: false, expanded: true);

        Render(model.UnstagedRows).ShouldBe(["src/ui/", "  a.cs"]);
    }

    // ---------------------------------------------------------------- mode and the setting

    [AvaloniaFact]
    public async Task Varsayilan_AGAC()
    {
        // GitExtensions' default is `DiffListSortType.FilePath` — the tree.
        WorkingTreeViewModel model = await CreateAsync(Unstaged("src/a.cs"));

        model.ListMode.ShouldBe(WorkingTreeListMode.Tree);
        model.IsTreeMode.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Duz_listede_satirlar_TAM_YOLU_gosterir_ve_klasor_yok()
    {
        WorkingTreeViewModel model = await CreateAsync(
            Unstaged("src/ui/a.cs"),
            Unstaged("kok.txt"));

        model.ListMode = WorkingTreeListMode.Flat;

        Render(model.UnstagedRows).ShouldBe(["kok.txt", "src/ui/a.cs"]);
        model.UnstagedRows.ShouldAllBe(row => !row.IsFolder);

        model.ListMode = WorkingTreeListMode.Tree;

        Render(model.UnstagedRows).ShouldBe(["src/ui/", "  a.cs", "kok.txt"]);
    }

    [AvaloniaFact]
    public async Task Mod_secimi_ayarlara_YAZILIR_ve_geri_OKUNUR()
    {
        InMemorySettingsStore settings = new();

        WorkingTreeViewModel model = await CreateAsync(Unstaged("src/a.cs"));
        model.AttachSettings(settings);

        model.ListMode = WorkingTreeListMode.Flat;
        settings.Current.Layout.CommitFileTree.ShouldBeFalse();

        // A second window opens: the choice comes back.
        WorkingTreeViewModel again = await CreateAsync(Unstaged("src/a.cs"));
        again.AttachSettings(settings);

        again.ListMode.ShouldBe(WorkingTreeListMode.Flat);
    }

    [AvaloniaFact]
    public void Ayarin_varsayilani_AGAC()
    {
        new AppSettings().Layout.CommitFileTree.ShouldBeTrue();
    }
}
