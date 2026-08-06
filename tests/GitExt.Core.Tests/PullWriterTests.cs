using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T07 — pull.
/// </summary>
/// <remarks>
/// Ölçümün üç sessiz noktası: ayarsız+iraksayan durumda git'in <b>fetch'i yapıp sonra
/// reddetmesi</b>, çakışmanın çıkış kodundan okunamaması ve <c>--autostash</c> geri
/// koymasının <b>çıkış kodu 0</b> ile çakışabilmesi.
/// </remarks>
public class PullWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Local,
        TestRepository Upstream,
        TestRepository Other,
        PullWriter Writer,
        GitWriteQueue Queue) : IDisposable
    {
        public string Path => Local.Path;

        public void Dispose()
        {
            Queue.Dispose();
            Local.Dispose();
            Other.Dispose();
            Upstream.Dispose();
        }

        /// <summary>"Başkası" uzağa bir commit iter.</summary>
        public void RemoteCommit(string content, string file = "f.txt")
        {
            Other.WriteFile(file, content);
            Other.Git("add", "-A");
            Other.Git("commit", "-m", $"uzak-{content}");
            Other.Git("push", "-q", "up", "HEAD:main");
        }

        /// <summary>Yerelde bir commit.</summary>
        public void LocalCommit(string content, string file = "yerel.txt")
        {
            Local.WriteFile(file, content);
            Local.Git("add", "-A");
            Local.Git("commit", "-m", $"yerel-{content}");
        }

        public int MergeCommitCount =>
            Local.Git("log", "--merges", "--oneline")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        public string Head => Local.Git("rev-parse", "HEAD").Trim();
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository upstream = TestRepository.CreateBare();

        TestRepository other = TestRepository.CreateEmpty();
        other.WriteFile("f.txt", "s1\n");
        other.Git("add", "-A");
        other.Git("commit", "-m", "ilk");
        other.Git("remote", "add", "up", upstream.Path);
        other.Git("push", "-q", "up", "HEAD:main");

        TestRepository local = TestRepository.CreateEmpty();
        local.Git("remote", "add", "origin", upstream.Path);
        local.Git("fetch", "-q", "origin");
        local.Git("checkout", "-q", "-b", "main", "--track", "origin/main");

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(
            local,
            upstream,
            other,
            new PullWriter(new GitWriter(runner, queue), runner, new GitConfigReader(runner)),
            queue);
    }

    // ---- Strateji çözümü ----

    [Fact]
    public async Task Ayar_yokken_varsayilan_BIRLESTIR_ve_kaynagi_yaziliyor()
    {
        using Harness harness = await CreateAsync();

        ResolvedPullStrategy strategy =
            await harness.Writer.ResolveStrategyAsync(harness.Path, cancellationToken: Ct);

        strategy.Strategy.ShouldBe(PullStrategy.Merge);
        strategy.Source.ShouldBe(PullStrategySource.ApplicationDefault);
    }

    [Fact]
    public async Task Dal_ayari_pull_rebase_i_EZIYOR()
    {
        // ÖLÇÜLDÜ: `pull.rebase=true` + `branch.main.rebase=false` → git MERGE yaptı.
        using Harness harness = await CreateAsync();

        harness.Local.Git("config", "pull.rebase", "true");
        harness.Local.Git("config", "branch.main.rebase", "false");

        ResolvedPullStrategy strategy =
            await harness.Writer.ResolveStrategyAsync(harness.Path, cancellationToken: Ct);

        strategy.Strategy.ShouldBe(PullStrategy.Merge);
        strategy.Source.ShouldBe(PullStrategySource.BranchSetting);
        strategy.ConfigValue.ShouldBe("false");
    }

    [Theory]
    [InlineData("true", PullStrategy.Rebase)]
    [InlineData("interactive", PullStrategy.Rebase)]
    [InlineData("merges", PullStrategy.Rebase)]
    [InlineData("false", PullStrategy.Merge)]
    public async Task pull_rebase_degerleri_dogru_cevriliyor(string value, PullStrategy expected)
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("config", "pull.rebase", value);

        ResolvedPullStrategy strategy =
            await harness.Writer.ResolveStrategyAsync(harness.Path, cancellationToken: Ct);

        strategy.Strategy.ShouldBe(expected);
        strategy.Source.ShouldBe(PullStrategySource.PullRebaseSetting);
    }

    [Fact]
    public async Task pull_ff_only_ayari_okunuyor()
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("config", "pull.ff", "only");

        ResolvedPullStrategy strategy =
            await harness.Writer.ResolveStrategyAsync(harness.Path, cancellationToken: Ct);

        strategy.Strategy.ShouldBe(PullStrategy.FastForwardOnly);
        strategy.Source.ShouldBe(PullStrategySource.PullFfSetting);
    }

    [Fact]
    public async Task Kullanicinin_secimi_ayarlari_eziyor()
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("config", "pull.rebase", "true");

        ResolvedPullStrategy strategy = await harness.Writer.ResolveStrategyAsync(
            harness.Path, PullStrategy.FastForwardOnly, Ct);

        strategy.Strategy.ShouldBe(PullStrategy.FastForwardOnly);
        strategy.Source.ShouldBe(PullStrategySource.UserChoice);
    }

    // ---- Pull davranışı ----

    [Fact]
    public async Task Guncelken_HEAD_ilerlemiyor()
    {
        using Harness harness = await CreateAsync();

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.AlreadyUpToDate.ShouldBeTrue();
        result.HasConflicts.ShouldBeFalse();
        result.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ileri_sarma_HEAD_i_tasiyor_ve_degisikligi_bildiriyor()
    {
        using Harness harness = await CreateAsync();

        harness.RemoteCommit("s2\n");

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.AlreadyUpToDate.ShouldBeFalse();
        result.HeadAfter.ShouldBe(harness.Head);
        result.Changes.ShouldContain(change => change.RefName == "refs/remotes/origin/main");
        harness.MergeCommitCount.ShouldBe(0);
    }

    [Fact]
    public async Task Ayarsiz_IRAKSAYAN_depoda_git_REDDETMIYOR_cunku_bayrak_aciktan_geciliyor()
    {
        // 🔴 Bu testin varlık sebebi: ayarsız + iraksayan durumda çıplak `git pull`
        // ÇALIŞMAYI REDDEDİYOR (rc=128, dokuz satır `hint:`) — üstelik reddetmeden ÖNCE
        // fetch aşamasını tamamlıyor, yani depo değişiyor ama kullanıcı "başarısız" görüyor.
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.Strategy.Strategy.ShouldBe(PullStrategy.Merge);
        result.HasConflicts.ShouldBeFalse();
        harness.MergeCommitCount.ShouldBe(1);
    }

    [Fact]
    public async Task KARSI_KANIT_ciplak_git_pull_ayarsiz_iraksayan_depoda_REDDEDIYOR()
    {
        // Bu test, `PullWriter`'ın neden her zaman açık bayrak geçtiğini kanıtlıyor.
        // ⚠️ Ayrı bir depoda: çıplak komut çalışsaydı depoyu değiştirir ve asıl testin
        // kurulumunu bozardı.
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");

        (int exitCode, string error) = harness.Local.TryGit("pull");

        exitCode.ShouldNotBe(0);
        error.ShouldContain("divergent branches");

        // Ve ölçümdeki asıl sinsi kısım: reddetmesine rağmen FETCH tamamlanmış oluyor.
        harness.Local.Git("rev-parse", "origin/main").Trim()
            .ShouldBe(harness.Other.Git("rev-parse", "HEAD").Trim());
    }

    [Fact]
    public async Task Rebase_stratejisi_merge_commit_URETMIYOR()
    {
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");

        PullResult result = await harness.Writer.PullAsync(
            harness.Path, new PullOptions { Strategy = PullStrategy.Rebase }, Ct);

        result.HasConflicts.ShouldBeFalse();
        harness.MergeCommitCount.ShouldBe(0);
        harness.Local.Git("log", "--oneline", "-2").ShouldContain("yerel-a");
    }

    [Fact]
    public async Task ff_only_iraksayan_depoda_HATA_veriyor_ve_HEAD_e_dokunmuyor()
    {
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");
        string before = harness.Head;

        await Should.ThrowAsync<GitException>(() => harness.Writer.PullAsync(
            harness.Path, new PullOptions { Strategy = PullStrategy.FastForwardOnly }, Ct));

        harness.Head.ShouldBe(before);
    }

    [Fact]
    public async Task Cakisma_ISTISNA_degil_SONUC_olarak_bildiriliyor()
    {
        // Çakışma bir hata değil: depo çakışma durumunda ve yapılacak iş belli. İstisna
        // olarak yükselseydi arayüz yalnızca kırmızı bir kutu gösterirdi.
        using Harness harness = await CreateAsync();

        harness.Local.WriteFile("f.txt", "YEREL\n");
        harness.Local.Git("commit", "-am", "yerel-cakisma");
        harness.RemoteCommit("UZAK\n");

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.HasConflicts.ShouldBeTrue();
        result.AutoStashConflict.ShouldBeFalse();
        harness.Local.Git("status", "--porcelain").ShouldContain("UU");

        harness.Local.Git("merge", "--abort");
    }

    [Fact]
    public async Task AUTOSTASH_geri_koyma_cakismasi_cikis_kodu_0_ILE_geliyor()
    {
        // 🔴 ÖLÇÜLDÜ: bu durumda `git pull` ÇIKIŞ KODU 0 veriyor, ama çalışma ağacında
        // `UU` dosya ve dosyanın İÇİNDE çakışma işaretleri var. Yalnızca çıkış koduna
        // bakan bir arayüz "pull başarılı" derdi — P06-T02'deki `switch --merge` tuzağının
        // aynısı.
        using Harness harness = await CreateAsync();

        harness.RemoteCommit("UZAK\n");
        harness.Local.WriteFile("f.txt", "KAYDEDILMEMIS-YEREL\n");

        PullResult result = await harness.Writer.PullAsync(
            harness.Path,
            new PullOptions { Strategy = PullStrategy.Rebase, AutoStash = true },
            Ct);

        result.HasConflicts.ShouldBeTrue();
        result.AutoStashConflict.ShouldBeTrue("pull başarılı, çakışan kullanıcının stash'i");

        // Kullanıcının çalışması kayıp değil: stash listede duruyor.
        harness.Local.Git("stash", "list").ShouldContain("autostash");

        harness.Local.Git("reset", "--hard", "-q");
        harness.Local.Git("stash", "drop");
    }

    [Fact]
    public async Task Autostash_cakismasizken_temiz_ve_dosya_yerinde()
    {
        using Harness harness = await CreateAsync();

        harness.RemoteCommit("s2\n");
        harness.Local.WriteFile("baska.txt", "kirli\n");

        PullResult result = await harness.Writer.PullAsync(
            harness.Path,
            new PullOptions { Strategy = PullStrategy.Rebase, AutoStash = true },
            Ct);

        result.HasConflicts.ShouldBeFalse();
        result.AutoStashConflict.ShouldBeFalse();
        harness.Local.Git("stash", "list").Trim().ShouldBeEmpty();
        File.Exists(Path.Combine(harness.Path, "baska.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Geri_alma_komutu_GERCEKTEN_calisiyor()
    {
        using Harness harness = await CreateAsync();

        harness.LocalCommit("a");
        harness.RemoteCommit("s2\n");
        string before = harness.Head;

        PullResult result = await harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct);

        result.HeadBefore.ShouldBe(before);
        result.RecoveryCommand.ShouldBe($"git reset --hard {before}");
        harness.MergeCommitCount.ShouldBe(1);

        harness.Local.Git("reset", "--hard", before, "-q");

        harness.Head.ShouldBe(before);
        harness.MergeCommitCount.ShouldBe(0);
        harness.Local.Git("log", "--oneline", "-1").ShouldContain("yerel-a");
    }

    [Fact]
    public async Task Kirli_agacta_pull_HEAD_e_dokunmuyor()
    {
        using Harness harness = await CreateAsync();

        harness.RemoteCommit("UZAK\n");
        harness.Local.WriteFile("f.txt", "kirli\n");
        string before = harness.Head;

        await Should.ThrowAsync<GitException>(() =>
            harness.Writer.PullAsync(harness.Path, new PullOptions(), Ct));

        harness.Head.ShouldBe(before);
    }
}
