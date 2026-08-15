using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T02 — Handling lock collisions.
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED:</b> the lock file is <b>empty</b> (no process id) and git does not treat an old lock
/// differently — so there is no reliable answer to the question "did the owner die". That is why the
/// lock is <b>never removed on its own</b>; the user is shown its age and the decision is left to them
/// (GitExtensions does the same: <c>IndexLockManager</c> only checks for existence and ties removal to
/// a menu command).
/// </para>
/// </remarks>
public class GitLockTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GitException Locked() =>
        new(GitFailureKind.IndexLocked, "kilitli", "git add", 128, "index.lock");

    // ---- Lock inspection ----

    [Fact]
    public void Kilit_yoksa_null_doner()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        GitLock.Inspect(Path.Combine(repository.Path, ".git")).ShouldBeNull();
    }

    [Fact]
    public void Kilit_varsa_yolu_ve_yasi_bildirilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        string gitDirectory = Path.Combine(repository.Path, ".git");
        string lockFile = Path.Combine(gitDirectory, GitLock.IndexLockName);

        File.WriteAllText(lockFile, string.Empty);
        File.SetLastWriteTimeUtc(lockFile, DateTime.UtcNow - TimeSpan.FromMinutes(30));

        GitLockInfo info = GitLock.Inspect(gitDirectory).ShouldNotBeNull();

        info.Path.ShouldBe(lockFile);
        info.Age.ShouldBeGreaterThan(TimeSpan.FromMinutes(25));
        info.LooksStale.ShouldBeTrue();
    }

    [Fact]
    public void Yeni_kilit_bayat_SAYILMAZ()
    {
        // A legitimate lock lasts on the order of milliseconds (measured: an add of 300 files took 12 ms),
        // but the threshold is deliberately very wide: a wrong "stale" decision corrupts the index.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        string gitDirectory = Path.Combine(repository.Path, ".git");
        File.WriteAllText(Path.Combine(gitDirectory, GitLock.IndexLockName), string.Empty);

        GitLock.Inspect(gitDirectory).ShouldNotBeNull().LooksStale.ShouldBeFalse();
    }

    [Fact]
    public void Saat_kaymasi_negatif_yas_uretmez()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        string gitDirectory = Path.Combine(repository.Path, ".git");
        string lockFile = Path.Combine(gitDirectory, GitLock.IndexLockName);

        File.WriteAllText(lockFile, string.Empty);
        File.SetLastWriteTimeUtc(lockFile, DateTime.UtcNow + TimeSpan.FromHours(1));

        GitLock.Inspect(gitDirectory).ShouldNotBeNull().Age.ShouldBe(TimeSpan.Zero);
    }

    // ---- Removal ----

    [Fact]
    public void Onaysiz_silme_REDDEDILIR()
    {
        // Had the rule stayed in a comment, someone could later call it without confirmation; the signature makes it mandatory.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        string gitDirectory = Path.Combine(repository.Path, ".git");
        string lockFile = Path.Combine(gitDirectory, GitLock.IndexLockName);

        File.WriteAllText(lockFile, string.Empty);

        GitLockInfo info = GitLock.Inspect(gitDirectory).ShouldNotBeNull();

        Should.Throw<InvalidOperationException>(() => GitLock.Remove(info, userConfirmed: false));

        File.Exists(lockFile).ShouldBeTrue();
    }

    [Fact]
    public void Onayli_silme_kilidi_kaldirir_ve_yazma_yeniden_calisir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        string gitDirectory = Path.Combine(repository.Path, ".git");
        string lockFile = Path.Combine(gitDirectory, GitLock.IndexLockName);

        repository.WriteFile("yeni.txt", "icerik\n");
        File.WriteAllText(lockFile, string.Empty);

        repository.TryGit("add", "-A").ExitCode.ShouldNotBe(0);

        GitLock.Remove(GitLock.Inspect(gitDirectory).ShouldNotBeNull(), userConfirmed: true);

        repository.TryGit("add", "-A").ExitCode.ShouldBe(0);
    }

    // ---- Retry ----

    [Fact]
    public async Task Kilit_hatasi_yeniden_denenir()
    {
        int attempts = 0;

        int result = await GitLockRetry.RunAsync(
            _ =>
            {
                attempts++;
                return attempts < 3 ? throw Locked() : Task.FromResult(42);
            },
            new GitLockRetryOptions { InitialDelay = TimeSpan.FromMilliseconds(1) },
            Ct);

        result.ShouldBe(42);
        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task Denemeler_tukenince_son_hata_yukselir()
    {
        int attempts = 0;

        GitException exception = await Should.ThrowAsync<GitException>(
            GitLockRetry.RunAsync<int>(
                _ =>
                {
                    attempts++;
                    throw Locked();
                },
                new GitLockRetryOptions
                {
                    MaximumAttempts = 4,
                    InitialDelay = TimeSpan.FromMilliseconds(1),
                },
                Ct));

        exception.Kind.ShouldBe(GitFailureKind.IndexLocked);
        attempts.ShouldBe(4);
    }

    [Fact]
    public async Task Kilit_disindaki_hatalar_YENIDEN_DENENMEZ()
    {
        // Retrying an authentication error eight times does nothing but keep the user
        // waiting.
        int attempts = 0;

        await Should.ThrowAsync<GitException>(
            GitLockRetry.RunAsync<int>(
                _ =>
                {
                    attempts++;
                    throw new GitException(
                        GitFailureKind.AuthenticationRequired, "yetki", "git push", 128, "");
                },
                new GitLockRetryOptions { InitialDelay = TimeSpan.FromMilliseconds(1) },
                Ct));

        attempts.ShouldBe(1);
    }

    // ---- Classification ----

    [Fact]
    public void Index_kilidi_siniflandirilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        string lockFile = Path.Combine(repository.Path, ".git", GitLock.IndexLockName);
        repository.WriteFile("x.txt", "a\n");
        File.WriteAllText(lockFile, string.Empty);

        (_, string error) = repository.TryGit("add", "-A");

        GitFailureClassifier.Classify(error).ShouldBe(GitFailureKind.IndexLocked);
    }

    [Fact]
    public void REF_kilidi_de_siniflandirilir()
    {
        // ⚠️ MEASURED: "index.lock" does NOT appear in the ref lock message —
        // "cannot lock ref 'HEAD': Unable to create '…/main.lock': File exists."
        // A classifier that only looks at the first pattern would count this as `Unknown`.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        string branch = repository.Git("rev-parse", "--abbrev-ref", "HEAD").Trim();
        string refLock = Path.Combine(repository.Path, ".git", "refs", "heads", branch + ".lock");

        Directory.CreateDirectory(Path.GetDirectoryName(refLock)!);
        File.WriteAllText(refLock, string.Empty);

        repository.WriteFile("y.txt", "b\n");
        repository.Git("add", "-A");

        (_, string error) = repository.TryGit("commit", "-m", "deneme");

        error.ShouldNotContain("index.lock");
        GitFailureClassifier.Classify(error).ShouldBe(GitFailureKind.IndexLocked);
    }
}
