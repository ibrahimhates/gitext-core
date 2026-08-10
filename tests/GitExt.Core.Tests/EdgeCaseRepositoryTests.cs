using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T15 — kenar durum depoları.
/// </summary>
/// <remarks>
/// <para>
/// Planın listesi: boş repo · tek commit · çok dal · çok tag · derin dizin ağacı ·
/// büyük dosyalar · binary ağırlıklı · shallow clone · bare repo.
/// </para>
/// <para>
/// Bunların ortak yanı, "normal" bir depoda hiç görünmeyen varsayımları kırmaları:
/// <c>HEAD</c>'in var olduğu, çalışma ağacının bulunduğu, geçmişin kökten başladığı.
/// Bir kenar durum çöküyorsa kullanıcı onu <b>ilk açılışta</b> yaşar.
/// </para>
/// </remarks>
public class EdgeCaseRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(CommitLogReader Reader, RefReader Refs, RepositoryLocator Locator)> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct)
            .ConfigureAwait(true);

        GitProcessRunner runner = new(executable);

        return (new CommitLogReader(runner), new RefReader(runner), new RepositoryLocator(runner));
    }

    /// <summary>
    /// Boş depoda <c>--all</c> ile okuma hata değil boş liste veriyor (P09-T15).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — iki çağrı biçimi boş depoda AYRI davranıyor:</b>
    /// <code>
    /// git log                → fatal: your current branch 'main' does not have any commits yet  (128)
    /// git log --all          → (boş çıktı, 0)
    /// </code>
    /// Arayüz <c>IncludeAllRefs</c> ile okuduğu için yeni oluşturulmuş bir depo hata
    /// ekranıyla değil boş listeyle açılıyor. Ama fark <b>tesadüfi değil, korunması
    /// gereken</b> bir seçim: sorguyu <c>--all</c>'suz kurmak yeni deponun ilk
    /// açılışını hataya çevirirdi.
    /// </remarks>
    [Fact]
    public async Task Bos_depo_hata_degil_bos_liste_veriyor()
    {
        using TestRepository repo = TestRepository.CreateEmpty();
        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { IncludeAllRefs = true, MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        commits.ShouldBeEmpty();
    }

    /// <remarks>
    /// Aynı deponun <c>--all</c>'suz okunması git tarafından <b>hata</b> sayılıyor.
    /// Bu test o farkı sabitliyor: davranış değişirse — git bir gün 0 döndürmeye
    /// başlarsa ya da biri sorguyu değiştirirse — burada görünür.
    /// </remarks>
    [Fact]
    public async Task Bos_depoda_HEAD_uzerinden_okuma_hata_veriyor()
    {
        using TestRepository repo = TestRepository.CreateEmpty();
        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        await Should.ThrowAsync<GitException>(async () => await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task Tek_commitlik_depo_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        commits.Count.ShouldBe(1);
        commits[0].Parents.ShouldBeEmpty("kök commit'in ebeveyni olmamalı");
    }

    /// <remarks>
    /// Bare depoda çalışma ağacı <b>yok</b>. Ağaç varsayan bir okuma yolu burada
    /// "must be run in a work tree" ile ölürdü.
    /// </remarks>
    [Fact]
    public async Task Bare_depo_calisma_agaci_olmadan_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateBare();
        (_, _, RepositoryLocator locator) = await CreateAsync().ConfigureAwait(true);

        RepositoryLocation location = await locator.LocateAsync(repo.Path, Ct).ConfigureAwait(true);

        location.IsBare.ShouldBeTrue();
        location.WorkTreeRoot.ShouldBeNull();
    }

    /// <remarks>
    /// Çok sayıda ref, <c>for-each-ref</c> çıktısını büyütüyor. 500 etiket, ayrıştırıcının
    /// tek seferde okuduğu metni ~kilobaytlara çıkarıyor; kayıt ayracı hatası burada
    /// görünür hâle gelirdi.
    /// </remarks>
    [Fact]
    public async Task Cok_sayida_etiket_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        for (int i = 0; i < 500; i++)
        {
            repo.Git("tag", $"v{i}");
        }

        (_, RefReader refs, _) = await CreateAsync().ConfigureAwait(true);

        RepositoryRefs all = await refs.ReadAsync(repo.Path, Ct).ConfigureAwait(true);

        all.Tags.Count.ShouldBe(500);
    }

    /// <remarks>
    /// Boş bir konu satırı (<c>--allow-empty-message</c>) alan ayracını iki NUL yan yana
    /// getiriyor — Faz 07'de kayıtları ortadan bölen hata tam olarak buydu.
    /// </remarks>
    [Fact]
    public async Task Bos_commit_mesaji_kaydi_bolmuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        repo.WriteFile("x.txt", "x");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "--allow-empty-message", "-m", "");

        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        commits.Count.ShouldBe(2, "boş mesajlı commit kaydı ikiye böldü");
        commits[0].Subject.ShouldBeEmpty();
    }

    /// <remarks>
    /// Shallow clone'da geçmiş <b>kesik</b>: en eski commit'in ebeveyni tanımlı ama
    /// depoda yok. Ebeveyni çözmeye çalışan bir kod burada patlardı; grafik bunu
    /// "geçmişin sınırı" olarak göstermeli.
    /// </remarks>
    [Fact]
    public async Task Shallow_clone_kesik_gecmisle_okunuyor()
    {
        using TestRepository origin = TestRepository.CreateWithSingleCommit();

        for (int i = 0; i < 5; i++)
        {
            origin.WriteFile($"f{i}.txt", $"{i}");
            origin.Git("add", "-A");
            origin.Git("commit", "-q", "-m", $"commit {i}");
        }

        string shallowPath = Path.Combine(Path.GetTempPath(), $"gitext-shallow-{Guid.NewGuid():N}");

        try
        {
            origin.Git("clone", "--depth", "2", "--no-local", origin.Path, shallowPath);

            (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

            IReadOnlyList<CommitInfo> commits = await reader
                .ReadAsync(shallowPath, new CommitLogQuery { MaxCount = 10 }, Ct)
                .ConfigureAwait(true);

            commits.Count.ShouldBeLessThanOrEqualTo(3, "shallow clone tüm geçmişi getirdi");
            commits.ShouldNotBeEmpty();
        }
        finally
        {
            if (Directory.Exists(shallowPath))
            {
                DeleteRecursive(shallowPath);
            }
        }
    }

    /// <remarks>
    /// Binary içerik metin olarak çözülmeye çalışılırsa ya patlar ya da bozuk veri
    /// üretir. Commit meta verisi binary dosyalardan etkilenmemeli.
    /// </remarks>
    [Fact]
    public async Task Binary_dosyali_depo_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        byte[] payload = new byte[64 * 1024];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(Path.Combine(repo.Path, "veri.bin"), payload);

        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "binary");

        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        commits[0].Subject.ShouldBe("binary");
    }

    /// <remarks>
    /// Türkçe karakterli yollar <c>-z</c> olmadan C-tırnaklanıyor (Faz 06'da ölçüldü).
    /// Depo adında da geçebiliyor.
    /// </remarks>
    [Fact]
    public async Task Turkce_karakterli_yol_okunuyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();

        repo.WriteFile("şğüıöç/çalışma dosyası.txt", "içerik");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "Türkçe yol");

        (CommitLogReader reader, _, _) = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 1 }, Ct)
            .ConfigureAwait(true);

        commits[0].Subject.ShouldBe("Türkçe yol");
    }

    private static void DeleteRecursive(string path)
    {
        // Klonlanan depodaki nesne dosyaları salt okunur olabiliyor; doğrudan silmek
        // erişim hatası verir.
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
