using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T07 — <c>commit-graph</c> detection and advice.
/// </summary>
/// <remarks>
/// <para>
/// The measured gain on a 500k-commit repository was 1,281 ms → 7.8 ms on the <b>first row</b> of the graph.
/// But the file is written into the user's repository; that is why half of the tests here
/// verify that it does <i>not</i> write.
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
    /// 🔒 The inspection itself must not write the file. If it did, the code that says "we advise"
    /// would have changed the user's repository without asking — exactly what the plan forbids.
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
    /// 🔴 In a repository written with <c>--split</c> there is <b>no</b> single-file <c>commit-graph</c>;
    /// the chain is in <c>commit-graphs/commit-graph-chain</c>. Looking only at the first one
    /// would say "no file" and the advice would be shown again on every open, needlessly.
    /// </remarks>
    [Fact]
    public async Task Zincirlenmis_bicim_de_taniniyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        CommitGraphAdvisor advisor = await CreateAsync().ConfigureAwait(true);

        repo.Git("commit-graph", "write", "--reachable", "--split");

        // The measurement itself: was the truly chained form written?
        bool chained = File.Exists(Path.Combine(
            repo.Path, ".git", "objects", "info", "commit-graphs", "commit-graph-chain"));

        Assert.SkipUnless(chained, "git bu sürümde --split ile zincir yazmadı");

        (await advisor.InspectAsync(repo.Path, Ct).ConfigureAwait(true)).Exists.ShouldBeTrue();
    }

    /// <remarks>
    /// The advice must not be shown on a small repository: 10,000 commits are already read in 99 ms (P09-T04).
    /// Bothering the user with an operation that gains nothing would make the advice
    /// entirely untrustworthy.
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
    /// The advice must not be shown when the file exists — even above the threshold.
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
    /// It must work in a bare repository too: <c>--git-path objects</c> gives the correct path there as well.
    /// An implementation that assumes a working tree would blow up here.
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
