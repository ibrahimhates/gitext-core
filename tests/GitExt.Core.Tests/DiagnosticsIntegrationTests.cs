using GitExt.Core.Diagnostics;
using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T03 — teşhisin gerçek git çağrılarıyla uçtan uca doğrulanması.
/// </summary>
/// <remarks>
/// Birim testleri toplayıcının kendisini doğruluyor; buradaki soru farklı: <b>gerçekten
/// bağlanmış mı?</b> Bir teşhis paneli, ölçtüğünü sandığı yola bağlı değilse sessizce
/// boş kalır ve o boşluk "sorun yok" diye okunur.
/// </remarks>
public class DiagnosticsIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <remarks>
    /// Ölçüm noktası <see cref="GitProcessRunner"/>'a bağlandı: ADR-0002 gereği her git
    /// çağrısı oradan geçiyor. Yazıcı/okuyucu sınıflarına tek tek eklemek, eklenmeyi
    /// unutulan yollar üretirdi.
    /// </remarks>
    [Fact]
    public async Task ShouldRecordStatisticsForRealGitCalls()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct).ConfigureAwait(true);
        GitProcessRunner runner = new(executable, log, logger: null, diagnostics: diagnostics);

        await runner.RunAsync(GitCommand.Create(repo.Path, "status", "--porcelain=v2"), Ct)
            .ConfigureAwait(true);
        await runner.RunAsync(GitCommand.Create(repo.Path, "log", "--oneline"), Ct)
            .ConfigureAwait(true);
        await runner.RunAsync(GitCommand.Create(repo.Path, "log", "-n", "1"), Ct)
            .ConfigureAwait(true);

        IReadOnlyList<GitCommandStatistics> statistics = diagnostics.CommandStatistics;

        statistics.Select(s => s.Name).ShouldBe(["log", "status"], ignoreOrder: true);
        statistics.Single(s => s.Name == "log").Count.ShouldBe(2);
        statistics.Single(s => s.Name == "status").Count.ShouldBe(1);

        // Gerçek bir süreç ölçülebilir bir süre alıyor; sıfır çıkması ölçümün hiç
        // bağlanmadığı anlamına gelirdi.
        statistics.Single(s => s.Name == "status").TotalDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    /// <remarks>
    /// 🔴 Başarısız komut da sayılmalı. Yalnızca başarılıları saymak, teşhis panelini
    /// "her şey yolunda" gösterirken kullanıcının arayüzünün hata döngüsünde olmasına
    /// izin verirdi.
    /// </remarks>
    [Fact]
    public async Task ShouldCountFailedRealGitCall()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct).ConfigureAwait(true);
        GitProcessRunner runner = new(executable, log, logger: null, diagnostics: diagnostics);

        // Var olmayan bir ref: git sıfır olmayan kodla çıkıyor.
        await runner.RunAsync(
                GitCommand.Create(repo.Path, "rev-parse", "--verify", "yok-boyle-bir-ref"),
                Ct)
            .ConfigureAwait(true);

        GitCommandStatistics statistics = diagnostics.CommandStatistics.Single();

        statistics.Name.ShouldBe("rev-parse");
        statistics.FailureCount.ShouldBe(1);
    }

    /// <remarks>
    /// Komut bittiğinde aktif listeden düşmeli. Düşmezse panel, biten işleri "devam
    /// ediyor" diye gösterir — teşhisi tam tersine çevirir.
    /// </remarks>
    [Fact]
    public async Task ShouldNotLeaveFinishedCallsActive()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct).ConfigureAwait(true);
        GitProcessRunner runner = new(executable, log, logger: null, diagnostics: diagnostics);

        await runner.RunAsync(GitCommand.Create(repo.Path, "status"), Ct).ConfigureAwait(true);

        diagnostics.ActiveOperations.ShouldBeEmpty();
    }

    /// <remarks>
    /// Akış yolu (<see cref="GitProcessRunner.StreamNulSeparatedAsync"/>) ayrı bir kod
    /// yolu; commit listesi tam olarak oradan geliyor ve teşhiste en çok merak edilen
    /// süre de o. İzlenmezse panel, uygulamanın en pahalı çağrısını hiç göstermezdi.
    /// </remarks>
    [Fact]
    public async Task ShouldNotLeaveStreamingCallsActive()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        InMemoryGitCommandLog log = new();
        using PerformanceDiagnostics diagnostics = new(log);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct).ConfigureAwait(true);
        GitProcessRunner runner = new(executable, log, logger: null, diagnostics: diagnostics);

        GitCommand command = GitCommand.Create(repo.Path, "log", "-z", "--format=%H");

        await foreach (string _ in runner.StreamNulSeparatedAsync(command, Ct).ConfigureAwait(true))
        {
            // Akış tüketiliyor; ilgilenilen şey bitişte listenin boşalması.
        }

        diagnostics.ActiveOperations.ShouldBeEmpty();
    }
}
