using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T10 — ağ işlemlerinde ilerleme ve iptal (arayüz tarafı).
/// </summary>
public class ProgressTests
{
    private static (PushViewModel Model, FakePushWriter Push) CreatePush()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushWriter push = new();

        return (new PushViewModel(remotes, push), push);
    }

    private static Task LoadAsync(PushViewModel model) => model.LoadAsync(
        "/depo",
        "main",
        [FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true) with { Upstream = "origin/main" }]);

    [AvaloniaFact]
    public async Task Ilerleme_ekrana_TASINIYOR()
    {
        (PushViewModel model, FakePushWriter push) = CreatePush();

        push.ReportProgress =
        [
            new GitProgress("Counting objects", 40, 4, 10, IsRemote: true),
            new GitProgress("Writing objects", 90, 9, 10),
        ];

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        push.SeenProgress.ShouldNotBeNull("ilerleme kanalı hiç geçirilmemiş");
    }

    [AvaloniaFact]
    public async Task Islem_bitince_ilerleme_TEMIZLENIYOR()
    {
        // Biten bir işlemin çubuğu ekranda kalırsa kullanıcı hâlâ çalıştığını sanır.
        (PushViewModel model, _) = CreatePush();

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasProgress.ShouldBeFalse();
        model.IsBusy.ShouldBeFalse();
        model.CanCancel.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task IPTAL_hata_olarak_degil_bilgi_olarak_bildiriliyor()
    {
        // 🔑 Kullanıcının kendi bastığı düğme "hata" diye geri dönmemeli.
        (PushViewModel model, FakePushWriter push) = CreatePush();

        push.CancelOnRun = true;

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.Notice.ShouldBe("The operation was cancelled.");
        model.HasWarning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Iptal_jetonu_yaziciya_GERCEKTEN_gecirilyor()
    {
        // İptal düğmesinin bir işe yaraması için jetonun git'e kadar inmesi gerek;
        // yalnızca beklemeyi bırakmak arkada çalışan bir süreç bırakırdı.
        (PushViewModel model, FakePushWriter push) = CreatePush();

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        push.SeenToken.ShouldNotBe(CancellationToken.None);
        push.SeenToken.CanBeCanceled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Yuzde_yoksa_cubuk_BELIRSIZ()
    {
        (PushViewModel model, _) = CreatePush();

        await LoadAsync(model);

        model.IsProgressIndeterminate.ShouldBeTrue();
    }

    // -------------------------------------------------------------- yerleşim

    [AvaloniaFact]
    public async Task Ilerleme_paneli_yalnizca_calisirken_GORUNUYOR()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        PushViewModel model = new(remotes, new FakePushWriter());
        await LoadAsync(model);

        PushWindow window = new() { DataContext = model, Width = 660, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetControl<StackPanel>("ProgressPanel").IsVisible.ShouldBeFalse();
        window.GetControl<Button>("CancelRunButton").ShouldNotBeNull();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Pull_ekraninda_da_ilerleme_paneli_VAR()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        PullViewModel model = new(remotes, new FakeFetchWriter(), new FakePullWriter());
        await model.LoadAsync("/depo", "main", [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1)).Ref]);

        PullWindow window = new() { DataContext = model, Width = 620, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetControl<StackPanel>("ProgressPanel").ShouldNotBeNull();
        window.GetControl<ProgressBar>("ProgressBar").ShouldNotBeNull();

        window.Close();
    }
}
