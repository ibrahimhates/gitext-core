using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T01 — Yazma işlemlerinin serileştirilmesi.
/// </summary>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ (kod yazılmadan önce):</b> git eşzamanlı yazmayı <b>beklemiyor</b>. Aynı depoda
/// 8 paralel <c>git add</c> çalıştırıldığında 7'si
/// <c>fatal: Unable to create '…/index.lock': File exists</c> ile düştü.
/// </para>
/// <para>
/// Bu yüzden buradaki asıl test <b>gerçek git ile</b> yapılıyor: sahte bir işlemin sırayla
/// çalışması kuyruğun doğru olduğunu göstermez, git'in gerçekten şikâyet etmemesi gösterir.
/// </para>
/// </remarks>
public class GitWriteQueueTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const int Writers = 8;

    [Fact]
    public async Task Ayni_depoya_eszamanli_yazmalar_seri_calisir()
    {
        using GitWriteQueue queue = new();

        int active = 0;
        int maxActive = 0;
        object gate = new();

        async Task Work()
        {
            await queue.RunAsync("/tmp/depo/.git", async _ =>
            {
                lock (gate)
                {
                    active++;
                    maxActive = Math.Max(maxActive, active);
                }

                await Task.Delay(15, Ct);

                lock (gate)
                {
                    active--;
                }
            }, Ct);
        }

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => Work()));

        maxActive.ShouldBe(1);
    }

    [Fact]
    public async Task Farkli_depolar_birbirini_BEKLEMEZ()
    {
        // Worktree'ler ayrı index'e sahip (ölçüldü: iki worktree'de eşzamanlı add çakışmıyor).
        // Hepsini tek kuyruğa almak kullanıcıyı gereksiz yere bekletirdi.
        using GitWriteQueue queue = new();

        using SemaphoreSlim bothStarted = new(0, 2);
        using SemaphoreSlim release = new(0, 2);

        async Task Work(string gitDirectory)
        {
            await queue.RunAsync(gitDirectory, async _ =>
            {
                bothStarted.Release();
                await release.WaitAsync(Ct);
            }, Ct);
        }

        Task first = Work("/tmp/bir/.git");
        Task second = Work("/tmp/iki/.git");

        // İkisi de başlayabilmeli: aynı kuyrukta olsalardı ikincisi hiç başlamazdı.
        await bothStarted.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await bothStarted.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        release.Release(2);

        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Ayni_depo_farkli_yazimla_verilse_de_ayni_kuyruga_duser()
    {
        // "/repo/.git" ile "/repo/.git/" aynı depo; ayrışsalardı serileştirme SESSİZCE
        // devre dışı kalırdı.
        using GitWriteQueue queue = new();

        int active = 0;
        int maxActive = 0;
        object gate = new();

        async Task Work(string path)
        {
            await queue.RunAsync(path, async _ =>
            {
                lock (gate)
                {
                    active++;
                    maxActive = Math.Max(maxActive, active);
                }

                await Task.Delay(20, Ct);

                lock (gate)
                {
                    active--;
                }
            }, Ct);
        }

        await Task.WhenAll(
            Work("/tmp/depo/.git"),
            Work("/tmp/depo/.git/"),
            Work("/tmp/depo/alt/../.git"));

        maxActive.ShouldBe(1);
        queue.TrackedRepositories.ShouldBe(1);
    }

    [Fact]
    public async Task Islem_hata_verse_de_kuyruk_serbest_kalir()
    {
        // Serbest bırakılmazsa depo kalıcı olarak kilitlenir ve uygulama donmuş görünür.
        using GitWriteQueue queue = new();

        await Should.ThrowAsync<InvalidOperationException>(
            queue.RunAsync("/tmp/depo/.git", _ => throw new InvalidOperationException("patladı"), Ct));

        bool ran = false;

        await queue.RunAsync("/tmp/depo/.git", _ =>
        {
            ran = true;
            return Task.CompletedTask;
        }, Ct);

        ran.ShouldBeTrue();
    }

    [Fact]
    public async Task Iptal_beklemeyi_sonlandirir()
    {
        using GitWriteQueue queue = new();
        using SemaphoreSlim started = new(0, 1);
        using SemaphoreSlim release = new(0, 1);

        Task holder = queue.RunAsync("/tmp/depo/.git", async _ =>
        {
            started.Release();
            await release.WaitAsync(Ct);
        }, Ct);

        await started.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        using CancellationTokenSource cancelled = new();
        Task waiting = queue.RunAsync("/tmp/depo/.git", _ => Task.CompletedTask, cancelled.Token);

        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(waiting);

        release.Release();
        await holder;
    }

    // ---- Gerçek git ----

    [Fact]
    public void GERCEK_git_kilit_varken_BEKLEMEZ_hemen_duser()
    {
        // Kuyruğun varlık sebebinin DETERMİNİST kanıtı: aşağıdaki yarış testi zamanlamaya
        // bağlı ve yavaş bir CI makinesinde zayıflayabilir; bu test her zaman aynı sonucu
        // verir.
        using TestRepository repository = CreateRepositoryWithChanges();

        string lockFile = Path.Combine(repository.Path, ".git", "index.lock");
        File.WriteAllText(lockFile, string.Empty);

        try
        {
            (int exitCode, string error) = repository.TryGit("add", "-A");

            exitCode.ShouldNotBe(0);
            error.ShouldContain("index.lock");
        }
        finally
        {
            File.Delete(lockFile);
        }
    }

    [Fact]
    public void Okumalar_kilit_varken_CALISIR()
    {
        // "Okumalar kuyruğa girmez" kararının dayanağı: git opsiyonel kilidi alamayınca
        // sessizce vazgeçiyor, hata vermiyor.
        using TestRepository repository = CreateRepositoryWithChanges();

        string lockFile = Path.Combine(repository.Path, ".git", "index.lock");
        File.WriteAllText(lockFile, string.Empty);

        try
        {
            repository.TryGit("status", "--porcelain=v2").ExitCode.ShouldBe(0);
            repository.TryGit("log", "--oneline", "-n", "1").ExitCode.ShouldBe(0);
            repository.TryGit("diff", "--stat").ExitCode.ShouldBe(0);
            repository.TryGit("diff", "--cached", "--stat").ExitCode.ShouldBe(0);
            repository.TryGit("for-each-ref").ExitCode.ShouldBe(0);
        }
        finally
        {
            File.Delete(lockFile);
        }
    }

    [Fact]
    public async Task GERCEK_git_ile_kuyruksuz_yazmalar_cakisiyor()
    {
        // Karşı kanıt: kuyruğun gerçekten bir şeyi çözdüğünü göstermek için önce
        // problemin var olduğu gösteriliyor. Bu test kuyruğu KULLANMIYOR.
        using TestRepository repository = CreateRepositoryWithChanges();

        int failures = 0;

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => Task.Run(() =>
        {
            (int exitCode, string error) = repository.TryGit("add", "-A");

            if (exitCode != 0 && error.Contains("index.lock", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref failures);
            }
        }, Ct)));

        // Ölçümde 8 yazardan 7'si düşmüştü. Zamanlamaya bağlı olduğu için "en az bir"
        // deniyor. ⚠️ Yavaş/tek çekirdekli bir CI makinesinde paralellik azalıp sıfır
        // çıkabilir; davranışın DETERMİNİST kanıtı için
        // `GERCEK_git_kilit_varken_BEKLEMEZ_hemen_duser` var.
        failures.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GERCEK_git_ile_kuyruklu_yazmalar_cakismiyor()
    {
        using TestRepository repository = CreateRepositoryWithChanges();
        using GitWriteQueue queue = new();

        string gitDirectory = Path.Combine(repository.Path, ".git");

        int failures = 0;

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ =>
            queue.RunAsync(gitDirectory, _ => Task.Run(() =>
            {
                (int exitCode, string error) = repository.TryGit("add", "-A");

                if (exitCode != 0 && error.Contains("index.lock", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref failures);
                }
            }, Ct), Ct)));

        failures.ShouldBe(0);
    }

    private static TestRepository CreateRepositoryWithChanges()
    {
        TestRepository repository = TestRepository.CreateEmpty();

        for (int i = 0; i < 150; i++)
        {
            repository.WriteFile($"dosya{i}.txt", $"satir {i}\n");
        }

        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        for (int i = 0; i < 150; i++)
        {
            repository.WriteFile($"dosya{i}.txt", $"satir {i}\ndegisiklik\n");
        }

        return repository;
    }
}
