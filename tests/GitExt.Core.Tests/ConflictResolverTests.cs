using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T04 + P07-T05 — conflict resolution flow and external tool.
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

    // ------------------------------------------------------------ progress

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
        // 🔴 MEASURED: running `--continue` without resolving gives rc=128
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

    // ------------------------------------------------------------ resolving

    [Fact]
    public async Task TARAF_ALMAK_cakismayi_GERCEKTEN_temizliyor()
    {
        // 🔴 MEASURED: `git checkout --ours` writes the content but the file is still `U` in the index.
        // If `git add` does not follow, the user thinks "I resolved it" and `--continue` is refused.
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
        // version"); the correct resolution is to delete or to add the file.
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

    // ------------------------------------------------------------ external tool

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
        // git prints two lists; letting the user pick one that is not installed would make them
        // click a button that does not work.
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
        // ⚠️ MEASURED: `git mergetool` leaves a `<name>.orig` behind for every file and it stays
        // in the tree as an untracked file. If it is not mentioned, the user asks "where did this
        // file come from".
        using Harness harness = await ContentConflictAsync();

        // A fake tool that takes the other side: a real GUI tool cannot be run in a test.
        string script = Path.Combine(harness.Path, "arac.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\ncat \"$2\" > \"$4\"\n", Ct);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // 🔴 The path goes into the configuration in a form THE SHELL can read: git runs the merge
        // command through `sh`, and in MSYS sh a backslash is an escape character — `C:\…\arac.sh`
        // would arrive as `C:…arac.sh` (measured with Git for Windows under Wine, the same trap as
        // the sequence editor in RebaseTodoSession). Forward slashes plus quotes work on every
        // platform; on Unix the path is used unchanged.
        string command = OperatingSystem.IsWindows()
            ? $"\"{script.Replace('\\', '/')}\""
            : script;

        harness.Repository.Git(
            "config", "--local", "mergetool.sahte.cmd", $"{command} $LOCAL $REMOTE $BASE $MERGED");

        // 🔴 MEASURED (macOS CI): without `trustExitCode` git does not believe the tool, it looks at
        // the FILE'S TIMESTAMP — `test "$MERGED" -nt "$BACKUP"` in git-mergetool--lib. The backup is
        // taken immediately before the tool runs and this fake tool finishes in microseconds, so on
        // a shell that compares whole seconds (macOS's /bin/sh is bash 3.2) the resolved file does
        // not look "newer". git then prints "seems unchanged", asks "Was the merge successful
        // [y/n]?" — and there is no terminal: `read` hits EOF, git restores the conflicted file and
        // exits 1. Reproduced on Linux by hand: with the tool setting an older mtime the same
        // "merge of f.txt failed" comes out, and with trustExitCode it does not.
        // The measurement in RunAsync (the index decides, not the exit code) is untouched — this is
        // about how git judges THE TOOL, and this tool's exit code really is reliable.
        harness.Repository.Git("config", "--local", "mergetool.sahte.trustExitCode", "true");

        MergeToolResult result = await harness.Tools.RunAsync(
            harness.Path, tool: "sahte", cancellationToken: Ct);

        result.IsResolved.ShouldBeTrue();
        result.BackupFiles.ShouldContain(path => path.Value == "f.txt.orig");
    }
}
