using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T03 — "Create new tag here…" and "Delete tag…".
/// </summary>
/// <remarks>
/// <para>
/// What is verified is <b>the arguments that reach <c>ITagWriter</c></b>, not that a window opened.
/// The measured git behaviours behind them:
/// </para>
/// <list type="bullet">
/// <item><c>git tag &lt;existing&gt;</c> → <c>fatal: tag 'v1.0' already exists</c>, exit 128.
/// <c>--force</c> turns it into <c>Updated tag 'v1.0' (was …)</c>. So <c>Force</c> must be off
/// unless the user asked for it — passing it always would silently move other people's tags.</item>
/// <item><c>-m</c> is what makes the tag <b>annotated</b> (<c>objecttype</c> goes from
/// <c>commit</c> to <c>tag</c>). A lightweight tag must carry no message.</item>
/// <item><c>git tag -d &lt;missing&gt;</c> → <c>error: tag 'x' not found.</c>, exit <b>1</b>.</item>
/// </list>
/// </remarks>
public class TagCommandTests
{
    private sealed class FakeTagWriter : ITagWriter
    {
        public List<TagOptions> Created { get; } = [];

        public List<string> Deleted { get; } = [];

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<GitTag>> ListAsync(
            string workingDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GitTag>>([]);

        public Task CreateAsync(
            string workingDirectory,
            TagOptions options,
            CancellationToken cancellationToken = default)
        {
            Created.Add(options);

            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task DeleteAsync(
            string workingDirectory,
            string name,
            CancellationToken cancellationToken = default)
        {
            Deleted.Add(name);

            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class FakeTagPrompt : ITagPrompt
    {
        public CreateTagDecision CreateAnswer { get; set; } = CreateTagDecision.Cancelled;

        public DeleteTagDecision DeleteAnswer { get; set; } = DeleteTagDecision.Cancelled;

        public CreateTagRequest? CreateRequest { get; private set; }

        public DeleteTagRequest? DeleteRequest { get; private set; }

        public Task<CreateTagDecision> RequestCreateAsync(CreateTagRequest request)
        {
            CreateRequest = request;
            return Task.FromResult(CreateAnswer);
        }

        public Task<DeleteTagDecision> RequestDeleteAsync(DeleteTagRequest request)
        {
            DeleteRequest = request;
            return Task.FromResult(DeleteAnswer);
        }
    }

    private static async Task<CommitListViewModel> LoadedAsync(
        ITagWriter? writer,
        ITagPrompt? prompt,
        IReadOnlyList<TagInfo>? tags = null)
    {
        CommitListViewModel model = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(5)),
            new FakeRefReader(FakeGitData.Refs(
                localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(5), isCurrent: true)],
                tags: tags ?? [FakeGitData.Tag("v1.0", FakeGitData.Sha(3))])),
            new FakeCommitSignatureReader(),
            new FakeDiffReader([FakeGitData.Diff("a.cs")]),
            writer)
        {
            TagPrompt = prompt,
        };

        await model.OpenAsync("/tmp/depo");
        model.SelectedIndex = 0;

        return model;
    }

    [AvaloniaFact]
    public async Task Hafif_etiket_mesajsiz_ve_zorlamasiz_olusturulur()
    {
        FakeTagWriter writer = new();
        FakeTagPrompt prompt = new()
        {
            CreateAnswer = new CreateTagDecision
            {
                Confirmed = true,
                Name = "v2.0",
                Kind = TagKind.Lightweight,
                Message = "bu mesaj yok sayilmali",
            },
        };

        CommitListViewModel model = await LoadedAsync(writer, prompt);

        await model.CreateTagCommand.ExecuteAsync(null);

        TagOptions options = writer.Created.ShouldHaveSingleItem();
        options.Name.ShouldBe("v2.0");
        options.Target.ShouldBe(model.Rows[0].Commit.Id.Value);

        // 🔴 SABOTAGE GUARD — `-m` is what turns a tag annotated; a lightweight tag must send none.
        options.Message.ShouldBeNull();

        // 🔴 SABOTAGE GUARD — `--force` silently moves an existing tag; it must only be sent when
        // the user ticked the box.
        options.Force.ShouldBeFalse();
        options.Sign.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Aciklamali_etiket_mesaji_gecer()
    {
        FakeTagWriter writer = new();
        FakeTagPrompt prompt = new()
        {
            CreateAnswer = new CreateTagDecision
            {
                Confirmed = true,
                Name = "v2.0",
                Kind = TagKind.Annotated,
                Message = "surum notlari",
            },
        };

        CommitListViewModel model = await LoadedAsync(writer, prompt);

        await model.CreateTagCommand.ExecuteAsync(null);

        writer.Created.ShouldHaveSingleItem().Message.ShouldBe("surum notlari");
    }

    [AvaloniaFact]
    public async Task Imzali_etiket_sign_bayragini_gecer()
    {
        FakeTagWriter writer = new();
        FakeTagPrompt prompt = new()
        {
            CreateAnswer = new CreateTagDecision
            {
                Confirmed = true,
                Name = "v2.0",
                Kind = TagKind.SignWithDefaultKey,
                Message = "imzali",
            },
        };

        CommitListViewModel model = await LoadedAsync(writer, prompt);

        await model.CreateTagCommand.ExecuteAsync(null);

        TagOptions options = writer.Created.ShouldHaveSingleItem();
        options.Sign.ShouldBeTrue();
        options.Message.ShouldBe("imzali");
    }

    [AvaloniaFact]
    public async Task Zorla_isaretliyse_force_gecer()
    {
        FakeTagWriter writer = new();
        FakeTagPrompt prompt = new()
        {
            CreateAnswer = new CreateTagDecision { Confirmed = true, Name = "v1.0", Force = true },
        };

        CommitListViewModel model = await LoadedAsync(writer, prompt);

        await model.CreateTagCommand.ExecuteAsync(null);

        writer.Created.ShouldHaveSingleItem().Force.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Iptal_edilirse_git_calismaz()
    {
        FakeTagWriter writer = new();
        FakeTagPrompt prompt = new() { CreateAnswer = CreateTagDecision.Cancelled };

        CommitListViewModel model = await LoadedAsync(writer, prompt);

        await model.CreateTagCommand.ExecuteAsync(null);

        writer.Created.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Var_olan_etiketler_dialoga_bildirilir()
    {
        FakeTagWriter writer = new();
        FakeTagPrompt prompt = new();

        CommitListViewModel model = await LoadedAsync(
            writer,
            prompt,
            [FakeGitData.Tag("v1.0", FakeGitData.Sha(3)), FakeGitData.Tag("v1.1", FakeGitData.Sha(4))]);

        await model.CreateTagCommand.ExecuteAsync(null);

        prompt.CreateRequest.ShouldNotBeNull().ExistingTags.ShouldBe(["v1.0", "v1.1"]);
    }

    [AvaloniaFact]
    public async Task Etiket_yazma_hatasi_siniflandirilmis_mesajla_bildirilir()
    {
        FakeTagWriter writer = new()
        {
            Failure = new GitException(
                GitFailureKind.Unknown,
                "Etiket olusturulamadi.",
                "git tag v1.0",
                exitCode: 128,
                standardError: "fatal: tag 'v1.0' already exists"),
        };

        FakeTagPrompt prompt = new()
        {
            CreateAnswer = new CreateTagDecision { Confirmed = true, Name = "v1.0" },
        };

        CommitListViewModel model = await LoadedAsync(writer, prompt);

        await model.CreateTagCommand.ExecuteAsync(null);

        model.ErrorMessage.ShouldNotBeNullOrWhiteSpace();

        // git's own output is what says WHICH tag clashed; the summary alone does not.
        model.ErrorDetails.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task Etiket_silinir()
    {
        FakeTagWriter writer = new();
        FakeTagPrompt prompt = new()
        {
            DeleteAnswer = new DeleteTagDecision { Confirmed = true, Name = "v1.0" },
        };

        CommitListViewModel model = await LoadedAsync(writer, prompt);

        await model.DeleteTagCommand.ExecuteAsync("v1.0");

        writer.Deleted.ShouldBe(["v1.0"]);
        prompt.DeleteRequest.ShouldNotBeNull().Name.ShouldBe("v1.0");
    }

    [AvaloniaFact]
    public async Task Silme_onaylanmazsa_git_calismaz()
    {
        FakeTagWriter writer = new();
        FakeTagPrompt prompt = new() { DeleteAnswer = DeleteTagDecision.Cancelled };

        CommitListViewModel model = await LoadedAsync(writer, prompt);

        await model.DeleteTagCommand.ExecuteAsync("v1.0");

        writer.Deleted.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Yazici_yoksa_komut_calistirilamaz()
    {
        CommitListViewModel model = await LoadedAsync(writer: null, prompt: new FakeTagPrompt());

        model.CreateTagCommand.CanExecute(null).ShouldBeFalse();
        model.DeleteTagCommand.CanExecute("v1.0").ShouldBeFalse();
    }

    [Theory]
    [InlineData("v1.0", true)]
    [InlineData("release/2024", true)]
    [InlineData("", false)]
    [InlineData("bad name", false)]
    [InlineData("bad~name", false)]
    [InlineData("..", false)]
    [InlineData("a//b", false)]
    [InlineData("end.", false)]
    [InlineData("a.lock", false)]
    [InlineData("refs/tags/v1.0", false)]
    // 🔴 MEASURED — `git tag -lead` does NOT create a tag; git reads `-lead` as the options
    // `-l -e -a -d` and answered "options '-d' and '-l' cannot be used together". TagWriter appends
    // the name positionally, so this rejection is the only thing keeping such a name out of git.
    [InlineData("-lead", false)]
    public void Etiket_adi_kurallari_gitin_kurallariyla_ayni(string name, bool valid)
    {
        TagNameRules.IsValid(name).ShouldBe(valid);
    }
}
