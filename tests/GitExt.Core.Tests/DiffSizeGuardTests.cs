using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T06 — Büyük ve binary dosya koruması.
/// </summary>
/// <remarks>
/// <b>ÖLÇÜLDÜ:</b> tamamı değişen 12,7 MB'lık bir metin dosyası <b>23 MB</b> yama üretiyor ve
/// git bunu 0,12 saniyede yapıyor — yani sorun git'te değil, o çıktıyı belleğe alıp yüz
/// binlerce satır nesnesi yaratmakta (Faz 03'te nesne başı ek yük ölçülmüştü).
/// </remarks>
public class DiffSizeGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<DiffReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new DiffReader(new GitProcessRunner(executable));
    }

    private static CommitId Head(TestRepository repository) =>
        CommitId.Parse(repository.Git("rev-parse", "HEAD").Trim());

    /// <summary>Tamamı değişen <paramref name="lines"/> satırlık bir dosya içeren depo.</summary>
    private static TestRepository CreateWithLargeChange(int lines)
    {
        TestRepository repository = TestRepository.CreateEmpty();

        repository.WriteFile("buyuk.txt", Build(lines, "ilk"));
        repository.WriteFile("kucuk.txt", "tek satır\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("buyuk.txt", Build(lines, "ikinci"));
        repository.WriteFile("kucuk.txt", "tek satır değişti\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        return repository;

        static string Build(int count, string tag) =>
            string.Join('\n', Enumerable.Range(1, count).Select(i => $"{tag} satır {i}")) + "\n";
    }

    [Fact]
    public async Task Sinirlari_asan_dosyanin_icerigi_okunmaz_ama_listede_kalir()
    {
        using TestRepository repository = CreateWithLargeChange(500);

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path, Head(repository), new DiffOptions { MaximumChangedLines = 100 }, Ct);

        FileDiff big = diffs.Single(d => d.Path.Value == "buyuk.txt");
        FileDiff small = diffs.Single(d => d.Path.Value == "kucuk.txt");

        big.IsTooLarge.ShouldBeTrue();
        big.HasHunks.ShouldBeFalse();

        // Küçük dosya etkilenmemeli — koruma dosya BAŞINA uygulanıyor.
        small.IsTooLarge.ShouldBeFalse();
        small.HasHunks.ShouldBeTrue();
    }

    [Fact]
    public async Task Icerik_okunmasa_da_satir_sayilari_DOGRU_kalir()
    {
        // Sayılar --numstat'tan geliyor ve içerik üretilmeden alınıyor; dosya listesinde
        // "+500 −500" göstermek için yamayı okumak gerekmiyor.
        using TestRepository repository = CreateWithLargeChange(500);

        DiffReader reader = await CreateReaderAsync();

        FileDiff big = (await reader.ReadCommitAsync(
                repository.Path, Head(repository), new DiffOptions { MaximumChangedLines = 100 }, Ct))
            .Single(d => d.Path.Value == "buyuk.txt");

        big.AddedLines.ShouldBe(500);
        big.RemovedLines.ShouldBe(500);
        big.ChangedLines.ShouldBe(1000);
    }

    [Fact]
    public async Task Sinir_kapatilinca_icerik_yine_de_okunur()
    {
        // Arayüzdeki "yine de göster" bunu kullanır.
        using TestRepository repository = CreateWithLargeChange(500);

        DiffReader reader = await CreateReaderAsync();

        FileDiff big = (await reader.ReadCommitAsync(
                repository.Path, Head(repository), new DiffOptions { MaximumChangedLines = 0 }, Ct))
            .Single(d => d.Path.Value == "buyuk.txt");

        big.IsTooLarge.ShouldBeFalse();
        big.HasHunks.ShouldBeTrue();
        big.AddedLines.ShouldBe(500);
    }

    [Fact]
    public async Task Sinir_altindaki_dosya_etkilenmez()
    {
        using TestRepository repository = CreateWithLargeChange(50);

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path, Head(repository), new DiffOptions { MaximumChangedLines = 20_000 }, Ct);

        diffs.ShouldAllBe(d => !d.IsTooLarge);
        diffs.ShouldAllBe(d => d.HasHunks);
    }

    [Fact]
    public async Task Numstat_binary_dosyada_sayi_vermez()
    {
        // ÖLÇÜLDÜ: binary dosyada numstat `-` veriyor. Bunu 0 saymak "hiç değişmedi"
        // demek olurdu; null bırakılıp hunk'lardan hesaplanan değere düşülüyor.
        using TestRepository repository = TestRepository.CreateEmpty();
        File.WriteAllBytes(Path.Combine(repository.Path, "veri.bin"), [0, 1, 2, 3]);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        File.WriteAllBytes(Path.Combine(repository.Path, "veri.bin"), [9, 9, 9, 9, 9]);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        FileDiff diff = (await reader.ReadCommitAsync(repository.Path, Head(repository), cancellationToken: Ct))
            .Single();

        diff.IsBinary.ShouldBeTrue();
        diff.StatAdded.ShouldBeNull();
        diff.StatRemoved.ShouldBeNull();
        diff.IsTooLarge.ShouldBeFalse();
    }

    [Fact]
    public async Task Yeniden_adlandirmada_numstat_dogru_eslesir()
    {
        // ÖLÇÜLDÜ: rename'de numstat yolu BOŞ bırakıp iki yolu ayrı NUL jetonu olarak
        // veriyor (`0⇥0⇥` + eski + yeni). Bunu okumayan bir ayrıştırıcı sonraki tüm
        // dosyaların satır sayılarını kaydırırdı.
        using TestRepository repository = TestRepository.CreateEmpty();

        string content = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"satır {i}")) + "\n";

        repository.WriteFile("eski.txt", content);
        repository.WriteFile("digeri.txt", "bir\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.Git("mv", "eski.txt", "yeni.txt");
        repository.WriteFile("digeri.txt", "bir\niki\nüç\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path, Head(repository), cancellationToken: Ct);

        FileDiff renamed = diffs.Single(d => d.Change == FileChangeKind.Renamed);
        FileDiff other = diffs.Single(d => d.Path.Value == "digeri.txt");

        renamed.AddedLines.ShouldBe(0);
        renamed.RemovedLines.ShouldBe(0);

        // Kayma olsaydı bu dosya rename'in sayılarını alırdı.
        other.AddedLines.ShouldBe(2);
        other.RemovedLines.ShouldBe(0);
    }

    [Fact]
    public async Task Cikti_siniri_asilinca_yarim_veri_ayristirilmaz()
    {
        // Son savunma hattı: yarım çıktıyı ayrıştırmak sessizce EKSİK diff göstermek olurdu.
        using TestRepository repository = CreateWithLargeChange(2000);

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path,
            Head(repository),
            new DiffOptions { MaximumChangedLines = 0, MaximumOutputBytes = 4096 },
            Ct);

        // Dosya listesi yine gelmeli — kullanıcı neyin değiştiğini görmeli.
        diffs.Select(d => d.Path.Value).ShouldBe(["buyuk.txt", "kucuk.txt"], ignoreOrder: true);

        // Ama içerik yok ve bu açıkça işaretli.
        diffs.ShouldAllBe(d => !d.HasHunks);
        diffs.ShouldAllBe(d => d.IsTooLarge);

        // Satır sayıları yine doğru.
        diffs.Single(d => d.Path.Value == "buyuk.txt").AddedLines.ShouldBe(2000);
    }

    [Fact]
    public async Task Cikti_siniri_normal_diffi_etkilemez()
    {
        using TestRepository repository = CreateWithLargeChange(10);

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path,
            Head(repository),
            new DiffOptions { MaximumOutputBytes = 64L * 1024 * 1024 },
            Ct);

        diffs.ShouldAllBe(d => d.HasHunks);
        diffs.ShouldAllBe(d => !d.IsTooLarge);
    }
}
