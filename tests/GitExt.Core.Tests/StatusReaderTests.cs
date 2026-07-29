using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P02-T10 — Çalışma dizini durumu. Fazın en karmaşık ayrıştırıcısı.
/// Tüm satır tipleri gerçek <c>git</c> ile üretilip doğrulanıyor.
/// </summary>
public class StatusReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<StatusReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new StatusReader(new GitProcessRunner(executable));
    }

    [Fact]
    public async Task Temiz_depo_bos_liste_dondurur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        status.IsClean.ShouldBeTrue();
        status.Entries.ShouldBeEmpty();
        status.BranchName.ShouldBe("main");
        status.IsDetached.ShouldBeFalse();
        status.IsUnborn.ShouldBeFalse();
        status.Head.IsFull.ShouldBeTrue();
    }

    [Fact]
    public async Task Dogmamis_depo_initial_olarak_raporlanir()
    {
        // "# branch.oid (initial)" — ölçüldü.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("yeni.txt", "içerik\n");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        status.IsUnborn.ShouldBeTrue();
        status.Head.IsEmpty.ShouldBeTrue();
        status.BranchName.ShouldBe("main");
        status.Untracked.ShouldHaveSingleItem().Path.Value.ShouldBe("yeni.txt");
    }

    [Fact]
    public async Task Detached_HEAD_raporlanir()
    {
        // "# branch.head (detached)" — ölçüldü.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("commit", "--allow-empty", "-m", "ikinci");
        repository.Git("checkout", "--detach", "HEAD~1");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        status.IsDetached.ShouldBeTrue();
        status.BranchName.ShouldBeNull();
    }

    [Fact]
    public async Task Staged_ve_unstaged_degisiklikler_ayrilir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "bir\n");
        repository.WriteFile("b.txt", "iki\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "base");

        // a.txt: stage'lenmiş değişiklik
        repository.WriteFile("a.txt", "değişti ve eklendi\n");
        repository.Git("add", "a.txt");

        // b.txt: stage'lenmemiş değişiklik
        repository.WriteFile("b.txt", "değişti ama eklenmedi\n");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        FileStatus a = status.Entries.Single(e => e.Path.Value == "a.txt");
        a.StagedChange.ShouldBe(FileChangeKind.Modified);
        a.UnstagedChange.ShouldBe(FileChangeKind.Unmodified);
        a.IsStaged.ShouldBeTrue();

        FileStatus b = status.Entries.Single(e => e.Path.Value == "b.txt");
        b.StagedChange.ShouldBe(FileChangeKind.Unmodified);
        b.UnstagedChange.ShouldBe(FileChangeKind.Modified);
        b.IsUnstaged.ShouldBeTrue();
    }

    [Fact]
    public async Task Ekleme_ve_silme_dogru_siniflandirilir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("silinecek.txt", "içerik\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "base");

        repository.Git("rm", "-q", "silinecek.txt");
        repository.WriteFile("eklenen.txt", "yeni\n");
        repository.Git("add", "eklenen.txt");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        status.Entries.Single(e => e.Path.Value == "silinecek.txt")
            .StagedChange.ShouldBe(FileChangeKind.Deleted);
        status.Entries.Single(e => e.Path.Value == "eklenen.txt")
            .StagedChange.ShouldBe(FileChangeKind.Added);
    }

    [Fact]
    public async Task Rename_kaynak_yolu_ayri_kayittan_okunur()
    {
        // KRİTİK: -z modunda rename girdisi İKİ NUL kaydına yayılır (ölçüldü).
        // "2 …" satırı yeni yolla biter, bir SONRAKİ kayıt kaynak yoldur.
        // Tek kayıt varsayılsaydı sonraki tüm girdiler kayar ve veri sessizce bozulurdu.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("eski.txt", "yeterince uzun içerik\nikinci satır\nüçüncü satır\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "base");

        repository.Git("mv", "eski.txt", "yeni.txt");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        FileStatus renamed = status.Entries.ShouldHaveSingleItem();
        renamed.Path.Value.ShouldBe("yeni.txt");
        renamed.OriginalPath.ShouldNotBeNull();
        renamed.OriginalPath!.Value.Value.ShouldBe("eski.txt");
        renamed.StagedChange.ShouldBe(FileChangeKind.Renamed);
        renamed.SimilarityScore.ShouldBe(100);
    }

    [Fact]
    public async Task Rename_sonrasi_gelen_girdiler_kaymaz()
    {
        // Rename'in ikinci kaydı yanlış tüketilirse sonraki girdiler bozulur.
        // Bu test tam olarak o hizalamayı doğruluyor.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("eski.txt", "uzun içerik\nikinci\nüçüncü\n");
        repository.WriteFile("sonraki.txt", "içerik\n");
        repository.WriteFile("ucuncu.txt", "içerik\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "base");

        repository.Git("mv", "eski.txt", "yeni.txt");
        repository.WriteFile("sonraki.txt", "değişti\n");
        repository.Git("add", "sonraki.txt");
        repository.WriteFile("ucuncu.txt", "de değişti\n");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        status.Entries.Count.ShouldBe(3);
        status.Entries.Select(e => e.Path.Value)
            .ShouldBe(["yeni.txt", "sonraki.txt", "ucuncu.txt"], ignoreOrder: true);

        status.Entries.Single(e => e.Path.Value == "sonraki.txt")
            .StagedChange.ShouldBe(FileChangeKind.Modified);
        status.Entries.Single(e => e.Path.Value == "ucuncu.txt")
            .UnstagedChange.ShouldBe(FileChangeKind.Modified);
    }

    [Fact]
    public async Task Conflict_turleri_ayirt_edilir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("ortak.txt", "temel\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "base");

        repository.Git("checkout", "-q", "-b", "yan");
        repository.WriteFile("ortak.txt", "yan sürüm\n");
        repository.WriteFile("ikisi.txt", "yandan\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "yan");

        repository.Git("checkout", "-q", "main");
        repository.WriteFile("ortak.txt", "ana sürüm\n");
        repository.WriteFile("ikisi.txt", "anadan\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ana");

        // merge çakışacak; başarısız çıkış kodu bekleniyor.
        try
        {
            repository.Git("merge", "yan");
        }
        catch (InvalidOperationException)
        {
            // Beklenen: conflict.
        }

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        status.Conflicted.Count().ShouldBe(2);

        status.Entries.Single(e => e.Path.Value == "ortak.txt")
            .Conflict.ShouldBe(ConflictKind.BothModified);
        status.Entries.Single(e => e.Path.Value == "ikisi.txt")
            .Conflict.ShouldBe(ConflictKind.BothAdded);
    }

    [Theory]
    [InlineData("UU", ConflictKind.BothModified)]
    [InlineData("AA", ConflictKind.BothAdded)]
    [InlineData("DD", ConflictKind.BothDeleted)]
    [InlineData("AU", ConflictKind.AddedByUs)]
    [InlineData("UA", ConflictKind.AddedByThem)]
    [InlineData("DU", ConflictKind.DeletedByUs)]
    [InlineData("UD", ConflictKind.DeletedByThem)]
    [InlineData("..", ConflictKind.None)]
    public void Conflict_XY_ciftleri_eslenir(string xy, ConflictKind expected)
    {
        StatusReader.ParseConflict(xy).ShouldBe(expected);
    }

    [Theory]
    [InlineData("+2 -0", 2, 0)]
    [InlineData("+0 -3", 0, 3)]
    [InlineData("+5 -7", 5, 7)]
    [InlineData("+0 -0", 0, 0)]
    public void Ahead_behind_basligi_ayristirilir(string value, int ahead, int behind)
    {
        UpstreamTracking tracking = StatusReader.ParseAheadBehind(value);

        tracking.Ahead.ShouldBe(ahead);
        tracking.Behind.ShouldBe(behind);
    }

    [Fact]
    public async Task Upstream_ve_ahead_behind_basliklardan_okunur()
    {
        using TestRepository remote = TestRepository.CreateBare();
        using TestRepository local = TestRepository.CreateWithSingleCommit();

        local.Git("remote", "add", "origin", remote.Path);
        local.Git("push", "-q", "origin", "main");
        local.Git("branch", "--set-upstream-to=origin/main", "main");
        local.Git("commit", "--allow-empty", "-m", "ileri");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(local.Path, cancellationToken: Ct);

        status.Upstream.ShouldBe("origin/main");
        status.Tracking.Ahead.ShouldBe(1);
        status.Tracking.Behind.ShouldBe(0);
    }

    [Fact]
    public async Task Ignored_dosyalar_yalnizca_istenince_gelir()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile(".gitignore", "*.log\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "base");
        repository.WriteFile("debug.log", "günlük\n");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus without = await reader.ReadAsync(repository.Path, cancellationToken: Ct);
        without.Ignored.ShouldBeEmpty();

        WorkingTreeStatus with = await reader.ReadAsync(
            repository.Path, includeIgnored: true, cancellationToken: Ct);
        with.Ignored.ShouldContain(e => e.Path.Value == "debug.log");
    }

    [Fact]
    public async Task Bosluklu_ve_unicode_dosya_adlari_bozulmadan_okunur()
    {
        // Yol alanı boşluk içerebilir; sınırlı bölme (Split limit) bu yüzden şart.
        const string awkward = "klasör adı/çalışma günlüğü ÖĞÜŞİ.md";

        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "base");
        repository.WriteFile(awkward, "içerik\n");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        status.Untracked.ShouldHaveSingleItem().Path.Value.ShouldBe(awkward);
    }

    [Fact]
    public async Task Submodule_durumu_okunur()
    {
        using TestRepository inner = TestRepository.CreateWithSingleCommit();
        using TestRepository super = TestRepository.CreateWithSingleCommit();

        super.AddSubmodule(inner, "altmodul");
        super.Git("commit", "-m", "submodule eklendi");

        // Submodule içinde takip edilmeyen bir değişiklik oluştur.
        File.WriteAllText(Path.Combine(super.Path, "altmodul", "yeni.txt"), "içerik\n");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(super.Path, cancellationToken: Ct);

        FileStatus submodule = status.Entries.ShouldHaveSingleItem();
        submodule.Path.Value.ShouldBe("altmodul");
        submodule.Submodule.ShouldNotBeNull();
        submodule.Submodule!.Value.HasUntrackedChanges.ShouldBeTrue();
    }

    [Fact]
    public async Task Normal_dosyada_submodule_alani_null_olur()
    {
        // "N..." → submodule değil.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a.txt", "a\n");

        StatusReader reader = await CreateReaderAsync();

        WorkingTreeStatus status = await reader.ReadAsync(repository.Path, cancellationToken: Ct);

        status.Entries.ShouldHaveSingleItem().Submodule.ShouldBeNull();
    }
}
