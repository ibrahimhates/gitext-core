using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T04 — Diff seçenekleri, gerçek <c>git</c>'e karşı.
/// </summary>
public class DiffOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<DiffReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new DiffReader(new GitProcessRunner(executable));
    }

    private static CommitId Head(TestRepository repository) =>
        CommitId.Parse(repository.Git("rev-parse", "HEAD").Trim());

    private static string Lines(int count, params (int Index, string Text)[] overrides)
    {
        string[] lines = [.. Enumerable.Range(1, count).Select(i => $"satır {i}")];

        foreach ((int index, string text) in overrides)
        {
            lines[index] = text;
        }

        return string.Join('\n', lines) + "\n";
    }

    [Fact]
    public async Task Baglam_satiri_sayisi_ayarlanir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", Lines(20));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("a.txt", Lines(20, (9, "DEĞİŞTİ")));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();
        CommitId head = Head(repository);

        int ContextCount(IReadOnlyList<FileDiff> diffs) =>
            diffs.Single().Hunks.Sum(h => h.Lines.Count(l => l.Kind == DiffLineKind.Context));

        int none = ContextCount(await reader.ReadCommitAsync(
            repository.Path, head, new DiffOptions { ContextLines = 0 }, Ct));

        int one = ContextCount(await reader.ReadCommitAsync(
            repository.Path, head, new DiffOptions { ContextLines = 1 }, Ct));

        int five = ContextCount(await reader.ReadCommitAsync(
            repository.Path, head, new DiffOptions { ContextLines = 5 }, Ct));

        none.ShouldBe(0);
        one.ShouldBe(2);
        five.ShouldBe(10);
    }

    [Fact]
    public async Task Sifir_baglamda_tek_satirlik_hunk_basligi_dogru_okunur()
    {
        // ÖLÇÜLDÜ: -U0 başlığı tek satırlık biçime düşürüyor (`@@ -4 +4 @@`), uzunluk YOK.
        // Varsayılanı 0 almak satır numaralarını kaydırırdı.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", Lines(8));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("a.txt", Lines(8, (3, "DEĞİŞTİ")));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        DiffHunk hunk = (await reader.ReadCommitAsync(
                repository.Path, Head(repository), new DiffOptions { ContextLines = 0 }, Ct))
            .Single().Hunks.Single();

        hunk.OldLength.ShouldBe(1);
        hunk.NewLength.ShouldBe(1);
        hunk.Lines.Single(l => l.Kind == DiffLineKind.Added).NewLineNumber.ShouldBe(4);
    }

    [Fact]
    public async Task Bosluk_yoksayilinca_dosya_listeden_de_duser()
    {
        // ÖLÇÜLDÜ ve KRİTİK: -w yalnızca yamayı boşaltmıyor, dosyayı HAM bölümden de
        // düşürüyor. Öyle olmasaydı ham kayıt sayısı ile yama bloğu sayısı uyuşmaz ve
        // ayrıştırıcı hata fırlatırdı.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("bosluk.txt", "a\nb\nc\n");
        repository.WriteFile("gercek.txt", "x\ny\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("bosluk.txt", "a   \n   b\nc\n");
        repository.WriteFile("gercek.txt", "x\nY\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();
        CommitId head = Head(repository);

        IReadOnlyList<FileDiff> all = await reader.ReadCommitAsync(repository.Path, head, cancellationToken: Ct);

        IReadOnlyList<FileDiff> ignored = await reader.ReadCommitAsync(
            repository.Path, head, new DiffOptions { Whitespace = WhitespaceMode.IgnoreAll }, Ct);

        all.Select(d => d.Path.Value).ShouldBe(["bosluk.txt", "gercek.txt"], ignoreOrder: true);
        ignored.Single().Path.Value.ShouldBe("gercek.txt");
    }

    [Fact]
    public async Task Yeniden_adlandirma_esigi_uygulanir()
    {
        // ÖLÇÜLDÜ: %69 benzerlikli dosyada -M50% rename buluyor, -M90% bulmuyor.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("kaynak.txt", Lines(20));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        File.Delete(Path.Combine(repository.Path, "kaynak.txt"));
        repository.WriteFile("hedef.txt", Lines(20,
            (0, "A"), (1, "B"), (2, "C"), (3, "D"), (4, "E"), (5, "F")));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "taşındı");

        DiffReader reader = await CreateReaderAsync();
        CommitId head = Head(repository);

        IReadOnlyList<FileDiff> loose = await reader.ReadCommitAsync(
            repository.Path, head, new DiffOptions { RenameThreshold = 50 }, Ct);

        IReadOnlyList<FileDiff> strict = await reader.ReadCommitAsync(
            repository.Path, head, new DiffOptions { RenameThreshold = 90 }, Ct);

        loose.Single().Change.ShouldBe(FileChangeKind.Renamed);

        strict.Count.ShouldBe(2);
        strict.ShouldContain(d => d.Change == FileChangeKind.Added);
        strict.ShouldContain(d => d.Change == FileChangeKind.Deleted);
    }

    [Fact]
    public async Task Kopyalama_tespiti_find_copies_harder_GEREKTIRIR()
    {
        // ÖLÇÜLDÜ: -C tek başına, DEĞİŞTİRİLMEMİŞ bir dosyadan yapılan kopyayı bulamıyor
        // (durum A kalıyor). --find-copies-harder ile C100 oluyor.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("orijinal.txt", Lines(15));
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        File.Copy(
            Path.Combine(repository.Path, "orijinal.txt"),
            Path.Combine(repository.Path, "kopya.txt"));

        repository.Git("add", "-A");
        repository.Git("commit", "-m", "kopyalandı");

        DiffReader reader = await CreateReaderAsync();
        CommitId head = Head(repository);

        FileDiff withoutHarder = (await reader.ReadCommitAsync(
                repository.Path, head, new DiffOptions { DetectCopies = true }, Ct))
            .Single();

        FileDiff withHarder = (await reader.ReadCommitAsync(
                repository.Path,
                head,
                new DiffOptions { DetectCopies = true, FindCopiesHarder = true },
                Ct))
            .Single();

        withoutHarder.Change.ShouldBe(FileChangeKind.Added);

        withHarder.Change.ShouldBe(FileChangeKind.Copied);
        withHarder.OldPath!.Value.Value.ShouldBe("orijinal.txt");
        withHarder.Path.Value.ShouldBe("kopya.txt");
    }

    [Fact]
    public async Task Bos_satir_farklari_yoksayilabilir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "bir\niki\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("a.txt", "bir\n\n\niki\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "boş satırlar");

        DiffReader reader = await CreateReaderAsync();
        CommitId head = Head(repository);

        IReadOnlyList<FileDiff> normal = await reader.ReadCommitAsync(repository.Path, head, cancellationToken: Ct);

        IReadOnlyList<FileDiff> ignored = await reader.ReadCommitAsync(
            repository.Path, head, new DiffOptions { IgnoreBlankLines = true }, Ct);

        normal.Single().AddedLines.ShouldBe(2);

        // ÖLÇÜLDÜ: --ignore-blank-lines dosyayı listeden DÜŞÜRMÜYOR; ham bölümde kalıyor
        // ama yama bloğu üretilmiyor. (-w'den farklı: orada dosya aynılaşırsa iki bölümden
        // de düşüyor.) Ayrıştırıcı bu yüzden blob kimliğiyle eşleyip hunk'sız döndürüyor.
        // Böyle dosyaların listede gizlenip gizlenmeyeceği bir arayüz kararı (P04-T08).
        FileDiff unchanged = ignored.Single();
        unchanged.Path.Value.ShouldBe("a.txt");
        unchanged.HasHunks.ShouldBeFalse();
    }

    [Fact]
    public async Task Esikler_gecerli_araliga_sikistirilir()
    {
        // Kullanıcı arayüzünden gelen bozuk bir değer git'i hata verdirmemeli.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.WriteFile("README.md", "# test\ndeğişti\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ikinci");

        DiffReader reader = await CreateReaderAsync();

        IReadOnlyList<FileDiff> diffs = await reader.ReadCommitAsync(
            repository.Path,
            Head(repository),
            new DiffOptions { RenameThreshold = 5000, CopyThreshold = -3, DetectCopies = true },
            Ct);

        diffs.ShouldNotBeEmpty();
    }
}
