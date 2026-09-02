using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T06 — what is offered after a rejected push, against
/// <c>FormPush.AskForAutoPullOnPushRejectedAction</c>.
/// </summary>
/// <remarks>
/// The pull options are offered only for the current branch — a branch that is not checked out
/// cannot be pulled into — and the remembered answer skips the question entirely, including a
/// remembered "do nothing".
/// </remarks>
public class PushRejectionTests
{
    private sealed class RecordingConfirmer : IPushConfirmer
    {
        public int Asked { get; private set; }

        public PushRejectedRequest? LastRequest { get; private set; }

        public PushRejectedDecision Decision { get; set; } = PushRejectedDecision.Cancelled;

        public Task<PushConfirmation> ConfirmNewBranchAsync(string remoteBranch, string remote) =>
            Task.FromResult(PushConfirmation.Accepted);

        public Task<TrackingReferenceAnswer> ConfirmTrackingReferenceAsync(
            string localBranch,
            string remoteBranch) =>
            Task.FromResult(TrackingReferenceAnswer.No);

        public Task<bool> ConfirmConfigureRemoteAsync() => Task.FromResult(false);

        public Task ReportErrorAsync(string message) => Task.CompletedTask;

        public Task<PushRejectedDecision> AskRejectedActionAsync(PushRejectedRequest request)
        {
            Asked++;
            LastRequest = request;

            return Task.FromResult(Decision);
        }
    }

    private sealed record Harness(
        PushViewModel Model,
        FakePushWriter Push,
        RecordingConfirmer Confirmer,
        InMemorySettingsStore Settings,
        List<PushRejectedAction> Recoveries);

    private static async Task<Harness> CreateAsync(bool rejected = true)
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushWriter push = new();

        if (rejected)
        {
            push.Result = new PushResult
            {
                Refs =
                [
                    new PushRefResult('!', "refs/heads/main", "refs/heads/main", "[rejected]", "fetch first"),
                ],
            };
        }

        RecordingConfirmer confirmer = new();
        InMemorySettingsStore settings = new();
        List<PushRejectedAction> recoveries = [];

        PushViewModel model = new(remotes, push, confirmer: confirmer, settings: settings)
        {
            RecoveryRequested = action =>
            {
                recoveries.Add(action);
                return Task.CompletedTask;
            },
        };

        await model.LoadAsync(
            "/repo",
            "main",
            [FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true)]);

        return new Harness(model, push, confirmer, settings, recoveries);
    }

    [AvaloniaFact]
    public async Task Basarili_pushta_soru_sorulmaz()
    {
        Harness harness = await CreateAsync(rejected: false);

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Reddedilen_pushta_ne_yapilacagi_sorulur()
    {
        Harness harness = await CreateAsync();

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldBe(1);
        harness.Confirmer.LastRequest!.Remote.ShouldBe("origin");
    }

    [AvaloniaFact]
    public async Task Bulunulan_dalda_pull_secenekleri_sunulur()
    {
        Harness harness = await CreateAsync();

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.LastRequest!.AllOptions.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Baska_bir_dalda_yalniz_zorlama_sunulur()
    {
        Harness harness = await CreateAsync();

        harness.Model.SourceBranch = "feature";

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.LastRequest!.AllOptions.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Vazgecilince_hicbir_sey_yapilmaz()
    {
        Harness harness = await CreateAsync();

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Recoveries.ShouldBeEmpty();
        harness.Push.Pushed.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Rebase_ile_cek_secilince_kurtarma_kancasi_cagrilir()
    {
        Harness harness = await CreateAsync();

        harness.Confirmer.Decision = new PushRejectedDecision
        {
            Action = PushRejectedAction.PullRebase,
        };

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Recoveries.ShouldBe([PushRejectedAction.PullRebase]);
    }

    [AvaloniaFact]
    public async Task Zorla_gonder_secilince_kira_ile_yeniden_denenir()
    {
        Harness harness = await CreateAsync();

        harness.Confirmer.Decision = new PushRejectedDecision
        {
            Action = PushRejectedAction.ForceWithLease,
        };

        harness.Model.ForceWithLease.ShouldBeFalse();

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Model.ForceWithLease.ShouldBeTrue();
        harness.Push.Pushed.Count.ShouldBe(2);
        harness.Push.Pushed[^1].ForceWithLease.ShouldBeTrue();

        // 🔴 Only once. The forced push is rejected here too, and the fake keeps answering
        // "force" — without the guard this would push in a loop.
        harness.Confirmer.Asked.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Bir_daha_sorma_isaretlenince_ayara_yazilir_ve_bir_daha_sorulmaz()
    {
        Harness harness = await CreateAsync();

        harness.Confirmer.Decision = new PushRejectedDecision
        {
            Action = PushRejectedAction.PullMerge,
            DoNotAskAgain = true,
        };

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Settings.Current.Confirmations.AutoPullOnPushRejected.ShouldBe("merge");
        harness.Confirmer.Asked.ShouldBe(1);

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldBe(1);
        harness.Recoveries.ShouldBe([PushRejectedAction.PullMerge, PushRejectedAction.PullMerge]);
    }

    [AvaloniaFact]
    public async Task Hatirlanan_hicbir_sey_yapma_da_soruyu_susturur()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Confirmations.AutoPullOnPushRejected = "none";

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldBe(0);
        harness.Recoveries.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Tanimsiz_ayar_degeri_soruyu_engellemez()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Confirmations.AutoPullOnPushRejected = "bozuk-deger";

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Kurtarma_kancasi_yokken_kullaniciya_ne_yapacagi_yazilir()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushWriter push = new();

        push.Result = new PushResult
        {
            Refs =
            [
                new PushRefResult('!', "refs/heads/main", "refs/heads/main", "[rejected]", "fetch first"),
            ],
        };

        RecordingConfirmer confirmer = new()
        {
            Decision = new PushRejectedDecision { Action = PushRejectedAction.PullMerge },
        };

        PushViewModel model = new(remotes, push, confirmer: confirmer);

        await model.LoadAsync(
            "/repo",
            "main",
            [FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true)]);

        await model.RunCommand.ExecuteAsync(null);

        model.Advice.ShouldNotBeNull();
    }
}
