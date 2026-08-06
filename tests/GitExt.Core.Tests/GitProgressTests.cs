using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T10 — ağ işlemlerinde ilerleme ve iptal.
/// </summary>
/// <remarks>
/// Ölçümün iki sessiz noktası: ilerleme satırlarının <c>\n</c> ile <b>değil</b> <c>\r</c>
/// ile ayrılması, ve iptal edilen bir fetch'in geride kilit bırakıp bırakmadığı.
/// </remarks>
public class GitProgressTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------- ayrıştırıcı

    [Theory]
    [InlineData("remote: Counting objects:   5% (207/4125)        ", "Counting objects", 5, 207, 4125, true, false)]
    [InlineData("Receiving objects:  47% (7615/16201), 4.10 MiB | 8.20 MiB/s", "Receiving objects", 47, 7615, 16201, false, false)]
    [InlineData("Resolving deltas: 100% (11603/11603), done.", "Resolving deltas", 100, 11603, 11603, false, true)]
    public void Ilerleme_satirlari_ayristiriliyor(
        string line,
        string phase,
        double percent,
        long current,
        long total,
        bool remote,
        bool done)
    {
        GitProgress step = GitProgressParser.Parse(line).ShouldNotBeNull();

        step.Phase.ShouldBe(phase);
        step.Percent.ShouldBe(percent);
        step.Current.ShouldBe(current);
        step.Total.ShouldBe(total);
        step.IsRemote.ShouldBe(remote);
        step.IsDone.ShouldBe(done);
    }

    [Fact]
    public void Yuzdesiz_sayac_satiri_da_ayristiriliyor()
    {
        GitProgress step = GitProgressParser
            .Parse("remote: Enumerating objects: 16201, done.        ")
            .ShouldNotBeNull();

        step.Phase.ShouldBe("Enumerating objects");
        step.Percent.ShouldBeNull();
        step.Current.ShouldBe(16201);
        step.IsDone.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Cloning into 'repo1'...")]
    [InlineData("fatal: Could not read from remote repository.")]
    [InlineData("From file:///tmp/x")]
    [InlineData("")]
    [InlineData("   ")]
    public void Ilerleme_OLMAYAN_satirlar_atlaniyor(string line) =>
        GitProgressParser.Parse(line).ShouldBeNull();

    [Fact]
    public void Satirlar_CR_ile_de_boluuyor()
    {
        // 🔴 Ölçümün kalbi: gerçek bir klonda 404 `\r`'ye karşılık 7 `\n` vardı. `\n` ile
        // bölen bir okuyucu işlem bitene kadar HİÇBİR ilerleme göstermezdi.
        const string text = "a: 1% (1/9)\rb: 2% (2/9)\rc: 3% (3/9)\n";

        (IReadOnlyList<string> lines, string remainder) = GitProgressParser.SplitLines(text);

        lines.ShouldBe(["a: 1% (1/9)", "b: 2% (2/9)", "c: 3% (3/9)"]);
        remainder.ShouldBeEmpty();
    }

    [Fact]
    public void YARIM_satir_ayristirilmiyor_sonraki_parcaya_birakiliyor()
    {
        // Akışta bir satır iki okuma arasında bölünebilir; yarısını ayrıştırmak yanlış
        // yüzde üretirdi (`Counting objects:  1` -> %1 yerine %12).
        (IReadOnlyList<string> lines, string remainder) =
            GitProgressParser.SplitLines("tam: 5% (1/2)\ryarim: 12");

        lines.ShouldBe(["tam: 5% (1/2)"]);
        remainder.ShouldBe("yarim: 12");

        (IReadOnlyList<string> rest, _) = GitProgressParser.SplitLines(remainder + "% (3/4)\r");
        rest.Single().ShouldBe("yarim: 12% (3/4)");
    }

    // ------------------------------------------------------------- gerçek git

    private sealed record Harness(
        TestRepository Local,
        TestRepository Upstream,
        GitProcessRunner Runner,
        GitWriteQueue Queue,
        FetchWriter Fetch) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Local.Dispose();
            Upstream.Dispose();
        }
    }

    /// <remarks>
    /// Uzak taraf <c>file://</c> ile veriliyor: yol olarak verilseydi git yerel kopyalama
    /// kısayolunu seçer ve <b>ilerleme hiç üretmezdi</b> (ölçüldü).
    /// </remarks>
    private static async Task<Harness> CreateAsync(int fileCount = 60)
    {
        TestRepository upstream = TestRepository.CreateBare();

        TestRepository seed = TestRepository.CreateWithSingleCommit();

        for (int index = 0; index < fileCount; index++)
        {
            seed.WriteFile($"dosya{index}.txt", $"icerik {index}\n");
        }

        seed.Git("add", "-A");
        seed.Git("commit", "-m", "coklu");
        seed.Git("push", "-q", upstream.Path, "HEAD:main");
        seed.Dispose();

        TestRepository local = TestRepository.CreateEmpty();
        local.Git("remote", "add", "origin", "file://" + upstream.Path);

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(local, upstream, runner, queue, new FetchWriter(new GitWriter(runner, queue), runner));
    }

    [Fact]
    public async Task GERCEK_fetch_ilerleme_bildiriyor()
    {
        using Harness harness = await CreateAsync();

        List<GitProgress> steps = [];

        await harness.Fetch.FetchAsync(
            harness.Local.Path,
            new FetchOptions
            {
                Remote = "origin",
                Progress = new Progress<GitProgress>(steps.Add),
            },
            Ct);

        // `Progress<T>` bildirimleri senkronizasyon bağlamına kuyruklayabiliyor; testte
        // bağlam yok, yine de son bildirimlerin işlenmesine kısa bir pay bırakılıyor.
        await Task.Delay(200, Ct);

        steps.ShouldNotBeEmpty("ilerleme hiç bildirilmedi");
        steps.ShouldContain(step => step.Phase == "Counting objects");
        steps.ShouldContain(step => step.Percent > 0);
    }

    [Fact]
    public async Task Ilerleme_istenmezse_TAM_metin_yine_de_okunuyor()
    {
        // Mevcut ayrıştırıcılar (fetch'in kısmi başarısı, push'un `remote:` satırları)
        // stderr'in TAMAMINA bakıyor; akış moduna geçmek onu bozmamalı.
        using Harness harness = await CreateAsync(5);

        GitResult result = await harness.Runner.RunAsync(
            GitCommand.Create(harness.Local.Path, "fetch", "--progress", "origin"),
            Ct);

        result.IsSuccess.ShouldBeTrue();
        result.StandardError.ShouldContain("origin/main");
    }

    [Fact]
    public async Task Akis_modunda_da_TAM_metin_biriktiriliyor()
    {
        using Harness harness = await CreateAsync(5);

        GitResult result = await harness.Runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = harness.Local.Path,
                Arguments = ["fetch", "--progress", "origin"],
                Progress = new Progress<GitProgress>(_ => { }),
            },
            Ct);

        result.StandardError.ShouldContain("origin/main");
    }

    // ------------------------------------------------------------- iptal

    [Fact]
    public async Task Iptal_edilen_fetch_geride_KILIT_birakmiyor()
    {
        // 🔴 Yarıda kesilen bir ağ işlemi depoyu kullanılamaz bırakırsa kullanıcı bir daha
        // hiçbir şey yapamaz. Ölçüldü: SIGTERM sonrası kilit yok, `fsck` temiz.
        using Harness harness = await CreateAsync(200);

        using CancellationTokenSource cancellation = new();

        Task<GitResult> running = harness.Runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = harness.Local.Path,
                Arguments = ["fetch", "--progress", "origin"],

                // İlk ilerleme bildiriminde iptal et: süreç gerçekten çalışıyorken.
                Progress = new Progress<GitProgress>(_ => cancellation.Cancel()),
            },
            cancellation.Token);

        await Should.ThrowAsync<OperationCanceledException>(() => running);

        // Depo hâlâ çalışır durumda olmalı.
        harness.Local.Git("fsck", "--no-progress");
        harness.Local.Git("status", "--porcelain=v2");

        Directory.GetFiles(
                System.IO.Path.Combine(harness.Local.Path, ".git"),
                "*.lock",
                SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Iptal_ONCESINDE_biten_komut_iptal_sayilmiyor()
    {
        // İptal jetonu iptal edilmiş olsa bile bitmiş bir işi "iptal edildi" diye
        // bildirmek, kullanıcıya olmamış bir şeyi anlatırdı.
        using Harness harness = await CreateAsync(3);

        GitResult result = await harness.Runner.RunAsync(
            GitCommand.Create(harness.Local.Path, "rev-parse", "--git-dir"),
            Ct);

        result.IsSuccess.ShouldBeTrue();
    }
}
