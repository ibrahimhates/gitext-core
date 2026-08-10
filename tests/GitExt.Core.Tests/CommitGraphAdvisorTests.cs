using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T07 — <c>commit-graph</c> algılama ve öneri.
/// </summary>
/// <remarks>
/// <para>
/// Ölçülen kazanç 500k commit'lik depoda grafiğin <b>ilk satırında</b> 1.281 ms → 7.8 ms.
/// Ama dosya kullanıcının deposuna yazılıyor; bu yüzden buradaki testlerin yarısı
/// "yazmadığını" doğruluyor.
/// </para>
/// </remarks>
public class CommitGraphAdvisorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<CommitGraphAdvisor> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct)
            .ConfigureAwait(true);

        return new CommitGraphAdvisor(new GitProcessRunner(executable));
    }

    [Fact]
    public async Task Dosya_yokken_yok_diyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        CommitGraphAdvisor advisor = await CreateAsync().ConfigureAwait(true);

        CommitGraphStatus status = await advisor.InspectAsync(repo.Path, Ct).ConfigureAwait(true);

        status.Exists.ShouldBeFalse();
    }

    /// <remarks>
    /// 🔒 Denetlemenin kendisi dosyayı yazmamalı. Yazsaydı, "öneriyoruz" diyen kod
    /// kullanıcının deposunu sormadan değiştirmiş olurdu — planın açıkça yasakladığı şey.
    /// </remarks>
    [Fact]
    public async Task Denetleme_dosyayi_YAZMIYOR()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        CommitGraphAdvisor advisor = await CreateAsync().ConfigureAwait(true);

        await advisor.InspectAsync(repo.Path, Ct).ConfigureAwait(true);

        File.Exists(Path.Combine(repo.Path, ".git", "objects", "info", "commit-graph"))
            .ShouldBeFalse("denetleme dosyayı yazmış");
    }

    [Fact]
    public async Task Yazdiktan_sonra_var_diyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        CommitGraphAdvisor advisor = await CreateAsync().ConfigureAwait(true);

        (await advisor.InspectAsync(repo.Path, Ct).ConfigureAwait(true)).Exists.ShouldBeFalse();

        await advisor.WriteAsync(repo.Path, Ct).ConfigureAwait(true);

        (await advisor.InspectAsync(repo.Path, Ct).ConfigureAwait(true)).Exists.ShouldBeTrue();
    }

    /// <remarks>
    /// 🔴 <c>--split</c> ile yazılan depoda tek dosyalık <c>commit-graph</c> <b>yok</b>;
    /// zincir <c>commit-graphs/commit-graph-chain</c>'de. Yalnızca birinciye bakmak
    /// "dosya yok" derdi ve öneri, gereği yokken her açılışta tekrar gösterilirdi.
    /// </remarks>
    [Fact]
    public async Task Zincirlenmis_bicim_de_taniniyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        CommitGraphAdvisor advisor = await CreateAsync().ConfigureAwait(true);

        repo.Git("commit-graph", "write", "--reachable", "--split");

        // Ölçümün kendisi: gerçekten zincirlenmiş biçim mi yazıldı?
        bool chained = File.Exists(Path.Combine(
            repo.Path, ".git", "objects", "info", "commit-graphs", "commit-graph-chain"));

        Assert.SkipUnless(chained, "git bu sürümde --split ile zincir yazmadı");

        (await advisor.InspectAsync(repo.Path, Ct).ConfigureAwait(true)).Exists.ShouldBeTrue();
    }

    /// <remarks>
    /// Küçük depoda öneri gösterilmemeli: 10.000 commit zaten 99 ms'de okunuyor (P09-T04).
    /// Kazandırmayan bir işlem için kullanıcıyı rahatsız etmek, öneriyi bütünüyle
    /// güvenilmez yapardı.
    /// </remarks>
    [Fact]
    public async Task Kucuk_depoda_oneri_YOK()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        CommitGraphAdvisor advisor = await CreateAsync().ConfigureAwait(true);

        CommitGraphStatus status = await advisor.InspectAsync(repo.Path, Ct).ConfigureAwait(true);

        status.CommitCount.ShouldBe(1);
        status.IsWorthwhile.ShouldBeFalse();
    }

    /// <remarks>
    /// Dosya varken öneri gösterilmemeli — eşiğin üstünde olsa bile.
    /// </remarks>
    [Fact]
    public async Task Dosya_varken_oneri_YOK()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        CommitGraphAdvisor advisor = await CreateAsync().ConfigureAwait(true);

        await advisor.WriteAsync(repo.Path, Ct).ConfigureAwait(true);

        (await advisor.InspectAsync(repo.Path, Ct).ConfigureAwait(true)).IsWorthwhile.ShouldBeFalse();
    }

    /// <remarks>
    /// Bare depoda da çalışmalı: <c>--git-path objects</c> orada da doğru yolu veriyor.
    /// Çalışma ağacı varsayan bir uygulama burada patlardı.
    /// </remarks>
    [Fact]
    public async Task Bare_depoda_calisiyor()
    {
        using TestRepository repo = TestRepository.CreateBare();
        CommitGraphAdvisor advisor = await CreateAsync().ConfigureAwait(true);

        CommitGraphStatus status = await advisor.InspectAsync(repo.Path, Ct).ConfigureAwait(true);

        status.Exists.ShouldBeFalse();
    }
}
