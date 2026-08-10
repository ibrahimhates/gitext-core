using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T06 — fetch.
/// </summary>
/// <remarks>
/// Ölçümün üç sessiz noktası test ediliyor: çıktının tamamının <c>stderr</c>'de olması
/// (bu yüzden ne değiştiği <b>ref farkıyla</b> hesaplanıyor), <c>--all</c>'un kısmi
/// başarısı ve budamanın geri alınamazlığı.
/// </remarks>
public class FetchWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Local,
        TestRepository Upstream,
        TestRepository Seed,
        FetchWriter Writer,
        GitWriteQueue Queue) : IDisposable
    {
        public string Path => Local.Path;

        public void Dispose()
        {
            Queue.Dispose();
            Local.Dispose();
            Seed.Dispose();
            Upstream.Dispose();
        }

        /// <summary>Uzak depoya yeni bir commit iter.</summary>
        public void PushCommit(string name, string branch = "main")
        {
            Seed.WriteFile($"{name}.txt", $"{name}\n");
            Seed.Git("add", "-A");
            Seed.Git("commit", "-m", name);
            Seed.Git("push", "-q", "up", $"HEAD:{branch}");
        }

        public IReadOnlyList<string> RemoteRefs =>
        [
            .. Local.Git("for-each-ref", "--format=%(refname)", "refs/remotes")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()),
        ];
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository upstream = TestRepository.CreateBare();

        TestRepository seed = TestRepository.CreateWithSingleCommit();
        seed.Git("remote", "add", "up", upstream.Path);
        seed.Git("push", "-q", "up", "HEAD:main");

        TestRepository clone = TestRepository.CreateEmpty();
        clone.Git("remote", "add", "origin", upstream.Path);
        clone.Git("fetch", "-q", "origin");

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(
            clone,
            upstream,
            seed,
            new FetchWriter(new GitWriter(runner, queue), runner),
            queue);
    }

    [Fact]
    public async Task Degisiklik_yokken_bos_sonuc()
    {
        using Harness harness = await CreateAsync();

        FetchResult result = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin" }, Ct);

        result.Changes.ShouldBeEmpty();
        result.Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Yeni_commit_GUNCELLENEN_ref_olarak_bildiriliyor()
    {
        // 🔴 Bu bilginin git'in ÇIKTISINDAN okunmadığına dikkat: fetch her şeyi stderr'e
        // yazıyor ve makine-okunur `--porcelain` yalnızca git 2.41+'da var (minimum 2.30).
        using Harness harness = await CreateAsync();

        harness.PushCommit("ikinci");

        FetchResult result = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin" }, Ct);

        RefChange change = result.Changes.Single();
        change.RefName.ShouldBe("refs/remotes/origin/main");
        change.ShortName.ShouldBe("origin/main");
        change.Kind.ShouldBe(RefChangeKind.Updated);
        change.OldId.ShouldNotBeNull();
        change.NewId.ShouldNotBe(change.OldId);
    }

    [Fact]
    public async Task Sembolik_origin_HEAD_ikinci_kez_bildirilMIYOR()
    {
        // 🔴 `refs/remotes/origin/HEAD` sembolik ve `origin/main`'i izliyor; `%(objectname)`
        // onu çözdüğü için main her güncellendiğinde İKİ değişiklik görünüyordu.
        using Harness harness = await CreateAsync();

        harness.Local.Git("remote", "set-head", "origin", "main");
        harness.Local.Git("for-each-ref", "--format=%(refname)", "refs/remotes")
            .ShouldContain("refs/remotes/origin/HEAD");

        harness.PushCommit("ikinci");

        FetchResult result = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin" }, Ct);

        result.Changes.Select(change => change.RefName).ShouldBe(["refs/remotes/origin/main"]);
    }

    [Fact]
    public async Task Yeni_dal_OLUSTURULAN_ref()
    {
        using Harness harness = await CreateAsync();

        harness.PushCommit("dal-commit", branch: "ozellik");

        FetchResult result = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin" }, Ct);

        result.Changes.ShouldContain(change =>
            change.RefName == "refs/remotes/origin/ozellik" && change.Kind == RefChangeKind.Created);
    }

    [Fact]
    public async Task Dry_run_hicbir_sey_DEGISTIRMIYOR()
    {
        using Harness harness = await CreateAsync();

        harness.PushCommit("ikinci");

        FetchResult result = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin", DryRun = true }, Ct);

        result.DryRun.ShouldBeTrue();
        result.Changes.ShouldBeEmpty();

        // Karşı kanıt: gerçekten yazılmadı.
        harness.Local.Git("rev-parse", "origin/main").Trim()
            .ShouldNotBe(harness.Seed.Git("rev-parse", "HEAD").Trim());
    }

    [Fact]
    public async Task Prune_silinen_dali_kaldiriyor_ve_SILME_olarak_bildiriyor()
    {
        using Harness harness = await CreateAsync();

        harness.PushCommit("dal-commit", branch: "gecici");
        await harness.Writer.FetchAsync(harness.Path, new FetchOptions { Remote = "origin" }, Ct);
        harness.RemoteRefs.ShouldContain("refs/remotes/origin/gecici");

        harness.Seed.Git("push", "-q", "up", ":gecici");

        // Budamasız fetch dokunmuyor — ölçüldü, bu yüzden ayrı bir bayrak.
        FetchResult withoutPrune = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin" }, Ct);

        withoutPrune.Changes.ShouldBeEmpty();
        harness.RemoteRefs.ShouldContain("refs/remotes/origin/gecici");

        FetchResult pruned = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin", Prune = true }, Ct);

        pruned.Changes.ShouldContain(change =>
            change.RefName == "refs/remotes/origin/gecici" && change.Kind == RefChangeKind.Deleted);

        harness.RemoteRefs.ShouldNotContain("refs/remotes/origin/gecici");
    }

    [Fact]
    public async Task Budama_onizlemesi_ne_kaybedilecegini_ONCEDEN_soyluyor()
    {
        // 🔴 Budanan ref'in reflog'u da gidiyor; yalnızca orada duran commit budayan bir
        // `gc` sonrası KAYBOLUYOR (ölçüldü). Bu yüzden bilgi budamadan önce toplanıyor.
        using Harness harness = await CreateAsync();

        harness.PushCommit("dal-commit", branch: "gecici");
        await harness.Writer.FetchAsync(harness.Path, new FetchOptions { Remote = "origin" }, Ct);

        string doomedSha = harness.Local.Git("rev-parse", "origin/gecici").Trim();
        harness.Seed.Git("push", "-q", "up", ":gecici");

        PrunePreview preview = await harness.Writer.PreviewPruneAsync(harness.Path, "origin", Ct);

        RefChange doomed = preview.WouldDelete.Single();
        doomed.RefName.ShouldBe("refs/remotes/origin/gecici");
        doomed.OldId.ShouldBe(doomedSha);

        preview.RecoveryCommands.Single()
            .ShouldBe($"git update-ref refs/remotes/origin/gecici {doomedSha}");

        // Önizleme hiçbir şeyi değiştirmemiş olmalı.
        harness.RemoteRefs.ShouldContain("refs/remotes/origin/gecici");
    }

    [Fact]
    public async Task Budama_kurtarma_komutu_GERCEKTEN_calisiyor()
    {
        using Harness harness = await CreateAsync();

        harness.PushCommit("dal-commit", branch: "gecici");
        await harness.Writer.FetchAsync(harness.Path, new FetchOptions { Remote = "origin" }, Ct);
        harness.Seed.Git("push", "-q", "up", ":gecici");

        PrunePreview preview = await harness.Writer.PreviewPruneAsync(harness.Path, "origin", Ct);
        string[] command = preview.RecoveryCommands.Single().Split(' ');

        await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin", Prune = true }, Ct);

        harness.RemoteRefs.ShouldNotContain("refs/remotes/origin/gecici");

        harness.Local.Git([.. command.Skip(1)]);

        harness.RemoteRefs.ShouldContain("refs/remotes/origin/gecici");
    }

    [Fact]
    public async Task Onizleme_origin_HEAD_i_kayip_saymiyor()
    {
        // `origin/HEAD` sembolik ve uzakta `refs/heads/HEAD` yok; elenmeseydi her budama
        // önizlemesi yanlış bir kayıp uyarısı üretirdi.
        using Harness harness = await CreateAsync();

        harness.Local.Git("remote", "set-head", "origin", "main");
        harness.RemoteRefs.ShouldContain("refs/remotes/origin/HEAD");

        PrunePreview preview = await harness.Writer.PreviewPruneAsync(harness.Path, "origin", Ct);

        preview.WouldDelete.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tum_remoteler_bir_tanesi_BOZUKKEN_digerleri_yine_de_geliyor()
    {
        // 🔴 ÖLÇÜLDÜ: çıkış kodu 1 ama iyi remote fetch EDİLDİ. İstisnayı olduğu gibi
        // bırakmak, gelmiş değişiklikleri kullanıcıdan gizlerdi.
        using Harness harness = await CreateAsync();

        harness.PushCommit("ikinci");
        harness.Local.Git("remote", "add", "bozuk", "/tmp/gitext-yok-boyle-depo.git");

        FetchResult result = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions(), Ct);

        result.Changes.ShouldContain(change => change.RefName == "refs/remotes/origin/main");

        FetchFailure failure = result.Failures.Single();
        failure.Remote.ShouldBe("bozuk");
        failure.Message.ShouldNotBeNullOrWhiteSpace();

        result.FailedCompletely.ShouldBeFalse();
    }

    [Fact]
    public async Task Ulasilamayan_remote_RemoteUnreachable_olarak_siniflandiriliyor()
    {
        // 🔴 Bu tür olmadan kullanıcıya "Bu klasör bir Git deposu değil" denirdi — klasör
        // iyiyken. git iki satır birden yazıyor ve genel kalıp yanlış olanı yakalıyordu.
        using Harness harness = await CreateAsync();

        harness.Local.Git("remote", "add", "bozuk", "/tmp/gitext-yok-boyle-depo.git");

        GitException error = await Should.ThrowAsync<GitException>(() =>
            harness.Writer.FetchAsync(harness.Path, new FetchOptions { Remote = "bozuk" }, Ct));

        error.Kind.ShouldBe(GitFailureKind.RemoteUnreachable);
        error.Message.ShouldNotContain("klasör bir Git deposu değil");
    }

    [Fact]
    public async Task Etiketler_geliyor_ve_prune_tags_olmadan_SILINMIYOR()
    {
        using Harness harness = await CreateAsync();

        harness.Seed.Git("tag", "v1");
        harness.Seed.Git("push", "-q", "up", "v1");

        FetchResult withTag = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin" }, Ct);

        withTag.Changes.ShouldContain(change => change.IsTag && change.ShortName == "v1");

        harness.Seed.Git("push", "-q", "up", ":refs/tags/v1");

        // ÖLÇÜLDÜ: `--prune` tek başına etikete dokunmuyor.
        FetchResult pruneOnly = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin", Prune = true }, Ct);

        pruneOnly.Changes.ShouldBeEmpty();
        harness.Local.Git("tag").ShouldContain("v1");

        FetchResult pruneTags = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin", Prune = true, PruneTags = true }, Ct);

        pruneTags.Changes.ShouldContain(change =>
            change.IsTag && change.ShortName == "v1" && change.Kind == RefChangeKind.Deleted);

        harness.Local.Git("tag").Trim().ShouldBeEmpty();
    }

    [Fact]
    public async Task No_tags_ile_etiket_GELMIYOR()
    {
        using Harness harness = await CreateAsync();

        harness.Seed.Git("tag", "v1");
        harness.PushCommit("ikinci");
        harness.Seed.Git("push", "-q", "up", "v1");

        FetchResult result = await harness.Writer.FetchAsync(
            harness.Path, new FetchOptions { Remote = "origin", Tags = FetchTagMode.None }, Ct);

        result.Changes.ShouldNotContain(change => change.IsTag);
        harness.Local.Git("tag").Trim().ShouldBeEmpty();
    }

    // ================================================= P09-T13 paralel fetch

    /// <summary>
    /// Başarısızlık satırı sıralı ve paralel fetch'te farklı biçimde geliyor (P09-T13).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ.</b> Aynı bozuk remote, iki modda iki farklı satır üretiyor:
    /// <code>
    /// -j1  →  error: could not fetch bozuk
    /// -j0  →  could not fetch 'bozuk' (exit code: 128)
    /// </code>
    /// Paralel biçimde <c>error:</c> öneki yok ve ad tırnaklı. Yalnızca sıralı biçimi
    /// tanıyan ayrıştırıcı, paralel fetch'te başarısızlığı hiç görmez: git 1 ile çıkar,
    /// kısmi başarı yakalanmaz ve gerçekten gelmiş değişiklikler kullanıcıdan gizlenir.
    /// Paralelleştirme ilk denemede tam olarak böyle kırıldı.
    /// </remarks>
    [Theory]
    [InlineData("error: could not fetch bozuk", "bozuk")]
    [InlineData("could not fetch 'bozuk' (exit code: 128)", "bozuk")]
    [InlineData("could not fetch \"bozuk\" (exit code: 128)", "bozuk")]
    public void Basarisizlik_satiri_her_iki_bicimde_de_okunuyor(string line, string expected)
    {
        IReadOnlyList<FetchFailure> failures = FetchWriter.ParseFailures(
            "fatal: '/yok' does not appear to be a git repository\n" + line + "\n");

        failures.Single().Remote.ShouldBe(expected);
    }

    /// <remarks>
    /// Remote adı tırnaklardan ve <c>(exit code: N)</c> kuyruğundan arındırılmalı;
    /// arındırılmazsa kullanıcıya remote adı diye <c>'bozuk' (exit code: 128)</c>
    /// gösterilirdi.
    /// </remarks>
    [Fact]
    public void Paralel_bicimde_ad_tirnaklardan_ariniyor()
    {
        IReadOnlyList<FetchFailure> failures = FetchWriter.ParseFailures(
            "could not fetch 'origin' (exit code: 128)\n");

        failures.Single().Remote.ShouldBe("origin");
        failures.Single().Remote.ShouldNotContain("exit code");
    }

    /// <remarks>
    /// Birden çok remote başarısız olduğunda her biri ayrı kayıt olmalı; paralel modda
    /// satırlar araya karışabiliyor.
    /// </remarks>
    [Fact]
    public void Birden_cok_basarisiz_remote_ayri_ayri_okunuyor()
    {
        IReadOnlyList<FetchFailure> failures = FetchWriter.ParseFailures(
            "fatal: 'bir' does not appear to be a git repository\n"
            + "could not fetch 'bir' (exit code: 128)\n"
            + "fatal: 'iki' does not appear to be a git repository\n"
            + "could not fetch 'iki' (exit code: 128)\n");

        failures.Select(f => f.Remote).ShouldBe(["bir", "iki"]);
    }
}
