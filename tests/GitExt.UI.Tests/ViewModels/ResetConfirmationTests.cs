using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T15 — yıkıcı işlemlerin onayı ve güvenlik ağı.
/// </summary>
public class ResetConfirmationTests
{
    private static FileStatus Modified(string path) =>
        new() { Path = RepositoryPath.Parse(path), UnstagedChange = FileChangeKind.Modified };

    private static FileStatus Untracked(string path) =>
        new() { Path = RepositoryPath.Parse(path), IsUntracked = true };

    private static FileStatus Staged(string path) =>
        new() { Path = RepositoryPath.Parse(path), StagedChange = FileChangeKind.Modified };

    private sealed record Harness(
        WorkingTreeViewModel Model,
        FakeWorkingTreeWriter Writer,
        FakeConfirmer Confirmer);

    private static async Task<Harness> CreateAsync(
        ResetChangesDecision? decision,
        params FileStatus[] entries)
    {
        FakeStatusReader status = new(entries);
        FakeWorkingTreeWriter writer = new(status);
        FakeConfirmer confirmer = new(decision);

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()),
            workingTreeWriter: writer)
        {
            Confirmer = confirmer,
        };

        await model.OpenAsync("/tmp/depo");

        return new Harness(model, writer, confirmer);
    }

    [AvaloniaFact]
    public async Task Onay_verilmezse_HICBIR_SEY_yapilmaz()
    {
        // Diyalogdan iptal dönerse yıkıcı komut hiç çalışmamalı.
        Harness harness = await CreateAsync(ResetChangesDecision.Cancelled, Modified("a.txt"));

        await harness.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);

        harness.Confirmer.AskCount.ShouldBe(1);
        harness.Writer.Calls.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Onaylayici_YOKSA_islem_calismaz()
    {
        // 🔴 Onaysız yıkıcı işlem çalıştırmaktansa hiç çalıştırmamak yeğdir: onaylayıcı
        // atanmadıysa (pencere kurulmadıysa) komut sessizce hiçbir şey yapmalı.
        FakeStatusReader status = new([Modified("a.txt")]);
        FakeWorkingTreeWriter writer = new(status);

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            new FakeCommitWriter(status),
            new DiffViewModel(new FakeDiffReader()),
            workingTreeWriter: writer);

        await model.OpenAsync("/tmp/depo");
        await model.ResetChangesAsync(DiscardScope.All);

        writer.Calls.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Onaylaninca_kapsam_dogru_gecirilir()
    {
        Harness harness = await CreateAsync(
            new ResetChangesDecision { Confirmed = true },
            Modified("a.txt"));

        await harness.Model.ResetChangesAsync(DiscardScope.All);

        harness.Writer.Calls.ShouldContain(call => call.StartsWith("discard:All", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Takip_edilmeyenler_yalnizca_ISTENIRSE_silinir()
    {
        // GitExtensions'ta da ayrı bir onay kutusu: "Also delete new files and/or
        // directories". Değişiklikleri sıfırlamak, yeni dosyaları silmeyi ima etmez.
        Harness harness = await CreateAsync(
            new ResetChangesDecision { Confirmed = true, DeleteUntracked = false },
            Modified("a.txt"),
            Untracked("yeni.cs"));

        await harness.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);

        harness.Writer.Calls.ShouldNotContain(call => call.StartsWith("delete:", StringComparison.Ordinal));

        // İstendiğinde silinmeli.
        Harness second = await CreateAsync(
            new ResetChangesDecision { Confirmed = true, DeleteUntracked = true },
            Modified("a.txt"),
            Untracked("yeni.cs"));

        await second.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);

        second.Writer.Calls.ShouldContain("delete:yeni.cs");
    }

    [AvaloniaFact]
    public async Task Takip_edilmeyen_dosya_MODIFIED_listesine_KARISMAZ()
    {
        // 🔴 Karışsaydı `git restore` takip edilmeyen bir yol için düşer ve işlemin
        // tamamı hata verirdi — kullanıcı hiçbir şeyin sıfırlanmadığını görürdü.
        Harness harness = await CreateAsync(
            new ResetChangesDecision { Confirmed = true, DeleteUntracked = true },
            Modified("a.txt"),
            Untracked("yeni.cs"));

        await harness.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);

        harness.Confirmer.LastRequest!.ModifiedPaths.Select(p => p.Value).ShouldBe(["a.txt"]);
        harness.Confirmer.LastRequest.UntrackedPaths.Select(p => p.Value).ShouldBe(["yeni.cs"]);

        harness.Writer.Calls.ShouldContain("discard:UnstagedOnly:a.txt");
    }

    [AvaloniaFact]
    public async Task Tum_degisiklikler_kapsaminda_STAGE_LENMISLER_de_sayilir()
    {
        Harness harness = await CreateAsync(
            new ResetChangesDecision { Confirmed = true },
            Modified("a.txt"),
            Staged("b.txt"));

        await harness.Model.ResetChangesAsync(DiscardScope.All);

        harness.Confirmer.LastRequest!.IncludesStaged.ShouldBeTrue();
        harness.Confirmer.LastRequest.ModifiedPaths.Select(p => p.Value)
            .ShouldBe(["a.txt", "b.txt"], ignoreOrder: true);
    }

    [AvaloniaFact]
    public async Task Degisiklik_yokken_sorulmaz()
    {
        Harness harness = await CreateAsync(new ResetChangesDecision { Confirmed = true });

        await harness.Model.ResetChangesAsync(DiscardScope.All);

        harness.Confirmer.AskCount.ShouldBe(0);
        harness.Writer.Calls.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Bir_daha_sorma_sonraki_islemde_sormaz()
    {
        Harness harness = await CreateAsync(
            new ResetChangesDecision { Confirmed = true, DoNotAskAgain = true },
            Modified("a.txt"),
            Modified("b.txt"));

        await harness.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);
        harness.Confirmer.AskCount.ShouldBe(1);

        harness.Model.Unstaged.Count.ShouldBe(0);

        // İkinci bir dosya geldiğinde artık sormamalı.
        await harness.Model.OpenAsync("/tmp/depo");
        harness.Writer.Calls.Clear();

        await harness.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);

        harness.Confirmer.AskCount.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Bir_daha_sorma_GUVENLIK_AGINI_kapatmaz()
    {
        // 🔑 Onay atlanabilir, yedek atlanamaz: kullanıcı sormamayı seçtiğinde bile içerik
        // yedeklenmeli ve geri alma sunulmalı.
        Harness harness = await CreateAsync(
            new ResetChangesDecision { Confirmed = true, DoNotAskAgain = true },
            Modified("a.txt"));

        await harness.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);

        harness.Model.CanUndoReset.ShouldBeTrue();
        harness.Model.ResetNotice.ShouldNotBeNullOrEmpty();
    }

    [AvaloniaFact]
    public async Task Geri_al_yedekleri_yaziyor_ve_serit_kapaniyor()
    {
        Harness harness = await CreateAsync(
            new ResetChangesDecision { Confirmed = true, DeleteUntracked = true },
            Untracked("yeni.cs"));

        await harness.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);
        harness.Model.CanUndoReset.ShouldBeTrue();

        await harness.Model.UndoResetAsync();

        harness.Writer.Restored.ShouldBe(["yeni.cs"]);
        harness.Model.CanUndoReset.ShouldBeFalse();
        harness.Model.ResetNotice!.ShouldContain("were restored");
    }

    [AvaloniaFact]
    public async Task KISMI_kurtarma_basarili_gibi_gosterilmez()
    {
        // `gc --prune=now` yedeği anında siliyor (ölçüldü). Kurtarılamayan dosya varken
        // "geri yüklendi" demek, kullanıcıya olmayan bir sonuç vaat etmek olurdu.
        Harness harness = await CreateAsync(
            new ResetChangesDecision { Confirmed = true, DeleteUntracked = true },
            Untracked("bir.cs"),
            Untracked("iki.cs"));

        await harness.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);

        harness.Writer.PrunedBackupCount = 1;

        await harness.Model.UndoResetAsync();

        harness.Model.ResetNotice!.ShouldContain("1/2");
        harness.Model.ResetNotice!.ShouldContain("no backup");
    }

    [AvaloniaFact]
    public async Task Serit_elle_kapatilabiliyor()
    {
        Harness harness = await CreateAsync(
            new ResetChangesDecision { Confirmed = true },
            Modified("a.txt"));

        await harness.Model.ResetChangesAsync(DiscardScope.UnstagedOnly);
        harness.Model.ResetNotice.ShouldNotBeNull();

        harness.Model.ClearResetNotice();

        harness.Model.ResetNotice.ShouldBeNull();
        harness.Model.CanUndoReset.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Hook_atlama_varsayilan_olarak_KAPALI()
    {
        // Hook'ları atlamak sessiz bir varsayılan olamaz: `pre-commit` kullanıcının kendi
        // kalite kontrolü.
        Harness harness = await CreateAsync(null, Staged("a.txt"));

        harness.Model.SkipHooks.ShouldBeFalse();

        harness.Model.Message.Text = "mesaj";
        await harness.Model.CommitAsync();

        harness.Model.SkipHooks.ShouldBeFalse();
    }
}
