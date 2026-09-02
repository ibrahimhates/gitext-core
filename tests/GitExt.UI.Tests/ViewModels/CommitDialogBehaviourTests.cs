using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T03 — what the commit screen does around a commit, against GitExtensions' <c>FormCommit</c>.
/// </summary>
/// <remarks>
/// The close rule is <c>FormCommit.DoCommit</c>'s: <c>CloseCommitDialogAfterCommit</c> closes the
/// window straight away; otherwise anything left unstaged keeps it open, and only when nothing is
/// left does <c>CloseCommitDialogAfterLastCommit</c> decide. Both default to <see langword="true"/>
/// there, so out of the box the window closes — which is the behaviour asked for.
/// </remarks>
public class CommitDialogBehaviourTests
{
    private sealed class RecordingPrompt : ICommitPrompt
    {
        public List<string> Asked { get; } = [];

        public AmendConfirmation AmendAnswer { get; set; } = new() { Confirmed = true };

        public NoStagedChangesChoice NoStagedAnswer { get; set; } = NoStagedChangesChoice.Cancel;

        public bool LastCanStageAll { get; private set; }

        public Task<AmendConfirmation> ConfirmAmendAsync()
        {
            Asked.Add("amend");
            return Task.FromResult(AmendAnswer);
        }

        public Task<bool> ConfirmEmptyMergeCommitAsync()
        {
            Asked.Add("empty-merge");
            return Task.FromResult(true);
        }

        public Task<NoStagedChangesChoice> AskNoStagedChangesAsync(bool canStageAll)
        {
            Asked.Add("no-staged");
            LastCanStageAll = canStageAll;

            return Task.FromResult(NoStagedAnswer);
        }

        public Task ReportMergeConflictsAsync()
        {
            Asked.Add("conflicts");
            return Task.CompletedTask;
        }

        public Task ReportMissingCommitMessageAsync()
        {
            Asked.Add("no-message");
            return Task.CompletedTask;
        }

        public List<string> ValidationQuestions { get; } = [];

        public bool ValidationAnswer { get; set; } = true;

        public NotOnBranchChoice NotOnBranchAnswer { get; set; } = NotOnBranchChoice.Continue;

        public Task<bool> ConfirmCommitValidationAsync(string question)
        {
            Asked.Add("validation");
            ValidationQuestions.Add(question);

            return Task.FromResult(ValidationAnswer);
        }

        public Task<NotOnBranchChoice> AskNotOnBranchAsync()
        {
            Asked.Add("not-on-branch");
            return Task.FromResult(NotOnBranchAnswer);
        }
    }

    private static FileStatus Staged(string path) =>
        new() { Path = RepositoryPath.Parse(path), StagedChange = FileChangeKind.Modified };

    private static FileStatus Unstaged(string path) =>
        new() { Path = RepositoryPath.Parse(path), UnstagedChange = FileChangeKind.Modified };

    private static FileStatus Conflicted(string path) =>
        new() { Path = RepositoryPath.Parse(path), Conflict = ConflictKind.BothModified };

    private sealed record Harness(
        WorkingTreeViewModel Model,
        FakeStatusReader Status,
        FakeCommitWriter Commits,
        RecordingPrompt Prompt,
        InMemorySettingsStore Settings);

    private static async Task<Harness> CreateAsync(params FileStatus[] entries)
    {
        FakeStatusReader status = new(entries.Length > 0 ? entries : [Staged("a.txt")]);
        FakeStagingWriter staging = new(status);
        FakeCommitWriter commits = new(status);
        RecordingPrompt prompt = new();
        InMemorySettingsStore settings = new();

        WorkingTreeViewModel model = new(
            status, staging, commits, new DiffViewModel(new FakeDiffReader()))
        {
            Prompt = prompt,
            Settings = settings,
        };

        await model.OpenAsync("/tmp/depo");
        model.Message.Text = "konu";

        return new Harness(model, status, commits, prompt, settings);
    }

    private static async Task<int> CountClosesAsync(WorkingTreeViewModel model, Func<Task> act)
    {
        int closes = 0;
        model.CloseRequested += (_, _) => closes++;

        await act();

        return closes;
    }

    [AvaloniaFact]
    public async Task Commit_sonrasi_pencere_varsayilan_olarak_kapanir()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.CloseDialogAfterEachCommit.ShouldBeTrue();

        int closes = await CountClosesAsync(harness.Model, () => harness.Model.CommitAsync());

        harness.Commits.Messages.Count.ShouldBe(1);
        closes.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Ayar_kapaliyken_hazirlanmamis_dosya_kalirsa_pencere_acik_kalir()
    {
        Harness harness = await CreateAsync(Staged("a.txt"), Unstaged("b.txt"));

        harness.Settings.Current.Commit.CloseDialogAfterEachCommit = false;

        // The commit takes the staged file; the unstaged one is still there to carry on with.
        harness.Status.OnRead = () =>
        {
            if (harness.Status.Entries.Count == 0)
            {
                harness.Status.Entries.Add(Unstaged("b.txt"));
            }
        };

        int closes = await CountClosesAsync(harness.Model, () => harness.Model.CommitAsync());

        harness.Model.Unstaged.Count.ShouldBeGreaterThan(0);
        closes.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Ayar_kapaliyken_son_committen_sonra_pencere_kapanir()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.CloseDialogAfterEachCommit = false;
        harness.Settings.Current.Commit.CloseDialogAfterAllChangesCommitted.ShouldBeTrue();

        int closes = await CountClosesAsync(harness.Model, () => harness.Model.CommitAsync());

        harness.Model.Unstaged.ShouldBeEmpty();
        closes.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Iki_ayar_da_kapaliyken_pencere_hic_kapanmaz()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.CloseDialogAfterEachCommit = false;
        harness.Settings.Current.Commit.CloseDialogAfterAllChangesCommitted = false;

        int closes = await CountClosesAsync(harness.Model, () => harness.Model.CommitAsync());

        harness.Commits.Messages.Count.ShouldBe(1);
        closes.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Basarisiz_commit_pencereyi_kapatmaz()
    {
        Harness harness = await CreateAsync();

        harness.Commits.Failure = new GitExt.Core.Git.GitException(
            GitExt.Core.Git.GitFailureKind.Unknown,
            "hata",
            "git commit -F -",
            1,
            "pre-commit reddetti");

        int closes = await CountClosesAsync(harness.Model, () => harness.Model.CommitAsync());

        closes.ShouldBe(0);
        harness.Model.ErrorMessage.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task Cozulmemis_cakisma_varken_commit_yapilmaz()
    {
        Harness harness = await CreateAsync(Conflicted("a.txt"), Staged("b.txt"));

        harness.Model.HasConflicts.ShouldBeTrue();

        int closes = await CountClosesAsync(harness.Model, () => harness.Model.CommitAsync());

        harness.Prompt.Asked.ShouldContain("conflicts");
        harness.Commits.Messages.ShouldBeEmpty();
        closes.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Amend_isaretliyken_onay_sorulur()
    {
        Harness harness = await CreateAsync();

        harness.Model.Amend = true;

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldContain("amend");
        harness.Commits.Messages.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Amend_onayina_hayir_denince_commit_yapilmaz()
    {
        Harness harness = await CreateAsync();

        harness.Prompt.AmendAnswer = AmendConfirmation.Cancelled;
        harness.Model.Amend = true;

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldContain("amend");
        harness.Commits.Messages.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Amend_onayinda_bir_daha_sorma_ayara_yazilir_ve_bir_daha_sorulmaz()
    {
        Harness harness = await CreateAsync();

        harness.Prompt.AmendAnswer = new AmendConfirmation { Confirmed = true, DoNotAskAgain = true };
        harness.Model.Amend = true;

        await harness.Model.CommitAsync();

        harness.Settings.Current.Commit.DontConfirmAmend.ShouldBeTrue();

        harness.Prompt.Asked.Clear();
        harness.Model.Amend = true;

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldNotContain("amend");
    }

    [AvaloniaFact]
    public async Task Amend_isaretli_degilken_onay_sorulmaz()
    {
        Harness harness = await CreateAsync();

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldNotContain("amend");
        harness.Commits.Messages.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Onaylayici_yokken_commit_eskisi_gibi_calisir()
    {
        FakeStatusReader status = new([Staged("a.txt")]);
        FakeCommitWriter commits = new(status);

        WorkingTreeViewModel model = new(
            status, new FakeStagingWriter(status), commits, new DiffViewModel(new FakeDiffReader()));

        await model.OpenAsync("/tmp/depo");
        model.Message.Text = "konu";

        int closes = await CountClosesAsync(model, () => model.CommitAsync());

        commits.Messages.Count.ShouldBe(1);

        // No settings store either, so the defaults apply — and GitExtensions' default closes.
        closes.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Dogrulama_sinirlari_kapaliyken_soru_sorulmaz()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.ValidationMaxCharsFirstLine.ShouldBe(0);
        harness.Settings.Current.Commit.ValidationMaxCharsPerLine.ShouldBe(0);
        harness.Settings.Current.Commit.ValidationSecondLineMustBeEmpty.ShouldBeFalse();

        harness.Model.Message.Text = new string('u', 300);

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldNotContain("validation");
        harness.Commits.Messages.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Ilk_satir_siniri_asilinca_sorulur()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.ValidationMaxCharsFirstLine = 20;
        harness.Model.Message.Text = new string('u', 50);

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldContain("validation");
        harness.Commits.Messages.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Ilk_satir_sorusuna_hayir_denince_commit_yapilmaz()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.ValidationMaxCharsFirstLine = 20;
        harness.Prompt.ValidationAnswer = false;
        harness.Model.Message.Text = new string('u', 50);

        await harness.Model.CommitAsync();

        harness.Commits.Messages.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Sinir_altindaki_ilk_satir_sorulmaz()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.ValidationMaxCharsFirstLine = 50;
        harness.Model.Message.Text = "kisa konu";

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldNotContain("validation");
    }

    [AvaloniaFact]
    public async Task Uzun_satir_sorusu_o_satiri_gosterir()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.ValidationMaxCharsPerLine = 10;
        harness.Model.Message.Text = "konu\n\ncok uzun bir govde satiri";

        await harness.Model.CommitAsync();

        harness.Prompt.ValidationQuestions.ShouldNotBeEmpty();
        harness.Prompt.ValidationQuestions[0].ShouldContain("cok uzun bir govde satiri");
    }

    [AvaloniaFact]
    public async Task Ikinci_satir_bos_degilse_sorulur()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.ValidationSecondLineMustBeEmpty = true;
        harness.Model.Message.Text = "konu\ngovde hemen ikinci satirda";

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldContain("validation");
    }

    [AvaloniaFact]
    public async Task Ikinci_satir_bosken_sorulmaz()
    {
        Harness harness = await CreateAsync();

        harness.Settings.Current.Commit.ValidationSecondLineMustBeEmpty = true;
        harness.Model.Message.Text = "konu\n\ngovde";

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldNotContain("validation");
        harness.Commits.Messages.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Ayrik_HEADde_commit_oncesi_uyari_verilir()
    {
        Harness harness = await CreateAsync();

        harness.Model.IsDetachedHead = true;

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldContain("not-on-branch");
        harness.Commits.Messages.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Ayrik_HEAD_uyarisina_hayir_denince_commit_yapilmaz()
    {
        Harness harness = await CreateAsync();

        harness.Model.IsDetachedHead = true;
        harness.Prompt.NotOnBranchAnswer = NotOnBranchChoice.Cancel;

        await harness.Model.CommitAsync();

        harness.Commits.Messages.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Ayrik_HEAD_uyarisi_susturulabilir()
    {
        Harness harness = await CreateAsync();

        harness.Model.IsDetachedHead = true;
        harness.Settings.Current.Commit.DontConfirmCommitIfNoBranch = true;

        await harness.Model.CommitAsync();

        harness.Prompt.Asked.ShouldNotContain("not-on-branch");
        harness.Commits.Messages.Count.ShouldBe(1);
    }
}
