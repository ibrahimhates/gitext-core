using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P14-T20 — <c>git bisect</c>, against real git.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>MEASURED — the exit code says nothing.</b> Every step returns 0, the last one included.
/// Whether the search is over is only in the output.
/// </para>
/// <para>
/// 🔴 <b>MEASURED — finishing does not end the bisect.</b> After the culprit is named the
/// <c>BISECT_*</c> files are still there and HEAD is still detached; only <c>reset</c> clears them.
/// </para>
/// </remarks>
public class BisectWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(BisectWriter Writer, TestRepository Repository)> CreateAsync(
        int commits = 12)
    {
        TestRepository repository = TestRepository.CreateWithSingleCommit();

        for (int index = 2; index <= commits; index++)
        {
            repository.WriteFile("f.txt", string.Concat(Enumerable.Repeat("satir\n", index)));
            repository.Git("add", "f.txt");
            repository.Commit($"commit {index}");
        }

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return (new BisectWriter(new GitWriter(runner, queue)), repository);
    }

    [Fact]
    public async Task Baslatmak_iki_uc_beklendigini_soyluyor()
    {
        (BisectWriter bisect, TestRepository repository) = await CreateAsync();
        using TestRepository _ = repository;

        BisectStatus status = await bisect.StartAsync(repository.Path, cancellationToken: Ct);

        status.IsFinished.ShouldBeFalse();
        status.Output.ShouldContain("waiting");
    }

    [Fact]
    public async Task Baslarken_uclar_verilince_ilk_adima_gidiliyor()
    {
        (BisectWriter bisect, TestRepository repository) = await CreateAsync();
        using TestRepository _ = repository;

        string first = repository.Git("rev-list", "--max-parents=0", "HEAD").Trim();

        BisectStatus status = await bisect
            .StartAsync(repository.Path, badCommit: "HEAD", goodCommit: first, cancellationToken: Ct);

        status.IsFinished.ShouldBeFalse();
        status.RemainingRevisions.ShouldNotBeNull();
        status.RemainingRevisions!.Value.ShouldBeGreaterThan(0);
        status.RemainingSteps.ShouldNotBeNull();
    }

    [Fact]
    public async Task Bisect_sirasinda_HEAD_ayrik()
    {
        (BisectWriter bisect, TestRepository repository) = await CreateAsync();
        using TestRepository _ = repository;

        string first = repository.Git("rev-list", "--max-parents=0", "HEAD").Trim();

        await bisect.StartAsync(repository.Path, "HEAD", first, Ct);

        repository.Git("rev-parse", "--abbrev-ref", "HEAD").Trim().ShouldBe("HEAD");
    }

    [Fact]
    public async Task Arama_ilk_bozuk_commiti_buluyor()
    {
        (BisectWriter bisect, TestRepository repository) = await CreateAsync();
        using TestRepository _ = repository;

        string first = repository.Git("rev-list", "--max-parents=0", "HEAD").Trim();

        BisectStatus status = await bisect.StartAsync(repository.Path, "HEAD", first, Ct);

        // Every commit is "good" until the search narrows to one; answering "bad" each time drives
        // it to a definite answer without needing to know which commit git will offer.
        for (int guard = 0; guard < 20 && !status.IsFinished; guard++)
        {
            status = await bisect
                .MarkAsync(repository.Path, BisectVerdict.Bad, cancellationToken: Ct)
                .ConfigureAwait(true);
        }

        status.IsFinished.ShouldBeTrue(status.Output);
        status.FirstBadCommit.ShouldNotBeNull();
        status.FirstBadCommit!.Length.ShouldBe(40);
    }

    [Fact]
    public async Task Bitis_bisecti_KAPATMIYOR()
    {
        (BisectWriter bisect, TestRepository repository) = await CreateAsync();
        using TestRepository _ = repository;

        string first = repository.Git("rev-list", "--max-parents=0", "HEAD").Trim();

        BisectStatus status = await bisect.StartAsync(repository.Path, "HEAD", first, Ct);

        for (int guard = 0; guard < 20 && !status.IsFinished; guard++)
        {
            status = await bisect.MarkAsync(repository.Path, BisectVerdict.Bad, cancellationToken: Ct);
        }

        status.IsFinished.ShouldBeTrue();

        // The marker file git leaves behind is what the banner reads; it survives the answer.
        File.Exists(Path.Combine(repository.Path, ".git", "BISECT_LOG")).ShouldBeTrue();
        repository.Git("rev-parse", "--abbrev-ref", "HEAD").Trim().ShouldBe("HEAD");
    }

    [Fact]
    public async Task Sifirlamak_dala_geri_donuyor()
    {
        (BisectWriter bisect, TestRepository repository) = await CreateAsync();
        using TestRepository _ = repository;

        string first = repository.Git("rev-list", "--max-parents=0", "HEAD").Trim();

        await bisect.StartAsync(repository.Path, "HEAD", first, Ct);
        await bisect.ResetAsync(repository.Path, Ct);

        File.Exists(Path.Combine(repository.Path, ".git", "BISECT_LOG")).ShouldBeFalse();
        repository.Git("rev-parse", "--abbrev-ref", "HEAD").Trim().ShouldBe("main");
    }

    [Fact]
    public async Task Atlamak_baska_bir_commit_oneriyor()
    {
        (BisectWriter bisect, TestRepository repository) = await CreateAsync();
        using TestRepository _ = repository;

        string first = repository.Git("rev-list", "--max-parents=0", "HEAD").Trim();

        await bisect.StartAsync(repository.Path, "HEAD", first, Ct);

        string before = repository.Git("rev-parse", "HEAD").Trim();

        BisectStatus status = await bisect
            .MarkAsync(repository.Path, BisectVerdict.Skip, cancellationToken: Ct);

        string after = repository.Git("rev-parse", "HEAD").Trim();

        status.IsFinished.ShouldBeFalse();
        after.ShouldNotBe(before);
    }

    [Fact]
    public async Task Bisect_disinda_isaretlemek_HATA_veriyor()
    {
        (BisectWriter bisect, TestRepository repository) = await CreateAsync(commits: 3);
        using TestRepository _ = repository;

        await Should.ThrowAsync<GitException>(() =>
            bisect.MarkAsync(repository.Path, BisectVerdict.Good, cancellationToken: Ct));
    }
}

/// <summary>P14-T20 — reading git's bisect output.</summary>
public class BisectOutputParserTests
{
    [Fact]
    public void Ilerleme_satirindan_sayilar_okunuyor()
    {
        BisectStatus status = BisectOutputParser.Parse(
            "Bisecting: 5 revisions left to test after this (roughly 3 steps)\n[abc] commit 6");

        status.RemainingRevisions.ShouldBe(5);
        status.RemainingSteps.ShouldBe(3);
        status.IsFinished.ShouldBeFalse();
    }

    [Fact]
    public void Sifir_kalan_da_okunuyor()
    {
        BisectStatus status = BisectOutputParser.Parse(
            "Bisecting: 0 revisions left to test after this (roughly 0 steps)");

        status.RemainingRevisions.ShouldBe(0);
        status.RemainingSteps.ShouldBe(0);
    }

    [Fact]
    public void Ilk_bozuk_commit_yakalaniyor()
    {
        BisectStatus status = BisectOutputParser.Parse(
            "3c34e8d7a1844835d88cc9f4415af2f8e088a7f0 is the first 'bad' commit\ncommit 3c34e8d");

        status.IsFinished.ShouldBeTrue();
        status.FirstBadCommit.ShouldBe("3c34e8d7a1844835d88cc9f4415af2f8e088a7f0");
    }

    [Fact]
    public void Bilinmeyen_metin_sessizce_geciliyor()
    {
        // git's wording changes between versions; an unknown line must not fail the operation.
        BisectStatus status = BisectOutputParser.Parse("some future wording nobody expected");

        status.IsFinished.ShouldBeFalse();
        status.RemainingRevisions.ShouldBeNull();
        status.Output.ShouldContain("future wording");
    }

    [Fact]
    public void Bos_cikti_cokmuyor()
    {
        BisectOutputParser.Parse(null).IsFinished.ShouldBeFalse();
        BisectOutputParser.Parse(string.Empty).RemainingSteps.ShouldBeNull();
    }
}
