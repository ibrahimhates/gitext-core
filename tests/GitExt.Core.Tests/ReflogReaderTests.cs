using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T14 — the reflog browser.
/// </summary>
/// <remarks>
/// This reader is the <b>insurance policy</b> of the phase: every operation in Phase 07 rewrites
/// history, and this is where the user gets back what they lost.
/// </remarks>
public class ReflogReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ReflogReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new ReflogReader(new GitProcessRunner(executable));
    }

    // -------------------------------------------------------------- parsing

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
        // 🔴 MEASURED: `%s` prints the tab in the commit subject as-is. Had a TAB separator been
        // used, this line would produce an extra field and the author name would be read wrongly.
        string output = "\u001eabc123\0HEAD@{0}\0commit: x\0konu\tsekmeli\01786083979\0Yazar\n";

        IReadOnlyList<ReflogEntry> entries = ReflogReader.Parse(output);

        entries.Count.ShouldBe(1);
        entries[0].Subject.ShouldBe("konu\tsekmeli");
        entries[0].AuthorName.ShouldBe("Yazar");
    }

    [Fact]
    public void BOS_ALANLI_kayit_IKIYE_BOLUNMUYOR()
    {
        // 🔴 MEASURED: while the record separator was a NUL PAIR, an empty field (an empty commit
        // message) put two NULs side by side and could not be told apart from the separator — the
        // record was split in two from the middle. Seen in real output: `<sha>\0\0T\0\0`.
        // The separator is now \x1e (ASCII Record Separator).
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
        // ⚠️ `HEAD@{3}` is a SLIDING reference: as soon as a new operation adds an entry to the
        // reflog it points at a different commit. If the user copies the command and runs it five
        // minutes later they would go back to the wrong place.
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

    // ------------------------------------------------------------- real git

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

        // The newest entry comes first.
        entries[0].Action.ShouldBe(ReflogAction.Commit);
        entries[0].Timestamp.ShouldBeGreaterThan(DateTimeOffset.UnixEpoch);
        entries[0].AuthorName.ShouldBe("gitext-core tests");
    }

    [Fact]
    public async Task RESET_ile_kaybolan_commit_reflogda_BULUNUYOR()
    {
        // The most important scenario of the phase: the user went back with --hard and wants the
        // commit back.
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
        // 🔴 In the first version reachability was computed with
        // `rev-list --all --no-walk=unsorted HEAD`; because `--no-walk` does NOT walk the history
        // only the tip came back, and EVERY older entry after the first commit looked "lost".
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
