using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T14 — reflog tarayıcısı.
/// </summary>
/// <remarks>
/// Bu okuyucu fazın <b>sigortası</b>: Faz 07'deki her işlem geçmişi yeniden yazıyor ve
/// kullanıcı kaybettiğini buradan geri alacak.
/// </remarks>
public class ReflogReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ReflogReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new ReflogReader(new GitProcessRunner(executable));
    }

    // ----------------------------------------------------------- ayrıştırma

    [Fact]
    public void Alanlar_NUL_ile_ayriliyor()
    {
        string output =
            "\u001eabc123\0HEAD@{0}\0reset: moving to HEAD~1\0konu\01786083979\0Yazar\n"
            + "\u001edef456\0HEAD@{1}\0commit: c3\0c3\01786083978\0Yazar\n";

        IReadOnlyList<ReflogEntry> entries = ReflogReader.Parse(output);

        entries.Count.ShouldBe(2);
        entries[0].ObjectId.ShouldBe("abc123");
        entries[0].Selector.ShouldBe("HEAD@{0}");
        entries[0].Message.ShouldBe("reset: moving to HEAD~1");
        entries[0].Subject.ShouldBe("konu");
        entries[0].AuthorName.ShouldBe("Yazar");
        entries[1].ObjectId.ShouldBe("def456");
    }

    [Fact]
    public void SEKME_iceren_commit_mesaji_alanlari_KAYDIRMIYOR()
    {
        // 🔴 ÖLÇÜLDÜ: `%s` commit konusundaki sekmeyi olduğu gibi basıyor. TAB ayırıcı
        // kullanılsaydı bu satır fazladan alan üretir ve yazar adı yanlış okunurdu.
        string output = "\u001eabc123\0HEAD@{0}\0commit: x\0konu\tsekmeli\01786083979\0Yazar\n";

        IReadOnlyList<ReflogEntry> entries = ReflogReader.Parse(output);

        entries.Count.ShouldBe(1);
        entries[0].Subject.ShouldBe("konu\tsekmeli");
        entries[0].AuthorName.ShouldBe("Yazar");
    }

    [Fact]
    public void BOS_ALANLI_kayit_IKIYE_BOLUNMUYOR()
    {
        // 🔴 ÖLÇÜLDÜ: kayıt ayracı NUL ÇİFTİ iken, boş bir alan (boş commit mesajı) iki
        // NUL'u yan yana getiriyor ve ayraçtan ayırt edilemiyordu — kayıt ortasından
        // ikiye bölünüyordu. Gerçek çıktıda görüldü: `<sha>\0\0T\0\0`.
        // Ayraç artık \x1e (ASCII Record Separator).
        string output =
            "\u001eabc123\0HEAD@{0}\0commit: x\0\01786083979\0Yazar\n"
            + "\u001edef456\0HEAD@{1}\0commit: y\0konu\01786083978\0Yazar\n";

        IReadOnlyList<ReflogEntry> entries = ReflogReader.Parse(output);

        entries.Count.ShouldBe(2, "boş konu kaydı bölmemeli");
        entries[0].Subject.ShouldBeEmpty();
        entries[0].AuthorName.ShouldBe("Yazar");
        entries[1].ObjectId.ShouldBe("def456");
    }

    [Fact]
    public void Eksik_alanli_kayit_UYDURULMUYOR()
    {
        string output = "\u001eabc123\0HEAD@{0}\n" + "\u001edef456\0HEAD@{1}\0commit: c\0k\01\0Y\n";

        IReadOnlyList<ReflogEntry> entries = ReflogReader.Parse(output);

        entries.Count.ShouldBe(1);
        entries[0].ObjectId.ShouldBe("def456");
    }

    [Theory]
    [InlineData("commit: ilk", ReflogAction.Commit)]
    [InlineData("commit (initial): ilk", ReflogAction.Commit)]
    [InlineData("commit (amend): duzeltme", ReflogAction.Amend)]
    [InlineData("reset: moving to HEAD~1", ReflogAction.Reset)]
    [InlineData("checkout: moving from main to dal", ReflogAction.Checkout)]
    [InlineData("rebase (finish): returning to refs/heads/main", ReflogAction.Rebase)]
    [InlineData("cherry-pick: bir sey", ReflogAction.CherryPick)]
    [InlineData("revert: bir sey", ReflogAction.Revert)]
    [InlineData("merge dal: Fast-forward", ReflogAction.Merge)]
    [InlineData("branch: Created from HEAD", ReflogAction.Branch)]
    [InlineData("pull: Fast-forward", ReflogAction.Pull)]
    [InlineData("bilinmeyen sey", ReflogAction.Other)]
    public void Eylem_metinden_cikariliyor(string message, ReflogAction expected) =>
        ReflogReader.ClassifyAction(message).ShouldBe(expected);

    [Fact]
    public void Geri_alma_komutu_SECICI_degil_SHA_kullaniyor()
    {
        // ⚠️ `HEAD@{3}` KAYAN bir referans: yeni bir işlem reflog'a girdi eklediğinde
        // başka bir commit'i gösterir. Kullanıcı komutu kopyalayıp beş dakika sonra
        // çalıştırırsa yanlış yere dönerdi.
        ReflogEntry entry = new()
        {
            ObjectId = "0123456789abcdef",
            Selector = "HEAD@{3}",
            Message = "commit: x",
        };

        entry.RecoveryCommand.ShouldBe("git reset --hard 0123456789abcdef");
        entry.RecoveryCommand.ShouldNotContain("@{");
        entry.ShortId.ShouldBe("0123456");
    }

    // ----------------------------------------------------------- gerçek git

    [Fact]
    public async Task Gercek_depoda_islemler_sirayla_okunuyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");
        repository.Commit("ikinci");

        IReadOnlyList<ReflogEntry> entries =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, "HEAD", cancellationToken: Ct);

        entries.ShouldNotBeEmpty();

        // En yeni girdi başta.
        entries[0].Action.ShouldBe(ReflogAction.Commit);
        entries[0].Timestamp.ShouldBeGreaterThan(DateTimeOffset.UnixEpoch);
        entries[0].AuthorName.ShouldBe("gitext-core tests");
    }

    [Fact]
    public async Task RESET_ile_kaybolan_commit_reflogda_BULUNUYOR()
    {
        // Fazın en önemli senaryosu: kullanıcı --hard ile geri gitti, commit'i geri istiyor.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");
        repository.Commit("kaybolacak");

        string lost = repository.Git("rev-parse", "HEAD").Trim();
        repository.Git("reset", "--hard", "HEAD~1");

        IReadOnlyList<ReflogEntry> entries =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, "HEAD", cancellationToken: Ct);

        ReflogEntry? found = entries.FirstOrDefault(entry => entry.ObjectId == lost);

        found.ShouldNotBeNull("kaybolan commit reflog'da olmalı");
        found.IsUnreachable.ShouldBeTrue("artık hiçbir ref'ten erişilemiyor");
    }

    [Fact]
    public async Task ERISILEBILIR_commitler_kayip_diye_ISARETLENMIYOR()
    {
        // 🔴 İlk yazımda erişilebilirlik `rev-list --all --no-walk=unsorted HEAD` ile
        // hesaplanıyordu; `--no-walk` geçmişi GEZMEDİĞİ için yalnızca uç dönüyordu ve
        // ilk commit'ten sonraki HER eski girdi "kayıp" görünüyordu.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        for (int index = 0; index < 3; index++)
        {
            repository.WriteFile($"f{index}.txt", "x\n");
            repository.Git("add", ".");
            repository.Commit($"c{index}");
        }

        IReadOnlyList<ReflogEntry> entries =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, "HEAD", cancellationToken: Ct);

        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(entry => !entry.IsUnreachable);
    }

    [Fact]
    public async Task Reflogu_olmayan_depo_BOS_liste_donduruyor()
    {
        using TestRepository repository = TestRepository.CreateEmpty();

        IReadOnlyList<ReflogEntry> entries =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, "HEAD", cancellationToken: Ct);

        entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Turkce_yazar_adi_ve_konu_BOZULMUYOR()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.Git("config", "--local", "user.name", "Şükrü Çağrı");
        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "a.txt");
        repository.Commit("ğüşiöç değişikliği");

        IReadOnlyList<ReflogEntry> entries =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, "HEAD", cancellationToken: Ct);

        entries[0].AuthorName.ShouldBe("Şükrü Çağrı");
        entries[0].Subject.ShouldBe("ğüşiöç değişikliği");
    }

    [Fact]
    public async Task Tum_reflar_okunabiliyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("branch", "yan");

        IReadOnlyList<ReflogEntry> entries = await (await CreateReaderAsync()).ReadAsync(repository.Path, cancellationToken: Ct);

        entries.ShouldContain(entry => entry.Selector.Contains("yan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Limit_uygulaniyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        for (int index = 0; index < 5; index++)
        {
            repository.Commit($"c{index}");
        }

        IReadOnlyList<ReflogEntry> entries =
            await (await CreateReaderAsync()).ReadAsync(repository.Path, "HEAD", limit: 3, cancellationToken: Ct);

        entries.Count.ShouldBe(3);
    }
}
