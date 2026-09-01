using Avalonia.Headless.XUnit;
using GitExt.Core.Git;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T01 — the process window's behaviour table, read off GitExtensions <c>FormStatus.Done</c>.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>success · box unchecked</term><description>closes by itself</description></item>
/// <item><term>success · box checked</term><description>stays</description></item>
/// <item><term>failure · either way</term><description>stays</description></item>
/// <item><term>abort</term><description>counts as failure, stays</description></item>
/// <item><term>running</term><description>cannot be closed at all</description></item>
/// </list>
/// </remarks>
public class ProcessDialogTests
{
    private static ProcessDialogViewModel Create(
        ProcessDialogOperation operation,
        InMemorySettingsStore? settings = null,
        bool useDialogSettings = true,
        bool canAbort = true) =>
        new(
            new ProcessDialogRequest
            {
                Title = "Process",
                Operation = operation,
                UseDialogSettings = useDialogSettings,
                CanAbort = canAbort,
            },
            settings);

    private static ProcessDialogOperation Succeeds(string output = "tamam\n") =>
        (sink, _) =>
        {
            sink.Report(new GitOutputChunk(output, IsError: false));
            return Task.FromResult(true);
        };

    private static ProcessDialogOperation Fails(string output = "hata\n") =>
        (sink, _) =>
        {
            sink.Report(new GitOutputChunk(output, IsError: true));
            return Task.FromResult(false);
        };

    private static int CountCloseRequests(ProcessDialogViewModel model)
    {
        int closes = 0;
        model.CloseRequested += (_, _) => closes++;
        return closes;
    }

    [AvaloniaFact]
    public async Task Basarili_islem_kutu_isaretsizken_pencereyi_kapatir()
    {
        InMemorySettingsStore settings = new();
        settings.Current.General.CloseProcessDialog = true;

        using ProcessDialogViewModel model = Create(Succeeds(), settings);

        int closes = 0;
        model.CloseRequested += (_, _) => closes++;

        model.KeepDialogOpen.ShouldBeFalse();

        await model.StartAsync();

        model.State.ShouldBe(ProcessDialogState.Success);
        closes.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Basarili_islem_kutu_isaretliyken_pencereyi_acik_birakir()
    {
        InMemorySettingsStore settings = new();

        using ProcessDialogViewModel model = Create(Succeeds(), settings);

        int closes = 0;
        model.CloseRequested += (_, _) => closes++;

        model.KeepDialogOpen.ShouldBeTrue();

        await model.StartAsync();

        model.State.ShouldBe(ProcessDialogState.Success);
        closes.ShouldBe(0);
        model.IsFinished.ShouldBeTrue();
        model.OkCommand.CanExecute(null).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Hata_kutu_isaretsiz_olsa_bile_pencereyi_acik_birakir()
    {
        InMemorySettingsStore settings = new();
        settings.Current.General.CloseProcessDialog = true;

        using ProcessDialogViewModel model = Create(Fails(), settings);

        int closes = 0;
        model.CloseRequested += (_, _) => closes++;

        await model.StartAsync();

        model.State.ShouldBe(ProcessDialogState.Error);
        model.HasFailed.ShouldBeTrue();
        closes.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Islem_suresince_pencere_kapatilamaz()
    {
        TaskCompletionSource gate = new();

        using ProcessDialogViewModel model = Create(async (_, _) =>
        {
            await gate.Task;
            return true;
        });

        Task running = model.StartAsync();

        model.IsRunning.ShouldBeTrue();
        model.CanClose.ShouldBeFalse();
        model.IsProgressVisible.ShouldBeTrue();

        gate.SetResult();
        await running;

        model.CanClose.ShouldBeTrue();
        model.IsProgressVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Durdurma_hata_sayilir_ve_ciktiya_yazilir()
    {
        TaskCompletionSource gate = new();

        using ProcessDialogViewModel model = Create(async (_, token) =>
        {
            await using CancellationTokenRegistration registration =
                token.Register(() => gate.TrySetResult());

            await gate.Task;
            token.ThrowIfCancellationRequested();
            return true;
        });

        int closes = CountCloseRequests(model);

        Task running = model.StartAsync();

        model.AbortCommand.CanExecute(null).ShouldBeTrue();
        model.AbortCommand.Execute(null);

        await running;

        model.State.ShouldBe(ProcessDialogState.Error);
        model.Output.ShouldContain("Aborted");
        closes.ShouldBe(0);
    }

    [AvaloniaFact]
    public void Durdurulamayan_islemde_dugme_gizli()
    {
        using ProcessDialogViewModel model = Create(Succeeds(), canAbort: false);

        model.CanAbort.ShouldBeFalse();
        model.AbortCommand.CanExecute(null).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Bittikten_sonra_kutuyu_kaldirmak_pencereyi_kapatir()
    {
        InMemorySettingsStore settings = new();

        using ProcessDialogViewModel model = Create(Succeeds(), settings);

        int closes = 0;
        model.CloseRequested += (_, _) => closes++;

        await model.StartAsync();

        closes.ShouldBe(0);

        model.KeepDialogOpen = false;

        closes.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Hatadan_sonra_kutuyu_kaldirmak_pencereyi_kapatmaz()
    {
        InMemorySettingsStore settings = new();

        using ProcessDialogViewModel model = Create(Fails(), settings);

        int closes = 0;
        model.CloseRequested += (_, _) => closes++;

        await model.StartAsync();

        model.KeepDialogOpen = false;

        closes.ShouldBe(0);
    }

    [AvaloniaFact]
    public void Kutu_ayara_ters_yazilir()
    {
        InMemorySettingsStore settings = new();

        using ProcessDialogViewModel model = Create(Succeeds(), settings);

        model.KeepDialogOpen = false;
        settings.Current.General.CloseProcessDialog.ShouldBeTrue();

        model.KeepDialogOpen = true;
        settings.Current.General.CloseProcessDialog.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Diyalog_ayarlari_kapaliyken_pencere_hicbir_zaman_kendi_kapanmaz()
    {
        InMemorySettingsStore settings = new();
        settings.Current.General.CloseProcessDialog = true;

        using ProcessDialogViewModel model = Create(Succeeds(), settings, useDialogSettings: false);

        int closes = CountCloseRequests(model);

        await model.StartAsync();

        model.State.ShouldBe(ProcessDialogState.Success);
        closes.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Cikti_parca_parca_ekrana_yazilir()
    {
        using ProcessDialogViewModel model = Create((sink, _) =>
        {
            sink.Report(new GitOutputChunk("remote: Enume", IsError: false));
            sink.Report(new GitOutputChunk("rating objects: 12\n", IsError: false));
            sink.Report(new GitOutputChunk("done.\n", IsError: false));
            return Task.FromResult(true);
        });

        await model.StartAsync();

        model.Output.ShouldBe("remote: Enumerating objects: 12\ndone.");
        model.HasOutput.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Ilerleme_yuzdesi_gelince_cubuk_belirli_hale_gelir()
    {
        using ProcessDialogViewModel model = Create((sink, _) =>
        {
            sink.Report(new GitOutputChunk("Receiving objects:  42% (42/100)\r", IsError: true));
            return Task.FromResult(true);
        });

        model.IsProgressIndeterminate.ShouldBeTrue();

        await model.StartAsync();

        model.IsProgressIndeterminate.ShouldBeFalse();
        model.ProgressPercent.ShouldBe(42);
    }

    [AvaloniaFact]
    public async Task Bilinmeyen_hata_ciktiya_dusurulur_ve_islem_basarisiz_olur()
    {
        using ProcessDialogViewModel model = Create((_, _) =>
            throw new InvalidOperationException("beklenmedik"));

        await model.StartAsync();

        model.State.ShouldBe(ProcessDialogState.Error);
        model.Output.ShouldContain("beklenmedik");
    }

    [AvaloniaFact]
    public async Task Yeniden_deneme_ciktiyi_sifirlar()
    {
        int runs = 0;

        using ProcessDialogViewModel model = Create((sink, _) =>
        {
            runs++;
            sink.Report(new GitOutputChunk($"deneme {runs}\n", IsError: false));
            return Task.FromResult(runs > 1);
        });

        await model.StartAsync();

        model.State.ShouldBe(ProcessDialogState.Error);
        model.Output.ShouldBe("deneme 1");

        await model.RetryAsync();

        model.State.ShouldBe(ProcessDialogState.Success);
        model.Output.ShouldBe("deneme 2");
        model.Output.ShouldNotContain("deneme 1");
    }

    [AvaloniaFact]
    public async Task Hazir_hata_penceresi_hicbir_sey_calistirmaz()
    {
        using ProcessDialogViewModel model = new(
            ProcessDialogRequest.ForError("Push failed", "! [rejected] main -> main"));

        int closes = CountCloseRequests(model);

        model.CanAbort.ShouldBeFalse();
        model.UseDialogSettings.ShouldBeFalse();

        await model.StartAsync();

        model.State.ShouldBe(ProcessDialogState.Error);
        model.Output.ShouldContain("! [rejected] main -> main");
        closes.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Baslik_ilerleme_satirini_gosterir_bitince_geri_doner()
    {
        TaskCompletionSource gate = new();

        using ProcessDialogViewModel model = Create(async (sink, _) =>
        {
            sink.Report(new GitOutputChunk("Receiving objects:  10% (1/10)\r", IsError: true));
            await gate.Task;
            return true;
        });

        Task running = model.StartAsync();

        model.Title.ShouldNotBe("Process");

        gate.SetResult();
        await running;

        model.Title.ShouldBe("Process");
    }
}
