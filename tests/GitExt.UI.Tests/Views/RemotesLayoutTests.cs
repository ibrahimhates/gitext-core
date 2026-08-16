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
/// P06-T05 — the remotes screen's layout and the deletion confirmation.
/// </summary>
/// <remarks>
/// § 9: the <b>position and order</b> of the elements must be the same as in GitExtensions'
/// <c>FormRemotes</c>. The order in the source: <c>Url</c> → <c>Name</c> → … →
/// <c>checkBoxSepPushUrl</c> → <c>Push Url</c>, with <c>Save changes</c> at the bottom; the list on the
/// left, <c>New</c>/<c>Delete</c> to the right of the list.
/// <para>
/// The test compares <b>position</b>: fields swapping places shows "everything is there" on screen but
/// breaks the user's muscle memory.
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
        // Unimplemented commands are "disabled but in place" (§ 9): slotting one in later would break
        // the order. Its content is P06-T07.
        Window window = await ShowAsync();

        window.GetControl<TabItem>("PullBehaviorTab").IsEnabled.ShouldBeFalse();
    }

    // ---- The deletion confirmation ----

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
        // The P05-T15 rule: when the way back is on screen, a checkbox is enough.
        RemoveRemoteDialog dialog = RemoveDialog(new RemoteRemovalRequest
        {
            Name = "origin",
            RecoveryCommands = ["git remote add origin https://example.com/a.git", "git fetch origin"],
        });

        string text = dialog.GetControl<TextBox>("RecoveryCommands").Text ?? string.Empty;

        text.ShouldContain("git remote add origin");
        text.ShouldContain("git fetch origin");

        // ⚠️ The difference from deleting a branch has to be written out: the commands do not bring the
        // objects back.
        (dialog.GetControl<TextBlock>("RecoveryNote").Text ?? string.Empty).ShouldContain("fetch");
    }

    [AvaloniaFact]
    public void Etki_metni_her_durumda_FARKLI()
    {
        // A single "are you sure?" text does not give the user what they need to decide.
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
