using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T06 + P06-T07 — Pull/Fetch ekranı (ViewModel tarafı).
/// </summary>
public class PullTests
{
    private const string Path = "/depo";

    private static (PullViewModel Model, FakeFetchWriter Fetch, FakePullWriter Pull) Create(
        ResolvedPullStrategy? configured = null)
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });
        remotes.Remotes.Add(new GitRemote { Name = "upstream", FetchUrls = ["https://example.com/b.git"] });

        FakeFetchWriter fetch = new();
        FakePullWriter pull = new();

        if (configured is not null)
        {
            pull.Configured = configured;
        }

        return (new PullViewModel(remotes, fetch, pull), fetch, pull);
    }

    /// <remarks>
    /// ⚠️ Sembolik <c>origin/HEAD</c> bilerek listede: git onu <c>origin/HEAD</c> değil
    /// <b><c>origin</c></b> diye kısaltıyor (P03-T12'de ölçülmüş) ve sahte veri bunu
    /// yansıtıyor — süzme yanlışsa test kırmızı olmalı.
    /// </remarks>
    private static IReadOnlyList<GitRef> Branches() =>
    [
        FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1)).Ref,
        FakeGitData.RemoteBranch("origin/dev", FakeGitData.Sha(2)).Ref,
        FakeGitData.SymbolicRemoteHead("origin", "refs/remotes/origin/main", FakeGitData.Sha(1)).Ref,
        FakeGitData.RemoteBranch("upstream/main", FakeGitData.Sha(3)).Ref,
    ];

    private static Task LoadAsync(PullViewModel model) =>
        model.LoadAsync(Path, "main", Branches());

    [AvaloniaFact]
    public async Task Ekran_kullanicinin_AYARIYLA_aciliyor_ve_sebebi_yazili()
    {
        // 🔑 Ayarı rebase olan kullanıcıya merge seçili bir ekran açmak, ona sessizce
        // beklemediği şeyi yaptırırdı.
        (PullViewModel model, _, _) = Create(
            new ResolvedPullStrategy(PullStrategy.Rebase, PullStrategySource.BranchSetting, "true"));

        await LoadAsync(model);

        model.Action.ShouldBe(PullAction.Rebase);
        model.IsRebase.ShouldBeTrue();
        model.StrategyNotice.ShouldNotBeNull();
        model.StrategyNotice!.ShouldContain("branch.main.rebase");
    }

    [AvaloniaFact]
    public async Task Ayar_yokken_birlestirme_ve_bu_da_YAZILI()
    {
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);

        model.Action.ShouldBe(PullAction.Merge);
        model.StrategyNotice!.ShouldContain("varsayılan");
    }

    [AvaloniaFact]
    public async Task Komut_onizlemesi_secimlerle_birlikte_degisiyor()
    {
        // "Komutu göster" ilkesi: metin gerçek seçimlerden üretiliyor.
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);

        model.CommandPreview.ShouldBe("git pull --no-rebase origin main");

        model.Action = PullAction.Rebase;
        model.AutoStash = true;
        model.CommandPreview.ShouldBe("git pull --rebase --autostash origin main");

        model.Prune = true;
        model.PruneTags = true;
        model.Tags = FetchTagMode.None;
        model.CommandPreview.ShouldBe(
            "git pull --rebase --autostash --prune --prune-tags --no-tags origin main");

        model.Action = PullAction.FetchOnly;
        model.CommandPreview.ShouldBe("git fetch --prune --prune-tags --no-tags origin");
    }

    [AvaloniaFact]
    public async Task Uzak_dal_listesi_secili_remote_a_gore_SUZULUYOR()
    {
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);

        model.SelectedRemote.ShouldBe("origin");

        // `origin/HEAD` sembolik — listede olmamalı (P03-T12 ve P06-T06'nın dersi).
        model.RemoteBranches.ShouldBe(["main", "dev"]);
        model.SelectedBranch.ShouldBe("main");

        model.SelectedRemote = "upstream";
        model.RemoteBranches.ShouldBe(["main"]);
    }

    [AvaloniaFact]
    public async Task PruneTags_yalnizca_Prune_isaretliyken_ACIK()
    {
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);

        model.CanPruneTags.ShouldBeFalse();

        model.Prune = true;
        model.CanPruneTags.ShouldBeTrue();
        model.PruneTags = true;

        // Prune kapatılınca alt seçenek de kapanmalı: git `--prune-tags`i tek başına
        // kabul etmiyor.
        model.Prune = false;
        model.PruneTags.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Yalnizca_getir_secildiginde_FETCH_calisiyor_pull_DEGIL()
    {
        (PullViewModel model, FakeFetchWriter fetch, FakePullWriter pull) = Create();

        await LoadAsync(model);

        model.Action = PullAction.FetchOnly;
        model.Prune = true;
        await model.RunCommand.ExecuteAsync(null);

        pull.Pulled.ShouldBeEmpty();
        fetch.Fetched.Single().Remote.ShouldBe("origin");
        fetch.Fetched.Single().Prune.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Fetch_kismi_basarisizligi_UYARI_olarak_gosteriliyor()
    {
        // 🔴 `--all`'da bir remote bozukken diğerleri geliyor; sessiz kalmak olmaz.
        (PullViewModel model, FakeFetchWriter fetch, _) = Create();

        fetch.Result = new FetchResult
        {
            Changes = [new RefChange("refs/remotes/origin/main", "a", "b", RefChangeKind.Updated)],
            Failures = [new FetchFailure("bozuk", "Uzak depoya ulaşılamadı.")],
        };

        await LoadAsync(model);
        model.Action = PullAction.FetchOnly;
        await model.RunCommand.ExecuteAsync(null);

        model.HasWarning.ShouldBeTrue();
        model.Warning!.ShouldContain("bozuk");
        model.Notice!.ShouldContain("güncellendi");
    }

    [AvaloniaFact]
    public async Task Pull_secimleri_oldugu_gibi_gecirilyor()
    {
        (PullViewModel model, _, FakePullWriter pull) = Create();

        await LoadAsync(model);

        model.Action = PullAction.Rebase;
        model.AutoStash = true;
        model.SelectedBranch = "dev";
        await model.RunCommand.ExecuteAsync(null);

        PullOptions options = pull.Pulled.Single();
        options.Strategy.ShouldBe(PullStrategy.Rebase);
        options.AutoStash.ShouldBeTrue();
        options.Remote.ShouldBe("origin");
        options.Branch.ShouldBe("dev");
    }

    [AvaloniaFact]
    public async Task Cakisma_UYARI_olarak_gosteriliyor()
    {
        (PullViewModel model, _, FakePullWriter pull) = Create();

        pull.Result = new PullResult
        {
            Strategy = new ResolvedPullStrategy(PullStrategy.Merge, PullStrategySource.UserChoice, null),
            HeadBefore = "aaaa",
            HeadAfter = "bbbb",
            HasConflicts = true,
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasWarning.ShouldBeTrue();
        model.Warning!.ShouldContain("ÇÖZÜLMEMİŞ");
    }

    [AvaloniaFact]
    public async Task AUTOSTASH_cakismasi_AYRI_bir_metinle_anlatiliyor()
    {
        // 🔴 İki durumun kullanıcıya söylediği iş farklı: burada pull BAŞARILI, çakışan
        // kullanıcının kendi kaydedilmemiş değişikliği ve stash'te duruyor.
        (PullViewModel model, _, FakePullWriter pull) = Create();

        pull.Result = new PullResult
        {
            Strategy = new ResolvedPullStrategy(PullStrategy.Rebase, PullStrategySource.UserChoice, null),
            HeadBefore = "aaaa",
            HeadAfter = "bbbb",
            HasConflicts = true,
            AutoStashConflict = true,
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.Warning!.ShouldContain("stash");
        model.Warning!.ShouldNotContain("Birleştirme çakışmayla durdu");
    }

    [AvaloniaFact]
    public async Task HEAD_ilerlediyse_GERI_ALMA_komutu_gosteriliyor()
    {
        (PullViewModel model, _, FakePullWriter pull) = Create();

        pull.Result = new PullResult
        {
            Strategy = new ResolvedPullStrategy(PullStrategy.Merge, PullStrategySource.UserChoice, null),
            HeadBefore = "1234567890",
            HeadAfter = "abcdef1234",
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasRecoveryCommand.ShouldBeTrue();
        model.RecoveryCommand.ShouldBe("git reset --hard 1234567890");
    }

    [AvaloniaFact]
    public async Task Zaten_guncelken_geri_alma_komutu_GOSTERILMIYOR()
    {
        // Hiçbir şey değişmediyse "geri al" demek kullanıcıyı kuşkuya düşürürdü.
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasRecoveryCommand.ShouldBeFalse();
        model.Notice.ShouldBe("Zaten güncel.");
    }

    [AvaloniaFact]
    public async Task Depo_acikken_MENU_komutu_etkin_ve_ekran_DOLU_aciliyor()
    {
        // Menü bağlantısı: eksik bir bağımlılıkta öğe sessizce ölü kalırdı
        // (`MainWindowBindingTests`'in yakaladığı hatanın aynı sınıfı).
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePullPrompt prompt = new();

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
            fetchWriter: new FakeFetchWriter(),
            pullWriter: new FakePullWriter())
        {
            PullPrompt = prompt,
        };

        model.CanPull.ShouldBeFalse("depo açılmadan etkin olmamalı");

        await model.OpenRepositoryAsync("/depo");

        model.CanPull.ShouldBeTrue();
        await model.PullCommand.ExecuteAsync(null);

        prompt.Shown.ShouldNotBeNull();
        prompt.Shown!.Remotes.ShouldBe(["origin"]);
        prompt.Shown.CurrentBranch.ShouldBe("main");
        prompt.Shown.RemoteBranches.ShouldBe(["main"]);
    }

    [AvaloniaFact]
    public async Task Remote_secilmeden_calistirilamaz()
    {
        (PullViewModel model, _, _) = Create();

        await LoadAsync(model);
        model.CanRun.ShouldBeTrue();

        model.SelectedRemote = null;
        model.CanRun.ShouldBeFalse();
    }
}
