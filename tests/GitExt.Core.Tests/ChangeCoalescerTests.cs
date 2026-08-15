

namespace GitExt.Core.Tests;

/// <summary>
/// Event coalescing rules (P05-T14).
/// </summary>
/// <remarks>
/// Time is advanced manually: had a real timer been used these tests would be both slow and
/// fragile on a slow machine.
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
        // MEASURED: a branch switch over 800 files produced 2102 events, all within ~50 ms.
        ChangeCoalescer coalescer = Create();

        for (int i = 0; i < 2102; i++)
        {
            coalescer.Add(RepositoryChangeKind.WorkingTree, Start.AddMilliseconds(i % 50));
        }

        coalescer.TryTake(Start.AddMilliseconds(600), out _)
            .ShouldBe(RepositoryChangeKind.WorkingTree);

        // On the second drain nothing should be left pending.
        coalescer.TryTake(Start.AddMilliseconds(2000), out TimeSpan? wait).ShouldBeNull();
        wait.ShouldBeNull();
    }

    [Fact]
    public void Surekli_akan_olaylar_tazelemeyi_SONSUZA_KADAR_erteleyemez()
    {
        // MEASURED: a single-project `dotnet build` produced 92 events over 1.5 seconds.
        // A pure "reset the counter on every event" debounce would never fire during that time.
        ChangeCoalescer coalescer = Create(debounceMs: 500, maximumMs: 2000);

        // One event every 100 ms: the debounce window never fills.
        for (int ms = 0; ms <= 1900; ms += 100)
        {
            coalescer.Add(RepositoryChangeKind.WorkingTree, Start.AddMilliseconds(ms));
        }

        // It must fire the moment the upper bound is reached.
        coalescer.TryTake(Start.AddMilliseconds(2000), out _)
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Ust_sinir_dolarken_bekleme_suresi_kisalir()
    {
        ChangeCoalescer coalescer = Create(debounceMs: 500, maximumMs: 2000);

        coalescer.Add(RepositoryChangeKind.WorkingTree, Start);

        // If 500 ms were waited for an event arriving after 1800 ms the upper bound would be exceeded: 200 ms must remain.
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

        // The broader one must win; otherwise the commit list would stay stale after a
        // branch switch.
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

        // An event arriving immediately afterwards must wait until the minimum interval elapses —
        // even if the upper bound (200 ms) has already been reached.
        coalescer.Add(RepositoryChangeKind.WorkingTree, Start.AddMilliseconds(200));
        coalescer.TryTake(Start.AddMilliseconds(1000), out TimeSpan? wait).ShouldBeNull();
        wait.ShouldBe(TimeSpan.FromMilliseconds(4100));

        coalescer.TryTake(Start.AddMilliseconds(5100), out _)
            .ShouldBe(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Ilk_tazeleme_minimum_araliktan_etkilenmez()
    {
        // When a repository is freshly opened there is no "last refresh"; the user must see the first change immediately.
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
