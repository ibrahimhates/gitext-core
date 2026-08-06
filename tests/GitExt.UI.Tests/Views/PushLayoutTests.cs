using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T08 — Push ekranının yerleşimi.
/// </summary>
/// <remarks>
/// § 9: sıra GitExtensions <c>FormPush</c>'tan — <c>GroupBox2</c> ("Push to": Remote / Url)
/// → <c>TabControlTagBranch</c> (Push branches · Push tags · Push multiple branches) →
/// alt sıra (Pull · Load SSH key · Push).
/// </remarks>
public class PushLayoutTests
{
    private static async Task<Window> ShowAsync(PushPlan? plan = null)
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushWriter push = new();

        if (plan is not null)
        {
            push.Plan = plan;
        }

        PushViewModel model = new(remotes, push);

        await model.LoadAsync(
            "/depo",
            "main",
            [
                FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true) with
                {
                    Upstream = "origin/main",
                },
            ]);

        PushWindow window = new() { DataContext = model, Width = 660, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static Rect BoundsIn(Window window, Control control)
    {
        Point origin = control.TranslatePoint(default, window)
            ?? throw new InvalidOperationException($"'{control.Name}' görsel ağaçta değil.");

        return new Rect(origin, control.Bounds.Size);
    }

    [AvaloniaFact]
    public async Task Sira_FormPush_ile_ayni()
    {
        Window window = await ShowAsync();

        Rect remote = BoundsIn(window, window.GetControl<ComboBox>("RemoteBox"));
        Rect url = BoundsIn(window, window.GetControl<RadioButton>("ToUrlRadio"));
        Rect tabs = BoundsIn(window, window.GetControl<TabControl>("TargetTabs"));
        Rect command = BoundsIn(window, window.GetControl<TextBox>("CommandPreviewBox"));
        Rect push = BoundsIn(window, window.GetControl<Button>("PushButton"));

        remote.Bottom.ShouldBeLessThanOrEqualTo(url.Y);
        url.Bottom.ShouldBeLessThanOrEqualTo(tabs.Y);
        tabs.Bottom.ShouldBeLessThanOrEqualTo(command.Y);
        command.Bottom.ShouldBeLessThanOrEqualTo(push.Y);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Alt_sirada_Pull_ve_SSH_dugmeleri_Push_tan_ONCE()
    {
        // GitExtensions'ta da alt sıra soldan sağa: Pull · Load SSH key · Push.
        Window window = await ShowAsync();

        Rect pull = BoundsIn(window, window.GetControl<Button>("PullButton"));
        Rect ssh = BoundsIn(window, window.GetControl<Button>("LoadSshKeyButton"));
        Rect push = BoundsIn(window, window.GetControl<Button>("PushButton"));

        pull.Right.ShouldBeLessThanOrEqualTo(ssh.X);
        ssh.Right.ShouldBeLessThanOrEqualTo(push.X);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Uc_sekme_de_YERINDE()
    {
        Window window = await ShowAsync();

        window.GetControl<TabItem>("BranchTab").ShouldNotBeNull();
        window.GetControl<TabItem>("TagTab").ShouldNotBeNull();
        window.GetControl<TabItem>("MultipleTab").ShouldNotBeNull();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Calistirilacak_komut_EKRANDA()
    {
        Window window = await ShowAsync();

        window.GetControl<TextBox>("CommandPreviewBox").Text
            .ShouldBe("git push --porcelain -- origin main:main");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Ciplak_zorlama_kutusu_YERINDE_ama_devre_disi()
    {
        // Kutuyu tamamen kaldırmak "bu program zorlayamıyor" izlenimi verirdi; yerinde
        // ama kapalı ve hemen altında nedeni yazılı.
        Window window = await ShowAsync();

        window.GetControl<CheckBox>("ForcePushBox").IsEnabled.ShouldBeFalse();
        (window.GetControl<TextBlock>("ForceDisabledText").Text ?? string.Empty)
            .ShouldContain("Kirayla zorla");

        window.GetControl<CheckBox>("ForceWithLeaseBox").IsEnabled.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task URL_ve_SSH_secenekleri_YERINDE_ama_devre_disi()
    {
        // İkisi de P06-T09'un kimlik doğrulama akışına bağlı (§ 9).
        Window window = await ShowAsync();

        window.GetControl<RadioButton>("ToUrlRadio").IsEnabled.ShouldBeFalse();
        window.GetControl<Button>("LoadSshKeyButton").IsEnabled.ShouldBeFalse();
        window.GetControl<RadioButton>("ToRemoteRadio").IsChecked.ShouldBe(true);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Secenekler_paneli_baslangicta_GIZLI()
    {
        // GitExtensions'ta `PushOptionsPanel` da "Show options" tıklanana kadar gizli.
        Window window = await ShowAsync();

        window.GetControl<StackPanel>("PushOptionsPanel").IsVisible.ShouldBeFalse();

        window.GetControl<ToggleButton>("ShowOptionsToggle").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        window.GetControl<StackPanel>("PushOptionsPanel").IsVisible.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Kira_bilgisi_zorlama_secilince_GORUNUYOR()
    {
        Window window = await ShowAsync();

        window.GetControl<TextBlock>("LeaseNoticeText").IsVisible.ShouldBeFalse();

        window.GetControl<ToggleButton>("ShowOptionsToggle").IsChecked = true;
        window.GetControl<CheckBox>("ForceWithLeaseBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        window.GetControl<TextBlock>("LeaseNoticeText").IsVisible.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Coklu_sekmede_sutun_sirasi_FormPush_grid_i_ile_ayni()
    {
        Window window = await ShowAsync();

        window.GetControl<TabItem>("MultipleTab").IsSelected = true;
        Dispatcher.UIThread.RunJobs();

        Grid header = window.GetControl<Grid>("BranchGridHeader");
        IReadOnlyList<string> labels =
        [
            .. header.Children.OfType<TextBlock>().Select(block => block.Text ?? string.Empty),
        ];

        labels.ShouldBe(["Yerel dal", "Uzak dal", "İleri/geri", "Gönder", "Uzaktakini sil"]);

        window.Close();
    }
}
