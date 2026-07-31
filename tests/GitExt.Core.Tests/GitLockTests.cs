using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T02 — Kilit çakışmasının ele alınması.
/// </summary>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ:</b> kilit dosyası <b>boş</b> (süreç kimliği yok) ve git eski bir kilide farklı
/// davranmıyor — yani "sahibi öldü mü" sorusunun güvenilir cevabı yok. Bu yüzden kilit
/// <b>asla kendiliğinden silinmiyor</b>; kullanıcıya yaşı gösterilip karar ona bırakılıyor
/// (GitExtensions da aynısını yapıyor: <c>IndexLockManager</c> yalnızca varlığı kontrol edip
/// silmeyi menü komutuna bağlıyor).
/// </para>
/// </remarks>
public class GitLockTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GitException Locked() =>
        new(GitFailureKind.IndexLocked, "kilitli", "git add", 128, "index.lock");

    // ---- Kilit incelemesi ----

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
        // Meşru kilit süresi milisaniyeler mertebesinde (ölçüldü: 300 dosyalık add 12 ms),
        // ama eşik bilinçli olarak çok geniş: yanlış "bayat" kararı index'i bozar.
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

    // ---- Silme ----

    [Fact]
    public void Onaysiz_silme_REDDEDILIR()
    {
        // Kural yorumda kalsaydı biri ileride onaysız çağırabilirdi; imza zorunlu kılıyor.
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

    // ---- Yeniden deneme ----

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
        // Kimlik doğrulama hatasını sekiz kez tekrarlamak kullanıcıyı bekletmekten
        // başka işe yaramaz.
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

    // ---- Sınıflandırma ----

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
        // ⚠️ ÖLÇÜLDÜ: ref kilidi mesajında "index.lock" GEÇMİYOR —
        // "cannot lock ref 'HEAD': Unable to create '…/main.lock': File exists."
        // Yalnızca ilk kalıba bakan sınıflandırıcı bunu `Unknown` sayardı.
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
