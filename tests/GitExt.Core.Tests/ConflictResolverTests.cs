using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T04 + P07-T05 — çakışma çözüm akışı ve harici araç.
/// </summary>
public class ConflictResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        ConflictResolver Resolver,
        MergeToolRunner Tools,
        GitWriteQueue Queue) : IDisposable
    {
        public string Path => Repository.Path;

        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }
    }

    private static async Task<Harness> CreateAsync(
        Action<TestRepository> onBranch,
        Action<TestRepository> onMain,
        string startCommand = "merge")
    {
        TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("f.txt", "a\nb\nc\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ortak ata");

        repository.Git("checkout", "-q", "-b", "yan");
        onBranch(repository);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "yan");

        repository.Git("checkout", "-q", "main");
        onMain(repository);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ana");

        repository.TryGit(startCommand, "yan");

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();
        GitWriter writer = new(runner, queue);
        InProgressOperationReader operations = new(runner);

        return new Harness(
            repository,
            new ConflictResolver(writer, runner, operations),
            new MergeToolRunner(writer, runner),
            queue);
    }

    private static Task<Harness> ContentConflictAsync(string startCommand = "merge") =>
        CreateAsync(
            branch => branch.WriteFile("f.txt", "a\nYAN\nc\n"),
            main => main.WriteFile("f.txt", "a\nANA\nc\n"),
            startCommand);

    // ------------------------------------------------------------ ilerleme

    [Fact]
    public async Task Kalan_cakisma_sayisi_ve_devam_komutu_dogru()
    {
        using Harness harness = await ContentConflictAsync();

        ConflictProgress progress = await harness.Resolver.GetProgressAsync(harness.Path, Ct);

        progress.Operation.ShouldBe(InProgressOperation.Merge);
        progress.RemainingCount.ShouldBe(1);
        progress.IsResolved.ShouldBeFalse();
        progress.ContinueCommand.ShouldBe("git merge --continue");
        progress.AbortCommand.ShouldBe("git merge --abort");
    }

    [Fact]
    public async Task COZULMEDEN_devam_SUNULMUYOR()
    {
        // 🔴 ÖLÇÜLDÜ: çözülmeden `--continue` çalıştırmak rc=128 veriyor
        // ("Committing is not possible because you have unmerged files").
        using Harness harness = await ContentConflictAsync();

        ConflictProgress progress = await harness.Resolver.GetProgressAsync(harness.Path, Ct);

        progress.CanContinue.ShouldBeFalse();
    }

    [Fact]
    public async Task Hepsi_cozulunce_devam_SUNULUYOR()
    {
        using Harness harness = await ContentConflictAsync();
        RepositoryPath path = RepositoryPath.Parse("f.txt");

        await harness.Resolver.TakeSideAsync(harness.Path, path, ResolutionSide.Ours, Ct);

        ConflictProgress progress = await harness.Resolver.GetProgressAsync(harness.Path, Ct);

        progress.IsResolved.ShouldBeTrue();
        progress.CanContinue.ShouldBeTrue();
    }

    [Theory]
    [InlineData("merge", InProgressOperation.Merge)]
    [InlineData("cherry-pick", InProgressOperation.CherryPick)]
    public async Task Devam_komutu_ISLEME_gore_degisiyor(string command, InProgressOperation expected)
    {
        using Harness harness = await ContentConflictAsync(command);

        ConflictProgress progress = await harness.Resolver.GetProgressAsync(harness.Path, Ct);

        progress.Operation.ShouldBe(expected);
        progress.ContinueCommand.ShouldBe($"git {command} --continue");
    }

    [Fact]
    public async Task Islem_yokken_devam_da_iptal_de_SUNULMUYOR()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        using GitWriteQueue queue = new();
        GitWriter writer = new(runner, queue);
        ConflictResolver resolver = new(writer, runner, new InProgressOperationReader(runner));

        ConflictProgress progress = await resolver.GetProgressAsync(repository.Path, Ct);

        progress.Operation.ShouldBe(InProgressOperation.None);
        progress.CanContinue.ShouldBeFalse();
        progress.ContinueCommand.ShouldBeNull();
        progress.AbortCommand.ShouldBeNull();
    }

    // ------------------------------------------------------------ çözme

    [Fact]
    public async Task TARAF_ALMAK_cakismayi_GERCEKTEN_temizliyor()
    {
        // 🔴 ÖLÇÜLDÜ: `git checkout --ours` içeriği yazıyor ama dosya index'te hâlâ `U`.
        // Ardından `git add` gelmezse kullanıcı "çözdüm" sanır ve `--continue` reddedilir.
        using Harness harness = await ContentConflictAsync();
        RepositoryPath path = RepositoryPath.Parse("f.txt");

        await harness.Resolver.TakeSideAsync(harness.Path, path, ResolutionSide.Theirs, Ct);

        File.ReadAllText(path.ToAbsolutePath(harness.Path)).ShouldBe("a\nYAN\nc\n");
        (await harness.Resolver.GetProgressAsync(harness.Path, Ct)).RemainingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Elle_duzenlenen_icerik_yazilip_isaretleniyor()
    {
        using Harness harness = await ContentConflictAsync();
        RepositoryPath path = RepositoryPath.Parse("f.txt");

        await harness.Resolver.WriteResolvedAsync(
            harness.Path, path, Encoding.UTF8.GetBytes("a\nELLE\nc\n"), Ct);

        File.ReadAllText(path.ToAbsolutePath(harness.Path)).ShouldBe("a\nELLE\nc\n");
        (await harness.Resolver.GetProgressAsync(harness.Path, Ct)).IsResolved.ShouldBeTrue();
    }

    [Fact]
    public async Task VARLIK_cakismasi_SILEREK_cozulebiliyor()
    {
        // deleted-by-us: `checkout --ours` burada rc=1 veriyor ("does not have our
        // version"); doğru çözüm dosyayı silmek ya da eklemek.
        using Harness harness = await CreateAsync(
            branch => branch.WriteFile("f.txt", "DEGISTI\n"),
            main => main.Git("rm", "-q", "f.txt"));

        await harness.Resolver.RemoveAsync(harness.Path, RepositoryPath.Parse("f.txt"), Ct);

        (await harness.Resolver.GetProgressAsync(harness.Path, Ct)).IsResolved.ShouldBeTrue();
    }

    [Fact]
    public async Task Devam_MERGE_commitini_olusturuyor()
    {
        using Harness harness = await ContentConflictAsync();
        RepositoryPath path = RepositoryPath.Parse("f.txt");

        await harness.Resolver.TakeSideAsync(harness.Path, path, ResolutionSide.Ours, Ct);
        await harness.Resolver.ContinueAsync(harness.Path, Ct);

        harness.Repository.Git("log", "--oneline").ShouldContain("Merge branch");

        ConflictProgress after = await harness.Resolver.GetProgressAsync(harness.Path, Ct);
        after.Operation.ShouldBe(InProgressOperation.None);
    }

    [Fact]
    public async Task Iptal_calisma_agacini_ESKI_haline_donduruyor()
    {
        using Harness harness = await ContentConflictAsync();
        string before = harness.Repository.Git("rev-parse", "HEAD").Trim();

        await harness.Resolver.AbortAsync(harness.Path, Ct);

        harness.Repository.Git("rev-parse", "HEAD").Trim().ShouldBe(before);
        File.ReadAllText(Path.Combine(harness.Path, "f.txt")).ShouldBe("a\nANA\nc\n");
        (await harness.Resolver.GetProgressAsync(harness.Path, Ct))
            .Operation.ShouldBe(InProgressOperation.None);
    }

    [Fact]
    public async Task Surmeyen_islemde_devam_ISTISNA_firlatiyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        using GitWriteQueue queue = new();
        GitWriter writer = new(runner, queue);
        ConflictResolver resolver = new(writer, runner, new InProgressOperationReader(runner));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await resolver.ContinueAsync(repository.Path, Ct));
    }

    // ------------------------------------------------------------ harici araç

    [Fact]
    public async Task Yapilandirilmamis_merge_tool_NULL()
    {
        using Harness harness = await ContentConflictAsync();

        (await harness.Tools.GetConfiguredToolAsync(harness.Path, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Yapilandirilmis_merge_tool_okunuyor()
    {
        using Harness harness = await ContentConflictAsync();
        harness.Repository.Git("config", "--local", "merge.tool", "meld");

        (await harness.Tools.GetConfiguredToolAsync(harness.Path, Ct)).ShouldBe("meld");
    }

    [Fact]
    public void KURULU_OLMAYAN_araclar_ayirt_ediliyor()
    {
        // git iki liste basıyor; kurulu olmayanı seçtirmek kullanıcıyı çalışmayan bir
        // düğmeye tıklatırdı.
        const string output = """
            'git mergetool --tool=<tool>' may be set to one of the following:
            		meld             Use Meld
            		vimdiff          Use Vim with a custom layout

            The following tools are valid, but not currently available:
            		bc               Use Beyond Compare
            """;

        IReadOnlyList<MergeTool> tools = MergeToolRunner.ParseToolHelp(output);

        tools.Count.ShouldBe(3);
        tools.First(tool => tool.Name == "meld").IsAvailable.ShouldBeTrue();
        tools.First(tool => tool.Name == "vimdiff").IsAvailable.ShouldBeTrue();
        tools.First(tool => tool.Name == "bc").IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Araclar_gercek_gitten_listeleniyor()
    {
        using Harness harness = await ContentConflictAsync();

        IReadOnlyList<MergeTool> tools = await harness.Tools.ListToolsAsync(harness.Path, Ct);

        tools.ShouldNotBeEmpty();
        tools.ShouldContain(tool => tool.Name == "vimdiff");
    }

    [Fact]
    public async Task Harici_arac_cakismayi_cozuyor_ve_ORIG_yedegi_bildiriliyor()
    {
        // ⚠️ ÖLÇÜLDÜ: `git mergetool` her dosya için bir `<ad>.orig` bırakıyor ve bu
        // takip edilmeyen dosya olarak ağaçta kalıyor. Söylenmezse kullanıcı "bu dosya
        // nereden çıktı" diye sorar.
        using Harness harness = await ContentConflictAsync();

        // Karşı tarafı alan sahte bir araç: gerçek bir GUI aracı testte çalıştırılamaz.
        string script = Path.Combine(harness.Path, "arac.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\ncat \"$2\" > \"$4\"\n", Ct);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        harness.Repository.Git(
            "config", "--local", "mergetool.sahte.cmd", $"{script} $LOCAL $REMOTE $BASE $MERGED");

        MergeToolResult result = await harness.Tools.RunAsync(
            harness.Path, tool: "sahte", cancellationToken: Ct);

        result.IsResolved.ShouldBeTrue();
        result.BackupFiles.ShouldContain(path => path.Value == "f.txt.orig");
    }
}
