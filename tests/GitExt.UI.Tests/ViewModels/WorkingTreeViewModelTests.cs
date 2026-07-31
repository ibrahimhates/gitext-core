using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T09 — çalışma dizini görünümü.
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
        FakeStagingWriter Staging);

    private static async Task<Harness> CreateAsync(params FileStatus[] entries)
    {
        FakeStatusReader status = new(entries);
        FakeStagingWriter staging = new(status);

        WorkingTreeViewModel model = new(status, staging, new DiffViewModel(new FakeDiffReader()));

        await model.OpenAsync("/tmp/depo");

        return new Harness(model, status, staging);
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
        // 🔴 Plandan bilinçli sapma: plan ayrı bir "untracked" bölümü öngörüyordu.
        // GitExtensions'ta öyle bir bölüm YOK — takip edilmeyenler Unstaged listesinde.
        // Üçüncü bir liste, stage etmek için iki ayrı yere bakmayı gerektirirdi (CLAUDE.md § 9).
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
        // 🔑 Bu ekranın asıl kullanım biçimi: dosyaları sırayla gözden geçirip stage'lemek.
        // Seçim listenin başına fırlarsa kullanıcı her dosyada elle geri gitmek zorunda kalır.
        Harness harness = await CreateAsync(Unstaged("a.txt"), Unstaged("b.txt"), Unstaged("c.txt"));

        harness.Model.SelectedUnstagedIndex = 1;
        await harness.Model.StageSelectedAsync();

        // "b.txt" gitti; aynı indeks artık "c.txt".
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
        // ⚠️ Atlamak cazip ama tehlikeli: son dosyasını stage'leyen kullanıcının `Space`
        // tuşu bu kez az önce stage'lediği dosyayı GERİ ALIRDI.
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
        // ⚠️ Yolsuz `git add -A --` deponun tamamını stage'lerdi (P05-T03'teki koruma).
        Harness harness = await CreateAsync(Staged("a.txt"));

        await harness.Model.StageAllAsync();

        harness.Staging.Calls.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Secim_hangi_listedeyse_diff_o_tarafi_gosterir()
    {
        Harness harness = await CreateAsync(Unstaged("a.txt"), Staged("b.txt"));

        // Etkin listeyi görünümde ODAK belirliyor (`GotFocus`); ViewModel tarafında bunun
        // karşılığı `IsStagedListActive`. Seçim indeksi tek başına yetmez: iki listede aynı
        // anda seçim durabiliyor ve zaten seçili bir satıra tıklamak indeksi değiştirmiyor.
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
        // Dosyalar zaten solda iki liste hâlinde; ikinci bir liste "seçim hangisinden?"
        // sorusunu doğururdu.
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
            status, new FakeStagingWriter(status), new DiffViewModel(new FakeDiffReader()));

        await model.OpenAsync("/tmp/depo");

        model.ErrorMessage.ShouldNotBeNull();
        model.ErrorDetails.ShouldNotBeNull().Output.ShouldContain("fatal: bozuk index");
    }
}
