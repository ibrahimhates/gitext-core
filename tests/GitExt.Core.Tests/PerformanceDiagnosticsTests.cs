using GitExt.Core.Diagnostics;
using GitExt.Core.Git;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T03 — performans teşhis toplayıcısı.
/// </summary>
/// <remarks>
/// Teşhis panelinin değeri doğru sayı göstermesinde. Yanlış bir istatistik, "yavaş"
/// şikâyetini yanlış yere yönlendirir — hiç sayı olmamasından daha kötüdür.
/// </remarks>
public class PerformanceDiagnosticsTests
{
    // ------------------------------------------------------- komut adı çıkarma

    /// <remarks>
    /// 🔴 <c>-c key=value</c> alt komuttan <b>önce</b> geliyor. İlk kelimeyi almak bütün
    /// bu komutları <c>-c</c> diye gruplardı; istatistik tablosunda "hangi komut pahalı"
    /// sorusu görünmez olurdu. Değer kısmı da atlanmalı, yoksa <c>core.editor=…</c> komut
    /// adı sanılır.
    /// </remarks>
    [Theory]
    [InlineData("git log --oneline", "log")]
    [InlineData("git -c core.editor=false rebase --continue", "rebase")]
    [InlineData("git -c a=1 -c b=2 status --porcelain=v2", "status")]
    [InlineData("git --no-pager diff", "diff")]
    [InlineData("log -n 50", "log")]
    [InlineData("", "(bilinmiyor)")]
    [InlineData("   ", "(bilinmiyor)")]
    [InlineData("git", "(bilinmiyor)")]
    [InlineData("git -c only=option", "(bilinmiyor)")]
    public void ShouldExtractSubcommandName(string commandLine, string expected) =>
        PerformanceDiagnostics.ExtractCommandName(commandLine).ShouldBe(expected);

    // ------------------------------------------------------- istatistik toplama

    [Fact]
    public void ShouldGroupRunsBySubcommand()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        Record(log, TimeSpan.FromMilliseconds(10), success: true, "log", "--oneline");
        Record(log, TimeSpan.FromMilliseconds(30), success: true, "log", "-n", "50");
        Record(log, TimeSpan.FromMilliseconds(5), success: true, "status");

        IReadOnlyList<GitCommandStatistics> statistics = diagnostics.CommandStatistics;

        statistics.Count.ShouldBe(2);

        GitCommandStatistics logStatistics = statistics.Single(s => s.Name == "log");
        logStatistics.Count.ShouldBe(2);
        logStatistics.TotalDuration.ShouldBe(TimeSpan.FromMilliseconds(40));
        logStatistics.AverageDuration.ShouldBe(TimeSpan.FromMilliseconds(20));
        logStatistics.MaxDuration.ShouldBe(TimeSpan.FromMilliseconds(30));
    }

    /// <remarks>
    /// Sıralama toplam süreye göre: teşhiste aranan şey "zamanın nereye gittiği".
    /// Adet sıralaması hızlı ama çok çağrılan bir komutu tepeye koyar ve asıl pahalı
    /// olanı gizlerdi.
    /// </remarks>
    [Fact]
    public void ShouldOrderByTotalDurationDescending()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        // status daha çok çağrılıyor ama toplamda daha ucuz.
        for (int i = 0; i < 10; i++)
        {
            Record(log, TimeSpan.FromMilliseconds(1), success: true, "status");
        }

        Record(log, TimeSpan.FromMilliseconds(500), success: true, "log");

        diagnostics.CommandStatistics[0].Name.ShouldBe("log");
    }

    [Fact]
    public void ShouldCountFailures()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        Record(log, TimeSpan.FromMilliseconds(10), success: true, "push");
        Record(log, TimeSpan.FromMilliseconds(10), success: false, "push");
        Record(log, TimeSpan.FromMilliseconds(10), success: false, "push");

        GitCommandStatistics statistics = diagnostics.CommandStatistics.Single();

        statistics.Count.ShouldBe(3);
        statistics.FailureCount.ShouldBe(2);
    }

    /// <remarks>
    /// Sıfır çalıştırmada ortalama hesaplamak sıfıra bölme olurdu.
    /// </remarks>
    [Fact]
    public void ShouldReportZeroAverageForEmptyStatistics()
    {
        GitCommandStatistics empty = new()
        {
            Name = "log",
            Count = 0,
            TotalDuration = TimeSpan.Zero,
            MaxDuration = TimeSpan.Zero,
            FailureCount = 0,
        };

        empty.AverageDuration.ShouldBe(TimeSpan.Zero);
    }

    // ------------------------------------------------------- aktif işler

    [Fact]
    public void ShouldTrackActiveOperationUntilDisposed()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        diagnostics.ActiveOperations.ShouldBeEmpty();

        using (diagnostics.TrackOperation("fetch: origin"))
        {
            diagnostics.ActiveOperations.ShouldBe(["fetch: origin"]);
        }

        diagnostics.ActiveOperations.ShouldBeEmpty();
    }

    /// <remarks>
    /// Aynı adla iki iş aynı anda sürebilir (iki uzak depoya paralel fetch). Ada göre
    /// saklamak birini görünmez yapardı; kimlik sayaçtan geliyor.
    /// </remarks>
    [Fact]
    public void ShouldTrackOperationsWithIdenticalNamesSeparately()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        IDisposable first = diagnostics.TrackOperation("fetch");
        IDisposable second = diagnostics.TrackOperation("fetch");

        diagnostics.ActiveOperations.Count.ShouldBe(2);

        first.Dispose();
        diagnostics.ActiveOperations.Count.ShouldBe(1);

        second.Dispose();
        diagnostics.ActiveOperations.ShouldBeEmpty();
    }

    /// <remarks>
    /// İki kez <c>Dispose</c> etmek başka bir işi listeden düşürmemeli.
    /// </remarks>
    [Fact]
    public void ShouldIgnoreRepeatedDispose()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        IDisposable scope = diagnostics.TrackOperation("fetch");
        using IDisposable other = diagnostics.TrackOperation("pull");

        scope.Dispose();
        scope.Dispose();

        diagnostics.ActiveOperations.ShouldBe(["pull"]);
    }

    // ------------------------------------------------------- sıfırlama

    /// <remarks>
    /// 🔴 Sıfırlama <b>aktif işleri silmemeli</b>: hâlâ çalışıyorlar. Listeden düşerlerse
    /// bittikleri sanılır ve donmuş bir arayüzün sebebi tam da o anda görünmez olur.
    /// </remarks>
    [Fact]
    public void ShouldClearStatisticsButKeepActiveOperations()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        Record(log, TimeSpan.FromMilliseconds(10), success: true, "log");
        using IDisposable running = diagnostics.TrackOperation("clone: büyük depo");

        diagnostics.Reset();

        diagnostics.CommandStatistics.ShouldBeEmpty();
        diagnostics.ActiveOperations.ShouldBe(["clone: büyük depo"]);
    }

    // ------------------------------------------------------- yaşam döngüsü

    /// <remarks>
    /// <c>Dispose</c> aboneliği bırakmalı; bırakmazsa günlük teşhis nesnesini süresiz
    /// canlı tutar ve uzun oturumda sızıntı olur.
    /// </remarks>
    [Fact]
    public void ShouldStopCollectingAfterDispose()
    {
        InMemoryGitCommandLog log = new();
        PerformanceDiagnostics diagnostics = new(log);

        Record(log, TimeSpan.FromMilliseconds(10), success: true, "log");
        diagnostics.CommandStatistics.Count.ShouldBe(1);

        diagnostics.Dispose();
        Record(log, TimeSpan.FromMilliseconds(10), success: true, "status");

        diagnostics.CommandStatistics.Count.ShouldBe(1);
    }

    [Fact]
    public void ShouldReportMemoryAndUptime()
    {
        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        MemorySnapshot memory = diagnostics.Memory;

        memory.ManagedBytes.ShouldBeGreaterThan(0);
        memory.Gen0.ShouldBeGreaterThanOrEqualTo(0);

        diagnostics.Uptime.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    // ------------------------------------------------------- boş uygulama

    /// <remarks>
    /// Teşhis kapalıyken hiçbir çağrı patlamamalı — özellikle <c>TrackOperation</c>'ın
    /// döndürdüğü nesne <see langword="null"/> olamaz, <c>using</c> içinde kullanılıyor.
    /// </remarks>
    [Fact]
    public void NullDiagnosticsShouldBeSafe()
    {
        IPerformanceDiagnostics diagnostics = NullPerformanceDiagnostics.Instance;

        diagnostics.ActiveOperations.ShouldBeEmpty();
        diagnostics.CommandStatistics.ShouldBeEmpty();
        diagnostics.Uptime.ShouldBe(TimeSpan.Zero);
        diagnostics.Reset();

        using IDisposable scope = diagnostics.TrackOperation("iş");
        scope.ShouldNotBeNull();
    }

    /// <summary>
    /// Günlüğe tek bir çalıştırma kaydı yazar.
    /// </summary>
    /// <remarks>
    /// <paramref name="arguments"/> <c>git</c> olmadan veriliyor; günlüğe giren metni
    /// <see cref="GitCommand.ToDisplayString"/> üretiyor — testin ürettiği dize ile
    /// gerçekte kaydedilen dize aynı yoldan geçsin diye.
    /// </remarks>
    private static void Record(
        InMemoryGitCommandLog log,
        TimeSpan duration,
        bool success,
        params string[] arguments)
    {
        GitCommand command = GitCommand.Create("/tmp", arguments);

        log.Record(new GitResult(
            command,
            exitCode: success ? 0 : 1,
            standardOutput: [],
            standardError: success ? string.Empty : "hata",
            duration: duration));
    }
}
