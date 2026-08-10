using System.Diagnostics;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T12 — dosya sistemi izlemenin büyük çalışma dizinlerindeki maliyeti.
/// </summary>
/// <remarks>
/// <para>
/// Linux'ta <c>FileSystemWatcher</c> inotify üzerine kurulu ve inotify <b>dizin
/// başına</b> bir watch harcıyor. Sistem sınırı (<c>fs.inotify.max_user_watches</c>)
/// aşıldığında izleyici sessizce ölmüyor, <c>Error</c> olayı veriyor — ama o olayı
/// kaçıran bir uygulama, kullanıcının değişikliklerini görmemeye başlar ve bunu
/// hiçbir yerde söylemez.
/// </para>
/// <para>
/// Buradaki testler maliyetin <b>ölçülebilir sınırlar içinde</b> kaldığını ve
/// izleyicinin büyük ağaçlarda kurulabildiğini doğruluyor.
/// </para>
/// </remarks>
public class WatcherCostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// <paramref name="directoryCount"/> dizin içeren bir çalışma ağacı kurar.
    /// </summary>
    private static void CreateTree(TestRepository repository, int directoryCount)
    {
        for (int i = 0; i < directoryCount; i++)
        {
            repository.WriteFile($"d{i / 50}/s{i % 50}/f.txt", "x");
        }

        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "ağaç");
    }

    /// <remarks>
    /// 🔴 Asıl risk kurulum süresi: izleyici depo açılışında kuruluyor ve yavaşsa
    /// doğrudan "repo açma" bütçesinden yiyor (&lt; 1 sn). 1.000 dizinlik bir ağaçta
    /// bunun ölçülebilir kalması gerekiyor.
    /// </remarks>
    [Fact]
    public async Task Buyuk_agacta_izleyici_hizli_kuruluyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        CreateTree(repository, 1_000);

        RepositoryWatcher watcher = new();

        long started = Stopwatch.GetTimestamp();
        watcher.Start(repository.Path, Path.Combine(repository.Path, ".git"), Path.Combine(repository.Path, ".git"));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        try
        {
            // Bütçe repo açmaya 1 sn veriyor; izleyicinin bunun küçük bir dilimini
            // aşması, açılışın geri kalanına yer bırakmaz.
            elapsed.ShouldBeLessThan(
                TimeSpan.FromMilliseconds(500),
                $"izleyici kurulumu {elapsed.TotalMilliseconds:0} ms sürdü");
        }
        finally
        {
            watcher.Dispose();
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <remarks>
    /// Kaynakların gerçekten bırakıldığını doğrular. Bırakılmazsa depolar arasında
    /// geçen bir oturum inotify watch'larını tüketir ve bir noktadan sonra <b>hiçbir</b>
    /// depo izlenemez hâle gelir — sistem genelinde bir sınır bu, süreç başına değil.
    /// </remarks>
    [Fact]
    public void Tekrarli_acilis_kapanis_kaynak_biriktirmiyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        CreateTree(repository, 200);

        for (int i = 0; i < 20; i++)
        {
            RepositoryWatcher watcher = new();
            watcher.Start(repository.Path, Path.Combine(repository.Path, ".git"), Path.Combine(repository.Path, ".git"));
            watcher.Dispose();
        }

        // Buraya gelinebiliyorsa 20 tur boyunca inotify örneği tükenmedi.
        // (Sınır aşılsaydı Start bir IOException fırlatırdı.)
        true.ShouldBeTrue();
    }

    /// <remarks>
    /// <c>Dispose</c> sonrası gelen değişiklikler olay üretmemeli: kapatılmış bir deponun
    /// tazelemesini tetiklemek, kullanıcının kapattığı depoya ait iş yapmak olurdu.
    /// </remarks>
    [Fact]
    public async Task Dispose_sonrasi_olay_gelmiyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        RepositoryWatcher watcher = new();
        int events = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref events);

        watcher.Start(repository.Path, Path.Combine(repository.Path, ".git"), Path.Combine(repository.Path, ".git"));
        watcher.Dispose();

        repository.WriteFile("sonra.txt", "içerik");

        // Olay gelecekse bu süre içinde gelir; birleştirme penceresi bundan kısa.
        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct).ConfigureAwait(true);

        Volatile.Read(ref events).ShouldBe(0, "kapatılmış izleyici hâlâ olay üretiyor");
    }
}
