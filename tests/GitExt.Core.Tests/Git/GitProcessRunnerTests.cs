using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests.Git;

/// <summary>
/// Süreç yürütme katmanının davranış sözleşmesi (P02-T01, P02-T03).
/// Tümü <b>gerçek <c>git</c></b> süreçleri çalıştırır.
/// </summary>
public class GitProcessRunnerTests
{
    /// <summary>Test iptal edildiğinde alt süreçlerin de durması için.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<GitProcessRunner> CreateRunnerAsync(IGitCommandLog? log = null)
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new GitProcessRunner(executable, log);
    }

    [Fact]
    public async Task Basarili_komut_cikti_dondurur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            GitCommand.Create(repository.Path, "rev-parse", "--abbrev-ref", "HEAD"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.ExitCode.ShouldBe(0);
        result.GetStandardOutputText().Trim().ShouldBe("main");
    }

    [Fact]
    public async Task Basarisiz_komut_istisna_firlatmaz_sonuc_dondurur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            GitCommand.Create(repository.Path, "rev-parse", "boyle-bir-ref-yok"), Ct);

        // RunAsync sözleşmesi: çıkış kodu ne olursa olsun sonuç döner.
        result.IsSuccess.ShouldBeFalse();
        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunCheckedAsync_basarisizlikta_siniflandirilmis_istisna_firlatir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitProcessRunner runner = await CreateRunnerAsync();

        GitException exception = await Should.ThrowAsync<GitException>(
            runner.RunCheckedAsync(
                GitCommand.Create(repository.Path, "rev-parse", "boyle-bir-ref-yok"), Ct));

        exception.Kind.ShouldBe(GitFailureKind.UnknownRevision);
        // Ham stderr her zaman erişilebilir kalmalı (P02-T12).
        exception.StandardError.ShouldNotBeNullOrWhiteSpace();
        exception.CommandLine.ShouldContain("rev-parse");
    }

    [Fact]
    public async Task Git_deposu_olmayan_dizin_siniflandirilir()
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("gitext-not-a-repo-");

        try
        {
            GitProcessRunner runner = await CreateRunnerAsync();

            GitException exception = await Should.ThrowAsync<GitException>(
                runner.RunCheckedAsync(
                    GitCommand.Create(temporary.FullName, "status", "--porcelain=v2"), Ct));

            exception.Kind.ShouldBe(GitFailureKind.NotARepository);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Bosluk_ve_ozel_karakter_iceren_dosya_adlari_bozulmadan_gecer()
    {
        // Argümanlar dizi olarak geçtiği için kabuk yorumlaması olmamalı (ADR-0002 kural 4).
        // Bu isim kabuğa gitseydi $(whoami) çalışır, & komutu bölerdi.
        const string awkwardName = "dosya adı 'tırnaklı' $(whoami) & ; şey.txt";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile(awkwardName, "içerik\n");
        repository.Git("add", "--", awkwardName);
        repository.Commit("tuhaf isimli dosya");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult listing = await runner.RunAsync(
            GitCommand.Create(repository.Path, "ls-files", "-z"), Ct);

        listing.SplitStandardOutputAtNul().ShouldContain(awkwardName);
    }

    [Fact]
    public async Task Unicode_dosya_adlari_kacis_dizisi_olmadan_gelir()
    {
        // core.quotepath=false olmadan git bunu \303\247... diye sekizlik kaçışlarla döndürür.
        const string turkishName = "çalışma-günlüğü-ÖĞÜŞİ.txt";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile(turkishName, "içerik\n");
        repository.Git("add", "--", turkishName);
        repository.Commit("unicode dosya adı");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            GitCommand.Create(repository.Path, "ls-files", "-z"), Ct);

        result.SplitStandardOutputAtNul().ShouldContain(turkishName);
    }

    [Fact]
    public async Task Standart_girdi_uzerinden_commit_mesaji_gecirilebilir()
    {
        // Commit mesajları komut satırına gömülmez; stdin'den geçer (ADR-0002 kural 4).
        const string message = "başlık satırı\n\nGövde: $HOME `whoami` \"tırnak\" ve 'tek tırnak'.";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult commit = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["commit", "-F", "-"],
                StandardInput = Encoding.UTF8.GetBytes(message),
                IsReadOnly = false,
            },
            Ct);

        commit.IsSuccess.ShouldBeTrue(commit.StandardError);

        GitResult stored = await runner.RunAsync(
            GitCommand.Create(repository.Path, "log", "--format=%B", "-1"), Ct);

        stored.GetStandardOutputText().TrimEnd().ShouldBe(message);
    }

    [Fact]
    public async Task Buyuk_cikti_deadlock_olmadan_okunur()
    {
        // stdout ve stderr eşzamanlı okunmazsa boru dolar, süreç yazamaz ve asla bitmez.
        // Bu test o deadlock'ı yakalar: geçmezse zaman aşımına düşer.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");

        for (int i = 0; i < 500; i++)
        {
            repository.Commit($"commit {i} — gövdeyi biraz uzatmak için ek metin ekliyoruz");
        }

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["log", "--format=%H %s"],
                Timeout = TimeSpan.FromSeconds(60),
            },
            Ct);

        result.IsSuccess.ShouldBeTrue();
        result.GetStandardOutputText()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Length.ShouldBe(500);
    }

    [Fact]
    public async Task Ikili_icerik_bozulmadan_okunur()
    {
        // stdout string olarak okunsaydı geçersiz UTF-8 baytları U+FFFD'ye dönüşür ve
        // içerik geri dönülemez şekilde bozulurdu.
        byte[] binaryContent = [0x00, 0xFF, 0xFE, 0x42, 0x00, 0x80, 0x81, 0x0A];

        using TestRepository repository = TestRepository.CreateEmpty();
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(repository.Path, "blob.bin"), binaryContent, Ct);
        repository.Git("add", "blob.bin");
        repository.Commit("ikili dosya");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitResult result = await runner.RunAsync(
            GitCommand.Create(repository.Path, "show", "HEAD:blob.bin"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.StandardOutput.ShouldBe(binaryContent);
    }

    [Fact]
    public async Task Zaman_asimi_siniflandirilmis_hata_uretir()
    {
        // Deterministik olması için gerçekten yavaş bir iş gerekiyor: küçük bir depoda
        // `git log` milisaniyeler içinde biter ve kısa zaman aşımıyla yarışa girer.
        // Uzun süren bir pre-commit hook'u hem kesin yavaş, hem de gerçek bir senaryo.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");
        repository.InstallHook("pre-commit", "sleep 30\n");

        GitProcessRunner runner = await CreateRunnerAsync();

        GitException exception = await Should.ThrowAsync<GitException>(
            runner.RunAsync(
                new GitCommand
                {
                    WorkingDirectory = repository.Path,
                    Arguments = ["commit", "-m", "hook bunu geciktirecek"],
                    IsReadOnly = false,
                    Timeout = TimeSpan.FromMilliseconds(700),
                },
                Ct));

        exception.Kind.ShouldBe(GitFailureKind.Timeout);
    }

    [Fact]
    public async Task Zaman_asimi_sureci_gercekten_oldurur()
    {
        // Zaman aşımı hatası fırlatmak yetmez; alt süreç ağacı da ölmeli.
        // Hook hâlâ çalışıyor olsaydı index.lock tutulmaya devam ederdi ve
        // sonraki commit "Another git process seems to be running" ile kırılırdı.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");
        repository.InstallHook("pre-commit", "sleep 30\n");

        GitProcessRunner runner = await CreateRunnerAsync();

        await Should.ThrowAsync<GitException>(
            runner.RunAsync(
                new GitCommand
                {
                    WorkingDirectory = repository.Path,
                    Arguments = ["commit", "-m", "zaman aşımına uğrayacak"],
                    IsReadOnly = false,
                    Timeout = TimeSpan.FromMilliseconds(700),
                },
                Ct));

        // Hook'u kaldır ve depo hâlâ kullanılabilir mi diye bak.
        repository.InstallHook("pre-commit", "exit 0\n");

        GitResult status = await runner.RunAsync(
            GitCommand.Create(repository.Path, "status", "--porcelain=v2"), Ct);

        status.IsSuccess.ShouldBeTrue(status.StandardError);
    }

    [Fact]
    public async Task Iptal_token_i_calismayi_durdurur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitProcessRunner runner = await CreateRunnerAsync();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            runner.RunAsync(GitCommand.Create(repository.Path, "log"), cancellation.Token));
    }

    [Fact]
    public async Task Calistirilan_komutlar_gunluge_kaydedilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        InMemoryGitCommandLog log = new();
        GitProcessRunner runner = await CreateRunnerAsync(log);

        await runner.RunAsync(GitCommand.Create(repository.Path, "status", "--porcelain=v2"), Ct);

        GitCommandLogEntry entry = log.Entries.ShouldHaveSingleItem();
        entry.CommandLine.ShouldBe("git status --porcelain=v2");
        entry.IsSuccess.ShouldBeTrue();
        entry.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Gunluk_kapasitesini_asmaz()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        InMemoryGitCommandLog log = new(capacity: 3);
        GitProcessRunner runner = await CreateRunnerAsync(log);

        for (int i = 0; i < 6; i++)
        {
            await runner.RunAsync(GitCommand.Create(repository.Path, "rev-parse", "HEAD"), Ct);
        }

        log.Entries.Count.ShouldBe(3);
    }

    [Fact]
    public void Komut_gosterim_metni_terminale_kopyalanabilir()
    {
        GitCommand command = GitCommand.Create(
            "/tmp/repo", "commit", "-m", "boşluklu 'mesaj'");

        // Tırnaklama olmasaydı kullanıcı bu satırı kopyalayıp çalıştıramazdı.
        command.ToDisplayString().ShouldBe("git commit -m 'boşluklu '\\''mesaj'\\'''");
    }
}
