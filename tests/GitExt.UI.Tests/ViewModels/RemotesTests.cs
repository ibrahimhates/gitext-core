using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T05 — uzak depo yönetimi (ViewModel tarafı).
/// </summary>
public class RemotesTests
{
    private const string Path = "/depo";

    private static (RemotesViewModel Model, FakeRemoteReader Reader, FakeRemoteWriter Writer, FakeRemoteRemovalConfirmer Confirmer)
        Create(bool confirmRemoval = true, params GitRemote[] remotes)
    {
        FakeRemoteReader reader = new();
        reader.Remotes.AddRange(remotes);

        FakeRemoteWriter writer = new(reader);
        FakeRemoteRemovalConfirmer confirmer = new(confirmRemoval);

        return (new RemotesViewModel(reader, writer, confirmer), reader, writer, confirmer);
    }

    private static GitRemote Origin(string name = "origin", string url = "https://example.com/a.git") =>
        new() { Name = name, FetchUrls = [url], FetchRefspecs = [GitRemote.DefaultFetchRefspec(name)] };

    [AvaloniaFact]
    public async Task Liste_doluyor_ve_ilk_kayit_seciliyor()
    {
        (RemotesViewModel model, _, _, _) = Create(true, Origin(), Origin("upstream", "https://example.com/b.git"));

        await model.LoadAsync(Path);

        model.Remotes.Select(r => r.Name).ShouldBe(["origin", "upstream"]);
        model.Selected!.Name.ShouldBe("origin");
        model.Url.ShouldBe("https://example.com/a.git");
    }

    [AvaloniaFact]
    public async Task Listede_parola_MASKELI_duzenleme_kutusunda_HAM()
    {
        // 🔴 Maskeleme kutuya sızsaydı kullanıcı `***`'ı kaydeder ve parolasını bozardı.
        (RemotesViewModel model, _, _, _) =
            Create(true, Origin(url: "https://ali:s3cr3t@example.com/a.git"));

        await model.LoadAsync(Path);

        model.Remotes[0].DisplayUrl.ShouldBe("https://ali:***@example.com/a.git");
        model.Url.ShouldBe("https://ali:s3cr3t@example.com/a.git");
    }

    [AvaloniaFact]
    public async Task URL_siz_remote_listede_kaliyor()
    {
        (RemotesViewModel model, _, _, _) = Create(true, new GitRemote { Name = "hayalet" });

        await model.LoadAsync(Path);

        model.Remotes.Single().DisplayUrl.ShouldBe("(no URL configured)");
    }

    [AvaloniaFact]
    public async Task Yeni_ekleme_alanlari_temizliyor_ve_ekliyor()
    {
        (RemotesViewModel model, _, FakeRemoteWriter writer, _) = Create(true, Origin());

        await model.LoadAsync(Path);

        model.NewCommand.Execute(null);
        model.Selected.ShouldBeNull();
        model.Name.ShouldBeEmpty();
        model.Url.ShouldBeEmpty();

        model.Name = "upstream";
        model.Url = "https://example.com/b.git";
        await model.SaveCommand.ExecuteAsync(null);

        writer.Added.Single().Name.ShouldBe("upstream");
        model.Remotes.Select(r => r.Name).ShouldContain("upstream");
    }

    [AvaloniaFact]
    public async Task Gecersiz_ad_KAYDEDILEMIYOR_ve_sebebi_yaziliyor()
    {
        (RemotesViewModel model, _, FakeRemoteWriter writer, _) = Create();

        await model.LoadAsync(Path);
        model.NewCommand.Execute(null);

        model.Name = "a b";
        model.Url = "https://example.com/b.git";

        model.CanSave.ShouldBeFalse();
        model.NameProblem.ShouldNotBeNull();

        await model.SaveCommand.ExecuteAsync(null);
        writer.Added.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Ad_degisince_RENAME_url_degisince_SET_URL()
    {
        (RemotesViewModel model, _, FakeRemoteWriter writer, _) = Create(true, Origin());

        await model.LoadAsync(Path);

        model.Name = "uzak";
        model.Url = "https://example.com/yeni.git";
        await model.SaveCommand.ExecuteAsync(null);

        writer.Renamed.Single().ShouldBe(("origin", "uzak"));

        // Sıra önemli: URL YENİ ada yazılmalı, yoksa eski adı yazıp kaybederdik.
        writer.UrlChanges.Single().Name.ShouldBe("uzak");
        writer.UrlChanges.Single().Kind.ShouldBe(RemoteUrlKind.Fetch);
    }

    [AvaloniaFact]
    public async Task Yeniden_adlandirmada_git_in_UYARISI_yuzeye_cikiyor()
    {
        // 🔴 Çıkış kodu 0 ama iş yarım: varsayılan olmayan refspec güncellenmiyor.
        // Yalnızca rc'ye bakan arayüz "başarılı" derdi.
        (RemotesViewModel model, _, FakeRemoteWriter writer, _) = Create(true, Origin());
        writer.RenameWarnings = ["Not updating non-default fetch refspec …"];

        await model.LoadAsync(Path);

        model.Name = "uzak";
        await model.SaveCommand.ExecuteAsync(null);

        model.HasWarning.ShouldBeTrue();
        model.Warning!.ShouldContain("non-default");
    }

    [AvaloniaFact]
    public async Task Coklu_URL_de_kaydetme_KAPALI_ve_sebebi_yaziliyor()
    {
        // ÖLÇÜLDÜ: bu durumda `git remote set-url` "has multiple values" ile çöküyor.
        (RemotesViewModel model, _, _, _) = Create(true, new GitRemote
        {
            Name = "origin",
            FetchUrls = ["https://example.com/bir.git", "https://example.com/iki.git"],
        });

        await model.LoadAsync(Path);

        model.HasMultipleUrls.ShouldBeTrue();
        model.CanSave.ShouldBeFalse();
        model.MultipleUrlNotice.ShouldNotBeNull();
        model.MultipleUrlNotice!.ShouldContain("bir.git");
    }

    [AvaloniaFact]
    public async Task Silmede_ONAY_soruluyor_ve_plan_diyaloga_gidiyor()
    {
        (RemotesViewModel model, _, FakeRemoteWriter writer, FakeRemoteRemovalConfirmer confirmer) =
            Create(true, Origin());

        writer.Plan = new RemoteRemovalPlan
        {
            Remote = Origin(),
            TrackingBranches = ["origin/main", "origin/dev"],
            AffectedBranches = [("main", "origin/main")],
            IsPushDefault = true,
            RecoveryCommands = ["git remote add origin https://example.com/a.git", "git fetch origin"],
        };

        await model.LoadAsync(Path);
        await model.DeleteCommand.ExecuteAsync(null);

        RemoteRemovalRequest request = confirmer.Requests.Single();
        request.Name.ShouldBe("origin");
        request.TrackingBranchCount.ShouldBe(2);
        request.AffectedBranches.ShouldBe(["main"]);
        request.IsPushDefault.ShouldBeTrue();
        request.RecoveryCommands.Count.ShouldBe(2);

        writer.Removed.ShouldBe(["origin"]);
        model.Remotes.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Silme_plani_SILMEDEN_ONCE_isteniyor()
    {
        // Silme sonrası hiçbir bilgi okunamıyor; onay ekranı boş çıkardı.
        (RemotesViewModel model, _, FakeRemoteWriter writer, _) = Create(true, Origin());

        await model.LoadAsync(Path);
        await model.DeleteCommand.ExecuteAsync(null);

        writer.PlanRequestedBeforeRemoval.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Onay_verilmezse_SILINMIYOR()
    {
        (RemotesViewModel model, _, FakeRemoteWriter writer, _) = Create(false, Origin());

        await model.LoadAsync(Path);
        await model.DeleteCommand.ExecuteAsync(null);

        writer.Removed.ShouldBeEmpty();
        model.Remotes.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task git_hatasi_kullaniciya_ANLAMLI_metinle_gosteriliyor()
    {
        (RemotesViewModel model, _, FakeRemoteWriter writer, _) = Create();

        writer.Failure = new GitException(
            GitFailureKind.RemoteAlreadyExists,
            "Bu adda bir uzak depo zaten var.",
            "git remote add",
            exitCode: 3,
            standardError: "error: remote origin already exists.");

        await model.LoadAsync(Path);
        model.NewCommand.Execute(null);
        model.Name = "origin";
        model.Url = "https://example.com/a.git";

        await model.SaveCommand.ExecuteAsync(null);

        model.Notice.ShouldBe("A remote with that name already exists.");
    }

    /// <summary>
    /// Ana pencereden bağlantı: komut ancak depo açıkken VE bağımlılıklar verilmişken etkin.
    /// </summary>
    /// <remarks>
    /// Menü öğesi <c>Command</c>'a bağlı; eksik bir bağımlılıkta öğe <b>sessizce</b> ölü
    /// kalırdı (P03-T16'daki eksik DI kaydının uygulamayı yalnızca açılışta çökertmesiyle
    /// aynı sınıf).
    /// </remarks>
    private static MainWindowViewModel CreateMainWindow(
        bool withServices = true,
        bool withPrompt = true)
    {
        FakeRemoteReader reader = new();

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            remoteReader: withServices ? reader : null,
            remoteWriter: withServices ? new FakeRemoteWriter(reader) : null);

        if (withPrompt)
        {
            model.RemotesPrompt = new FakeRemotesPrompt();
        }

        return model;
    }

    [AvaloniaFact]
    public async Task Depo_acikken_komut_ETKIN()
    {
        MainWindowViewModel model = CreateMainWindow();

        model.CanManageRemotes.ShouldBeFalse("depo açılmadan etkin olmamalı");

        await model.OpenRepositoryAsync("/depo");

        model.CanManageRemotes.ShouldBeTrue();
        model.ManageRemotesCommand.CanExecute(null).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Bagimlilik_eksikse_komut_KAPALI()
    {
        MainWindowViewModel withoutServices = CreateMainWindow(withServices: false);
        MainWindowViewModel withoutPrompt = CreateMainWindow(withPrompt: false);

        await withoutServices.OpenRepositoryAsync("/depo");
        await withoutPrompt.OpenRepositoryAsync("/depo");

        withoutServices.CanManageRemotes.ShouldBeFalse();
        withoutPrompt.CanManageRemotes.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Ekran_acilirken_ViewModel_DOLU_geliyor()
    {
        FakeRemoteReader reader = new();
        reader.Remotes.Add(Origin());

        FakeRemotesPrompt prompt = new();

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(2)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            remoteReader: reader,
            remoteWriter: new FakeRemoteWriter(reader))
        {
            RemotesPrompt = prompt,
        };

        await model.OpenRepositoryAsync("/depo");
        await model.ManageRemotesCommand.ExecuteAsync(null);

        // Pencere boş bir listeyle açılsaydı kullanıcı "hiç remote yok" sanırdı.
        prompt.Shown!.Remotes.Select(r => r.Name).ShouldBe(["origin"]);
    }

    [AvaloniaFact]
    public async Task Ayri_push_url_kutusu_kapatilinca_pushurl_KALDIRILIYOR()
    {
        (RemotesViewModel model, _, FakeRemoteWriter writer, _) = Create(true, new GitRemote
        {
            Name = "origin",
            FetchUrls = ["https://example.com/a.git"],
            PushUrls = ["ssh://git@example.com/a.git"],
        });

        await model.LoadAsync(Path);
        model.SeparatePushUrl.ShouldBeTrue();

        model.SeparatePushUrl = false;
        await model.SaveCommand.ExecuteAsync(null);

        writer.UrlChanges.Single().Operation.ShouldBe("delete");
        writer.UrlChanges.Single().Kind.ShouldBe(RemoteUrlKind.Push);
    }
}
