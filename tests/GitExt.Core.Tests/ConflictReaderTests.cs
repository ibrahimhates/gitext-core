using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T01 + P07-T02 — çakışma tespiti ve üç sürüme erişim.
/// </summary>
/// <remarks>
/// Çakışma türleri elle uydurulmuyor; her biri <b>gerçek bir merge</b> ile üretiliyor.
/// Uydurma <c>u</c> satırı yazmak, ayrıştırıcıyı değil bizim çıktının nasıl göründüğüne
/// dair varsayımımızı test ederdi.
/// </remarks>
public class ConflictReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ConflictReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new ConflictReader(new GitProcessRunner(executable));
    }

    /// <summary>İki tarafın da aynı dosyaya dokunduğu bir çakışma kurar.</summary>
    private static TestRepository Conflicting(
        Action<TestRepository> onBranch,
        Action<TestRepository> onMain,
        string baseFile = "f.txt",
        string baseContent = "a\nb\nc\n")
    {
        TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile(baseFile, baseContent);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ortak ata");

        repository.Git("checkout", "-q", "-b", "yan");
        onBranch(repository);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "yan");

        repository.Git("checkout", "-q", "main");
        onMain(repository);
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ana");

        // Çakışacağı için başarısız olacak; fixture bunu bekliyor.
        repository.TryGit("merge", "yan");
        return repository;
    }

    // ------------------------------------------------------------ türler

    [Fact]
    public async Task BOTH_MODIFIED_dogru_okunuyor()
    {
        using TestRepository repository = Conflicting(
            branch => branch.WriteFile("f.txt", "a\nYAN\nc\n"),
            main => main.WriteFile("f.txt", "a\nANA\nc\n"));

        IReadOnlyList<ConflictedFile> files =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, Ct);

        ConflictedFile file = files.ShouldHaveSingleItem();
        file.Path.Value.ShouldBe("f.txt");
        file.Kind.ShouldBe(ConflictKind.BothModified);
        file.HasBase.ShouldBeTrue();
        file.HasOurs.ShouldBeTrue();
        file.HasTheirs.ShouldBeTrue();
        file.IsContentConflict.ShouldBeTrue();
    }

    [Fact]
    public async Task DELETED_BY_US_de_OURS_asamasi_YOK()
    {
        // Biz sildik, onlar değiştirdi. Üç yollu metin görünümü burada anlamsız:
        // birleştirilecek iki metin yok, verilecek bir karar var.
        using TestRepository repository = Conflicting(
            branch => branch.WriteFile("f.txt", "DEGISTI\n"),
            main => main.Git("rm", "-q", "f.txt"));

        IReadOnlyList<ConflictedFile> files =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, Ct);

        ConflictedFile file = files.ShouldHaveSingleItem();
        file.Kind.ShouldBe(ConflictKind.DeletedByUs);
        file.HasBase.ShouldBeTrue();
        file.HasOurs.ShouldBeFalse();
        file.HasTheirs.ShouldBeTrue();
        file.IsContentConflict.ShouldBeFalse();
    }

    [Fact]
    public async Task DELETED_BY_THEM_de_THEIRS_asamasi_YOK()
    {
        using TestRepository repository = Conflicting(
            branch => branch.Git("rm", "-q", "f.txt"),
            main => main.WriteFile("f.txt", "DEGISTI\n"));

        IReadOnlyList<ConflictedFile> files =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, Ct);

        ConflictedFile file = files.ShouldHaveSingleItem();
        file.Kind.ShouldBe(ConflictKind.DeletedByThem);
        file.HasOurs.ShouldBeTrue();
        file.HasTheirs.ShouldBeFalse();
    }

    [Fact]
    public async Task BOTH_ADDED_de_ORTAK_ATA_YOK()
    {
        using TestRepository repository = Conflicting(
            branch => branch.WriteFile("yeni.txt", "YAN\n"),
            main => main.WriteFile("yeni.txt", "ANA\n"));

        IReadOnlyList<ConflictedFile> files =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, Ct);

        ConflictedFile file = files.ShouldHaveSingleItem();
        file.Kind.ShouldBe(ConflictKind.BothAdded);
        file.HasBase.ShouldBeFalse("iki taraf da sıfırdan ekledi, ortak ata yok");
        file.HasOurs.ShouldBeTrue();
        file.HasTheirs.ShouldBeTrue();
    }

    [Fact]
    public async Task Cakisma_yokken_liste_BOS()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        IReadOnlyList<ConflictedFile> files =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, Ct);

        files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cakismayan_degisiklikler_LISTEYE_GIRMIYOR()
    {
        // Çakışan dosyanın yanında düz değişiklikler de olabilir; onlar buraya ait değil.
        using TestRepository repository = Conflicting(
            branch => branch.WriteFile("f.txt", "a\nYAN\nc\n"),
            main => main.WriteFile("f.txt", "a\nANA\nc\n"));

        repository.WriteFile("baska.txt", "alakasiz\n");
        repository.Git("add", "baska.txt");

        IReadOnlyList<ConflictedFile> files =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, Ct);

        files.ShouldHaveSingleItem().Path.Value.ShouldBe("f.txt");
    }

    // ------------------------------------------------------------ yol

    [Fact]
    public async Task TURKCE_ve_BOSLUKLU_yol_BOZULMUYOR()
    {
        // 🔴 ÖLÇÜLDÜ: `-z` olmadan git yolu C-tırnaklıyor (`şğüıöç.txt` →
        // `"\305\237\304\237…"`). Türkçe yollar sessizce bozulurdu.
        const string path = "bir dizin/şğüıöç dosyası.txt";

        using TestRepository repository = Conflicting(
            branch => branch.WriteFile(path, "a\nYAN\n"),
            main => main.WriteFile(path, "a\nANA\n"),
            baseFile: path,
            baseContent: "a\nb\n");

        IReadOnlyList<ConflictedFile> files =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, Ct);

        files.ShouldHaveSingleItem().Path.Value.ShouldBe(path);
    }

    // ------------------------------------------------------------ içerik

    [Fact]
    public async Task Uc_surum_de_AYRI_AYRI_okunuyor()
    {
        using TestRepository repository = Conflicting(
            branch => branch.WriteFile("f.txt", "a\nYAN\nc\n"),
            main => main.WriteFile("f.txt", "a\nANA\nc\n"));

        ConflictReader reader = await CreateReaderAsync();
        RepositoryPath path = RepositoryPath.Parse("f.txt");

        string? @base = await ReadTextAsync(reader, repository.Path, path, ConflictStage.Base);
        string? ours = await ReadTextAsync(reader, repository.Path, path, ConflictStage.Ours);
        string? theirs = await ReadTextAsync(reader, repository.Path, path, ConflictStage.Theirs);

        @base.ShouldBe("a\nb\nc\n");
        ours.ShouldBe("a\nANA\nc\n");
        theirs.ShouldBe("a\nYAN\nc\n");
    }

    [Fact]
    public async Task EKSIK_asama_null_donduruyor_BOS_METIN_DEGIL()
    {
        // 🔴 ÖLÇÜLDÜ: `git show :2:f.txt` eksik aşamada `fatal: … but not at stage 2`
        // veriyor. Hatayı yutup boş metin döndürmek "dosya boştu" gibi okunurdu; oysa
        // silinmiş bir dosyayla boş bir dosya kullanıcı için çok farklı şeyler.
        using TestRepository repository = Conflicting(
            branch => branch.WriteFile("f.txt", "DEGISTI\n"),
            main => main.Git("rm", "-q", "f.txt"));

        ConflictReader reader = await CreateReaderAsync();
        RepositoryPath path = RepositoryPath.Parse("f.txt");

        byte[]? ours = await reader.ReadStageAsync(repository.Path, path, ConflictStage.Ours, Ct);
        byte[]? theirs = await reader.ReadStageAsync(repository.Path, path, ConflictStage.Theirs, Ct);

        ours.ShouldBeNull("biz sildik — bu tarafta dosya YOK");
        theirs.ShouldNotBeNull();
    }

    [Fact]
    public async Task GERCEKTEN_BOS_dosya_null_DEGIL_bos_dizi()
    {
        // Yukarıdaki ayrımın diğer yarısı: boş içerik "yok" demek değil.
        using TestRepository repository = Conflicting(
            branch => branch.WriteFile("f.txt", string.Empty),
            main => main.WriteFile("f.txt", "a\nANA\nc\n"));

        ConflictReader reader = await CreateReaderAsync();

        byte[]? theirs = await reader.ReadStageAsync(
            repository.Path, RepositoryPath.Parse("f.txt"), ConflictStage.Theirs, Ct);

        theirs.ShouldNotBeNull();
        theirs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ikili_icerik_BAYT_olarak_korunuyor()
    {
        using TestRepository repository = Conflicting(
            branch => branch.WriteFile("f.txt", "a\nYAN\nc\n"),
            main => main.WriteFile("f.txt", "a\nANA\nc\n"));

        ConflictReader reader = await CreateReaderAsync();

        byte[]? ours = await reader.ReadStageAsync(
            repository.Path, RepositoryPath.Parse("f.txt"), ConflictStage.Ours, Ct);

        ours.ShouldNotBeNull();
        ours.ShouldBe(Encoding.UTF8.GetBytes("a\nANA\nc\n"));
    }

    [Fact]
    public async Task Cakismayan_dosyanin_asamasi_null()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        ConflictReader reader = await CreateReaderAsync();

        byte[]? ours = await reader.ReadStageAsync(
            repository.Path, RepositoryPath.Parse("README.md"), ConflictStage.Ours, Ct);

        ours.ShouldBeNull();
    }

    private static async Task<string?> ReadTextAsync(
        ConflictReader reader,
        string workingDirectory,
        RepositoryPath path,
        ConflictStage stage)
    {
        byte[]? bytes = await reader.ReadStageAsync(workingDirectory, path, stage, Ct);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }
}
