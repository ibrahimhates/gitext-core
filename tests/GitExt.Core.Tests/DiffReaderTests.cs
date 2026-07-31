using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T03 — Diff komut sarmalayıcıları, gerçek <c>git</c>'e karşı.
/// </summary>
public class DiffReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<DiffReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new DiffReader(new GitProcessRunner(executable));
    }

    private static CommitId Head(TestRepository repository, string revision = "HEAD") =>
        CommitId.Parse(repository.Git("rev-parse", revision).Trim());

    /// <summary>Kök → normal → (yan dal) → merge içeren bir depo kurar.</summary>
    private static TestRepository CreateWithMerge()
    {
        TestRepository repository = TestRepository.CreateEmpty();

        repository.WriteFile("kok.txt", "kök içerik\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "kök");

        repository.Git("checkout", "-q", "-b", "yan");
        repository.WriteFile("yan.txt", "yan dal\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "yan değişiklik");

        repository.Git("checkout", "-q", "-");
        repository.WriteFile("ana.txt", "ana dal\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ana değişiklik");

        repository.Git("merge", "-q", "--no-ff", "yan", "-m", "birleştir");

        return repository;
    }

    [Fact]
    public async Task Normal_committe_kendi_degisikligi_okunur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.WriteFile("README.md", "# test\ndeğişti\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(repository.Path, Head(repository), cancellationToken: Ct);

        diffs.Single().Path.Value.ShouldBe("README.md");
    }

    [Fact]
    public async Task Kok_committe_cokmeden_tum_dosyalar_gelir()
    {
        // ÖLÇÜLDÜ: `git diff <kök>^ <kök>` → "fatal: ambiguous argument". `--root` şart.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("bir.txt", "a\n");
        repository.WriteFile("iki.txt", "b\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "kök");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path, Head(repository), cancellationToken: Ct);

        diffs.Select(d => d.Path.Value).ShouldBe(["bir.txt", "iki.txt"], ignoreOrder: true);
        diffs.ShouldAllBe(d => d.Change == FileChangeKind.Added);
    }

    [Fact]
    public async Task Merge_committe_BOS_DONMEZ()
    {
        // Bu testin bütün varlık sebebi ölçülmüş bir tuzak: düz `git show <merge>` temiz bir
        // merge'de HİÇ çıktı vermiyor (`--cc` de öyle). Kullanıcı bunu hata sanardı.
        using TestRepository repository = CreateWithMerge();

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path, Head(repository), cancellationToken: Ct);

        diffs.ShouldNotBeEmpty();

        // Varsayılan ilk ebeveyn: merge'in ana hatta GETİRDİĞİ şey, yani yan daldaki dosya.
        diffs.ShouldContain(d => d.Path.Value == "yan.txt");
    }

    [Fact]
    public async Task Merge_ebeveyni_secilebilir()
    {
        using TestRepository repository = CreateWithMerge();

        DiffReader reader = await CreateReaderAsync();
        CommitId merge = Head(repository);

        IReadOnlyList<FileDiff> first = await reader.ReadCommitAsync(
            repository.Path, merge, new DiffOptions { MergeParent = 1 }, Ct);

        IReadOnlyList<FileDiff> second = await reader.ReadCommitAsync(
            repository.Path, merge, new DiffOptions { MergeParent = 2 }, Ct);

        // İlk ebeveyne göre yan dalın getirdiği, ikinci ebeveyne göre ana dalın getirdiği görünür.
        first.ShouldContain(d => d.Path.Value == "yan.txt");
        second.ShouldContain(d => d.Path.Value == "ana.txt");

        first.Select(d => d.Path.Value).ShouldNotBe(second.Select(d => d.Path.Value));
    }

    [Fact]
    public async Task Iki_revizyon_arasi_okunur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        repository.WriteFile("b.txt", "iki\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        repository.WriteFile("c.txt", "üç\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "üçüncü");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadBetweenAsync(
            repository.Path, "HEAD~2", "HEAD", cancellationToken: Ct);

        diffs.Select(d => d.Path.Value).ShouldBe(["b.txt", "c.txt"], ignoreOrder: true);
    }

    [Fact]
    public async Task Stagelenmis_ve_stagelenmemis_degisiklikler_ayri_okunur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        repository.WriteFile("stage.txt", "stage'lenecek\n");
        repository.Git("add", "stage.txt");

        repository.WriteFile("calisma.txt", "stage'lenmemiş\n");
        repository.Git("add", "-N", "calisma.txt");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> staged = await reader.ReadStagedAsync(repository.Path, cancellationToken: Ct);
        IReadOnlyList<FileDiff> unstaged = await reader.ReadUnstagedAsync(repository.Path, cancellationToken: Ct);

        staged.Select(d => d.Path.Value).ShouldContain("stage.txt");
        unstaged.Select(d => d.Path.Value).ShouldContain("calisma.txt");
        unstaged.Select(d => d.Path.Value).ShouldNotContain("stage.txt");
    }

    [Fact]
    public async Task Calisma_dizini_diffinde_yeni_blob_bos_kalir()
    {
        // ÖLÇÜLDÜ: çalışma dizini içeriği henüz blob değil, `--raw` sıfır kimlik veriyor
        // (`:100644 100644 d614168 0000000 M`). Bunu geçerli bir kimlik saymak yanıltıcı olur.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        repository.WriteFile("README.md", "# test\ndeğişti\n");

        DiffReader reader = await CreateReaderAsync();

        FileDiff diff = (await reader.ReadUnstagedAsync(repository.Path, cancellationToken: Ct))
            .Single(d => d.Path.Value == "README.md");

        diff.OldBlob.IsEmpty.ShouldBeFalse();
        diff.NewBlob.IsEmpty.ShouldBeTrue();
        diff.IsModeOnlyChange.ShouldBeFalse();
    }

    [Fact]
    public async Task Degisiklik_yoksa_bos_liste_doner()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        DiffReader reader = await CreateReaderAsync();

        (await reader.ReadUnstagedAsync(repository.Path, cancellationToken: Ct)).ShouldBeEmpty();
        (await reader.ReadStagedAsync(repository.Path, cancellationToken: Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Yeniden_adlandirma_tespiti_kapatilabilir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("eski.txt", "taşınacak içerik\nikinci satır\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.Git("mv", "eski.txt", "yeni.txt");
        repository.Git("commit", "-m", "taşındı");

        DiffReader reader = await CreateReaderAsync();
        CommitId head = Head(repository);

        IReadOnlyList<FileDiff> withDetection = await reader.ReadCommitAsync(
            repository.Path, head, new DiffOptions { DetectRenames = true }, Ct);

        IReadOnlyList<FileDiff> without = await reader.ReadCommitAsync(
            repository.Path, head, new DiffOptions { DetectRenames = false }, Ct);

        withDetection.Single().Change.ShouldBe(FileChangeKind.Renamed);

        // ÖLÇÜLDÜ: tespit git'te VARSAYILAN OLARAK AÇIK — `-M`'i atlamak kapatmıyor.
        // Kapatmak `--no-renames` gerektiriyor; bu test o hatayı yakaladı.
        // Tespit kapalıyken aynı değişiklik bir ekleme + bir silme olarak görünür.
        without.Count.ShouldBe(2);
        without.ShouldContain(d => d.Change == FileChangeKind.Added);
        without.ShouldContain(d => d.Change == FileChangeKind.Deleted);
    }

    [Fact]
    public async Task Bos_commit_kimligi_reddedilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        DiffReader reader = await CreateReaderAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await reader.ReadCommitAsync(repository.Path, default, cancellationToken: Ct));
    }
}
