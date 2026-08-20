using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests.Git;

/// <summary>
/// The behavioural contract of the process execution layer (P02-T01, P02-T03).
/// All of it runs <b>real <c>git</c></b> processes.
/// </summary>
public class GitProcessRunnerTests
{
    /// <summary>So that child processes stop too when the test is cancelled.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<GitProcessRunner> CreateRunnerAsync(IGitCommandLog? log = null)
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new GitProcessRunner(executable, log);
    }

    [Fact]
    public async Task Basarili_komut_cikti_dondurur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            GitCommand.Create(repository.Path, "rev-parse", "--abbrev-ref", "HEAD"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.ExitCode.ShouldBe(0);
        result.GetStandardOutputText().Trim().ShouldBe("main");
    }

    [Fact]
    public async Task Basarisiz_komut_istisna_firlatmaz_sonuc_dondurur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            GitCommand.Create(repository.Path, "rev-parse", "boyle-bir-ref-yok"), Ct);

        // The RunAsync contract: a result is returned whatever the exit code is.
        result.IsSuccess.ShouldBeFalse();
        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunCheckedAsync_basarisizlikta_siniflandirilmis_istisna_firlatir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitProcessRunner runner = await CreateRunnerAsync();

        GitException exception = await Should.ThrowAsync<GitException>(
            runner.RunCheckedAsync(
                GitCommand.Create(repository.Path, "rev-parse", "boyle-bir-ref-yok"), Ct));

        exception.Kind.ShouldBe(GitFailureKind.UnknownRevision);
        // The raw stderr must always stay accessible (P02-T12).
        exception.StandardError.ShouldNotBeNullOrWhiteSpace();
        exception.CommandLine.ShouldContain("rev-parse");
    }

    [Fact]
    public async Task Git_deposu_olmayan_dizin_siniflandirilir()
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("gitext-not-a-repo-");

        try
        {
            GitProcessRunner runner = await CreateRunnerAsync();

            GitException exception = await Should.ThrowAsync<GitException>(
                runner.RunCheckedAsync(
                    GitCommand.Create(temporary.FullName, "status", "--porcelain=v2"), Ct));

            exception.Kind.ShouldBe(GitFailureKind.NotARepository);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Bosluk_ve_ozel_karakter_iceren_dosya_adlari_bozulmadan_gecer()
    {
        // Because the arguments are passed as an array there must be no shell interpretation (ADR-0002 rule 4).
        // Had this name gone to a shell, $(whoami) would run and & would split the command.
        const string awkwardName = "dosya adı 'tırnaklı' $(whoami) & ; şey.txt";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile(awkwardName, "içerik\n");
        repository.Git("add", "--", awkwardName);
        repository.Commit("tuhaf isimli dosya");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult listing = await runner.RunAsync(
            GitCommand.Create(repository.Path, "ls-files", "-z"), Ct);

        listing.SplitStandardOutputAtNul().ShouldContain(awkwardName);
    }

    [Fact]
    public async Task Unicode_dosya_adlari_kacis_dizisi_olmadan_gelir()
    {
        // Without core.quotepath=false git returns this with octal escapes such as \303\247….
        const string turkishName = "çalışma-günlüğü-ÖĞÜŞİ.txt";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile(turkishName, "içerik\n");
        repository.Git("add", "--", turkishName);
        repository.Commit("unicode dosya adı");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            GitCommand.Create(repository.Path, "ls-files", "-z"), Ct);

        result.SplitStandardOutputAtNul().ShouldContain(turkishName);
    }

    [Fact]
    public async Task Standart_girdi_uzerinden_commit_mesaji_gecirilebilir()
    {
        // Commit messages are not embedded in the command line; they go through stdin (ADR-0002 rule 4).
        const string message = "başlık satırı\n\nGövde: $HOME `whoami` \"tırnak\" ve 'tek tırnak'.";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult commit = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["commit", "-F", "-"],
                StandardInput = Encoding.UTF8.GetBytes(message),
                IsReadOnly = false,
            },
            Ct);

        commit.IsSuccess.ShouldBeTrue(commit.StandardError);

        GitResult stored = await runner.RunAsync(
            GitCommand.Create(repository.Path, "log", "--format=%B", "-1"), Ct);

        stored.GetStandardOutputText().TrimEnd().ShouldBe(message);
    }

    [Fact]
    public async Task Buyuk_cikti_deadlock_olmadan_okunur()
    {
        // If stdout and stderr are not read concurrently the pipe fills up, the process cannot write and never finishes.
        // This test catches that deadlock: if it does not pass, it times out.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        // ⚠️ CI-ONLY FAILURE, cause not yet identified. On the Ubuntu runner `git log` sometimes
        // comes back with exit 128, "Could not read <oid> / Failed to traverse parents". Retrying
        // does NOT help, so whatever is wrong is permanent, not a transient read.
        //
        // What has been RULED OUT by measurement (none of these reproduce it, locally or under
        // load): commit count and write speed (500 → 200 changed nothing), auto gc (204 loose
        // objects against a 6700 threshold — it never triggers), tmpfs vs. real disk, CPU
        // starvation on all cores, and HOME being pointed at the repository root by the fixture.
        //
        // Rather than guess again, the test CAPTURES THE EVIDENCE when it fails.
        //
        // 🔴 The evidence from the last red run moved the problem: it is no longer `git log` that
        // dies, it is the FIXTURE, at commit 57 of 200, with `fatal: could not parse HEAD`.
        // REPRODUCED locally what produces exactly that message: a ref whose content is a
        // well-formed object id that no longer EXISTS. (An empty/short/non-hex ref gives
        // "reference broken" instead, and a damaged HEAD file gives "not a git repository".)
        //
        // So the ref is written while its object is missing — which is why retrying never helped
        // and why `git log` failed the same way earlier: same fault, seen one step later.
        for (int i = 0; i < 200; i++)
        {
            try
            {
                repository.Commit($"commit {i} — govdemi biraz uzatmak icin ek metin ekliyoruz");
            }
            catch (InvalidOperationException ex)
            {
                string gitDirectory = System.IO.Path.Combine(repository.Path, ".git");
                string headFile = System.IO.Path.Combine(gitDirectory, "HEAD");
                string objectsDirectory = System.IO.Path.Combine(gitDirectory, "objects");

                string head = File.Exists(headFile) ? File.ReadAllText(headFile).Trim() : "<HEAD YOK>";

                // Where HEAD points, and whether that ref's object actually exists.
                string refState = "<cozulemedi>";
                if (head.StartsWith("ref: ", StringComparison.Ordinal))
                {
                    string refFile = System.IO.Path.Combine(gitDirectory, head[5..].Replace('/', System.IO.Path.DirectorySeparatorChar));
                    if (File.Exists(refFile))
                    {
                        string oid = File.ReadAllText(refFile).Trim();
                        (int catExit, _) = repository.TryGit("cat-file", "-e", oid);
                        refState = $"{head[5..]} = {oid} (nesne var mi: {(catExit == 0 ? "EVET" : "HAYIR")})";
                    }
                    else
                    {
                        refState = $"{head[5..]} DOSYASI YOK";
                    }
                }

                int looseObjects = Directory.Exists(objectsDirectory)
                    ? Directory.EnumerateFiles(objectsDirectory, "*", SearchOption.AllDirectories).Count()
                    : -1;

                (_, string fsck) = repository.TryGit("fsck", "--no-progress");

                throw new InvalidOperationException(
                    $"""
                     Fixture {i}. commit'te coktu.
                     HEAD      : {head}
                     ref       : {refState}
                     gevsek obj: {looseObjects}
                     fsck      : {fsck.Trim()}
                     ---
                     {ex.Message}
                     """,
                    ex);
            }
        }

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["log", "--format=%H %s"],
                Timeout = TimeSpan.FromSeconds(60),
            },
            Ct);

        if (!result.IsSuccess)
        {
            (int fsckExit, string fsckError) = repository.TryGit("fsck", "--no-progress");
            (_, string countError) = repository.TryGit("count-objects", "-v");

            string objectsDirectory = System.IO.Path.Combine(repository.Path, ".git", "objects");
            int looseObjects = Directory.Exists(objectsDirectory)
                ? Directory.EnumerateFiles(objectsDirectory, "*", SearchOption.AllDirectories).Count()
                : -1;

            result.IsSuccess.ShouldBeTrue(
                $"""
                 git log exit {result.ExitCode}
                 stderr    : {result.StandardError.Trim()}
                 fsck exit : {fsckExit}
                 fsck      : {fsckError.Trim()}
                 count     : {countError.Trim()}
                 loose obj : {looseObjects} (200 commits expected)
                 """);
        }
        result.GetStandardOutputText()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Length.ShouldBe(200);
    }

    [Fact]
    public async Task Ikili_icerik_bozulmadan_okunur()
    {
        // Had stdout been read as a string, invalid UTF-8 bytes would turn into U+FFFD and the
        // content would be corrupted irreversibly.
        byte[] binaryContent = [0x00, 0xFF, 0xFE, 0x42, 0x00, 0x80, 0x81, 0x0A];

        using TestRepository repository = TestRepository.CreateEmpty();
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(repository.Path, "blob.bin"), binaryContent, Ct);
        repository.Git("add", "blob.bin");
        repository.Commit("ikili dosya");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            GitCommand.Create(repository.Path, "show", "HEAD:blob.bin"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.StandardOutput.ShouldBe(binaryContent);
    }

    [Fact]
    public async Task Zaman_asimi_siniflandirilmis_hata_uretir()
    {
        // A genuinely slow job is needed for determinism: on a small repository
        // `git log` finishes within milliseconds and races with a short timeout.
        // A long-running pre-commit hook is both reliably slow and a real scenario.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");
        repository.InstallHook("pre-commit", "sleep 30\n");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitException exception = await Should.ThrowAsync<GitException>(
            runner.RunAsync(
                new GitCommand
                {
                    WorkingDirectory = repository.Path,
                    Arguments = ["commit", "-m", "hook bunu geciktirecek"],
                    IsReadOnly = false,
                    Timeout = TimeSpan.FromMilliseconds(700),
                },
                Ct));

        exception.Kind.ShouldBe(GitFailureKind.Timeout);
    }

    [Fact]
    public async Task Zaman_asimi_sureci_gercekten_oldurur()
    {
        // Throwing a timeout error is not enough; the child process tree must die too.
        // Were the hook still running, index.lock would keep being held and the
        // next commit would break with "Another git process seems to be running".
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");
        repository.InstallHook("pre-commit", "sleep 30\n");

        GitProcessRunner runner = await CreateRunnerAsync();

        await Should.ThrowAsync<GitException>(
            runner.RunAsync(
                new GitCommand
                {
                    WorkingDirectory = repository.Path,
                    Arguments = ["commit", "-m", "zaman aşımına uğrayacak"],
                    IsReadOnly = false,
                    Timeout = TimeSpan.FromMilliseconds(700),
                },
                Ct));

        // Remove the hook and check whether the repository is still usable.
        repository.InstallHook("pre-commit", "exit 0\n");

        GitResult status = await runner.RunAsync(
            GitCommand.Create(repository.Path, "status", "--porcelain=v2"), Ct);

        status.IsSuccess.ShouldBeTrue(status.StandardError);
    }

    [Fact]
    public async Task Iptal_token_i_calismayi_durdurur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitProcessRunner runner = await CreateRunnerAsync();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            runner.RunAsync(GitCommand.Create(repository.Path, "log"), cancellation.Token));
    }

    [Fact]
    public async Task Calistirilan_komutlar_gunluge_kaydedilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        InMemoryGitCommandLog log = new();
        GitProcessRunner runner = await CreateRunnerAsync(log);

        await runner.RunAsync(GitCommand.Create(repository.Path, "status", "--porcelain=v2"), Ct);

        GitCommandLogEntry entry = log.Entries.ShouldHaveSingleItem();
        entry.CommandLine.ShouldBe("git status --porcelain=v2");
        entry.IsSuccess.ShouldBeTrue();
        entry.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Gunluk_kapasitesini_asmaz()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        InMemoryGitCommandLog log = new(capacity: 3);
        GitProcessRunner runner = await CreateRunnerAsync(log);

        for (int i = 0; i < 6; i++)
        {
            await runner.RunAsync(GitCommand.Create(repository.Path, "rev-parse", "HEAD"), Ct);
        }

        log.Entries.Count.ShouldBe(3);
    }

    [Fact]
    public void Komut_gosterim_metni_terminale_kopyalanabilir()
    {
        GitCommand command = GitCommand.Create(
            "/tmp/repo", "commit", "-m", "boşluklu 'mesaj'");

        // Without quoting the user could not copy this line and run it.
        command.ToDisplayString().ShouldBe("git commit -m 'boşluklu '\\''mesaj'\\'''");
    }
}
