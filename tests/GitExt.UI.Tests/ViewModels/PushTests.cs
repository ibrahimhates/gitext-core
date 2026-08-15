using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T08 — Push ekranı (ViewModel tarafı).
/// </summary>
public class PushTests
{
    private const string Path = "/depo";

    private static (PushViewModel Model, FakePushWriter Push) Create()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });
        remotes.Remotes.Add(new GitRemote { Name = "upstream", FetchUrls = ["https://example.com/b.git"] });

        FakePushWriter push = new();

        return (new PushViewModel(remotes, push), push);
    }

    private static IReadOnlyList<BranchInfo> Branches() =>
    [
        FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true) with
        {
            Upstream = "origin/main",
            Tracking = new UpstreamTracking(2, 0, IsGone: false),
        },
        FakeGitData.LocalBranch("ozellik", FakeGitData.Sha(2)),
    ];

    private static Task LoadAsync(PushViewModel model) => model.LoadAsync(Path, "main", Branches());

    [AvaloniaFact]
    public async Task Ekran_mevcut_dal_ve_origin_ile_aciliyor()
    {
        (PushViewModel model, _) = Create();

        await LoadAsync(model);

        model.SelectedRemote.ShouldBe("origin");
        model.SourceBranch.ShouldBe("main");
        model.RemoteBranch.ShouldBe("main");
        model.Tab.ShouldBe(PushTab.Branch);
    }

    [AvaloniaFact]
    public async Task Komut_onizlemesi_secimlerle_birlikte_degisiyor()
    {
        (PushViewModel model, _) = Create();

        await LoadAsync(model);

        model.CommandPreview.ShouldBe("git push --porcelain -- origin main:main");

        model.ForceWithLease = true;
        model.CommandPreview.ShouldBe(
            "git push --porcelain --force-with-lease=main:aaaabbbbcccc -- origin main:main");

        model.SetUpstream = true;
        model.CommandPreview.ShouldContain("--set-upstream");
    }

    [AvaloniaFact]
    public async Task Upstream_i_olmayan_dalda_kutu_KENDILIGINDEN_isaretli()
    {
        // Ölçüldü: upstream'i olmayan dalda çıplak `git push` çalışmıyor (çıkış kodu 128).
        (PushViewModel model, FakePushWriter push) = Create();

        push.Plan = push.Plan with { HasUpstream = false, RemoteTipObjectId = null, RemoteBranches = [] };

        await LoadAsync(model);

        model.SetUpstream.ShouldBeTrue();
        model.WouldCreateRemoteBranch.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task KIRA_cipasi_komuta_ACIK_olarak_yaziliyor()
    {
        // 🔴 Bu testin sabitlediği şey: bayrak asla çıplak yazılmıyor. Çıplak hâli, araya
        // giren bir fetch'ten sonra korumayı bırakıyor (Core'daki karşı-kanıt testi).
        (PushViewModel model, FakePushWriter push) = Create();

        await LoadAsync(model);
        model.ForceWithLease = true;
        await model.RunCommand.ExecuteAsync(null);

        PushOptions options = push.Pushed.Single();
        options.ForceWithLease.ShouldBeTrue();
        options.Refs.Single().ExpectedRemoteObjectId.ShouldBe("aaaabbbbcccc");
    }

    [AvaloniaFact]
    public async Task Hedef_adi_degisince_CIPA_dusuyor()
    {
        // Çıpa "main"in ucu; kullanıcı hedefi "baska" yaparsa o çıpa artık başka bir dalın
        // ucu olurdu — yanlış dalın kirasıyla zorlamak sessiz bir felaket olurdu.
        (PushViewModel model, FakePushWriter push) = Create();

        await LoadAsync(model);
        model.ForceWithLease = true;
        model.RemoteBranch = "baska";
        await model.RunCommand.ExecuteAsync(null);

        push.Pushed.Single().Refs.Single().ExpectedRemoteObjectId.ShouldBeNull();
        model.CommandPreview.ShouldNotContain("--force-with-lease");
    }

    [AvaloniaFact]
    public async Task Kira_bilgisi_kullaniciya_YAZILI()
    {
        (PushViewModel model, _) = Create();

        await LoadAsync(model);

        model.HasLeaseNotice.ShouldBeFalse();

        model.ForceWithLease = true;
        model.HasLeaseNotice.ShouldBeTrue();
        model.LeaseNotice!.ShouldContain("aaaabbbbcc");
    }

    [AvaloniaFact]
    public async Task Ciplak_zorlama_SUNULMUYOR_ve_nedeni_yazili()
    {
        (PushViewModel model, _) = Create();

        await LoadAsync(model);
        model.ForceWithLease = true;

        model.CommandPreview.ShouldNotContain("--force ");
        model.CommandPreview.ShouldNotEndWith("--force");
        PushViewModel.ForceDisabledReason.ShouldContain("Kirayla zorla");
    }

    // ---------------------------------------------------------------- sekmeler

    [AvaloniaFact]
    public async Task Etiket_sekmesi_tek_etiket_gonderiyor()
    {
        (PushViewModel model, FakePushWriter push) = Create();

        push.Plan = push.Plan with { Tags = ["v1", "v2"] };

        await LoadAsync(model);
        model.Tab = PushTab.Tag;
        model.SelectedTag = "v2";
        await model.RunCommand.ExecuteAsync(null);

        PushOptions options = push.Pushed.Single();
        options.Refs.Single().Source.ShouldBe("refs/tags/v2");
        options.Tags.ShouldBe(PushTagMode.None);
    }

    [AvaloniaFact]
    public async Task Tum_etiketler_secilince_refspec_YERINE_bayrak()
    {
        (PushViewModel model, FakePushWriter push) = Create();

        push.Plan = push.Plan with { Tags = ["v1"] };

        await LoadAsync(model);
        model.Tab = PushTab.Tag;
        model.AllTags = true;
        await model.RunCommand.ExecuteAsync(null);

        PushOptions options = push.Pushed.Single();
        options.Tags.ShouldBe(PushTagMode.All);
        options.Refs.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Coklu_sekmede_gonder_ve_sil_AYNI_satirda_secilemiyor()
    {
        (PushViewModel model, _) = Create();

        await LoadAsync(model);

        PushBranchRowViewModel row = model.Rows.First(item => item.LocalBranch == "main");

        row.Push = true;
        row.Delete = true;

        row.Push.ShouldBeFalse("silme seçilince gönderme düşmeli");

        row.Push = true;
        row.Delete.ShouldBeFalse("gönderme seçilince silme düşmeli");
    }

    [AvaloniaFact]
    public async Task Uzakta_OLMAYAN_dal_silinemiyor()
    {
        (PushViewModel model, _) = Create();

        await LoadAsync(model);

        model.Rows.First(row => row.LocalBranch == "main").CanDelete.ShouldBeTrue();
        model.Rows.First(row => row.LocalBranch == "ozellik").CanDelete.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Coklu_sekmede_silme_DELETE_refspec_i_uretiyor()
    {
        (PushViewModel model, FakePushWriter push) = Create();

        await LoadAsync(model);
        model.Tab = PushTab.MultipleBranches;
        model.Rows.First(row => row.LocalBranch == "main").Delete = true;
        await model.RunCommand.ExecuteAsync(null);

        PushSpec spec = push.Pushed.Single().Refs.Single();
        spec.Delete.ShouldBeTrue();
        spec.Destination.ShouldBe("main");
    }

    [AvaloniaFact]
    public async Task Coklu_sekmede_hicbir_sey_secilmeden_CALISTIRILAMAZ()
    {
        (PushViewModel model, _) = Create();

        await LoadAsync(model);
        model.Tab = PushTab.MultipleBranches;

        model.CanRun.ShouldBeFalse();

        model.Rows[0].Push = true;
        model.CanRun.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Ileri_geri_sayaci_satirda_gosteriliyor()
    {
        (PushViewModel model, _) = Create();

        await LoadAsync(model);

        model.Rows.First(row => row.LocalBranch == "main").AheadBehind.ShouldBe("↑2 ↓0");
        model.Rows.First(row => row.LocalBranch == "ozellik").AheadBehind.ShouldBe("takip yok");
    }

    // ----------------------------------------------------------------- sonuç

    [AvaloniaFact]
    public async Task KISMI_basari_hem_gideni_hem_reddedileni_soyluyor()
    {
        // 🔴 "Push başarısız" demek, gerçekten gitmiş bir dalı kullanıcıdan gizlerdi.
        (PushViewModel model, FakePushWriter push) = Create();

        push.Result = new PushResult
        {
            Refs =
            [
                new PushRefResult(' ', "refs/heads/main", "refs/heads/main", "aaa..bbb", null),
                new PushRefResult('!', "refs/heads/dev", "refs/heads/dev", "[rejected]", "fetch first"),
            ],
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasWarning.ShouldBeTrue();
        model.Warning!.ShouldContain("Some were pushed");
        model.Warning!.ShouldContain("dev");
        model.Notice!.ShouldContain("main");
    }

    [AvaloniaFact]
    public async Task BAYAT_kira_reddi_ayri_bir_ONERIYLE_anlatiliyor()
    {
        // Geride kalmakla kiranın tutmaması farklı şeyler: ilkinde "önce çek", ikincisinde
        // "ekranı açtığından beri değişti" demek gerekiyor.
        (PushViewModel model, FakePushWriter push) = Create();

        push.Result = new PushResult
        {
            Refs = [new PushRefResult('!', "refs/heads/main", "refs/heads/main", "[rejected]", "stale info")],
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasAdvice.ShouldBeTrue();
        model.Advice!.ShouldContain("changed since you opened this screen");
        model.Advice!.ShouldNotContain("Pull/Fetch ile getirin");
    }

    [AvaloniaFact]
    public async Task Uzak_kancanin_SEBEBI_kullaniciya_tasiniyor()
    {
        (PushViewModel model, FakePushWriter push) = Create();

        push.Result = new PushResult
        {
            Refs =
            [
                new PushRefResult(
                    '!', "refs/heads/main", "refs/heads/main",
                    "[remote rejected]", "pre-receive hook declined"),
            ],
            RemoteMessages = ["korumali dal: main"],
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.Advice!.ShouldContain("korumali dal: main");
    }

    [AvaloniaFact]
    public async Task Depo_acikken_MENU_komutu_etkin_ve_ekran_DOLU_aciliyor()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushPrompt prompt = new();

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(FakeGitData.Refs(
                    localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(2), isCurrent: true)],
                    remoteBranches: [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(2))])),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            remoteReader: remotes,
            pushWriter: new FakePushWriter())
        {
            PushPrompt = prompt,
        };

        model.CanPush.ShouldBeFalse("depo açılmadan etkin olmamalı");

        await model.OpenRepositoryAsync("/depo");

        model.CanPush.ShouldBeTrue();
        await model.PushCommand.ExecuteAsync(null);

        prompt.Shown.ShouldNotBeNull();
        prompt.Shown!.Remotes.ShouldBe(["origin"]);
        prompt.Shown.SourceBranches.ShouldBe(["main"]);
        prompt.Shown.CurrentBranch.ShouldBe("main");
    }
}
