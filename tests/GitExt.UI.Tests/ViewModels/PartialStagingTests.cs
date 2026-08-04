using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T10 — diff panelinden hunk ve satır seçimiyle stage/unstage.
/// </summary>
/// <remarks>
/// Yama üretiminin kendisi çekirdekte ve P05-T04/T05'te gerçek <c>git</c>'e karşı test
/// edildi. Buradaki soru farklı: <b>ekrandaki seçim doğru satırlara mı çevriliyor?</b>
/// P05-T04'te ölçülen tek yakalanmayan hata sınıfı buydu — yama geçerli olduğu için git
/// kabul ediyor ve içerik <b>sessizce</b> yanlış oluyor.
/// </remarks>
public class PartialStagingTests
{
    /// <summary>Seçimi kaydeden sahte host.</summary>
    private sealed class RecordingHost : IPartialStagingHost
    {
        public bool CanStage { get; set; } = true;

        public bool CanUnstage { get; set; }

        public PatchSelection? LastSelection { get; private set; }

        public bool? LastWasStage { get; private set; }

        public int CallCount { get; private set; }

        public Exception? Failure { get; set; }

        public Task ApplyAsync(FileDiff diff, PatchSelection selection, bool stage)
        {
            CallCount++;
            LastSelection = selection;
            LastWasStage = stage;

            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        /// <summary>Kaç kez yıkıcı geri alma istendi (P05-T15)?</summary>
        public int DiscardCount { get; private set; }

        public Task DiscardAsync(FileDiff diff, PatchSelection selection)
        {
            DiscardCount++;
            LastSelection = selection;

            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private static FileDiff TwoHunks()
    {
        DiffHunk first = new()
        {
            Header = "@@ -1,3 +1,3 @@",
            OldStart = 1,
            OldLength = 3,
            NewStart = 1,
            NewLength = 3,
            Lines =
            [
                new DiffLine(DiffLineKind.Context, "bir") { OldLineNumber = 1, NewLineNumber = 1 },
                new DiffLine(DiffLineKind.Removed, "iki eski") { OldLineNumber = 2 },
                new DiffLine(DiffLineKind.Added, "iki yeni") { NewLineNumber = 2 },
            ],
        };

        DiffHunk second = new()
        {
            Header = "@@ -10,2 +10,2 @@",
            OldStart = 10,
            OldLength = 2,
            NewStart = 10,
            NewLength = 2,
            Lines =
            [
                new DiffLine(DiffLineKind.Context, "on") { OldLineNumber = 10, NewLineNumber = 10 },
                new DiffLine(DiffLineKind.Added, "on bir") { NewLineNumber = 11 },
            ],
        };

        return new FileDiff
        {
            Path = RepositoryPath.Parse("a.cs"),
            Change = FileChangeKind.Modified,
            Hunks = [first, second],
        };
    }

    private static async Task<(DiffViewModel Model, RecordingHost Host)> LoadedAsync()
    {
        DiffViewModel model = new(new FakeDiffReader([TwoHunks()]));
        RecordingHost host = new();

        model.StagingHost = host;

        await model.ShowWorkingTreeAsync("/tmp/depo", staged: false);

        return (model, host);
    }

    // Satır düzeni: 0 = @@ birinci, 1 = "bir", 2 = "iki eski", 3 = "iki yeni",
    //               4 = @@ ikinci,  5 = "on",  6 = "on bir"

    [AvaloniaFact]
    public async Task Hunk_basligi_secilince_o_hunkun_TAMAMI_secilir()
    {
        // Ayrı bir "bu hunk'ı stage'le" komutu YOK — GitExtensions'ta da yok. Başlık satırı
        // zaten "bu hunk" demenin doğal yolu.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([0]);

        host.CallCount.ShouldBe(1);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(2);
        selection.IsSelected(0, 1).ShouldBeTrue();
        selection.IsSelected(0, 2).ShouldBeTrue();

        // İkinci hunk'a DOKUNULMAMALI.
        selection.IsSelected(1, 1).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Tek_satir_secilince_yalnizca_o_satir_secilir()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([3]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(1);
        selection.IsSelected(0, 2).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task BAGLAM_satiri_secilse_bile_secime_girmez()
    {
        // 🔴 P05-T04'te ölçülen sessiz hata sınıfı: bağlam satırı yamaya kendiliğinden
        // giriyor. "Seçildi" saymak, kullanıcının seçmediği bir değişikliği de almak olurdu.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([1]);

        host.CallCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Iki_hunktan_secilen_satirlar_birlikte_gonderilir()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([2, 6]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(2);
        selection.IsSelected(0, 1).ShouldBeTrue();
        selection.IsSelected(1, 1).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Hic_secim_yoksa_ve_duraklanan_satir_da_yoksa_HICBIR_SEY_yapilmaz()
    {
        // ⚠️ "Hiçbir şey seçmeden stage'le" sessizce TÜM dosyayı stage'lemek olurdu.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([]);

        host.CallCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Secim_yoksa_DURAKLANAN_satir_kullanilir()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.GoToNextChange().ShouldBeTrue();

        await model.StageSelectionAsync([]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(1);
        selection.IsSelected(0, 1).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Stage_ve_unstage_yonu_hosta_dogru_gecer()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        await model.StageSelectionAsync([2]);
        host.LastWasStage.ShouldBe(true);

        await model.UnstageSelectionAsync([2]);
        host.LastWasStage.ShouldBe(false);
    }

    [AvaloniaFact]
    public async Task Host_yoksa_komutlar_KAPALI()
    {
        // Commit geçmişinde ve karşılaştırma penceresinde staging anlamsız.
        DiffViewModel model = new(new FakeDiffReader([TwoHunks()]));

        await model.ShowCommitAsync("/tmp/depo", CommitId.Parse(new string('a', 40)));

        model.CanStageSelection.ShouldBeFalse();
        model.CanUnstageSelection.ShouldBeFalse();

        await model.StageSelectionAsync([0]);
    }

    [AvaloniaFact]
    public async Task Stage_ve_unstage_BIRBIRINI_dislar()
    {
        // GitExtensions'ta da öyle: stage yalnızca çalışma ağacı tarafında, unstage yalnızca
        // index tarafında görünüyor.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.CanStageSelection.ShouldBeTrue();
        model.CanUnstageSelection.ShouldBeFalse();

        host.CanStage = false;
        host.CanUnstage = true;
        model.NotifyStagingAvailabilityChanged();

        model.CanStageSelection.ShouldBeFalse();
        model.CanUnstageSelection.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_secili_CIFTIN_IKI_TARAFI_da_secime_girer()
    {
        // 🔴 Bir yan yana satır İKİ farklı unified satırı taşıyor: solda silinen, sağda onun
        // yerine eklenen. Yalnızca birini almak, kullanıcının gördüğü çiftin YARISINI
        // stage'lemek olurdu — yama geçerli çıkar ve içerik sessizce yanlış olur.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.ShowSideBySide = true;
        model.CanStageSelection.ShouldBeTrue();

        // Yan yana düzen: 0 = @@, 1 = "bir" (bağlam), 2 = "iki eski" ↔ "iki yeni".
        model.SideLines[2].Left.RawText.ShouldBe("iki eski");
        model.SideLines[2].Right.RawText.ShouldBe("iki yeni");

        await model.StageSelectionAsync([2]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(2);
        selection.IsSelected(0, 1).ShouldBeTrue();
        selection.IsSelected(0, 2).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_hunk_basligi_TUM_hunku_secer()
    {
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.ShowSideBySide = true;

        await model.StageSelectionAsync([0]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(2);
        selection.IsSelected(1, 1).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_DOLGU_satiri_secime_girmez()
    {
        // Dolgu "burada satır yok" demek; karşılığı olmayan bir şeyi stage'lemek anlamsız.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        model.ShowSideBySide = true;

        // İkinci hunk: solda karşılığı olmayan tek eklenen satır.
        int filler = model.SideLines
            .Select((row, index) => (row, index))
            .First(pair => pair.row.Left.IsFiller && !pair.row.IsHunkHeader)
            .index;

        await model.StageSelectionAsync([filler]);

        PatchSelection selection = host.LastSelection.ShouldNotBeNull();
        selection.Count.ShouldBe(1);
        selection.IsSelected(1, 1).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Git_reddederse_mesaj_kullaniciya_ULASIR()
    {
        // `git apply` sayı/bağlam hatalarını reddediyor (P05-T04). Sessiz kalmak
        // "tıkladım ama bir şey olmadı" durumu üretirdi.
        (DiffViewModel model, RecordingHost host) = await LoadedAsync();

        host.Failure = new GitExt.Core.Git.GitException(
            GitExt.Core.Git.GitFailureKind.Unknown,
            "Git komutu başarısız oldu.",
            "git apply --cached -",
            exitCode: 1,
            standardError: "error: corrupt patch at line 5");

        await model.StageSelectionAsync([2]);

        model.ErrorMessage.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void Sifirlama_YALNIZCA_calisma_agaci_tarafinda_kullanilabilir()
    {
        // P05-T15. Index tarafında "sıfırla" zaten *unstage* demek olurdu; iki komutun
        // aynı şeyi yapması kullanıcıya hangisinin ne yaptığını sordururdu.
        DiffViewModel model = new(new FakeDiffReader());

        // Host yokken hiçbir eylem kullanılamaz.
        model.CanDiscardSelection.ShouldBeFalse();

        RecordingHost worktreeSide = new() { CanStage = true, CanUnstage = false };
        model.StagingHost = worktreeSide;
        model.CanDiscardSelection.ShouldBeTrue();

        RecordingHost indexSide = new() { CanStage = false, CanUnstage = true };
        model.StagingHost = indexSide;
        model.CanDiscardSelection.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Kodlama_okuma_ve_yazma_arasinda_TASINIYOR()
    {
        // 🔴 P05-T16'da gerçek depoda ölçülen kusur: diff UTF-8 varsayılanıyla okunup yama
        // UTF-8 ile yazılınca Latin-5 bir dosyada `git apply` yamayı REDDEDİYOR
        // (`patch does not apply`). Halkalardan biri koptuğunda özellik çalışmıyor.
        FakeStatusReader status = new([
            new FileStatus
            {
                Path = RepositoryPath.Parse("tr.txt"),
                UnstagedChange = FileChangeKind.Modified,
            },
        ]);

        FakeStagingWriter staging = new(status);
        System.Text.Encoding latin5 = System.Text.Encoding.Latin1;

        WorkingTreeViewModel model = new(
            status,
            staging,
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()));

        model.Diff.ContentEncoding = latin5;

        await model.OpenAsync("/tmp/depo");

        FileDiff diff = TwoHunks();

        await ((IPartialStagingHost)model).ApplyAsync(
            diff, PatchSelection.Hunks(diff, 0), stage: true);

        staging.LastPartialEncoding.ShouldBeSameAs(latin5);
    }

    [AvaloniaFact]
    public async Task Ikili_dosyada_kismi_stage_COKMEZ_ve_hicbir_sey_yapmaz()
    {
        // P05-T16'da ölçüm programı tam burada çöktü: ikili dosyada hunk yok, `Hunks[0]`
        // patlıyor. Arayüzde satır seçimi de olamayacağı için komut sessizce hiçbir şey
        // yapmalı — ama çökmemeli.
        FileDiff binary = new()
        {
            Path = RepositoryPath.Parse("resim.png"),
            Change = FileChangeKind.Modified,
            IsBinary = true,
            Hunks = [],
        };

        RecordingHost host = new();
        DiffViewModel model = new(new FakeDiffReader([binary]))
        {
            StagingHost = host,
        };

        await model.ShowWorkingTreeAsync("/tmp/depo", staged: false);

        await Should.NotThrowAsync(() => model.StageSelectionAsync());
        await Should.NotThrowAsync(() => model.DiscardSelectionAsync());

        host.CallCount.ShouldBe(0);
        host.DiscardCount.ShouldBe(0);
    }
}
