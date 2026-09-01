using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T01 — a write operation runs <b>inside</b> the process window, as it does in GitExtensions.
/// </summary>
/// <remarks>
/// The window is not decoration: it is where the user sees git's own output. A push that runs past
/// it shows nothing, which is the state this project was in.
/// </remarks>
public class ProcessDialogIntegrationTests
{
    private sealed class RecordingProcessPrompt : IProcessDialogPrompt
    {
        public List<ProcessDialogViewModel> Shown { get; } = [];

        public string LastOutput { get; private set; } = string.Empty;

        public string LastCommandLine { get; private set; } = string.Empty;

        public async Task<bool> ShowAsync(ProcessDialogViewModel model)
        {
            Shown.Add(model);
            LastCommandLine = model.CommandLine;

            await model.StartAsync();

            LastOutput = model.Output;

            return model.State is ProcessDialogState.Success;
        }
    }

    private static async Task<(PushViewModel Model, FakePushWriter Push, RecordingProcessPrompt Prompt)>
        CreateAsync()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushWriter push = new();
        RecordingProcessPrompt prompt = new();

        PushViewModel model = new(remotes, push, process: prompt);

        await model.LoadAsync(
            "/repo",
            "main",
            [FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true)]);

        return (model, push, prompt);
    }

    [AvaloniaFact]
    public async Task Push_surec_penceresinde_calisir()
    {
        (PushViewModel model, FakePushWriter push, RecordingProcessPrompt prompt) = await CreateAsync();

        await model.RunCommand.ExecuteAsync(null);

        prompt.Shown.Count.ShouldBe(1);
        push.Pushed.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Gitin_ciktisi_pencerede_gorunur()
    {
        (PushViewModel model, FakePushWriter push, RecordingProcessPrompt prompt) = await CreateAsync();

        push.ReportOutput =
        [
            "Enumerating objects: 5, done.\n",
            "To https://example.com/a.git\n",
            "   1234567..89abcde  main -> main\n",
        ];

        await model.RunCommand.ExecuteAsync(null);

        push.SeenOutput.ShouldNotBeNull();
        prompt.LastOutput.ShouldContain("Enumerating objects: 5, done.");
        prompt.LastOutput.ShouldContain("main -> main");
    }

    [AvaloniaFact]
    public async Task Calisan_komut_pencerede_yazili()
    {
        (PushViewModel model, _, RecordingProcessPrompt prompt) = await CreateAsync();

        await model.RunCommand.ExecuteAsync(null);

        prompt.LastCommandLine.ShouldStartWith("git push");
        prompt.LastCommandLine.ShouldContain("origin");
    }

    [AvaloniaFact]
    public async Task Reddedilen_push_pencerede_hata_sayilir()
    {
        (PushViewModel model, FakePushWriter push, RecordingProcessPrompt prompt) = await CreateAsync();

        push.Result = new PushResult
        {
            Refs =
            [
                new PushRefResult('!', "refs/heads/main", "refs/heads/main", "[rejected]", "fetch first"),
            ],
        };

        await model.RunCommand.ExecuteAsync(null);

        prompt.Shown.Count.ShouldBe(1);
        prompt.Shown[0].State.ShouldBe(ProcessDialogState.Error);
    }

    [AvaloniaFact]
    public async Task Pencere_yokken_push_yine_de_calisir()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushWriter push = new();
        PushViewModel model = new(remotes, push);

        await model.LoadAsync(
            "/repo",
            "main",
            [FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true)]);

        await model.RunCommand.ExecuteAsync(null);

        push.Pushed.Count.ShouldBe(1);
    }

    private static (PullViewModel Model, FakeFetchWriter Fetch, FakePullWriter Pull, RecordingProcessPrompt Prompt)
        CreatePull()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakeFetchWriter fetch = new();
        FakePullWriter pull = new();
        RecordingProcessPrompt prompt = new();

        PullViewModel model = new(remotes, fetch, pull, process: prompt);

        return (model, fetch, pull, prompt);
    }

    private static Task LoadPullAsync(PullViewModel model) =>
        model.LoadAsync(
            "/repo",
            "main",
            [FakeGitData.RemoteBranch("origin/main", FakeGitData.Sha(1)).Ref]);

    [AvaloniaFact]
    public async Task Pull_surec_penceresinde_calisir_ve_cikti_gorunur()
    {
        (PullViewModel model, _, FakePullWriter pull, RecordingProcessPrompt prompt) = CreatePull();

        pull.ReportOutput = ["Updating 1234567..89abcde\n", "Fast-forward\n"];

        await LoadPullAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        prompt.Shown.Count.ShouldBe(1);
        pull.Pulled.Count.ShouldBe(1);
        prompt.LastCommandLine.ShouldStartWith("git pull");
        prompt.LastOutput.ShouldContain("Fast-forward");
    }

    [AvaloniaFact]
    public async Task Fetch_surec_penceresinde_calisir_ve_cikti_gorunur()
    {
        (PullViewModel model, FakeFetchWriter fetch, _, RecordingProcessPrompt prompt) = CreatePull();

        fetch.ReportOutput = ["From https://example.com/a\n", " * branch  main -> FETCH_HEAD\n"];

        await LoadPullAsync(model);
        model.Action = PullAction.FetchOnly;

        await model.RunCommand.ExecuteAsync(null);

        prompt.Shown.Count.ShouldBe(1);
        fetch.Fetched.Count.ShouldBe(1);
        prompt.LastCommandLine.ShouldStartWith("git fetch");
        prompt.LastOutput.ShouldContain("FETCH_HEAD");
    }

    [AvaloniaFact]
    public async Task Pencere_yokken_pull_yine_de_calisir()
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakeFetchWriter fetch = new();
        FakePullWriter pull = new();
        PullViewModel model = new(remotes, fetch, pull);

        await LoadPullAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        pull.Pulled.Count.ShouldBe(1);
    }
}
