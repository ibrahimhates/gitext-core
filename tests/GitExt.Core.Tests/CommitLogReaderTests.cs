using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P02-T08 / P02-T04 — Commit geçmişi okuma. Tümü gerçek <c>git</c> ile.
/// </summary>
public class CommitLogReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<CommitLogReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new CommitLogReader(new GitProcessRunner(executable));
    }

    [Fact]
    public async Task Bos_repo_bos_liste_dondurur()
    {
        // Commit'i olmayan repoda `git log` hata verir; bu, kullanıcının ilk açtığı
        // repo olabilir ve çökmemeli.
        using TestRepository repository = TestRepository.CreateEmpty();
        CommitLogReader reader = await CreateReaderAsync();

        GitException exception = await Should.ThrowAsync<GitException>(
            reader.ReadAsync(repository.Path, new CommitLogQuery(), Ct));

        // Şimdilik sınıflandırılmış bir hata bekliyoruz; boş repo davranışı
        // P02-T14'te ayrıca ele alınacak.
        exception.Kind.ShouldBeOneOf(GitFailureKind.UnknownRevision, GitFailureKind.Unknown);
    }

    [Fact]
    public async Task Tek_commit_tum_alanlariyla_okunur()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "içerik\n");
        repository.Git("add", "a.txt");
        repository.Git("commit", "-m", "ilk commit");

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> commits =
            await reader.ReadAsync(repository.Path, new CommitLogQuery(), Ct);

        CommitInfo commit = commits.ShouldHaveSingleItem();
        commit.Subject.ShouldBe("ilk commit");
        commit.Body.ShouldBeEmpty();
        commit.Id.IsFull.ShouldBeTrue();
        commit.IsRoot.ShouldBeTrue();
        commit.IsMerge.ShouldBeFalse();
        commit.Author.Name.ShouldBe("gitext-core tests");
        commit.Author.Email.ShouldBe("tests@gitext-core.invalid");
        commit.Author.When.Year.ShouldBeGreaterThan(2000);
    }

    [Fact]
    public async Task Bos_govdeli_commitler_alan_hizasini_bozmaz()
    {
        // KRİTİK: %x00 ayraçlı sabit alanlı kayıtlarda boş bir alan atılırsa sonraki
        // tüm alanlar kayar ve veri SESSİZCE yanlış olur. Boş ve dolu gövdeleri
        // dönüşümlü koyarak hizanın korunduğunu doğruluyoruz.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        repository.Git("commit", "-m", "gövdesiz bir");
        repository.Git("commit", "--allow-empty", "-m", "gövdeli", "-m", "gövde metni");
        repository.Git("commit", "--allow-empty", "-m", "gövdesiz iki");
        repository.Git("commit", "--allow-empty", "-m", "yine gövdeli", "-m", "ikinci gövde");

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> commits =
            await reader.ReadAsync(repository.Path, new CommitLogQuery(), Ct);

        commits.Count.ShouldBe(4);
        // En yeni önce.
        commits[0].Subject.ShouldBe("yine gövdeli");
        commits[0].Body.ShouldBe("ikinci gövde");
        commits[1].Subject.ShouldBe("gövdesiz iki");
        commits[1].Body.ShouldBeEmpty();
        commits[2].Subject.ShouldBe("gövdeli");
        commits[2].Body.ShouldBe("gövde metni");
        commits[3].Subject.ShouldBe("gövdesiz bir");
        commits[3].Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task Satir_sonu_iceren_govde_kaydi_bolmez()
    {
        // Ayraç olarak \n kullanılsaydı bu commit birden fazla kayıt gibi görünürdü.
        const string body = "Birinci satır\nİkinci satır\n\nBoş satırdan sonra üçüncü";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");
        repository.Git("commit", "-m", "çok satırlı", "-m", body);

        CommitLogReader reader = await CreateReaderAsync();

        CommitInfo commit = (await reader.ReadAsync(repository.Path, new CommitLogQuery(), Ct))
            .ShouldHaveSingleItem();

        commit.Subject.ShouldBe("çok satırlı");
        commit.Body.ShouldBe(body);
    }

    [Fact]
    public async Task Merge_commit_birden_fazla_ebeveyn_dondurur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("checkout", "-q", "-b", "yan-dal");
        repository.WriteFile("yan.txt", "yan\n");
        repository.Git("add", "yan.txt");
        repository.Git("commit", "-m", "yan dal commit");
        repository.Git("checkout", "-q", "main");
        repository.WriteFile("ana.txt", "ana\n");
        repository.Git("add", "ana.txt");
        repository.Git("commit", "-m", "ana dal commit");
        repository.Git("merge", "--no-ff", "-m", "birleştirme", "yan-dal");

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> commits =
            await reader.ReadAsync(repository.Path, new CommitLogQuery(), Ct);

        CommitInfo merge = commits.First(c => c.Subject == "birleştirme");
        merge.IsMerge.ShouldBeTrue();
        merge.Parents.Count.ShouldBe(2);
        merge.Parents.ShouldAllBe(p => p.IsFull);
    }

    [Fact]
    public async Task Octopus_merge_ikiden_fazla_ebeveyn_dondurur()
    {
        // Ebeveyn listesi bilinçli olarak sınırsız; "en fazla 2" varsayımı yanlış olurdu.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        foreach (string branch in new[] { "dal1", "dal2", "dal3" })
        {
            repository.Git("checkout", "-q", "-b", branch, "main");
            repository.WriteFile($"{branch}.txt", $"{branch}\n");
            repository.Git("add", $"{branch}.txt");
            repository.Git("commit", "-m", $"{branch} commit");
        }

        repository.Git("checkout", "-q", "main");
        repository.Git("merge", "--no-ff", "-m", "octopus", "dal1", "dal2", "dal3");

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> commits =
            await reader.ReadAsync(repository.Path, new CommitLogQuery(), Ct);

        CommitInfo octopus = commits.First(c => c.Subject == "octopus");
        octopus.Parents.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Utf8_olmayan_kodlamali_commit_utf8_olarak_gelir()
    {
        // ISO-8859-9 (Latin-5) olarak SAKLANAN bir mesaj, i18n.logOutputEncoding=UTF-8
        // sayesinde doğru karakterlerle gelmeli.
        //
        // Mesaj GERÇEK Latin-5 baytlarıyla yazılmalı. `-m "Türkçe"` yazıp commitEncoding'i
        // Latin-5 ilan etmek işe yaramaz: .NET argümanı UTF-8 olarak geçirir, git onu
        // Latin-5 sanıp bir kez daha çevirir ve mojibake üretir. Bu git'in doğru davranışıdır;
        // kodlama etiketi ile baytların uyuşması gerekir.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        // "Türkçe baslik" — Latin-5: ü=0xFC, ç=0xE7
        byte[] latin5Message = [0x54, 0xFC, 0x72, 0x6B, 0xE7, 0x65, 0x20, 0x62, 0x61, 0x73, 0x6C, 0x69, 0x6B];
        string messageFile = Path.Combine(repository.Path, "msg.txt");
        await File.WriteAllBytesAsync(messageFile, latin5Message, Ct);

        repository.Git("-c", "i18n.commitEncoding=ISO-8859-9", "commit", "-F", "msg.txt");

        CommitLogReader reader = await CreateReaderAsync();

        CommitInfo commit = (await reader.ReadAsync(repository.Path, new CommitLogQuery(), Ct))
            .ShouldHaveSingleItem();

        commit.Subject.ShouldBe("Türkçe baslik");
        commit.Encoding.ShouldBe("ISO-8859-9");
    }

    [Fact]
    public async Task Ref_isimleri_okunur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("tag", "v1.0");

        CommitLogReader reader = await CreateReaderAsync();

        CommitInfo commit = (await reader.ReadAsync(repository.Path, new CommitLogQuery(), Ct))
            .ShouldHaveSingleItem();

        commit.Refs.ShouldContain(r => r.Contains("v1.0", StringComparison.Ordinal));
        commit.Refs.ShouldContain(r => r.Contains("main", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MaxCount_ve_Skip_uygulanir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        for (int i = 0; i < 10; i++)
        {
            repository.Git("commit", "--allow-empty", "-m", $"commit {i}");
        }

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> page = await reader.ReadAsync(
            repository.Path, new CommitLogQuery { MaxCount = 3, Skip = 2 }, Ct);

        page.Count.ShouldBe(3);
        page[0].Subject.ShouldBe("commit 7");
        page[2].Subject.ShouldBe("commit 5");
    }

    [Fact]
    public async Task Yol_filtresi_uygulanir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");
        repository.Git("commit", "-m", "a eklendi");

        repository.WriteFile("alt/b.txt", "b\n");
        repository.Git("add", "alt/b.txt");
        repository.Git("commit", "-m", "b eklendi");

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> commits = await reader.ReadAsync(
            repository.Path,
            new CommitLogQuery { Paths = [RepositoryPath.Parse("alt/b.txt")] },
            Ct);

        commits.ShouldHaveSingleItem().Subject.ShouldBe("b eklendi");
    }

    [Fact]
    public async Task Akis_ve_toplu_okuma_ayni_sonucu_verir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        for (int i = 0; i < 25; i++)
        {
            // Gövdeli ve gövdesiz karışık: akışta da hiza korunmalı.
            if (i % 3 == 0)
            {
                repository.Git("commit", "--allow-empty", "-m", $"commit {i}");
            }
            else
            {
                repository.Git("commit", "--allow-empty", "-m", $"commit {i}", "-m", $"gövde {i}");
            }
        }

        CommitLogReader reader = await CreateReaderAsync();
        CommitLogQuery query = new();

        IReadOnlyList<CommitInfo> batch = await reader.ReadAsync(repository.Path, query, Ct);

        List<CommitInfo> streamed = [];
        await foreach (CommitInfo commit in reader.StreamAsync(repository.Path, query, Ct))
        {
            streamed.Add(commit);
        }

        streamed.Count.ShouldBe(batch.Count);
        streamed.Select(c => c.Id).ShouldBe(batch.Select(c => c.Id));
        streamed.Select(c => c.Subject).ShouldBe(batch.Select(c => c.Subject));
        streamed.Select(c => c.Body).ShouldBe(batch.Select(c => c.Body));
    }

    [Fact]
    public async Task Akis_erken_sonlandirilabilir()
    {
        // Sonsuz kaydırmanın temeli: ilk N kaydı alıp bırakabilmeliyiz.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        for (int i = 0; i < 200; i++)
        {
            repository.Git("commit", "--allow-empty", "-m", $"commit {i}");
        }

        CommitLogReader reader = await CreateReaderAsync();

        List<CommitInfo> first = [];
        await foreach (CommitInfo commit in reader.StreamAsync(repository.Path, new CommitLogQuery(), Ct))
        {
            first.Add(commit);
            if (first.Count == 5)
            {
                break;
            }
        }

        first.Count.ShouldBe(5);
        first[0].Subject.ShouldBe("commit 199");
    }

    [Fact]
    public async Task Imzali_commit_mesaji_bozmaz()
    {
        // P02-T14 — İmza, commit nesnesine `gpgsig` başlığı olarak ÇOK SATIRLI biçimde
        // yazılır ve mesajdan önce gelir. Ölçüldü: `%s`/`%b` alanlarına sızmıyor,
        // ama bu doğrulanmadan varsayılamaz — imza satırları mesaj sanılsaydı
        // her imzalı commit'in gövdesi çöp olurdu.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        if (!repository.TryEnableSshSigning())
        {
            Assert.Skip("ssh-keygen bulunamadı; imzalama testi atlandı.");
        }

        repository.Git("commit", "-S", "-m", "imzalı başlık", "-m", "gövde satırı bir\ngövde satırı iki");
        repository.Git("commit", "--allow-empty", "-m", "imzasız sonraki");

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> commits =
            await reader.ReadAsync(repository.Path, new CommitLogQuery(), Ct);

        commits.Count.ShouldBe(2);

        CommitInfo signed = commits.Single(c => c.Subject == "imzalı başlık");
        signed.Body.ShouldBe("gövde satırı bir\ngövde satırı iki");
        // İmza metni hiçbir alana sızmamalı.
        signed.Body.ShouldNotContain("SSH SIGNATURE");
        signed.Subject.ShouldNotContain("gpgsig");
        signed.Author.Name.ShouldBe("gitext-core tests");

        // İmzalı kayıttan sonraki commit de doğru okunmalı — hiza korunmuş olmalı.
        commits.Single(c => c.Subject == "imzasız sonraki").Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task Carpik_tarihli_depoda_cocuk_her_zaman_ebeveynden_once_gelir()
    {
        // ADR-0007'nin değişmezi. Grafik yerleşimi tek geçişli ileri tarama yapıyor;
        // bir ebeveyn çocuğundan önce gelirse kenar YUKARI bakar ve grafik bozulur.
        //
        // git log'un varsayılan (tarih) sırası bu garantiyi VERMEZ — ölçüldü.
        // Aşağıdaki depo tam olarak o durumu üretiyor: yan dalın tarihi merge base'inden eski.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        repository.CommitAtDate("base", "2020-01-01T00:00:00");
        repository.Git("checkout", "-q", "-b", "yan");
        repository.CommitAtDate("yan-cok-eski", "2010-01-01T00:00:00");
        repository.Git("checkout", "-q", "main");
        repository.CommitAtDate("main-yeni", "2022-01-01T00:00:00");
        repository.Git("merge", "--no-ff", "-m", "birlestirme", "yan");

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> commits = await reader.ReadAsync(
            repository.Path, new CommitLogQuery { IncludeAllRefs = true }, Ct);

        Dictionary<CommitId, int> position = [];
        for (int i = 0; i < commits.Count; i++)
        {
            position[commits[i].Id] = i;
        }

        foreach (CommitInfo commit in commits)
        {
            foreach (CommitId parent in commit.Parents)
            {
                if (position.TryGetValue(parent, out int parentIndex))
                {
                    parentIndex.ShouldBeGreaterThan(
                        position[commit.Id],
                        $"'{parent.ToShortString()}' ebeveyni, çocuğu '{commit.Id.ToShortString()}' "
                        + "commit'inden ÖNCE geldi — topolojik sıra ihlali (ADR-0007).");
                }
            }
        }
    }

    [Fact]
    public async Task Topolojik_sira_kapatilabilir()
    {
        // Sıranın önemsiz olduğu durumlarda (tek dosya geçmişi gibi) maliyetten kaçınmak için.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("commit", "--allow-empty", "-m", "ikinci");

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> commits = await reader.ReadAsync(
            repository.Path, new CommitLogQuery { TopologicalOrder = false }, Ct);

        commits.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Bosluklu_ve_unicode_yollar_filtrede_calisir()
    {
        const string awkward = "belgeler/çalışma günlüğü.md";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile(awkward, "içerik\n");
        repository.Git("add", "--", awkward);
        repository.Git("commit", "-m", "tuhaf yol");

        repository.WriteFile("baska.txt", "x\n");
        repository.Git("add", "baska.txt");
        repository.Git("commit", "-m", "başka dosya");

        CommitLogReader reader = await CreateReaderAsync();

        IReadOnlyList<CommitInfo> commits = await reader.ReadAsync(
            repository.Path,
            new CommitLogQuery { Paths = [RepositoryPath.Parse(awkward)] },
            Ct);

        commits.ShouldHaveSingleItem().Subject.ShouldBe("tuhaf yol");
    }
}
