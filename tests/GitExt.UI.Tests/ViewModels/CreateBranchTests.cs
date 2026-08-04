using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T01 — dal oluşturma akışı (ViewModel tarafı).
/// </summary>
public class CreateBranchTests
{
    private static MainWindowViewModel Create(
        FakeBranchWriter writer,
        FakeBranchPrompt prompt,
        int commitCount = 3)
    {
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(commitCount)),
                new FakeRefReader(),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            branchWriter: writer)
        {
            BranchPrompt = prompt,
        };

        return model;
    }

    [AvaloniaFact]
    public async Task Depo_acik_degilken_komut_CALISMIYOR()
    {
        FakeBranchWriter writer = new();
        MainWindowViewModel model = Create(writer, new FakeBranchPrompt());

        model.CanCreateBranch.ShouldBeFalse();

        await model.CreateBranchCommand.ExecuteAsync(null);

        writer.Created.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Dal_secili_COMMIT_ten_olusturuluyor()
    {
        // 🔴 GitExtensions'ta bu komutun etiketi "Create branch at this revision" ve yeri
        // commit sağ tık menüsü. Seçili commit'i yok sayıp HEAD'den oluşturmak, kullanıcının
        // istediğinden BAŞKA bir şey yapmak olurdu — üstelik sessizce.
        FakeBranchWriter writer = new();
        FakeBranchPrompt prompt = new(new CreateBranchDecision { Confirmed = true, Name = "ozellik" });
        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = 2;

        await model.CreateBranchCommand.ExecuteAsync(null);

        BranchCreateOptions created = writer.Created.ShouldHaveSingleItem();
        created.Name.ShouldBe("ozellik");
        created.StartPoint.ShouldBe(model.Commits.Rows[2].Commit.Id.Value);
    }

    [AvaloniaFact]
    public async Task Secim_yokken_baslangic_noktasi_HEAD()
    {
        FakeBranchWriter writer = new();
        MainWindowViewModel model = Create(writer, new FakeBranchPrompt());

        await model.OpenRepositoryAsync("/tmp/depo");
        model.Commits.SelectedIndex = -1;

        await model.CreateBranchCommand.ExecuteAsync(null);

        writer.Created.ShouldHaveSingleItem().StartPoint.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Iptal_edilince_HICBIR_SEY_yazilmiyor()
    {
        FakeBranchWriter writer = new();
        FakeBranchPrompt prompt = new(CreateBranchDecision.Cancelled);
        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        await model.CreateBranchCommand.ExecuteAsync(null);

        prompt.AskCount.ShouldBe(1);
        writer.Created.ShouldBeEmpty();
        model.BranchNotice.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Checkout_secimi_yaziciya_GECIYOR()
    {
        FakeBranchWriter writer = new();
        FakeBranchPrompt prompt = new(
            new CreateBranchDecision { Confirmed = true, Name = "ozellik", Checkout = false });
        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        await model.CreateBranchCommand.ExecuteAsync(null);

        writer.Created.ShouldHaveSingleItem().Checkout.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Kendiliginden_kurulan_upstream_KULLANICIYA_soyleniyor()
    {
        // ÖLÇÜLDÜ: uzak izleme dalından oluşturulunca upstream'i git KENDİSİ kuruyor.
        // Kullanıcı istemeden kurulan bir bağın sessiz kalması, sonraki `push`un nereye
        // gideceğini sürpriz yapardı.
        FakeBranchWriter writer = new() { Upstream = "origin/ozellik" };
        MainWindowViewModel model = Create(writer, new FakeBranchPrompt());

        await model.OpenRepositoryAsync("/tmp/depo");
        await model.CreateBranchCommand.ExecuteAsync(null);

        model.BranchNotice.ShouldNotBeNull();
        model.BranchNotice!.ShouldContain("origin/ozellik");
    }

    [AvaloniaFact]
    public async Task Hata_SINIFLANDIRILMIS_mesajla_bildiriliyor()
    {
        FakeBranchWriter writer = new()
        {
            Failure = new GitException(
                GitFailureKind.BranchAlreadyExists,
                "Bu adda bir dal zaten var.",
                "git branch ozellik",
                exitCode: 128,
                standardError: "fatal: a branch named 'ozellik' already exists"),
        };

        MainWindowViewModel model = Create(writer, new FakeBranchPrompt());

        await model.OpenRepositoryAsync("/tmp/depo");
        await model.CreateBranchCommand.ExecuteAsync(null);

        model.BranchNotice.ShouldBe("Bu adda bir dal zaten var.");

        // Ham stderr birincil mesaj DEĞİL (GitFailureKind'ın varlık sebebi).
        model.BranchNotice!.ShouldNotContain("fatal:");
    }

    [AvaloniaFact]
    public async Task Kirli_agac_bilgisi_diyaloga_GECIYOR()
    {
        FakeBranchWriter writer = new();
        FakeBranchPrompt prompt = new();
        MainWindowViewModel model = Create(writer, prompt);

        await model.OpenRepositoryAsync("/tmp/depo");
        await model.CreateBranchCommand.ExecuteAsync(null);

        // Durum okuyucu verilmedi → uyarı yok; ama istek yine de kuruluyor ve
        // başlangıç noktası açıklaması boş kalmıyor.
        prompt.LastRequest.ShouldNotBeNull();
        prompt.LastRequest.StartPointLabel.ShouldNotBeNullOrWhiteSpace();
    }
}
