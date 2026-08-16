using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T08 — file operations (discard, delete, <c>.gitignore</c>, <c>clean</c>).
/// </summary>
/// <remarks>
/// The phase's most dangerous task: every operation here can erase the user's <b>not-yet
/// committed</b> work. The tests' weight is on the <b>silent</b> behaviors found through
/// measurement — cases where git does nothing without giving an error.
/// </remarks>
public class WorkingTreeWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        WorkingTreeWriter Writer,
        StagingWriter Staging,
        DiffReader Diff,
        GitWriteQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        public string Path => Repository.Path;

        public string Read(string name) =>
            File.ReadAllText(System.IO.Path.Combine(Repository.Path, name));

        public bool Exists(string name) =>
            File.Exists(System.IO.Path.Combine(Repository.Path, name));

        public string Status(string name) =>
            Repository.Git("status", "--porcelain", "--", name).Trim();
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();
        GitWriter writer = new(runner, queue);

        repository.WriteFile("a.txt", "satir1\nsatir2\n");
        repository.WriteFile("b.txt", "b\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "init");

        return new Harness(
            repository,
            new WorkingTreeWriter(writer, runner),
            new StagingWriter(writer, runner),
            new DiffReader(runner),
            queue);
    }

    private static IReadOnlyList<RepositoryPath> Paths(params string[] values) =>
        [.. values.Select(value => RepositoryPath.Parse(value))];

    // ---- Geri alma (git restore) ----

    [Fact]
    public async Task Onaysiz_geri_alma_REDDEDILIR()
    {
        // Confirmation is mandatory as a parameter (the `GitLock.Remove` pattern from P05-T02):
        // leaving the rule in a comment wouldn't stop someone from calling it unconfirmed later.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "degisti\n");

        await Should.ThrowAsync<InvalidOperationException>(
            harness.Writer.DiscardChangesAsync(
                harness.Path, Paths("a.txt"), DiscardScope.UnstagedOnly, userConfirmed: false, Ct));

        harness.Read("a.txt").ShouldBe("degisti\n");
    }

    [Fact]
    public async Task Bos_yol_listesi_HICBIR_SEYI_geri_almaz()
    {
        // ⚠️ `git restore --` without a path would revert the ENTIRE repository (the same
        // protection as `git add -A --` in P05-T03).
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "degisti\n");
        harness.Repository.WriteFile("b.txt", "b degisti\n");

        await harness.Writer.DiscardChangesAsync(
            harness.Path, [], DiscardScope.All, userConfirmed: true, Ct);

        harness.Read("a.txt").ShouldBe("degisti\n");
        harness.Read("b.txt").ShouldBe("b degisti\n");
    }

    [Fact]
    public async Task Stage_lenmemis_degisiklik_atilir_STAGE_LENMIS_KORUNUR()
    {
        // MEASURED: plain `git restore` restores the working tree from the INDEX, not HEAD.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "stage edilmis\n");
        await harness.Staging.StageAsync(harness.Path, Paths("a.txt"), Ct);
        harness.Repository.WriteFile("a.txt", "stage edilmis + fazlasi\n");

        await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("a.txt"), DiscardScope.UnstagedOnly, userConfirmed: true, Ct);

        harness.Read("a.txt").ShouldBe("stage edilmis\n");
        harness.Repository.Git("show", ":a.txt").ShouldBe("stage edilmis\n");
    }

    [Fact]
    public async Task Tum_kapsamda_stage_lenmis_degisiklik_de_atilir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "stage edilmis\n");
        await harness.Staging.StageAsync(harness.Path, Paths("a.txt"), Ct);
        harness.Repository.WriteFile("a.txt", "stage edilmis + fazlasi\n");

        await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("a.txt"), DiscardScope.All, userConfirmed: true, Ct);

        harness.Read("a.txt").ShouldBe("satir1\nsatir2\n");
        harness.Status("a.txt").ShouldBeEmpty();
    }

    [Fact]
    public async Task Silinmis_dosya_geri_getirilir()
    {
        using Harness harness = await CreateAsync();
        File.Delete(Path.Combine(harness.Path, "b.txt"));

        await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("b.txt"), DiscardScope.UnstagedOnly, userConfirmed: true, Ct);

        harness.Read("b.txt").ShouldBe("b\n");
    }

    [Fact]
    public async Task Atilan_icerik_YEDEKTEN_geri_okunabilir()
    {
        // CLAUDE.md § 8: a way back for an operation that can't be undone. There's no reflog
        // here (the content was never committed), but it can be written to the object database.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "KAYBOLMAMASI GEREKEN\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("a.txt"), DiscardScope.UnstagedOnly, userConfirmed: true, Ct);

        harness.Read("a.txt").ShouldBe("satir1\nsatir2\n");

        DiscardBackup backup = backups.ShouldHaveSingleItem();
        backup.Path.Value.ShouldBe("a.txt");
        harness.Repository.Git("cat-file", "-p", backup.BlobId).ShouldBe("KAYBOLMAMASI GEREKEN\n");
    }

    [Fact]
    public async Task Diskte_olmayan_yol_yedeklenmeye_CALISILMAZ()
    {
        // `hash-object` fails on a nonexistent file; a deleted file has no content to back up.
        using Harness harness = await CreateAsync();
        File.Delete(Path.Combine(harness.Path, "b.txt"));

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DiscardChangesAsync(
            harness.Path, Paths("b.txt"), DiscardScope.UnstagedOnly, userConfirmed: true, Ct);

        backups.ShouldBeEmpty();
        harness.Read("b.txt").ShouldBe("b\n");
    }

    // ---- Deleting an untracked file ----

    [Fact]
    public async Task Takip_edilmeyen_dosya_silinir()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni.txt", "x\n");

        await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("yeni.txt"), userConfirmed: true, Ct);

        harness.Exists("yeni.txt").ShouldBeFalse();
    }

    [Fact]
    public async Task Silinen_takip_edilmeyen_dosyanin_icerigi_YEDEKLENIR()
    {
        // 🔴 The rationale for P05-T15, by measurement: a file deleted with `git clean` leaves
        // no trace at all in the object database — not even `git fsck --lost-found` finds it.
        // This was the repository's only truly unrecoverable operation. Yet untracked files
        // are typically NEW SOURCE FILES not yet committed: in this very repository, the
        // output of `git clean -dn` listed files that were being written at that moment.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni-kaynak.cs", "çok değerli emek\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("yeni-kaynak.cs"), userConfirmed: true, Ct);

        harness.Exists("yeni-kaynak.cs").ShouldBeFalse();

        backups.Count.ShouldBe(1);
        backups[0].Path.Value.ShouldBe("yeni-kaynak.cs");

        // The backup must actually be readable; returning an id but losing the content is useless.
        harness.Repository.Git("cat-file", "-p", backups[0].BlobId)
            .ShouldContain("çok değerli emek");
    }

    [Fact]
    public async Task Yedek_normal_gc_ile_KAYBOLMUYOR()
    {
        // ⚠️ MEASURED — T08's "not a guarantee" note was correct but too pessimistic: no ref
        // points to the backup, but `git gc` does NOT delete it (dangling objects are kept for
        // the default `gc.pruneExpire=2.weeks`). Only `gc --prune=now` deletes it. The recovery
        // path told to the user is therefore realistic.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("gidecek.txt", "kurtarılacak içerik\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("gidecek.txt"), userConfirmed: true, Ct);

        harness.Repository.Git("gc", "--quiet");

        harness.Repository.Git("cat-file", "-p", backups[0].BlobId)
            .ShouldContain("kurtarılacak içerik");
    }

    [Fact]
    public async Task Secili_satirlar_geri_alinir_INDEX_korunur()
    {
        // 🔴 MEASURED: `git apply --reverse` (WITHOUT --cached) applies the patch only to the
        // working tree; the file's staged version is left as is. If `--cached` were added, the
        // user saying "revert this line" would also change their index.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "a.txt");
        harness.Repository.Commit("temel");

        harness.Repository.WriteFile("a.txt", "BIR\niki\nUC\n");
        harness.Repository.Git("add", "a.txt");
        harness.Repository.WriteFile("a.txt", "BIR\niki\nUCUC\n");

        FileDiff diff = (await harness.Diff.ReadUnstagedAsync(harness.Path, cancellationToken: Ct))
            .Single();

        // Select the whole hunk: the file has a single change, and what's being measured is
        // whether the index is preserved.
        PatchSelection selection = PatchSelection.All(diff);

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DiscardPartialAsync(
            harness.Path, diff, selection, userConfirmed: true, cancellationToken: Ct);

        backups.Count.ShouldBe(1);

        // The working tree should return to its pre-patch state…
        harness.Read("a.txt").ShouldBe("BIR\niki\nUC\n");

        // …while the index should NOT be touched. ⚠️ Exact equality is required: checking
        // "does it contain UC" would also pass for "UCUC" (the lesson of P04-T09: where you
        // look must match what you're verifying).
        harness.Repository.Git("show", ":a.txt").ShouldBe("BIR\niki\nUC\n");
    }

    [Fact]
    public async Task Kismi_geri_alma_eol_crlf_altinda_SATIR_SONLARINI_bozmuyor()
    {
        // P05-T17 measurement: the patch is LF because it comes from `git diff`, while the file
        // in the working tree is CRLF. `git apply` (the worktree path) applies the same
        // filters itself, so the patch applies cleanly and CRLF is preserved. If it didn't, the
        // symptom wouldn't be silent — it would be `patch does not apply` — but a discard is an
        // operation that erases the user's work, so there's no room to leave this to an
        // untested assumption.
        using Harness harness = await CreateAsync();
        string path = System.IO.Path.Combine(harness.Path, "c.txt");

        harness.Repository.WriteFile(".gitattributes", "* text=auto eol=crlf\n");
        File.WriteAllBytes(path, "bir\r\niki\r\nuc\r\n"u8.ToArray());
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("temel");

        File.WriteAllBytes(path, "bir\r\nIKI\r\nuc\r\n"u8.ToArray());

        FileDiff diff = (await harness.Diff.ReadUnstagedAsync(harness.Path, cancellationToken: Ct))
            .Single();

        await harness.Writer.DiscardPartialAsync(
            harness.Path, diff, PatchSelection.All(diff), userConfirmed: true, cancellationToken: Ct);

        File.ReadAllBytes(path).ShouldBe("bir\r\niki\r\nuc\r\n"u8.ToArray());
    }

    [Fact]
    public async Task Kismi_geri_almada_onay_ZORUNLU()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("a.txt", "bir\n");
        harness.Repository.Git("add", "a.txt");
        harness.Repository.Commit("temel");
        harness.Repository.WriteFile("a.txt", "BIR\n");

        FileDiff diff = (await harness.Diff.ReadUnstagedAsync(harness.Path, cancellationToken: Ct))
            .Single();

        PatchSelection selection = PatchSelection.All(diff);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            harness.Writer.DiscardPartialAsync(
                harness.Path, diff, selection, userConfirmed: false, cancellationToken: Ct));
    }

    [Fact]
    public async Task Yedek_geri_yazilabiliyor()
    {
        // Taking a backup alone isn't the safety net: giving the user a blob id and expecting
        // them to type `git cat-file` is useless in a moment of panic.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("alt/dizin/yeni.cs", "geri gelmeli\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("alt/dizin/yeni.cs"), userConfirmed: true, Ct);

        harness.Exists("alt/dizin/yeni.cs").ShouldBeFalse();

        IReadOnlyList<DiscardBackup> restored =
            await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        restored.Count.ShouldBe(1);

        // The directory was also deleted (`clean -d`); writing back should recreate it.
        (await File.ReadAllTextAsync(
            Path.Combine(harness.Path, "alt/dizin/yeni.cs"), Ct))
            .ShouldBe("geri gelmeli\n");
    }

    [Fact]
    public async Task Yedek_IKILI_dosyada_da_BIREBIR()
    {
        using Harness harness = await CreateAsync();

        byte[] content = new byte[8192];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(Path.Combine(harness.Path, "resim.bin"), content, Ct);

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("resim.bin"), userConfirmed: true, Ct);

        await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        (await File.ReadAllBytesAsync(Path.Combine(harness.Path, "resim.bin"), Ct))
            .ShouldBe(content);
    }

    [Fact]
    public async Task Yedek_CRLF_donusumune_ugramaz()
    {
        // 🔴 MEASURED: without `--no-filters`, when `.gitattributes` has `text=auto`, git
        // converts CRLF to LF while writing the backup — the user's line endings would
        // silently change on restore. A backup that promises recovery but changes the content
        // is worse than taking no backup at all: the user thinks they recovered it.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitattributes", "* text=auto\n");
        harness.Repository.Git("add", ".gitattributes");
        harness.Repository.Commit("attributes");

        byte[] crlf = "birinci\r\nikinci\r\n"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(harness.Path, "crlf.txt"), crlf, Ct);

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("crlf.txt"), userConfirmed: true, Ct);

        await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        (await File.ReadAllBytesAsync(Path.Combine(harness.Path, "crlf.txt"), Ct))
            .ShouldBe(crlf);
    }

    [Fact]
    public async Task Yedek_CLEAN_FILTRESINDEN_etkilenmez()
    {
        // 🔴 MEASURED and the most dangerous: when a custom clean filter is present (how Git
        // LFS works), the backup written without `--no-filters` gets the FILTER'S OUTPUT
        // instead of the file itself — in measurement, content `GIZLI parola` became
        // `*** parola` in the backup.
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "filter.maskele.clean", "sed s/GIZLI/***/");
        harness.Repository.WriteFile(".gitattributes", "*.gizli filter=maskele\n");
        harness.Repository.Git("add", ".gitattributes");
        harness.Repository.Commit("filtre");

        harness.Repository.WriteFile("kasa.gizli", "GIZLI parola\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("kasa.gizli"), userConfirmed: true, Ct);

        await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        (await File.ReadAllTextAsync(Path.Combine(harness.Path, "kasa.gizli"), Ct))
            .ShouldBe("GIZLI parola\n");
    }

    [Fact]
    public async Task COK_dosyali_ikili_kurtarma_her_dosyayi_DOGRU_esliyor()
    {
        // The `cat-file --batch` stream is a single byte sequence: the contents arrive back to
        // back, and only the SIZE in the header determines the boundaries. If the parser drifts
        // by one byte, files get "recovered" with each other's content, and that's a silent
        // data loss — exactly what P05-T15 exists to prevent. Tested with binary content of
        // different sizes containing separator bytes (\n).
        using Harness harness = await CreateAsync();

        Dictionary<string, byte[]> contents = [];

        for (int i = 1; i <= 5; i++)
        {
            byte[] data = new byte[i * 1000];
            Random.Shared.NextBytes(data);

            // The line-ending bytes are deliberate: a parser searching for a separator breaks here.
            data[i * 10] = (byte)'\n';
            data[^1] = (byte)'\n';

            contents[$"ikili{i}.bin"] = data;
            await File.WriteAllBytesAsync(Path.Combine(harness.Path, $"ikili{i}.bin"), data, Ct);
        }

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths([.. contents.Keys]), userConfirmed: true, Ct);

        backups.Count.ShouldBe(5);

        IReadOnlyList<DiscardBackup> restored =
            await harness.Writer.RestoreBackupsAsync(harness.Path, backups, Ct);

        restored.Count.ShouldBe(5);

        foreach ((string name, byte[] expected) in contents)
        {
            (await File.ReadAllBytesAsync(Path.Combine(harness.Path, name), Ct))
                .ShouldBe(expected, $"{name} yanlış içerikle kurtarıldı");
        }
    }

    [Fact]
    public async Task Budanmis_yedek_digerlerini_ENGELLEMEZ()
    {
        // `gc --prune=now` deletes the backup instantly (measured). Partial recovery is better
        // than none at all; a single missing object must not drag the others down with it.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("duran.txt", "bu kurtarılmalı\n");

        IReadOnlyList<DiscardBackup> backups = await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("duran.txt"), userConfirmed: true, Ct);

        List<DiscardBackup> withMissing =
        [
            new DiscardBackup
            {
                Path = RepositoryPath.Parse("kayip.txt"),
                BlobId = "0000000000000000000000000000000000000000",
            },
            .. backups,
        ];

        IReadOnlyList<DiscardBackup> restored =
            await harness.Writer.RestoreBackupsAsync(harness.Path, withMissing, Ct);

        restored.Count.ShouldBe(1);
        harness.Exists("duran.txt").ShouldBeTrue();
        harness.Exists("kayip.txt").ShouldBeFalse();
    }

    [Fact]
    public async Task YOK_SAYILAN_dosya_da_silinir()
    {
        // 🔴 MEASURED: without `-x`, `git clean -f -- hata.log` exits 0 and the file STAYS.
        // The user expects the file they selected by name to be deleted; silently doing
        // nothing is the worst possible outcome for this task.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitignore", "*.log\n");
        harness.Repository.WriteFile("hata.log", "x\n");

        await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("hata.log"), userConfirmed: true, Ct);

        harness.Exists("hata.log").ShouldBeFalse();
    }

    [Fact]
    public async Task Takip_edilmeyen_DIZIN_silinir()
    {
        // MEASURED: without `-d` the directory isn't deleted at all, and no error is given either.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("dizin/ic.txt", "x\n");

        await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("dizin"), userConfirmed: true, Ct);

        Directory.Exists(Path.Combine(harness.Path, "dizin")).ShouldBeFalse();
    }

    [Fact]
    public async Task Onaysiz_silme_REDDEDILIR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni.txt", "x\n");

        await Should.ThrowAsync<InvalidOperationException>(
            harness.Writer.DeleteUntrackedAsync(
                harness.Path, Paths("yeni.txt"), userConfirmed: false, Ct));

        harness.Exists("yeni.txt").ShouldBeTrue();
    }

    [Fact]
    public async Task Bos_yol_listesi_HICBIR_SEYI_silmez()
    {
        // ⚠️ `git clean -f` without a path deletes the ENTIRE working tree.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni.txt", "x\n");

        await harness.Writer.DeleteUntrackedAsync(harness.Path, [], userConfirmed: true, Ct);

        harness.Exists("yeni.txt").ShouldBeTrue();
    }

    [Fact]
    public async Task Silme_IZLENEN_dosyaya_dokunmaz()
    {
        // Counter-evidence: adding `-x` must not turn into "delete everything".
        using Harness harness = await CreateAsync();

        await harness.Writer.DeleteUntrackedAsync(
            harness.Path, Paths("a.txt"), userConfirmed: true, Ct);

        harness.Exists("a.txt").ShouldBeTrue();
    }

    // ---- git clean (the whole tree) ----

    [Fact]
    public async Task Clean_takip_edilmeyenleri_siler_yok_sayilanlari_BIRAKIR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitignore", "*.log\n");
        harness.Repository.Git("add", ".gitignore");
        harness.Repository.Git("commit", "-m", "ignore");

        harness.Repository.WriteFile("yeni.txt", "x\n");
        harness.Repository.WriteFile("hata.log", "x\n");
        harness.Repository.WriteFile("dizin/ic.txt", "x\n");

        await harness.Writer.CleanAsync(harness.Path, CleanOptions.Default, userConfirmed: true, Ct);

        harness.Exists("yeni.txt").ShouldBeFalse();
        Directory.Exists(Path.Combine(harness.Path, "dizin")).ShouldBeFalse();

        // Ignored files are a separate decision: files that can't be regenerated, like `.env`,
        // are also commonly ignored.
        harness.Exists("hata.log").ShouldBeTrue();
    }

    [Fact]
    public async Task Clean_ISTENIRSE_yok_sayilanlari_da_siler()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitignore", "*.log\n");
        harness.Repository.Git("add", ".gitignore");
        harness.Repository.Git("commit", "-m", "ignore");
        harness.Repository.WriteFile("hata.log", "x\n");

        await harness.Writer.CleanAsync(
            harness.Path, new CleanOptions { IncludeIgnored = true }, userConfirmed: true, Ct);

        harness.Exists("hata.log").ShouldBeFalse();
    }

    [Fact]
    public async Task Clean_ic_ice_depoyu_ancak_ISTENIRSE_siler()
    {
        // 🔴 MEASURED: with plain `-f`, a nested repository does NOT appear in the output at
        // all — it's silently skipped.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("init", "icdepo");
        harness.Repository.WriteFile("icdepo/dosya.txt", "x\n");

        await harness.Writer.CleanAsync(harness.Path, CleanOptions.Default, userConfirmed: true, Ct);
        Directory.Exists(Path.Combine(harness.Path, "icdepo")).ShouldBeTrue();

        await harness.Writer.CleanAsync(
            harness.Path,
            new CleanOptions { IncludeNestedRepositories = true },
            userConfirmed: true,
            Ct);

        Directory.Exists(Path.Combine(harness.Path, "icdepo")).ShouldBeFalse();
    }

    [Fact]
    public async Task Onaysiz_clean_REDDEDILIR()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("yeni.txt", "x\n");

        await Should.ThrowAsync<InvalidOperationException>(
            harness.Writer.CleanAsync(harness.Path, CleanOptions.Default, userConfirmed: false, Ct));

        harness.Exists("yeni.txt").ShouldBeTrue();
    }

    // ---- .gitignore ----

    [Theory]
    [InlineData("duz.log")]
    [InlineData("bosluklu ad.txt")]
    [InlineData("#diyez.txt")]
    [InlineData("!unlem.txt")]
    [InlineData("kose[bracket].txt")]
    [InlineData("yildiz*.txt")]
    [InlineData("ters\\slash.txt")]
    public async Task Ozel_karakterli_ad_GERCEKTEN_yok_sayilir(string name)
    {
        // 🔴 MEASURED: writing the name RAW silently fails for `#`, `!`, `[`, and `\` — git
        // gives no error, and the file isn't ignored either. Verification is done by actually
        // asking real git via `check-ignore`; the pattern merely "looking correct" isn't enough.
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(name, "x\n");

        RepositoryPath path = RepositoryPath.Parse(name);

        GitIgnoreOutcome outcome = await harness.Writer.AddToGitIgnoreAsync(
            harness.Path, path, GitIgnorePattern.ForPath(path), Ct);

        outcome.ShouldBe(GitIgnoreOutcome.Added);

        // ⚠️ Verification is done with `-z`: `git status` QUOTES names with special characters
        // (`"ters\\slash.txt"`), so searching for the raw name in plain output would also say
        // "not found" for a broken version — meaning the test would silently pass for nothing
        // (the lesson of P04-T09).
        Untracked(harness).ShouldNotContain(name);

        // And by asking git directly. The exit code is checked, NOT the output: `check-ignore`
        // quotes names with special characters, and `-z` only works with `--stdin` (measured).
        // `TestRepository.Git` throws on a nonzero exit, so the test breaks if there's no match.
        Should.NotThrow(() => harness.Repository.Git("check-ignore", "--quiet", "--", name));
    }

    /// <summary>Untracked paths — via <c>-z</c>, i.e. unquoted raw names.</summary>
    private static IReadOnlyList<string> Untracked(Harness harness) =>
        [.. harness.Repository
            .Git("status", "--porcelain=v2", "-z", "--untracked-files=all")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(record => record.StartsWith("? ", StringComparison.Ordinal))
            .Select(record => record[2..])];

    [Fact]
    public async Task Satir_sonu_olmayan_gitignore_BOZULMAZ()
    {
        // 🔴 MEASURED: without adding a line ending, the new pattern glues onto the previous
        // one (`derleme/` + `/kok.txt` → `derleme//kok.txt`). The result isn't just that the
        // new pattern fails to work — the user's EXISTING pattern breaks too.
        using Harness harness = await CreateAsync();

        File.WriteAllText(Path.Combine(harness.Path, ".gitignore"), "derleme/");
        harness.Repository.WriteFile("derleme/cikti.o", "x\n");
        harness.Repository.WriteFile("kok.txt", "x\n");

        RepositoryPath path = RepositoryPath.Parse("kok.txt");
        await harness.Writer.AddToGitIgnoreAsync(
            harness.Path, path, GitIgnorePattern.ForPath(path), Ct);

        IReadOnlyList<string> untracked = Untracked(harness);

        untracked.ShouldNotContain("kok.txt");
        untracked.ShouldNotContain("derleme/cikti.o");
    }

    [Fact]
    public async Task IZLENEN_dosya_icin_gitignore_YAZILMAZ()
    {
        // 🔴 MEASURED: adding a tracked file to `.gitignore` does NOTHING — `git status`
        // keeps showing the file. Writing it and saying "added" would promise the user a
        // result that doesn't exist.
        using Harness harness = await CreateAsync();
        RepositoryPath path = RepositoryPath.Parse("a.txt");

        GitIgnoreOutcome outcome = await harness.Writer.AddToGitIgnoreAsync(
            harness.Path, path, GitIgnorePattern.ForPath(path), Ct);

        outcome.ShouldBe(GitIgnoreOutcome.PathIsTracked);
        harness.Exists(".gitignore").ShouldBeFalse();
    }

    [Fact]
    public async Task Zaten_yok_sayilan_dosya_icin_TEKRAR_yazilmaz()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile(".gitignore", "*.log\n");
        harness.Repository.WriteFile("hata.log", "x\n");

        RepositoryPath path = RepositoryPath.Parse("hata.log");

        GitIgnoreOutcome outcome = await harness.Writer.AddToGitIgnoreAsync(
            harness.Path, path, GitIgnorePattern.ForPath(path), Ct);

        outcome.ShouldBe(GitIgnoreOutcome.AlreadyIgnored);
        harness.Read(".gitignore").ShouldBe("*.log\n");
    }

    [Fact]
    public async Task Uzanti_deseni_ayni_uzantili_TUM_dosyalari_yok_sayar()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.WriteFile("bir.log", "x\n");
        harness.Repository.WriteFile("alt/iki.log", "x\n");

        RepositoryPath path = RepositoryPath.Parse("bir.log");
        string? pattern = GitIgnorePattern.ForExtensionOf(path);

        pattern.ShouldNotBeNull().ShouldBe("*.log");
        await harness.Writer.AddToGitIgnoreAsync(harness.Path, path, pattern!, Ct);

        IReadOnlyList<string> untracked = Untracked(harness);

        untracked.ShouldNotContain("bir.log");
        untracked.ShouldNotContain("alt/iki.log");
    }

    [Fact]
    public void Uzantisiz_ve_gizli_dosyalarda_uzanti_deseni_URETILMEZ()
    {
        // `.env` isn't an extension, it's a hidden file name; writing `*.env` means something different.
        GitIgnorePattern.ForExtensionOf(RepositoryPath.Parse("Makefile")).ShouldBeNull();
        GitIgnorePattern.ForExtensionOf(RepositoryPath.Parse(".env")).ShouldBeNull();
        GitIgnorePattern.ForExtensionOf(RepositoryPath.Parse("alt/.env")).ShouldBeNull();
    }

    [Fact]
    public void Dizin_deseni_yalnizca_alt_dizinler_icin_uretilir()
    {
        GitIgnorePattern.ForDirectoryOf(RepositoryPath.Parse("alt/derin/x.txt"))
            .ShouldBe("/alt/derin/");

        GitIgnorePattern.ForDirectoryOf(RepositoryPath.Parse("kok.txt")).ShouldBeNull();
    }
}
