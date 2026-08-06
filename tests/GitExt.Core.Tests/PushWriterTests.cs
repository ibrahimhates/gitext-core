using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T08 — push.
/// </summary>
/// <remarks>
/// Ölçümün dört sessiz noktası test ediliyor: <c>--porcelain</c> stdout'una karışan
/// insan-okunur satırlar, boşluk olan bayrak alanı, çıkış kodu 1 iken <b>gerçekten gitmiş</b>
/// ref'ler ve çıplak <c>--force-with-lease</c>'in bir fetch'ten sonra korumayı bırakması.
/// </remarks>
public class PushWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Local,
        TestRepository Upstream,
        TestRepository Other,
        PushWriter Writer,
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

        /// <summary>Uzaktaki bir ref'in ucu; ref yoksa <see langword="null"/>.</summary>
        public string? RemoteTip(string branch)
        {
            (int exitCode, _) = Upstream.TryGit("rev-parse", "--verify", "-q", $"refs/heads/{branch}");

            return exitCode == 0
                ? Upstream.Git("rev-parse", $"refs/heads/{branch}").Trim()
                : null;
        }

        public string Head => Local.Git("rev-parse", "HEAD").Trim();

        /// <summary>Yerelde yeni bir commit üretir.</summary>
        public void Commit(string name)
        {
            Local.WriteFile($"{name}.txt", $"{name}\n");
            Local.Git("add", "-A");
            Local.Git("commit", "-m", name);
        }

        /// <summary>Başka biri uzak depoyu ilerletir (yarış senaryosu).</summary>
        public void OtherPushes(string name, string branch = "main")
        {
            Other.WriteFile($"{name}.txt", $"{name}\n");
            Other.Git("add", "-A");
            Other.Git("commit", "-m", name);
            Other.Git("push", "-q", "origin", $"HEAD:{branch}");
        }
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository upstream = TestRepository.CreateBare();

        TestRepository local = TestRepository.CreateWithSingleCommit();
        local.Git("branch", "-M", "main");
        local.Git("remote", "add", "origin", upstream.Path);
        local.Git("push", "-q", "-u", "origin", "main");

        TestRepository other = TestRepository.CreateEmpty();
        other.Git("remote", "add", "origin", upstream.Path);
        other.Git("fetch", "-q", "origin");
        other.Git("checkout", "-q", "-B", "main", "origin/main");

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(
            local,
            upstream,
            other,
            new PushWriter(new GitWriter(runner, queue), runner),
            queue);
    }

    private static PushOptions Simple(string branch = "main", bool upstream = false) => new()
    {
        Remote = "origin",
        Refs = [new PushSpec(branch, branch)],
        SetUpstream = upstream,
    };

    // ---------------------------------------------------------------- temel

    [Fact]
    public async Task Ileri_sarma_BOSLUK_bayragiyla_dogru_siniflandiriliyor()
    {
        // 🔴 Porcelain'de normal ileri sarmanın bayrağı tek bir BOŞLUK. Satır Trim()'lenirse
        // alanlar kayar ve her başarılı push yanlış okunurdu.
        using Harness harness = await CreateAsync();

        harness.Commit("ikinci");

        PushResult result = await harness.Writer.PushAsync(harness.Path, Simple(), Ct);

        PushRefResult row = result.Refs.Single();
        row.Flag.ShouldBe(' ');
        row.Status.ShouldBe(PushRefStatus.FastForward);
        row.Destination.ShouldBe("refs/heads/main");
        row.ShortDestination.ShouldBe("main");
        row.Changed.ShouldBeTrue();
        result.Rejected.ShouldBeEmpty();

        harness.RemoteTip("main").ShouldBe(harness.Head);
    }

    [Fact]
    public async Task Yeni_dal_OLUSTURULDU_olarak_bildiriliyor()
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("checkout", "-q", "-b", "ozellik");
        harness.Commit("ozellik-commit");

        PushResult result = await harness.Writer.PushAsync(harness.Path, Simple("ozellik"), Ct);

        PushRefResult row = result.Refs.Single();
        row.Status.ShouldBe(PushRefStatus.Created);
        row.Summary.ShouldBe("[new branch]");
        harness.RemoteTip("ozellik").ShouldNotBeNull();
    }

    [Fact]
    public async Task Zaten_guncelken_DEGISMEDI_olarak_bildiriliyor()
    {
        using Harness harness = await CreateAsync();

        PushResult result = await harness.Writer.PushAsync(harness.Path, Simple(), Ct);

        result.Refs.Single().Status.ShouldBe(PushRefStatus.UpToDate);
        result.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task Upstream_kuruluyor_ve_INSAN_satiri_ayristiriciyi_bozmuyor()
    {
        // 🔴 `-u` ile git porcelain stdout'una `branch 'x' set up to track …` diye
        // insan-okunur bir satır KARIŞTIRIYOR (ölçüldü). Sekme sayısına bakmayan bir
        // ayrıştırıcı burada saçmalardı.
        using Harness harness = await CreateAsync();

        harness.Local.Git("checkout", "-q", "-b", "yeni");
        harness.Commit("yeni-commit");

        PushResult result = await harness.Writer.PushAsync(
            harness.Path, Simple("yeni", upstream: true), Ct);

        result.Refs.Count.ShouldBe(1);
        result.Refs[0].Status.ShouldBe(PushRefStatus.Created);

        harness.Local.Git("config", "--get", "branch.yeni.remote").Trim().ShouldBe("origin");
        harness.Local.Git("config", "--get", "branch.yeni.merge").Trim().ShouldBe("refs/heads/yeni");
    }

    [Fact]
    public async Task Deneme_uzak_depoyu_DEGISTIRMIYOR()
    {
        using Harness harness = await CreateAsync();

        string before = harness.RemoteTip("main")!;
        harness.Commit("ikinci");

        PushResult result = await harness.Writer.PushAsync(
            harness.Path, Simple() with { DryRun = true }, Ct);

        result.DryRun.ShouldBeTrue();
        result.Refs.Single().Changed.ShouldBeTrue("git ne OLACAĞINI söylüyor");
        harness.RemoteTip("main").ShouldBe(before, "ama uzak depo değişmemeli");
    }

    // ------------------------------------------------------------- reddetme

    [Fact]
    public async Task Geride_kalmis_dal_REDDEDILIYOR_ve_istisna_FIRLATILMIYOR()
    {
        using Harness harness = await CreateAsync();

        harness.OtherPushes("baskasinin");
        harness.Commit("benimki");

        PushResult result = await harness.Writer.PushAsync(harness.Path, Simple(), Ct);

        PushRefResult row = result.Refs.Single();
        row.Status.ShouldBe(PushRefStatus.Rejected);
        row.Rejection.ShouldBe(PushRejectionKind.Behind);
        result.IsPartial.ShouldBeFalse();
    }

    [Fact]
    public async Task KISMI_basari_GIDEN_dali_gizlemiyor()
    {
        // 🔴 ÖLÇÜLDÜ: çıkış kodu 1 ama bir dal GERÇEKTEN gitti. İstisnayı olduğu gibi
        // bırakmak kullanıcıya "hiçbir şey gitmedi" dedirtir, o da tekrar denerdi.
        using Harness harness = await CreateAsync();

        harness.Local.Git("branch", "ikinci-dal");
        harness.Local.Git("push", "-q", "origin", "ikinci-dal");

        // `ikinci-dal` uzakta ilerletiliyor -> yereldeki geride kalıyor.
        harness.Other.Git("push", "-q", "origin", "HEAD:ikinci-dal");
        harness.OtherPushes("uzakta", "ikinci-dal");

        harness.Commit("benimki");

        PushResult result = await harness.Writer.PushAsync(
            harness.Path,
            new PushOptions
            {
                Remote = "origin",
                Refs = [new PushSpec("main", "main"), new PushSpec("ikinci-dal", "ikinci-dal")],
            },
            Ct);

        result.IsPartial.ShouldBeTrue();
        result.Applied.Single().ShortDestination.ShouldBe("main");
        result.Rejected.Single().ShortDestination.ShouldBe("ikinci-dal");
        harness.RemoteTip("main").ShouldBe(harness.Head, "main gerçekten gitmiş olmalı");
    }

    [Fact]
    public async Task Uzak_kanca_reddederse_SEBEBI_tasiniyor()
    {
        using Harness harness = await CreateAsync();

        harness.Upstream.InstallHook("pre-receive", "echo 'korumali dal' >&2\nexit 1\n");
        harness.Commit("ikinci");

        PushResult result = await harness.Writer.PushAsync(harness.Path, Simple(), Ct);

        PushRefResult row = result.Refs.Single();
        row.Status.ShouldBe(PushRefStatus.Rejected);
        row.Rejection.ShouldBe(PushRejectionKind.RemoteRejected);

        // Porcelain yalnızca "(pre-receive hook declined)" diyor; NEDEN olduğu sadece
        // uzak tarafın `remote:` satırında.
        result.RemoteMessages.ShouldContain("korumali dal");
    }

    [Fact]
    public async Task Ulasilamayan_remote_ISTISNA_olarak_kaliyor()
    {
        // Porcelain hiç satır yazmadıysa (çıkış kodu 128) hata gerçekten ölümcül.
        using Harness harness = await CreateAsync();

        harness.Local.Git("remote", "add", "yok", "/olmayan/yol/x.git");

        GitException error = await Should.ThrowAsync<GitException>(
            () => harness.Writer.PushAsync(
                harness.Path,
                new PushOptions { Remote = "yok", Refs = [new PushSpec("main", "main")] },
                Ct));

        error.ExitCode.ShouldBe(128);
    }

    // ------------------------------------------------- force-with-lease

    [Fact]
    public async Task KARSI_KANIT_ciplak_force_with_lease_bir_FETCH_sonrasi_KORUMUYOR()
    {
        // 🔴 Bu test bizim kodumuzu değil git'in davranışını sabitliyor: kirayı git'in
        // örtük hâline bırakırsak, araya giren HERHANGİ bir fetch (bizim otomatik
        // tazelememiz dahil) korumayı iptal eder ve başkasının commit'i sessizce silinir.
        // Bu yüzden PushWriter çıpasız ref'e `--force-with-lease` yazmıyor.
        using Harness harness = await CreateAsync();

        harness.OtherPushes("baskasinin-emegi");
        string victim = harness.Upstream.Git("rev-parse", "refs/heads/main").Trim();

        harness.Commit("benimki");
        harness.Local.Git("fetch", "-q", "origin");

        (int exitCode, _) = harness.Local.TryGit("push", "--force-with-lease", "origin", "main");

        exitCode.ShouldBe(0, "git bunu KABUL ediyor — korumanın çöktüğü nokta");
        harness.RemoteTip("main").ShouldNotBe(victim, "ve başkasının commit'i gitti");
    }

    [Fact]
    public async Task Acik_cipa_BAYATSA_reddediyor_fetch_yapilmis_olsa_bile()
    {
        using Harness harness = await CreateAsync();

        // Kullanıcının ekranı açtığı andaki uç — kira çıpası.
        PushPlan plan = await harness.Writer.PlanAsync(harness.Path, "origin", "main", Ct);
        string anchor = plan.RemoteTipObjectId!;

        harness.OtherPushes("baskasinin-emegi");
        string victim = harness.Upstream.Git("rev-parse", "refs/heads/main").Trim();

        harness.Commit("benimki");
        harness.Local.Git("fetch", "-q", "origin");

        PushResult result = await harness.Writer.PushAsync(
            harness.Path,
            new PushOptions
            {
                Remote = "origin",
                Refs = [new PushSpec("main", "main") { ExpectedRemoteObjectId = anchor }],
                ForceWithLease = true,
            },
            Ct);

        result.Refs.Single().Rejection.ShouldBe(PushRejectionKind.StaleLease);
        harness.RemoteTip("main").ShouldBe(victim, "başkasının commit'i duruyor");
    }

    [Fact]
    public async Task Acik_cipa_TAZEYSE_zorluyor()
    {
        using Harness harness = await CreateAsync();

        // Kullanıcı gerçekten geçmişi yeniden yazıyor: aynı uç üstünde amend.
        harness.Local.Git("commit", "-q", "--amend", "-m", "yeniden yazildi");

        PushPlan plan = await harness.Writer.PlanAsync(harness.Path, "origin", "main", Ct);

        PushResult result = await harness.Writer.PushAsync(
            harness.Path,
            new PushOptions
            {
                Remote = "origin",
                Refs =
                [
                    new PushSpec("main", "main") { ExpectedRemoteObjectId = plan.RemoteTipObjectId },
                ],
                ForceWithLease = true,
            },
            Ct);

        result.Refs.Single().Status.ShouldBe(PushRefStatus.Forced);
        harness.RemoteTip("main").ShouldBe(harness.Head);
    }

    [Fact]
    public void Cipasiz_ref_e_lease_bayragi_YAZILMIYOR()
    {
        // Çıpa yoksa git'in örtük (ve ölçümde çöken) kirasına düşmemek için bayrak hiç
        // eklenmiyor — push zorlamasız denenir ve reddedilir.
        string command = PushWriter.Describe(new PushOptions
        {
            Remote = "origin",
            Refs = [new PushSpec("main", "main")],
            ForceWithLease = true,
        });

        command.ShouldNotContain("--force-with-lease");
    }

    // ------------------------------------------------------------- silme

    [Fact]
    public async Task Uzak_dal_siliniyor()
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("branch", "gidecek");
        harness.Local.Git("push", "-q", "origin", "gidecek");
        harness.RemoteTip("gidecek").ShouldNotBeNull();

        PushResult result = await harness.Writer.PushAsync(
            harness.Path,
            new PushOptions
            {
                Remote = "origin",
                Refs = [new PushSpec(string.Empty, "gidecek", Delete: true)],
            },
            Ct);

        PushRefResult row = result.Refs.Single();
        row.Status.ShouldBe(PushRefStatus.Deleted);
        row.ShortDestination.ShouldBe("gidecek");
        harness.RemoteTip("gidecek").ShouldBeNull();
    }

    [Fact]
    public async Task Olmayan_dali_silmek_ISTISNA_uretiyor()
    {
        // 🔴 ÖLÇÜLDÜ: bu durumda porcelain stdout'a HİÇBİR ŞEY yazmıyor; "satır yok"
        // sessizce "sorun yok" diye okunsaydı kullanıcı silindiğini sanırdı.
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<GitException>(
            () => harness.Writer.PushAsync(
                harness.Path,
                new PushOptions
                {
                    Remote = "origin",
                    Refs = [new PushSpec(string.Empty, "hic-olmadi", Delete: true)],
                },
                Ct));
    }

    // ------------------------------------------------------------ etiketler

    [Fact]
    public async Task Tum_etiketler_gonderiliyor()
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("tag", "v1");
        harness.Local.Git("tag", "-a", "v2", "-m", "iki");

        PushResult result = await harness.Writer.PushAsync(
            harness.Path,
            new PushOptions { Remote = "origin", Tags = PushTagMode.All },
            Ct);

        result.Refs.Where(row => row.IsTag).Select(row => row.ShortDestination)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["v1", "v2"]);
    }

    [Fact]
    public async Task Follow_tags_HAFIF_etiketi_atliyor()
    {
        // ⚠️ ÖLÇÜLDÜ: `--follow-tags` yalnızca annotated etiketleri gönderiyor. Arayüz
        // bunu yazmazsa kullanıcı hafif etiketinin uzakta olduğunu sanır.
        using Harness harness = await CreateAsync();

        harness.Commit("ikinci");
        harness.Local.Git("tag", "hafif");
        harness.Local.Git("tag", "-a", "agir", "-m", "agir");

        PushResult result = await harness.Writer.PushAsync(
            harness.Path,
            Simple() with { Tags = PushTagMode.FollowAnnotated },
            Ct);

        IReadOnlyList<string> tags = [.. result.Refs.Where(row => row.IsTag).Select(row => row.ShortDestination)];

        tags.ShouldContain("agir");
        tags.ShouldNotContain("hafif");
    }

    // --------------------------------------------------------------- plan

    [Fact]
    public async Task Plan_uzak_ucu_upstream_i_ve_konumu_okuyor()
    {
        using Harness harness = await CreateAsync();

        harness.Commit("ikinci");

        PushPlan plan = await harness.Writer.PlanAsync(harness.Path, "origin", "main", Ct);

        plan.RemoteTipObjectId.ShouldNotBeNull();
        plan.RemoteBranchExists.ShouldBeTrue();
        plan.WouldCreateBranch.ShouldBeFalse();
        plan.HasUpstream.ShouldBeTrue();
        plan.Tracking.Ahead.ShouldBe(1);
        plan.Tracking.Behind.ShouldBe(0);
    }

    [Fact]
    public async Task Plan_uzakta_OLMAYAN_dal_icin_cipa_vermiyor()
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("checkout", "-q", "-b", "hic-gitmedi");

        PushPlan plan = await harness.Writer.PlanAsync(harness.Path, "origin", "hic-gitmedi", Ct);

        plan.RemoteTipObjectId.ShouldBeNull();
        plan.WouldCreateBranch.ShouldBeTrue();
        plan.HasUpstream.ShouldBeFalse();
    }

    [Fact]
    public async Task Plan_sembolik_origin_HEAD_i_uzak_dal_saymiyor()
    {
        // Dördüncü kez aynı tuzak (P03-T12, P06-T05, P06-T06): silme listesinde "HEAD"
        // diye bir dal görünseydi kullanıcı olmayan bir şeyi silmeye çalışırdı.
        using Harness harness = await CreateAsync();

        harness.Local.Git("remote", "set-head", "origin", "main");

        PushPlan plan = await harness.Writer.PlanAsync(harness.Path, "origin", "main", Ct);

        plan.RemoteBranches.ShouldBe(["main"]);
    }

    [Fact]
    public async Task Plan_etiketleri_listeliyor()
    {
        using Harness harness = await CreateAsync();

        harness.Local.Git("tag", "v1");

        PushPlan plan = await harness.Writer.PlanAsync(harness.Path, "origin", "main", Ct);

        plan.Tags.ShouldBe(["v1"]);
    }

    // ------------------------------------------------------------ ayrıştırıcı

    [Fact]
    public void Ayristirici_TO_DONE_ve_insan_satirlarini_atliyor()
    {
        // Ölçülen gerçek çıktı, `-u` ve `push.autoSetupRemote` satırlarıyla birlikte.
        const string output = """
            To ../remote.git
            *\trefs/heads/yeni:refs/heads/yeni\t[new branch]
            branch 'yeni' set up to track 'origin/yeni'.
            Would set upstream of 'yeni' to 'yeni' of 'origin'
            Done
            """;

        IReadOnlyList<PushRefResult> rows = PushPorcelainParser.Parse(output.Replace("\\t", "\t", StringComparison.Ordinal));

        rows.Count.ShouldBe(1);
        rows[0].Status.ShouldBe(PushRefStatus.Created);
        rows[0].Destination.ShouldBe("refs/heads/yeni");
    }

    [Fact]
    public void Ayristirici_sebebi_ozetten_ayiriyor()
    {
        const string output = "!\\trefs/heads/main:refs/heads/main\\t[rejected] (stale info)";

        PushRefResult row = PushPorcelainParser
            .Parse(output.Replace("\\t", "\t", StringComparison.Ordinal))
            .Single();

        row.Summary.ShouldBe("[rejected]");
        row.Reason.ShouldBe("stale info");
        row.Rejection.ShouldBe(PushRejectionKind.StaleLease);
    }

    [Fact]
    public void Komut_onizlemesi_secimleri_yansitiyor()
    {
        string command = PushWriter.Describe(new PushOptions
        {
            Remote = "origin",
            Refs = [new PushSpec("main", "main") { ExpectedRemoteObjectId = "abc123" }],
            SetUpstream = true,
            ForceWithLease = true,
            Tags = PushTagMode.FollowAnnotated,
        });

        command.ShouldBe(
            "git push --porcelain --set-upstream --force-with-lease=main:abc123 "
            + "--follow-tags -- origin main:main");
    }
}
