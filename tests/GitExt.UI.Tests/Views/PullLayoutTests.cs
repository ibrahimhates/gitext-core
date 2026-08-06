using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T06 + P06-T07 — Pull/Fetch ekranının yerleşimi.
/// </summary>
/// <remarks>
/// § 9: sıra GitExtensions <c>FormPull</c>'dan — <c>GroupPullFrom</c> → <c>GroupBranch</c> →
/// <c>GroupMergeOptions</c> (Merge · Rebase · yalnızca Fetch) → <c>GroupTagOptions</c> →
/// <c>Prune</c>/<c>PruneTags</c> → <c>AutoStash</c> → <c>Pull</c>.
/// </remarks>
public class PullLayoutTests
{
    private static async Task<Window> ShowAsync(ResolvedPullStrategy? configured = null)
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePullWriter pull = new();

        if (configured is not null)
        {
            pull.Configured = configured;
        }

        PullViewModel model = new(remotes, new FakeFetchWriter(), pull);

        await model.LoadAsync("/depo", "main", [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1)).Ref]);

        PullWindow window = new() { DataContext = model, Width = 620, Height = 620 };
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
    public async Task Grup_sirasi_FormPull_ile_ayni()
    {
        Window window = await ShowAsync();

        Rect remote = BoundsIn(window, window.GetControl<ComboBox>("RemoteBox"));
        Rect branch = BoundsIn(window, window.GetControl<ComboBox>("BranchBox"));
        Rect merge = BoundsIn(window, window.GetControl<RadioButton>("MergeRadio"));
        Rect rebase = BoundsIn(window, window.GetControl<RadioButton>("RebaseRadio"));
        Rect fetchOnly = BoundsIn(window, window.GetControl<RadioButton>("FetchOnlyRadio"));
        Rect tags = BoundsIn(window, window.GetControl<RadioButton>("ReachableTagsRadio"));
        Rect prune = BoundsIn(window, window.GetControl<CheckBox>("PruneBox"));
        Rect autoStash = BoundsIn(window, window.GetControl<CheckBox>("AutoStashBox"));
        Rect pull = BoundsIn(window, window.GetControl<Button>("PullButton"));

        remote.Bottom.ShouldBeLessThanOrEqualTo(branch.Y);
        branch.Bottom.ShouldBeLessThanOrEqualTo(merge.Y);
        merge.Bottom.ShouldBeLessThanOrEqualTo(rebase.Y);
        rebase.Bottom.ShouldBeLessThanOrEqualTo(fetchOnly.Y);
        fetchOnly.Bottom.ShouldBeLessThanOrEqualTo(tags.Y);
        tags.Bottom.ShouldBeLessThanOrEqualTo(prune.Y);
        prune.Bottom.ShouldBeLessThanOrEqualTo(autoStash.Y);
        autoStash.Bottom.ShouldBeLessThanOrEqualTo(pull.Y);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Calistirilacak_komut_EKRANDA()
    {
        // "Komutu göster" ilkesi: kullanıcı basmadan ne olacağını okuyabilmeli.
        Window window = await ShowAsync();

        window.GetControl<TextBox>("CommandPreviewBox").Text
            .ShouldBe("git pull --no-rebase origin main");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Ayarin_ne_dedigi_EKRANDA()
    {
        Window window = await ShowAsync(
            new ResolvedPullStrategy(PullStrategy.Rebase, PullStrategySource.PullRebaseSetting, "true"));

        window.GetControl<RadioButton>("RebaseRadio").IsChecked.ShouldBe(true);
        (window.GetControl<TextBlock>("StrategyNoticeText").Text ?? string.Empty)
            .ShouldContain("pull.rebase");

        window.Close();
    }

    [AvaloniaFact]
    public async Task PruneTags_kutusu_Prune_isaretlenene_kadar_DEVRE_DISI()
    {
        Window window = await ShowAsync();

        window.GetControl<CheckBox>("PruneTagsBox").IsEnabled.ShouldBeFalse();

        window.GetControl<CheckBox>("PruneBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        window.GetControl<CheckBox>("PruneTagsBox").IsEnabled.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Yalnizca_getir_secilince_AutoStash_anlamsiz_ve_KAPALI()
    {
        // Fetch çalışma ağacına dokunmuyor; kutunun açık kalması yanlış bir vaat olurdu.
        Window window = await ShowAsync();

        window.GetControl<CheckBox>("AutoStashBox").IsEnabled.ShouldBeTrue();

        window.GetControl<RadioButton>("FetchOnlyRadio").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        window.GetControl<CheckBox>("AutoStashBox").IsEnabled.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task URL_secenegi_YERINDE_ama_devre_disi()
    {
        // Uygulanmamış komutlar "devre dışı ama yerinde" (§ 9); URL'den çekme P06-T09'a bağlı.
        Window window = await ShowAsync();

        window.GetControl<RadioButton>("FromUrlRadio").IsEnabled.ShouldBeFalse();
        window.GetControl<RadioButton>("FromRemoteRadio").IsChecked.ShouldBe(true);

        window.Close();
    }
}
