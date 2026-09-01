using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T04 — the questions asked before a push, against GitExtensions' <c>FormPush.PushChanges</c>.
/// </summary>
/// <remarks>
/// The gate that matters most is the new-branch one: pushing a branch the remote has never seen is
/// the one push that cannot be undone by pushing again, and GitExtensions asks before it.
/// </remarks>
public class PushConfirmationTests
{
    private sealed class RecordingConfirmer : IPushConfirmer
    {
        public List<string> Asked { get; } = [];

        public PushConfirmation NewBranchAnswer { get; set; } = PushConfirmation.Accepted;

        public TrackingReferenceAnswer TrackingAnswer { get; set; } = TrackingReferenceAnswer.No;

        public string? LastError { get; private set; }

        public string? NewBranchRemote { get; private set; }

        public string? NewBranchName { get; private set; }

        public Task<PushConfirmation> ConfirmNewBranchAsync(string remoteBranch, string remote)
        {
            Asked.Add("new-branch");
            NewBranchName = remoteBranch;
            NewBranchRemote = remote;

            return Task.FromResult(NewBranchAnswer);
        }

        public Task<TrackingReferenceAnswer> ConfirmTrackingReferenceAsync(
            string localBranch,
            string remoteBranch)
        {
            Asked.Add("tracking");
            return Task.FromResult(TrackingAnswer);
        }

        public Task<bool> ConfirmConfigureRemoteAsync()
        {
            Asked.Add("configure-remote");
            return Task.FromResult(false);
        }

        public Task ReportErrorAsync(string message)
        {
            Asked.Add("error");
            LastError = message;

            return Task.CompletedTask;
        }

        public Task<PushRejectedDecision> AskRejectedActionAsync(PushRejectedRequest request)
        {
            Asked.Add("rejected");
            return Task.FromResult(PushRejectedDecision.Cancelled);
        }
    }

    private static (PushViewModel Model, FakePushWriter Push, RecordingConfirmer Confirmer,
        InMemorySettingsStore Settings) Create(bool remoteKnowsBranch = true)
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushWriter push = new();

        push.Plan = push.Plan with
        {
            RemoteBranches = remoteKnowsBranch ? ["main"] : [],
            RemoteTipObjectId = remoteKnowsBranch ? "aaaabbbbcccc" : null,
            HasUpstream = remoteKnowsBranch,
        };

        RecordingConfirmer confirmer = new();
        InMemorySettingsStore settings = new();

        PushViewModel model = new(remotes, push, confirmer: confirmer, settings: settings);

        return (model, push, confirmer, settings);
    }

    private static Task LoadAsync(PushViewModel model) =>
        model.LoadAsync(
            "/repo",
            "main",
            [FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true)]);

    [AvaloniaFact]
    public async Task Uzakta_olan_dalda_yeni_dal_sorusu_sorulmaz()
    {
        (PushViewModel model, FakePushWriter push, RecordingConfirmer confirmer, _) = Create();

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldNotContain("new-branch");
        push.Pushed.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Uzakta_olmayan_dal_gonderilirken_uyari_verilir()
    {
        (PushViewModel model, FakePushWriter push, RecordingConfirmer confirmer, _) =
            Create(remoteKnowsBranch: false);

        await LoadAsync(model);

        model.WouldCreateRemoteBranch.ShouldBeTrue();

        await model.RunCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldContain("new-branch");
        confirmer.NewBranchName.ShouldBe("main");
        confirmer.NewBranchRemote.ShouldBe("origin");
        push.Pushed.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Yeni_dal_uyarisina_hayir_denince_push_yapilmaz()
    {
        (PushViewModel model, FakePushWriter push, RecordingConfirmer confirmer, _) =
            Create(remoteKnowsBranch: false);

        confirmer.NewBranchAnswer = PushConfirmation.Declined;

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldContain("new-branch");
        push.Pushed.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Bir_daha_sorma_isaretlenince_ayara_yazilir_ve_bir_daha_sorulmaz()
    {
        (PushViewModel model, FakePushWriter push, RecordingConfirmer confirmer,
            InMemorySettingsStore settings) = Create(remoteKnowsBranch: false);

        confirmer.NewBranchAnswer = new PushConfirmation
        {
            Confirmed = true,
            DoNotAskAgain = true,
        };

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        settings.Current.Confirmations.DontConfirmPushNewBranch.ShouldBeTrue();
        push.Pushed.Count.ShouldBe(1);

        confirmer.Asked.Clear();

        await model.RunCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldNotContain("new-branch");
        push.Pushed.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task Ayar_kapaliyken_soru_bastan_sorulmaz()
    {
        (PushViewModel model, FakePushWriter push, RecordingConfirmer confirmer,
            InMemorySettingsStore settings) = Create(remoteKnowsBranch: false);

        settings.Current.Confirmations.DontConfirmPushNewBranch = true;

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldNotContain("new-branch");
        push.Pushed.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Takip_referansi_yoksa_sorulur_ve_evet_upstream_kurar()
    {
        (PushViewModel model, FakePushWriter push, RecordingConfirmer confirmer, _) =
            Create(remoteKnowsBranch: false);

        confirmer.TrackingAnswer = TrackingReferenceAnswer.Yes;

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldContain("tracking");
        model.SetUpstream.ShouldBeTrue();
        push.Pushed.Single().SetUpstream.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Takip_referansina_hayir_denince_upstream_kurulmaz_ama_push_olur()
    {
        (PushViewModel model, FakePushWriter push, RecordingConfirmer confirmer, _) =
            Create(remoteKnowsBranch: false);

        confirmer.TrackingAnswer = TrackingReferenceAnswer.No;

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldContain("tracking");
        push.Pushed.Single().SetUpstream.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Takip_referansinda_vazgec_pushu_tumden_iptal_eder()
    {
        (PushViewModel model, FakePushWriter push, RecordingConfirmer confirmer, _) =
            Create(remoteKnowsBranch: false);

        confirmer.TrackingAnswer = TrackingReferenceAnswer.Cancel;

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldContain("tracking");
        push.Pushed.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Upstream_zaten_varsa_takip_sorusu_sorulmaz()
    {
        (PushViewModel model, FakePushWriter push, RecordingConfirmer confirmer, _) = Create();

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        confirmer.Asked.ShouldNotContain("tracking");
        push.Pushed.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Etiket_sekmesinde_etiket_secilmeden_push_yapilmaz()
    {
        // GitExtensions leaves the button enabled and reports "You need to select a tag to push"
        // when it is pressed; here CanRun keeps the button disabled, so the push never starts.
        // Same outcome, one screen earlier — IPushConfirmer.ReportErrorAsync stays as the guard
        // behind it.
        (PushViewModel model, FakePushWriter push, _, _) = Create();

        await LoadAsync(model);

        model.Tab = PushTab.Tag;

        model.CanRun.ShouldBeFalse();

        await model.RunCommand.ExecuteAsync(null);

        push.Pushed.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Onaylayici_yoksa_akis_kapilarsiz_calisir()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushWriter push = new();
        push.Plan = push.Plan with { RemoteBranches = [], RemoteTipObjectId = null, HasUpstream = false };

        PushViewModel model = new(remotes, push);

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        push.Pushed.Count.ShouldBe(1);
    }
}
