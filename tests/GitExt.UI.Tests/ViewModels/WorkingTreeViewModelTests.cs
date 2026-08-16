using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T09 — the working directory view.
/// </summary>
public class WorkingTreeViewModelTests
{
    private static FileStatus Unstaged(string path) =>
        new() { Path = RepositoryPath.Parse(path), UnstagedChange = FileChangeKind.Modified };

    private static FileStatus Staged(string path) =>
        new() { Path = RepositoryPath.Parse(path), StagedChange = FileChangeKind.Modified };

    private static FileStatus Untracked(string path) =>
        new() { Path = RepositoryPath.Parse(path), IsUntracked = true };

    private sealed record Harness(
        WorkingTreeViewModel Model,
        FakeStatusReader Status,
        FakeStagingWriter Staging,
        FakeCommitWriter Commits);

    private static async Task<Harness> CreateAsync(params FileStatus[] entries)
    {
        FakeStatusReader status = new(entries);
        FakeStagingWriter staging = new(status);
        FakeCommitWriter commits = new(status);

        WorkingTreeViewModel model = new(
            status, staging, commits, new DiffViewModel(new FakeDiffReader()));

        await model.OpenAsync("/tmp/depo");

        return new Harness(model, status, staging, commits);
    }

    [AvaloniaFact]
    public async Task Listeler_stage_durumuna_gore_ayrisir()
    {
        Harness harness = await CreateAsync(Unstaged("a.txt"), Staged("b.txt"));

        harness.Model.Unstaged.Select(r => r.Path.Value).ShouldBe(["a.txt"]);
        harness.Model.Staged.Select(r => r.Path.Value).ShouldBe(["b.txt"]);
    }

    [AvaloniaFact]
    public async Task Takip_edilmeyenler_UNSTAGED_listesinde_durur()
    {
        // 🔴 A deliberate divergence from the plan: the plan called for a separate "untracked" section.
        // GitExtensions has NO such section — untracked files sit in the Unstaged list.
        // A third list would require looking in two separate places to stage (CLAUDE.md § 9).
        Harness harness = await CreateAsync(Unstaged("a.txt"), Untracked("yeni.txt"));

        harness.Model.Unstaged.Select(r => r.Path.Value).ShouldBe(["a.txt", "yeni.txt"]);
        harness.Model.Unstaged.Single(r => r.IsUntracked).StatusLetter.ShouldBe("?");
    }

    [AvaloniaFact]
    public async Task Stage_edilen_dosya_karsi_listeye_gecer()
    {
        Harness harness = await CreateAsync(Unstaged("a.txt"), Unstaged("b.txt"));

        harness.Model.SelectedUnstagedIndex = 0;
        await harness.Model.StageSelectedAsync();

        harness.Model.Unstaged.Select(r => r.Path.Value).ShouldBe(["b.txt"]);
        harness.Model.Staged.Select(r => r.Path.Value).ShouldBe(["a.txt"]);
    }

    [AvaloniaFact]
    public async Task Stage_sonrasi_secim_SIRADAKI_dosyaya_kayar()
    {
        // 🔑 The way this screen is actually used: going through the files in order and staging them.
        // If the selection jumps to the top of the list, the user has to go back by hand for every file.
        Harness harness = await CreateAsync(Unstaged("a.txt"), Unstaged("b.txt"), Unstaged("c.txt"));

        harness.Model.SelectedUnstagedIndex = 1;
        await harness.Model.StageSelectedAsync();

        // "b.txt" is gone; the same index is now "c.txt".
        harness.Model.SelectedUnstagedIndex.ShouldBe(1);
        harness.Model.Unstaged[harness.Model.SelectedUnstagedIndex].Path.Value.ShouldBe("c.txt");
    }

    [AvaloniaFact]
    public async Task Son_dosya_stage_edilince_secim_bir_yukari_ceklir()
    {
        Harness harness = await CreateAsync(Unstaged("a.txt"), Unstaged("b.txt"));

        harness.Model.SelectedUnstagedIndex = 1;
        await harness.Model.StageSelectedAsync();

        harness.Model.SelectedUnstagedIndex.ShouldBe(0);
        harness.Model.Unstaged[0].Path.Value.ShouldBe("a.txt");
    }

    [AvaloniaFact]
    public async Task Bosalan_listede_KARSI_tarafa_atlanmaz()
    {
        // ⚠️ Tempting but dangerous to skip: for a user staging their last file, the `Space` key would
        // this time UNSTAGE the file they just staged.
        Harness harness = await CreateAsync(Unstaged("a.txt"));

        harness.Model.SelectedUnstagedIndex = 0;
        await harness.Model.StageSelectedAsync();

        harness.Model.Unstaged.ShouldBeEmpty();
        harness.Model.SelectedUnstagedIndex.ShouldBe(-1);
        harness.Model.SelectedRow.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Unstage_dosyayi_geri_alir()
    {
        Harness harness = await CreateAsync(Staged("a.txt"));

        harness.Model.SelectedStagedIndex = 0;
        await harness.Model.UnstageSelectedAsync();

        harness.Model.Staged.ShouldBeEmpty();
        harness.Model.Unstaged.Select(r => r.Path.Value).ShouldBe(["a.txt"]);
    }

    [AvaloniaFact]
    public async Task Tumunu_stage_le_hepsini_tasir()
    {
        Harness harness = await CreateAsync(Unstaged("a.txt"), Untracked("b.txt"));

        await harness.Model.StageAllAsync();

        harness.Model.Unstaged.ShouldBeEmpty();
        harness.Model.Staged.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task Bos_listede_tumunu_stage_le_GIT_CALISTIRMAZ()
    {
        // ⚠️ A pathless `git add -A --` would stage the whole repository (the guard from P05-T03).
        Harness harness = await CreateAsync(Staged("a.txt"));

        await harness.Model.StageAllAsync();

        harness.Staging.Calls.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Secim_hangi_listedeyse_diff_o_tarafi_gosterir()
    {
        Harness harness = await CreateAsync(Unstaged("a.txt"), Staged("b.txt"));

        // The active list is determined in the view by FOCUS (`GotFocus`); its counterpart on the
        // ViewModel side is `IsStagedListActive`. The selection index alone is not enough: both lists can
        // hold a selection at once, and clicking an already-selected row does not change the index.
        harness.Model.IsStagedListActive = false;
        harness.Model.SelectedUnstagedIndex = 0;
        harness.Model.SelectedRow!.IsStagedSide.ShouldBeFalse();

        harness.Model.IsStagedListActive = true;
        harness.Model.SelectedStagedIndex = 0;
        harness.Model.SelectedRow!.IsStagedSide.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Diff_bileseninin_kendi_dosya_listesi_GIZLENIR()
    {
        // The files are already in two lists on the left; a second list would raise the question "which
        // one is the selection from?".
        Harness harness = await CreateAsync(Unstaged("a.txt"));

        harness.Model.Diff.ShowFileList.ShouldBeFalse();
        harness.Model.Diff.ShowFlatFileList.ShouldBeFalse();
        harness.Model.Diff.ShowTreeFileList.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Degisiklik_yoksa_temiz_bildirilir()
    {
        Harness harness = await CreateAsync();

        harness.Model.IsClean.ShouldBeTrue();
        harness.Model.HasStagedChanges.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Durum_okuma_hatasi_ayrintisiyla_bildirilir()
    {
        FakeStatusReader status = new(
            entries: null,
            failure: new GitExt.Core.Git.GitException(
                GitExt.Core.Git.GitFailureKind.Unknown,
                "Git komutu başarısız oldu.",
                "git status",
                exitCode: 128,
                standardError: "fatal: bozuk index"));

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()));

        await model.OpenAsync("/tmp/depo");

        model.ErrorMessage.ShouldNotBeNull();
        model.ErrorDetails.ShouldNotBeNull().Output.ShouldContain("fatal: bozuk index");
    }
}
