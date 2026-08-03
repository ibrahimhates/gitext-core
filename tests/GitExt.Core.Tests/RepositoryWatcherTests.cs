using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// İzleyicinin <b>gerçek dosya sistemi ve gerçek <c>git</c></b> ile davranışı (P05-T14).
/// </summary>
/// <remarks>
/// <para>
/// Kurallar <see cref="RepositoryChangeClassifierTests"/> içinde saf olarak test ediliyor;
/// burada test edilen şey <b>zincirin tamamı</b>: git bir komut çalıştırdığında beklenen
/// olayın gerçekten gelip gelmediği. Sınıflandırıcı doğru olup izleyicinin yanlış yolu
/// vermesi mümkün, ve bu ancak burada yakalanır.
/// </para>
/// <para>
/// Testler gerçek zaman bekliyor; gecikmeler o yüzden kısa tutuldu.
/// </para>
/// </remarks>
public class RepositoryWatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Maximum = TimeSpan.FromMilliseconds(200);

    private static RepositoryWatcher CreateWatcher() =>
        new(debounceDelay: Debounce,
            maximumDelay: Maximum,
            minimumInterval: TimeSpan.Zero,
            periodicInterval: Timeout.InfiniteTimeSpan);

    private sealed class Recorder
    {
        private readonly List<RepositoryChangeKind> _events = [];
        private readonly Lock _gate = new();

        public void Attach(IRepositoryWatcher watcher) =>
            watcher.Changed += (_, e) => { lock (_gate) { _events.Add(e.Kind); } };

        public IReadOnlyList<RepositoryChangeKind> Events
        {
            get { lock (_gate) { return [.. _events]; } }
        }

        public void Clear() { lock (_gate) { _events.Clear(); } }

        /// <summary>Beklenen sayıda olay gelene kadar bekler.</summary>
        public async Task<bool> WaitAsync(int count, int timeoutMs = 5000)
        {
            for (int waited = 0; waited < timeoutMs; waited += 25)
            {
                if (Events.Count >= count)
                {
                    return true;
                }

                await Task.Delay(25, Ct);
            }

            return false;
        }
    }

    private static bool Start(RepositoryWatcher watcher, TestRepository repository)
    {
        string gitDirectory = Path.Combine(repository.Path, ".git");
        return watcher.Start(repository.Path, gitDirectory, gitDirectory);
    }

    [Fact]
    public async Task Calisma_agacindaki_dosya_degisimi_tazeleme_tetikler()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        repository.WriteFile("yeni.txt", "içerik");

        (await recorder.WaitAsync(1)).ShouldBeTrue();
        recorder.Events[0].ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public async Task HARICI_commit_depo_tazelemesi_tetikler()
    {
        // 🔴 Planı düzelten bulgunun testi. ÖLÇÜLDÜ: `git commit` 64 olay üretiyor ve
        // HEPSİ .git altında — çalışma ağacında sıfır. Plandaki ".git'i filtrele"
        // talimatı harfiyen uygulansaydı bu test kırmızı olurdu ve başka bir terminalde
        // yapılan commit ekranda hiç görünmezdi.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        repository.WriteFile("harici.txt", "içerik");
        repository.Commit("harici commit");

        (await recorder.WaitAsync(1)).ShouldBeTrue();
        recorder.Events.ShouldContain(RepositoryChangeKind.Repository);
    }

    [Fact]
    public async Task Dal_olusturma_depo_tazelemesi_tetikler()
    {
        // Yalnızca ref yazılıyor; çalışma ağacına dokunulmuyor.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        repository.Git("branch", "yeni-dal");

        (await recorder.WaitAsync(1)).ShouldBeTrue();
        recorder.Events.ShouldContain(RepositoryChangeKind.Repository);
    }

    [Fact]
    public async Task Gecmis_okumak_hicbir_olay_uretmez()
    {
        // Kilit filtresinin testi: `git log` .git altında geçici dosyalar dokunsa bile
        // tazeleme tetiklememeli.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        for (int i = 0; i < 6; i++)
        {
            repository.Git("log", "-n", "1", "--format=%H");
            repository.Git("rev-parse", "HEAD");
            await Task.Delay(100, Ct);
        }

        await Task.Delay(500, Ct);

        recorder.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Kendi_okumamiz_askidayken_HIC_olay_uretmez()
    {
        // 🔴 Sonsuz döngünün asıl kapısı. ⚠️ ÖLÇÜLDÜ ve BEKLENENDEN KÖTÜ: `git status`
        // yalnızca ilk çalıştırmada değil, "racily clean" penceresi boyunca ARDIŞIK
        // çalıştırmalarda da index'i yeniden yazıyor (`index.lock → index`) — yani
        // salt-okunur sanılan bir komut tazeleme sinyali üretiyor. Yeni yazılmış bir
        // depoda 6 ardışık okumanın 5'i olay üretti.
        //
        // Kilit filtresi bunu kapatmıyor (yeniden adlandırmanın hedefi `index`, kilit
        // değil). Kapatan tek şey tazeleme yolunun askı altında çalışmasıdır; ViewModel
        // tarafı da tam olarak öyle yapıyor (`AutoRefreshTests`).
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        using (watcher.Suspend())
        {
            for (int i = 0; i < 6; i++)
            {
                repository.Git("status", "--porcelain=v2", "--branch");
                repository.Git("log", "-n", "1", "--format=%H");
                await Task.Delay(100, Ct);
            }

            await Task.Delay(500, Ct);

            recorder.Events.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Taslak_yazimi_tazeleme_tetiklemez()
    {
        // P05-T13'ün taslağı `.git/GITEXT_COMMITMESSAGE`'a yazılıyor. Elenmeseydi kullanıcı
        // commit mesajı yazarken her kayıt tazeleme tetiklerdi.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        string draft = Path.Combine(repository.Path, ".git", CommitMessageStore.DraftFileName);

        for (int i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(draft, $"taslak {i}", Ct);
            await Task.Delay(30, Ct);
        }

        await Task.Delay(600, Ct);

        recorder.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cok_sayida_degisiklik_TEK_tazelemede_birlesir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        for (int i = 0; i < 200; i++)
        {
            repository.WriteFile($"dosya{i}.txt", "içerik");
        }

        (await recorder.WaitAsync(1)).ShouldBeTrue();
        await Task.Delay(400, Ct);

        // 200 dosya en fazla iki tazeleme yapmalı (üst sınır penceresine denk gelirse ikinci).
        recorder.Events.Count.ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task Askiya_alinmisken_tetiklenmez_devam_edince_tetiklenir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();

        IDisposable suspension = watcher.Suspend();

        repository.WriteFile("askida.txt", "içerik");
        await Task.Delay(500, Ct);

        recorder.Events.ShouldBeEmpty();

        suspension.Dispose();

        // Askı sırasında biriken değişiklik kaybolmamalı: kendi yazma işlemimiz bitince
        // dışarıdan gelen değişiklikleri de görmemiz gerekiyor.
        (await recorder.WaitAsync(1)).ShouldBeTrue();
    }

    [Fact]
    public async Task Durdurulunca_tetiklenmez()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        Start(watcher, repository).ShouldBeTrue();
        watcher.IsRunning.ShouldBeTrue();

        watcher.Stop();
        watcher.IsRunning.ShouldBeFalse();

        repository.WriteFile("kapali.txt", "içerik");
        await Task.Delay(500, Ct);

        recorder.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bagli_calisma_agacinda_git_dizini_AYRICA_izlenir()
    {
        // ⚠️ Bağlı çalışma ağacında git dizini çalışma ağacının DIŞINDA. İkinci izleyici
        // kurulmazsa o çalışma ağacındaki commit'ler hiç görülmez.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using TestRepository linked = repository.AddWorkTree("ikinci-dal");
        using RepositoryWatcher watcher = CreateWatcher();
        Recorder recorder = new();
        recorder.Attach(watcher);

        string gitDirectory = linked.Git("rev-parse", "--absolute-git-dir").Trim();
        string commonDirectory = ResolveCommonDirectory(linked);

        Path.GetFullPath(gitDirectory)
            .StartsWith(Path.GetFullPath(linked.Path) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            .ShouldBeFalse("bağlı çalışma ağacının git dizini ağacın dışında olmalı");

        // ⚠️ İki ayrı dizin: HEAD ve index bu ağacın kendi dizininde, REF'LER ortak dizinde.
        // Yalnızca git dizini izlenseydi buradaki commit'in ref güncellemesi kaçardı.
        commonDirectory.ShouldNotBe(gitDirectory);

        watcher.Start(linked.Path, gitDirectory, commonDirectory).ShouldBeTrue();

        linked.WriteFile("baglidosya.txt", "içerik");
        linked.Commit("bağlı ağaçta commit");

        (await recorder.WaitAsync(1)).ShouldBeTrue();
        recorder.Events.ShouldContain(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Var_olmayan_dizin_istisna_yerine_false_dondurur()
    {
        // Otomatik tazeleme bir kolaylık; kurulamadığında uygulama elle tazelemeyle
        // çalışmaya devam etmeli.
        using RepositoryWatcher watcher = CreateWatcher();

        string missing = Path.Combine(Path.GetTempPath(), $"yok-{Guid.NewGuid():N}");

        watcher.Start(missing, missing, missing).ShouldBeFalse();
        watcher.IsRunning.ShouldBeFalse();
    }

    /// <remarks>
    /// <c>--git-common-dir</c> normal depoda GÖRELİ döner (CLAUDE.md § 5, madde 8);
    /// çalışma dizinine göre elle çözülüyor.
    /// </remarks>
    private static string ResolveCommonDirectory(TestRepository repository)
    {
        string value = repository.Git("rev-parse", "--git-common-dir").Trim();

        return Path.GetFullPath(
            Path.IsPathRooted(value) ? value : Path.Combine(repository.Path, value));
    }
}
