

namespace GitExt.Core.Tests;

/// <summary>
/// Olay birleştirme kuralları (P05-T14).
/// </summary>
/// <remarks>
/// Zaman elle ilerletiliyor: gerçek zamanlayıcı kullanılsaydı bu testler hem yavaş hem
/// yavaş makinede kırılgan olurdu.
/// </remarks>
public class ChangeCoalescerTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static ChangeCoalescer Create(
        int debounceMs = 500,
        int maximumMs = 2000,
        int minimumIntervalMs = 0)
    {
        return new ChangeCoalescer(
            TimeSpan.FromMilliseconds(debounceMs),
            TimeSpan.FromMilliseconds(maximumMs),
            TimeSpan.FromMilliseconds(minimumIntervalMs));
    }

    [Fact]
    public void Bir_olay_debounce_suresi_kadar_bekletir()
    {
        ChangeCoalescer coalescer = Create();

        coalescer.Add(RepositoryChangeKind.WorkingTree, Start)
            .ShouldBe(TimeSpan.FromMilliseconds(500));

        coalescer.TryTake(Start, out TimeSpan? wait).ShouldBeNull();
        wait.ShouldBe(TimeSpan.FromMilliseconds(500));

        coalescer.TryTake(Start.AddMilliseconds(500), out _)
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Iki_bin_olay_TEK_tazelemeye_iner()
    {
        // ÖLÇÜLDÜ: 800 dosyalık dal değişimi 2102 olay üretti, hepsi ~50 ms içinde.
        ChangeCoalescer coalescer = Create();

        for (int i = 0; i < 2102; i++)
        {
            coalescer.Add(RepositoryChangeKind.WorkingTree, Start.AddMilliseconds(i % 50));
        }

        coalescer.TryTake(Start.AddMilliseconds(600), out _)
            .ShouldBe(RepositoryChangeKind.WorkingTree);

        // İkinci alışta bekleyen bir şey kalmamalı.
        coalescer.TryTake(Start.AddMilliseconds(2000), out TimeSpan? wait).ShouldBeNull();
        wait.ShouldBeNull();
    }

    [Fact]
    public void Surekli_akan_olaylar_tazelemeyi_SONSUZA_KADAR_erteleyemez()
    {
        // ÖLÇÜLDÜ: tek projelik bir `dotnet build` 1,5 saniye boyunca 92 olay üretti.
        // Saf "her olayda sayacı sıfırla" debounce bu süre boyunca hiç tetiklenmezdi.
        ChangeCoalescer coalescer = Create(debounceMs: 500, maximumMs: 2000);

        // Her 100 ms'de bir olay: debounce penceresi hiç dolmuyor.
        for (int ms = 0; ms <= 1900; ms += 100)
        {
            coalescer.Add(RepositoryChangeKind.WorkingTree, Start.AddMilliseconds(ms));
        }

        // Üst sınır dolduğu an tetiklenmeli.
        coalescer.TryTake(Start.AddMilliseconds(2000), out _)
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Ust_sinir_dolarken_bekleme_suresi_kisalir()
    {
        ChangeCoalescer coalescer = Create(debounceMs: 500, maximumMs: 2000);

        coalescer.Add(RepositoryChangeKind.WorkingTree, Start);

        // 1800 ms sonra gelen olay için 500 ms beklenirse üst sınır aşılır: 200 ms kalmalı.
        coalescer.Add(RepositoryChangeKind.WorkingTree, Start.AddMilliseconds(1800))
            .ShouldBe(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Depo_degisimi_calisma_agaci_degisimini_yutar()
    {
        ChangeCoalescer coalescer = Create();

        coalescer.Add(RepositoryChangeKind.WorkingTree, Start);
        coalescer.Add(RepositoryChangeKind.Repository, Start.AddMilliseconds(10));
        coalescer.Add(RepositoryChangeKind.WorkingTree, Start.AddMilliseconds(20));

        // Daha kapsamlı olan kazanmalı; aksi halde dal değişiminden sonra commit listesi
        // bayat kalırdı.
        coalescer.TryTake(Start.AddMilliseconds(600), out _)
            .ShouldBe(RepositoryChangeKind.Repository);
    }

    [Fact]
    public void Minimum_aralik_arka_arkaya_tazelemeyi_sinirlar()
    {
        ChangeCoalescer coalescer = Create(debounceMs: 100, maximumMs: 200, minimumIntervalMs: 5000);

        coalescer.Add(RepositoryChangeKind.WorkingTree, Start);
        coalescer.TryTake(Start.AddMilliseconds(100), out _)
            .ShouldBe(RepositoryChangeKind.WorkingTree);

        // Hemen ardından gelen olay minimum aralık dolana kadar beklemeli — üst sınır
        // (200 ms) dolmuş olsa bile.
        coalescer.Add(RepositoryChangeKind.WorkingTree, Start.AddMilliseconds(200));
        coalescer.TryTake(Start.AddMilliseconds(1000), out TimeSpan? wait).ShouldBeNull();
        wait.ShouldBe(TimeSpan.FromMilliseconds(4100));

        coalescer.TryTake(Start.AddMilliseconds(5100), out _)
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Ilk_tazeleme_minimum_araliktan_etkilenmez()
    {
        // Depo yeni açıldığında "son tazeleme" yok; kullanıcı ilk değişikliği hemen görmeli.
        ChangeCoalescer coalescer = Create(debounceMs: 100, maximumMs: 200, minimumIntervalMs: 30_000);

        coalescer.Add(RepositoryChangeKind.WorkingTree, Start);

        coalescer.TryTake(Start.AddMilliseconds(100), out _)
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Bekleyen_yokken_alis_null_doner()
    {
        ChangeCoalescer coalescer = Create();

        coalescer.HasPending.ShouldBeFalse();
        coalescer.TryTake(Start, out TimeSpan? wait).ShouldBeNull();
        wait.ShouldBeNull();
    }

    [Fact]
    public void Sifirlama_bekleyeni_atar()
    {
        ChangeCoalescer coalescer = Create();

        coalescer.Add(RepositoryChangeKind.Repository, Start);
        coalescer.HasPending.ShouldBeTrue();

        coalescer.Reset();

        coalescer.HasPending.ShouldBeFalse();
        coalescer.TryTake(Start.AddSeconds(10), out _).ShouldBeNull();
    }
}
