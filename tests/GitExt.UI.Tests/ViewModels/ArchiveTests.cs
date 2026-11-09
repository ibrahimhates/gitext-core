using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

public sealed class ArchiveTests
{
    private sealed class FakeArchiveWriter : IArchiveWriter
    {
        public Exception? Failure { get; set; }

        public List<ArchiveOptions> Written { get; } = [];

        public Task ArchiveAsync(string workingDirectory, ArchiveOptions options, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Written.Add(options);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingPrompt : IArchivePrompt
    {
        public ArchiveViewModel? Shown { get; private set; }

        public Task ShowAsync(ArchiveViewModel model)
        {
            Shown = model;
            return Task.CompletedTask;
        }
    }

    private static readonly CommitInfo Revision = FakeGitData.Commit(FakeGitData.Sha(3), [FakeGitData.Sha(2)], "three");
    private static readonly CommitInfo Other = FakeGitData.Commit(FakeGitData.Sha(1), [], "one");

    [Fact]
    public void Varsayilan_zip_ve_filtresiz()
    {
        ArchiveViewModel model = new("/depo", new FakeArchiveWriter(), Revision, null);

        ArchiveOptions options = model.BuildOptions("/out/a.zip");

        options.Format.ShouldBe(ArchiveFormat.Zip);
        options.Revision.ShouldBe(FakeGitData.Sha(3));
        options.Paths.ShouldBeEmpty();
        options.ChangedSince.ShouldBeNull();
        model.HasDiffRevision.ShouldBeFalse();
    }

    [Fact]
    public void Yollar_yalniz_isaretliyken_ve_satir_satir_geciyor()
    {
        ArchiveViewModel model = new("/depo", new FakeArchiveWriter(), Revision, null)
        {
            PathsText = " src \n\n docs/a b.md \n",
        };

        model.BuildOptions("/o").Paths.ShouldBeEmpty();

        model.FilterPaths = true;

        model.BuildOptions("/o").Paths.ShouldBe(["src", "docs/a b.md"]);
    }

    [Fact]
    public void Degisen_filtresi_ikinci_surum_olmadan_yok_sayiliyor()
    {
        ArchiveViewModel alone = new("/depo", new FakeArchiveWriter(), Revision, null) { ChangedOnly = true };
        ArchiveViewModel pair = new("/depo", new FakeArchiveWriter(), Revision, Other) { ChangedOnly = true };

        alone.BuildOptions("/o").ChangedSince.ShouldBeNull();
        pair.BuildOptions("/o").ChangedSince.ShouldBe(FakeGitData.Sha(1));
    }

    [Fact]
    public void Tar_secilince_bicim_ve_uzanti_degisiyor()
    {
        ArchiveViewModel model = new("/depo", new FakeArchiveWriter(), Revision, null) { IsTar = true };

        model.IsZip.ShouldBeFalse();
        model.BuildOptions("/o").Format.ShouldBe(ArchiveFormat.Tar);
        model.DefaultFileName.ShouldBe(CommitId.Parse(FakeGitData.Sha(3)).ToShortString() + ".tar");
    }

    [Fact]
    public async Task Degisen_dosya_yoksa_hata_gosteriliyor()
    {
        ArchiveViewModel model = new("/depo", new FakeArchiveWriter { Failure = new NothingToArchiveException() }, Revision, Other);

        (await model.ArchiveAsync("/o.zip", TestContext.Current.CancellationToken)).ShouldBeFalse();

        model.Error.ShouldBe("No file has changed between the two revisions.");
        model.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task Git_hatasi_gosteriliyor()
    {
        GitException failure = new(GitFailureKind.Unknown, "boom", "git archive", 128, "fatal: pathspec 'nope' did not match any files");
        ArchiveViewModel model = new("/depo", new FakeArchiveWriter { Failure = failure }, Revision, null);

        (await model.ArchiveAsync("/o.zip", TestContext.Current.CancellationToken)).ShouldBeFalse();

        model.Error.ShouldNotBeNullOrEmpty();
    }

    [AvaloniaFact]
    public async Task Ana_model_secili_commit_ve_ikinci_secimle_aciyor()
    {
        CapturingPrompt prompt = new();
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            advanced: new AdvancedOperationServices { Archive = new FakeArchiveWriter() })
        {
            ArchivePrompt = prompt,
        };

        await model.OpenRepositoryAsync("/depo");
        model.Commits.SelectedIndex = 0;

        await model.ArchiveAsync(Other);

        prompt.Shown!.Revision.ShouldBe(model.Commits.Rows[0].Commit);
        prompt.Shown.DiffRevision.ShouldBe(Other);
    }

    [AvaloniaFact]
    public async Task Diyalog_kaydedince_arsivleyip_kapaniyor()
    {
        FakeArchiveWriter writer = new();
        ArchiveViewModel model = new("/depo", writer, Revision, null);
        ArchiveDialog dialog = new() { DataContext = model, SavePathPrompt = _ => Task.FromResult<string?>("/out/r.zip") };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        await dialog.SaveAsync();

        writer.Written.Single().OutputPath.ShouldBe("/out/r.zip");
        dialog.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Diyalog_vazgecilince_arsivlemiyor_hatada_acik_kaliyor()
    {
        FakeArchiveWriter writer = new();
        ArchiveDialog cancelled = new() { DataContext = new ArchiveViewModel("/depo", writer, Revision, null), SavePathPrompt = _ => Task.FromResult<string?>(null) };
        cancelled.Show();
        await cancelled.SaveAsync();

        writer.Written.ShouldBeEmpty();
        cancelled.IsVisible.ShouldBeTrue();
        cancelled.Close();

        ArchiveViewModel failing = new("/depo", new FakeArchiveWriter { Failure = new NothingToArchiveException() }, Revision, Other);
        ArchiveDialog stays = new() { DataContext = failing, SavePathPrompt = _ => Task.FromResult<string?>("/o.zip") };
        stays.Show();
        await stays.SaveAsync();
        Dispatcher.UIThread.RunJobs();

        stays.IsVisible.ShouldBeTrue();
        stays.GetControl<TextBlock>("ErrorText").IsVisible.ShouldBeTrue();
        stays.Close();
    }

    private sealed class FakeDiffTool : IDiffToolRunner
    {
        public string? Tool { get; set; } = "fake";

        public List<(string First, string Second)> Opened { get; } = [];

        public Task<string?> GetConfiguredToolAsync(string workingDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tool);

        public Task RunDirDiffAsync(string workingDirectory, string firstRevision, string secondRevision, CancellationToken cancellationToken = default)
        {
            if (Tool is null)
            {
                return Task.FromException(new NoDiffToolConfiguredException());
            }

            Opened.Add((firstRevision, secondRevision));
            return Task.CompletedTask;
        }
    }

    private static async Task<MainWindowViewModel> MainAsync(FakeDiffTool tool)
    {
        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(FakeGitData.Refs()),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            advanced: new AdvancedOperationServices { DiffTool = tool });

        await model.OpenRepositoryAsync("/depo");
        return model;
    }

    [AvaloniaFact]
    public async Task Difftool_eskiden_yeniye_aciliyor()
    {
        FakeDiffTool tool = new();
        MainWindowViewModel model = await MainAsync(tool);

        await model.OpenWithDiffToolAsync(CommitId.Parse(FakeGitData.Sha(1)), CommitId.Parse(FakeGitData.Sha(3)));

        tool.Opened.ShouldBe([(FakeGitData.Sha(1), FakeGitData.Sha(3))]);
    }

    [AvaloniaFact]
    public async Task Difftool_ayarli_degilse_seritte_soyleniyor()
    {
        MainWindowViewModel model = await MainAsync(new FakeDiffTool { Tool = null });

        await model.OpenWithDiffToolAsync(CommitId.Parse(FakeGitData.Sha(1)), CommitId.Parse(FakeGitData.Sha(3)));

        model.BranchNotice.ShouldBe("No diff tool is configured. Set diff.tool (or diff.guitool) in git config.");
    }

    [Fact]
    public void Menu_ogesi_etkin()
    {
        RevisionGridContextMenu.Build(new RevisionGridMenuContext { CommitHash = FakeGitData.Sha(1) })
            .First(e => e.Id == RevisionGridMenuId.ArchiveRevision)
            .IsEnabled.ShouldBeTrue();
    }
}
