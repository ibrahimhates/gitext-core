using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T05 — uzak depo ekranının yerleşimi ve silme onayı.
/// </summary>
/// <remarks>
/// § 9: öğelerin <b>yeri ve sırası</b> GitExtensions <c>FormRemotes</c>'takiyle aynı olmalı.
/// Kaynaktaki sıra: <c>Url</c> → <c>Name</c> → … → <c>checkBoxSepPushUrl</c> →
/// <c>Push Url</c>, en altta <c>Save changes</c>; liste solda, <c>New</c>/<c>Delete</c>
/// listenin sağında.
/// <para>
/// Test <b>konum</b> karşılaştırıyor: alanların yer değiştirmesi ekranda "her şey var"
/// gösterir ama kullanıcının kas hafızasını kırar.
/// </para>
/// </remarks>
public class RemotesLayoutTests
{
    private static async Task<Window> ShowAsync(params GitRemote[] remotes)
    {
        FakeRemoteReader reader = new();
        reader.Remotes.AddRange(remotes.Length > 0
            ? remotes
            : [new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] }]);

        RemotesViewModel model = new(reader, new FakeRemoteWriter(reader));
        await model.LoadAsync("/depo");

        RemotesWindow window = new() { DataContext = model, Width = 760, Height = 420 };

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
    public async Task Alan_sirasi_FormRemotes_ile_ayni()
    {
        Window window = await ShowAsync();

        Rect url = BoundsIn(window, window.GetControl<TextBox>("UrlBox"));
        Rect name = BoundsIn(window, window.GetControl<TextBox>("NameBox"));
        Rect separate = BoundsIn(window, window.GetControl<CheckBox>("SeparatePushBox"));
        Rect pushUrl = BoundsIn(window, window.GetControl<TextBox>("PushUrlBox"));
        Rect save = BoundsIn(window, window.GetControl<Button>("SaveButton"));

        url.Bottom.ShouldBeLessThanOrEqualTo(name.Y);
        name.Bottom.ShouldBeLessThanOrEqualTo(separate.Y);
        separate.Bottom.ShouldBeLessThanOrEqualTo(pushUrl.Y);
        pushUrl.Bottom.ShouldBeLessThanOrEqualTo(save.Y);
    }

    [AvaloniaFact]
    public async Task Liste_SOLDA_dugmeler_sagda()
    {
        Window window = await ShowAsync();

        Rect list = BoundsIn(window, window.GetControl<ListBox>("RemotesList"));
        Rect newButton = BoundsIn(window, window.GetControl<Button>("NewButton"));
        Rect deleteButton = BoundsIn(window, window.GetControl<Button>("DeleteButton"));
        Rect editor = BoundsIn(window, window.GetControl<TextBox>("UrlBox"));

        list.Right.ShouldBeLessThanOrEqualTo(newButton.X);
        newButton.Bottom.ShouldBeLessThanOrEqualTo(deleteButton.Y);
        newButton.Right.ShouldBeLessThanOrEqualTo(editor.X);
    }

    [AvaloniaFact]
    public async Task Ayri_push_url_kapaliyken_kutu_DEVRE_DISI()
    {
        Window window = await ShowAsync();

        window.GetControl<TextBox>("PushUrlBox").IsEnabled.ShouldBeFalse();

        window.GetControl<CheckBox>("SeparatePushBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        window.GetControl<TextBox>("PushUrlBox").IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Pull_davranisi_sekmesi_YERINDE_ama_devre_disi()
    {
        // Uygulanmamış komutlar "devre dışı ama yerinde" (§ 9): sonradan araya sokmak
        // sırayı bozardı. İçeriği P06-T07.
        Window window = await ShowAsync();

        window.GetControl<TabItem>("PullBehaviorTab").IsEnabled.ShouldBeFalse();
    }

    // ---- Silme onayı ----

    private static RemoveRemoteDialog RemoveDialog(RemoteRemovalRequest request)
    {
        RemoveRemoteDialog dialog = new();
        dialog.Apply(request);

        return dialog;
    }

    [AvaloniaFact]
    public void Onay_kutusu_isaretlenmeden_KALDIR_kapali()
    {
        RemoveRemoteDialog dialog = RemoveDialog(new RemoteRemovalRequest { Name = "origin" });

        dialog.GetControl<Button>("RemoveButton").IsEnabled.ShouldBeFalse();

        dialog.GetControl<CheckBox>("ConfirmBox").IsChecked = true;

        dialog.GetControl<Button>("RemoveButton").IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Kurtarma_komutlari_EKRANDA()
    {
        // P05-T15 kuralı: kurtarma yolu ekrandaysa onay kutusu yeterli.
        RemoveRemoteDialog dialog = RemoveDialog(new RemoteRemovalRequest
        {
            Name = "origin",
            RecoveryCommands = ["git remote add origin https://example.com/a.git", "git fetch origin"],
        });

        string text = dialog.GetControl<TextBox>("RecoveryCommands").Text ?? string.Empty;

        text.ShouldContain("git remote add origin");
        text.ShouldContain("git fetch origin");

        // ⚠️ Dal silmeden farkı yazılı olmalı: komutlar nesneleri geri getirmiyor.
        (dialog.GetControl<TextBlock>("RecoveryNote").Text ?? string.Empty).ShouldContain("fetch");
    }

    [AvaloniaFact]
    public void Etki_metni_her_durumda_FARKLI()
    {
        // Tek bir "emin misiniz?" metni kullanıcıya kararını verecek bilgiyi vermez.
        string bos = RemoveRemoteDialog.DescribeImpact(new RemoteRemovalRequest { Name = "origin" });

        string tracking = RemoveRemoteDialog.DescribeImpact(new RemoteRemovalRequest
        {
            Name = "origin",
            TrackingBranchCount = 3,
        });

        string upstream = RemoveRemoteDialog.DescribeImpact(new RemoteRemovalRequest
        {
            Name = "origin",
            AffectedBranches = ["main"],
        });

        string pushDefault = RemoveRemoteDialog.DescribeImpact(new RemoteRemovalRequest
        {
            Name = "origin",
            IsPushDefault = true,
        });

        new[] { bos, tracking, upstream, pushDefault }
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(4);

        tracking.ShouldContain("3");
        upstream.ShouldContain("main");
    }
}
