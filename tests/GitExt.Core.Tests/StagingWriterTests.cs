using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T03 — Dosya seviyesinde stage / unstage.
/// </summary>
/// <remarks>
/// <para>
/// Uygulamanın <b>ilk yazma işlemleri</b>. Testler komutun metnini değil <b>etkisini</b>
/// doğruluyor: her senaryo gerçek bir depoda çalıştırılıp sonuç <c>git status</c> ile
/// okunuyor.
/// </para>
/// <para>
/// <b>ÖLÇÜLDÜ:</b> unstage tek komutla yapılamıyor — HEAD yokken <c>restore --staged</c>
/// <c>fatal: could not resolve 'HEAD'</c> ile çöküyor, HEAD varken <c>rm --cached</c> ise
/// dosyayı <i>silinmiş</i> olarak stage'liyor.
/// </para>
/// </remarks>
public class StagingWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(StagingWriter Writer, GitWriteQueue Queue)> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return (new StagingWriter(new GitWriter(runner, queue), runner), queue);
    }

    private static RepositoryPath[] Paths(params string[] values) =>
        [.. values.Select(RepositoryPath.Parse)];

    /// <summary>Bir yolun <c>git status --porcelain=v2</c> içindeki XY durum kodu.</summary>
    private static string Status(TestRepository repository, string path)
    {
        foreach (string line in repository.Git("status", "--porcelain=v2").Split('\n'))
        {
            if (line.Length > 4 && line.EndsWith(path, StringComparison.Ordinal))
            {
                return line.StartsWith("? ", StringComparison.Ordinal)
                    ? "??"
                    : line.Split(' ')[1];
            }
        }

        return string.Empty;
    }

    [Fact]
    public async Task Dosya_stage_lenir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("yeni.txt", "icerik\n");

        Status(repository, "yeni.txt").ShouldBe("??");

        await writer.StageAsync(repository.Path, Paths("yeni.txt"), Ct);

        // "A." = index'e eklendi, çalışma ağacında değişiklik yok.
        Status(repository, "yeni.txt").ShouldBe("A.");
    }

    [Fact]
    public async Task Stage_lenen_dosya_unstage_edilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("yeni.txt", "icerik\n");
        await writer.StageAsync(repository.Path, Paths("yeni.txt"), Ct);

        await writer.UnstageAsync(repository.Path, Paths("yeni.txt"), Ct);

        // Dosya takipsize döner ama DİSKTE KALIR.
        Status(repository, "yeni.txt").ShouldBe("??");
        File.Exists(Path.Combine(repository.Path, "yeni.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Takip_edilen_dosyanin_degisikligi_unstage_edilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("dosya.txt", "ilk\n");
        await writer.StageAsync(repository.Path, Paths("dosya.txt"), Ct);
        repository.Git("commit", "-m", "dosya eklendi");

        repository.WriteFile("dosya.txt", "ilk\ndegisiklik\n");
        await writer.StageAsync(repository.Path, Paths("dosya.txt"), Ct);
        Status(repository, "dosya.txt").ShouldBe("M.");

        await writer.UnstageAsync(repository.Path, Paths("dosya.txt"), Ct);

        // ⚠️ Burada `rm --cached` kullanılsaydı sonuç "D." olurdu: kullanıcı unstage
        // isterken dosyayı SİLİNMİŞ olarak stage'lenmiş görürdü.
        Status(repository, "dosya.txt").ShouldBe(".M");
    }

    [Fact]
    public async Task HEAD_YOKKEN_unstage_calisir()
    {
        // ÖLÇÜLDÜ: bu durumda `git restore --staged` çöküyor
        // (fatal: could not resolve 'HEAD'). İlk commit öncesi stage'lenen dosyayı geri
        // almak yaygın bir işlem; çökmemeli.
        using TestRepository repository = TestRepository.CreateEmpty();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("ilk.txt", "icerik\n");
        await writer.StageAsync(repository.Path, Paths("ilk.txt"), Ct);
        Status(repository, "ilk.txt").ShouldBe("A.");

        await writer.UnstageAsync(repository.Path, Paths("ilk.txt"), Ct);

        Status(repository, "ilk.txt").ShouldBe("??");
        File.Exists(Path.Combine(repository.Path, "ilk.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Silinen_dosya_da_stage_lenir()
    {
        // `git add` tek başına silmeleri almaz; `-A` gerekiyor (ölçüldü).
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("silinecek.txt", "icerik\n");
        await writer.StageAsync(repository.Path, Paths("silinecek.txt"), Ct);
        repository.Git("commit", "-m", "eklendi");

        File.Delete(Path.Combine(repository.Path, "silinecek.txt"));

        await writer.StageAsync(repository.Path, Paths("silinecek.txt"), Ct);

        Status(repository, "silinecek.txt").ShouldBe("D.");
    }

    [Fact]
    public async Task Tire_ile_baslayan_ve_bosluklu_yollar_calisir()
    {
        // Yollar `--` ayracından SONRA veriliyor; aksi hâlde git bunları seçenek sanardı.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("-tireli.txt", "a\n");
        repository.WriteFile("bosluklu ad.txt", "b\n");

        await writer.StageAsync(repository.Path, Paths("-tireli.txt", "bosluklu ad.txt"), Ct);

        Status(repository, "-tireli.txt").ShouldBe("A.");
        // `--porcelain=v2` boşluklu adı tırnaklamıyor (ölçüldü); yol olduğu gibi geçiyor.
        Status(repository, "bosluklu ad.txt").ShouldBe("A.");
    }

    [Fact]
    public async Task Bos_liste_HICBIR_SEY_yapmaz()
    {
        // ⚠️ Yol verilmeden `git add -A --` çalıştırmak TÜM depoyu stage'lerdi.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("dokunulmayacak.txt", "icerik\n");

        await writer.StageAsync(repository.Path, [], Ct);

        Status(repository, "dokunulmayacak.txt").ShouldBe("??");
    }

    [Fact]
    public async Task Untrack_dosyayi_diskte_birakir()
    {
        // Bu işlem BİLİNÇLİ olarak unstage'den ayrı: takip edilen bir dosyada sonuç
        // "silinmiş olarak stage'lendi" olur ve kullanıcı bunu isteyerek yapar.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("ayar.json", "{}\n");
        await writer.StageAsync(repository.Path, Paths("ayar.json"), Ct);
        repository.Git("commit", "-m", "ayar eklendi");

        await writer.UntrackAsync(repository.Path, Paths("ayar.json"), Ct);

        Status(repository, "ayar.json").ShouldBe("D.");
        File.Exists(Path.Combine(repository.Path, "ayar.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task Coklu_yol_tek_cagrida_stage_lenir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        repository.WriteFile("bir.txt", "1\n");
        repository.WriteFile("iki.txt", "2\n");
        repository.WriteFile("uc.txt", "3\n");

        await writer.StageAsync(repository.Path, Paths("bir.txt", "iki.txt"), Ct);

        Status(repository, "bir.txt").ShouldBe("A.");
        Status(repository, "iki.txt").ShouldBe("A.");

        // Verilmeyen yol etkilenmemeli.
        Status(repository, "uc.txt").ShouldBe("??");
    }

    [Fact]
    public async Task Eszamanli_stage_cagrilari_cakismaz()
    {
        // Yazma yolu kuyruktan geçiyor (P05-T01): doğrudan `git add` çalıştırılsaydı
        // bu senaryoda çakışma olurdu.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        (StagingWriter writer, GitWriteQueue queue) = await CreateAsync();
        using GitWriteQueue _ = queue;

        for (int i = 0; i < 12; i++)
        {
            repository.WriteFile($"eszamanli{i}.txt", $"{i}\n");
        }

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            writer.StageAsync(repository.Path, Paths($"eszamanli{i}.txt"), Ct)));

        for (int i = 0; i < 12; i++)
        {
            Status(repository, $"eszamanli{i}.txt").ShouldBe("A.");
        }
    }
}
