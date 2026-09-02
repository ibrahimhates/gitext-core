using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T07 — the questions asked before a pull, against <c>FormPull</c>.
/// </summary>
/// <remarks>
/// Both gates can lose something: pruning deletes every remote-tracking ref that is gone from the
/// remote, and pulling on a detached HEAD moves a HEAD that belongs to no branch.
/// </remarks>
public class PullConfirmationTests
{
    private sealed class RecordingConfirmer : IPullConfirmer
    {
        public List<string> Asked { get; } = [];

        public PushConfirmation PruneAnswer { get; set; } = PushConfirmation.Accepted;

        public bool DetachedAnswer { get; set; } = true;

        public string? LastRemote { get; private set; }

        public Task<PushConfirmation> ConfirmPruneAsync(string remote)
        {
            Asked.Add("prune");
            LastRemote = remote;

            return Task.FromResult(PruneAnswer);
        }

        public Task<bool> ConfirmDetachedHeadAsync()
        {
            Asked.Add("detached");
            return Task.FromResult(DetachedAnswer);
        }
    }

    private sealed record Harness(
        PullViewModel Model,
        FakeFetchWriter Fetch,
        FakePullWriter Pull,
        RecordingConfirmer Confirmer,
        InMemorySettingsStore Settings);

    private static async Task<Harness> CreateAsync(bool detached = false)
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakeFetchWriter fetch = new();
        FakePullWriter pull = new();
        RecordingConfirmer confirmer = new();
        InMemorySettingsStore settings = new();

        PullViewModel model = new(
            remotes, fetch, pull, settings: settings, confirmer: confirmer)
        {
            IsDetachedHead = detached,
        };

        await model.LoadAsync(
            "/repo",
            "main",
            [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1)).Ref]);

        return new Harness(model, fetch, pull, confirmer, settings);
    }

    [AvaloniaFact]
    public async Task Budama_kapaliyken_soru_sorulmaz()
    {
        Harness harness = await CreateAsync();

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldNotContain("prune");
        harness.Pull.Pulled.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Budama_acikken_uyari_verilir()
    {
        Harness harness = await CreateAsync();

        harness.Model.Prune = true;

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldContain("prune");
        harness.Confirmer.LastRemote.ShouldBe("origin");
        harness.Pull.Pulled.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Budama_uyarisina_hayir_denince_hicbir_sey_calismaz()
    {
        Harness harness = await CreateAsync();

        harness.Model.Prune = true;
        harness.Confirmer.PruneAnswer = PushConfirmation.Declined;

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldContain("prune");
        harness.Pull.Pulled.ShouldBeEmpty();
        harness.Fetch.Fetched.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Etiket_budamasi_da_ayni_uyariyi_getirir()
    {
        Harness harness = await CreateAsync();

        harness.Model.Action = PullAction.FetchOnly;
        harness.Model.PruneTags = true;

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldContain("prune");
    }

    [AvaloniaFact]
    public async Task Budama_uyarisinda_bir_daha_sorma_ayara_yazilir()
    {
        Harness harness = await CreateAsync();

        harness.Model.Prune = true;
        harness.Confirmer.PruneAnswer = new PushConfirmation
        {
            Confirmed = true,
            DoNotAskAgain = true,
        };

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Settings.Current.Confirmations.DontConfirmFetchAndPrune.ShouldBeTrue();

        harness.Confirmer.Asked.Clear();

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldNotContain("prune");
        harness.Pull.Pulled.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task Ayrik_HEADde_uyari_verilir()
    {
        Harness harness = await CreateAsync(detached: true);

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldContain("detached");
        harness.Pull.Pulled.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Ayrik_HEAD_uyarisina_hayir_denince_cekilmez()
    {
        Harness harness = await CreateAsync(detached: true);

        harness.Confirmer.DetachedAnswer = false;

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldContain("detached");
        harness.Pull.Pulled.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Dal_uzerindeyken_ayrik_HEAD_sorusu_sorulmaz()
    {
        Harness harness = await CreateAsync();

        await harness.Model.RunCommand.ExecuteAsync(null);

        harness.Confirmer.Asked.ShouldNotContain("detached");
    }

    [AvaloniaFact]
    public async Task Onaylayici_yokken_cekme_eskisi_gibi_calisir()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakeFetchWriter fetch = new();
        FakePullWriter pull = new();

        PullViewModel model = new(remotes, fetch, pull) { IsDetachedHead = true };

        await model.LoadAsync(
            "/repo",
            "main",
            [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1)).Ref]);

        model.Prune = true;

        await model.RunCommand.ExecuteAsync(null);

        pull.Pulled.Count.ShouldBe(1);
    }
}
