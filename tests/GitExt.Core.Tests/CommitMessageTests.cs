using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T13 — commit message helpers: history, template, <c>HEAD</c> message, draft.
/// </summary>
/// <remarks>
/// The weight of the tests is on <b>three traps found by measurement</b>: <c>~</c> only expands
/// with <c>--path</c>, the comment character does not have to be <c>#</c>, and the message file
/// git prepares is written with raw bytes.
/// </remarks>
public class CommitMessageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        CommitMessageReader Reader,
        CommitMessageStore Store,
        GitConfigReader Config) : IDisposable
    {
        public string Path => Repository.Path;

        public void Dispose() => Repository.Dispose();
    }

    private static async Task<Harness> CreateAsync(bool withCommit = true)
    {
        TestRepository repository = withCommit
            ? TestRepository.CreateWithSingleCommit()
            : TestRepository.CreateEmpty();

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitConfigReader config = new(runner);

        return new Harness(
            repository,
            new CommitMessageReader(runner, config),
            new CommitMessageStore(runner, config),
            config);
    }

    // ---- Reading configuration ----

    [Fact]
    public async Task Ayarsiz_anahtar_null_doner_hata_DEGIL()
    {
        // If every unconfigured repository threw, the commit screen would never open.
        using Harness harness = await CreateAsync();

        (await harness.Config.GetAsync(harness.Path, "commit.template", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Tilde_YALNIZCA_path_ile_genisliyor()
    {
        // 🔴 MEASURED: a plain `--get` returns the `~/…` value raw. Calling File.Exists with the raw
        // value would silently make every template starting with `~` "not found".
        // (TestRepository sets HOME to the repository root.)
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "--local", "commit.template", "~/sablon.txt");

        string? raw = await harness.Config.GetAsync(harness.Path, "commit.template", Ct);
        string? expanded = await harness.Config.GetPathAsync(harness.Path, "commit.template", Ct);

        raw.ShouldBe("~/sablon.txt");
        expanded.ShouldNotBeNull().ShouldNotStartWith("~");
        expanded.ShouldEndWith("sablon.txt");
    }

    [Fact]
    public async Task Ayni_anahtarin_SON_degeri_kazanir()
    {
        // git's own rule is "the last writer wins"; taking the first line would be silently wrong.
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "--local", "--add", "commit.template", "birinci.txt");
        harness.Repository.Git("config", "--local", "--add", "commit.template", "ikinci.txt");

        (await harness.Config.GetAsync(harness.Path, "commit.template", Ct)).ShouldBe("ikinci.txt");
    }

    // ---- Message history ----

    [Fact]
    public async Task Son_mesajlar_YENIDEN_ESKIYE_okunur()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Commit("ikinci konu\n\nikinci gövde");
        harness.Repository.Commit("üçüncü konu");

        IReadOnlyList<string> messages = await harness.Reader.ReadRecentAsync(harness.Path, 10, false, Ct);

        messages[0].ShouldBe("üçüncü konu");
        messages[1].ShouldBe("ikinci konu\n\nikinci gövde");
        messages[2].ShouldBe("ilk commit");
    }

    [Fact]
    public async Task Cok_satirli_mesajlar_birbirine_KARISMAZ()
    {
        // Without `-z` the record separator would be the line ending and there would be no way to tell
        // where a multi-line message ends (measured).
        using Harness harness = await CreateAsync();

        harness.Repository.Commit("konu\n\nsatir bir\nsatir iki\nsatir üç");
        harness.Repository.Commit("sonraki");

        IReadOnlyList<string> messages = await harness.Reader.ReadRecentAsync(harness.Path, 2, false, Ct);

        messages.Count.ShouldBe(2);
        messages[1].ShouldBe("konu\n\nsatir bir\nsatir iki\nsatir üç");
    }

    [Fact]
    public async Task Bos_mesajli_commit_listeyi_KAYDIRMAZ()
    {
        // A commit with an empty message is real (P02-T04): it is created with `--allow-empty-message`
        // and rebase/import tools produce them. In `-z` output it comes through as an empty field.
        using Harness harness = await CreateAsync();

        harness.Repository.Git(
            "commit", "--allow-empty", "--allow-empty-message", "-m", string.Empty);
        harness.Repository.Commit("bostan sonraki");

        IReadOnlyList<string> messages = await harness.Reader.ReadRecentAsync(harness.Path, 5, false, Ct);

        messages.ShouldBe(["bostan sonraki", "ilk commit"]);
    }

    [Fact]
    public async Task Yalnizca_kendi_commitlerim_filtrelenir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Git(
            "commit", "--allow-empty", "--author=Baskasi <baska@example.invalid>",
            "-m", "baskasinin mesaji");
        harness.Repository.Commit("benim mesajim");

        IReadOnlyList<string> mine =
            await harness.Reader.ReadRecentAsync(harness.Path, 10, onlyCurrentUser: true, Ct);

        IReadOnlyList<string> all =
            await harness.Reader.ReadRecentAsync(harness.Path, 10, onlyCurrentUser: false, Ct);

        mine.ShouldNotContain("baskasinin mesaji");
        mine.ShouldContain("benim mesajim");
        all.ShouldContain("baskasinin mesaji");
    }

    [Fact]
    public async Task Yazar_deseni_ALT_DIZE_eslesmesi_yapmaz()
    {
        // MEASURED: `--author` matches as a regular expression. An unanchored pattern would count the
        // commits of everyone whose name occurs inside another name as "mine".
        using Harness harness = await CreateAsync();

        harness.Repository.Git(
            "commit", "--allow-empty",
            "--author=gitext-core tests uzatilmis <tests@gitext-core.invalid>",
            "-m", "benzeyen ad");
        harness.Repository.Commit("gercek benim");

        IReadOnlyList<string> mine =
            await harness.Reader.ReadRecentAsync(harness.Path, 10, onlyCurrentUser: true, Ct);

        mine.ShouldContain("gercek benim");
        mine.ShouldNotContain("benzeyen ad");
    }

    [Fact]
    public async Task Commitsiz_depoda_gecmis_BOS_liste()
    {
        // `git log` exits 128 here; that would mean showing an exception to a user making
        // their first commit.
        using Harness harness = await CreateAsync(withCommit: false);

        (await harness.Reader.ReadRecentAsync(harness.Path, 5, false, Ct)).ShouldBeEmpty();
        (await harness.Reader.ReadHeadMessageAsync(harness.Path, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task HEAD_mesaji_amend_icin_okunur()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Commit("düzeltilecek konu\n\ngövde");

        (await harness.Reader.ReadHeadMessageAsync(harness.Path, Ct))
            .ShouldBe("düzeltilecek konu\n\ngövde");
    }

    // ---- Template ----

    [Fact]
    public async Task Sablon_ayarsizsa_null()
    {
        using Harness harness = await CreateAsync();

        (await harness.Reader.ReadTemplateAsync(harness.Path, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Sablon_okunur()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("sablon.txt", "konu\n\n# yardim satiri\ngövde\n");
        harness.Repository.Git("config", "--local", "commit.template", "sablon.txt");

        CommitTemplate template = (await harness.Reader.ReadTemplateAsync(harness.Path, Ct)).ShouldNotBeNull();

        template.IsMissing.ShouldBeFalse();
        template.Text.ShouldNotBeNull().ShouldContain("gövde");
    }

    [Fact]
    public async Task Goreli_sablon_yolu_KOKE_gore_cozulur()
    {
        // 🔴 MEASURED: git resolves the relative path against the root of the working tree, not against
        // the directory the command runs in — even with a file of the same name in a subdirectory, the
        // one at the root was read. Otherwise we would show the user a different template than the one they see in the terminal.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("sablon.txt", "KOK SABLONU\n");
        harness.Repository.WriteFile("alt/sablon.txt", "ALT SABLONU\n");
        harness.Repository.Git("config", "--local", "commit.template", "sablon.txt");

        string subdirectory = Path.Combine(harness.Path, "alt");

        CommitTemplate template =
            (await harness.Reader.ReadTemplateAsync(subdirectory, Ct)).ShouldNotBeNull();

        template.Text.ShouldNotBeNull().ShouldContain("KOK SABLONU");
    }

    [Fact]
    public async Task Var_olmayan_sablon_SESSIZCE_bos_gecmez()
    {
        // git itself exits 128 with `fatal: could not read` in this case, meaning the user's commit in
        // the terminal does not work either. Showing "empty template" would hide the broken
        // configuration.
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "--local", "commit.template", "yok-boyle-dosya.txt");

        CommitTemplate template = (await harness.Reader.ReadTemplateAsync(harness.Path, Ct)).ShouldNotBeNull();

        template.IsMissing.ShouldBeTrue();
        template.Path.ShouldContain("yok-boyle-dosya.txt");
    }

    // ---- Comment character ----

    [Fact]
    public async Task Yorum_karakteri_ayardan_okunur()
    {
        // 🔴 MEASURED: in a repository with `core.commentChar=';'` git strips the `;` lines and KEEPS
        // the `#` lines. A blind `#` filter would both leave the real comments in and delete the
        // user's issue line.
        using Harness harness = await CreateAsync();

        (await harness.Reader.ReadCommentCharacterAsync(harness.Path, Ct)).ShouldBe("#");

        harness.Repository.Git("config", "--local", "core.commentChar", ";");

        (await harness.Reader.ReadCommentCharacterAsync(harness.Path, Ct)).ShouldBe(";");
    }

    [Fact]
    public void Yorum_temizleme_yalnizca_SATIR_BASINDAKI_oneki_alir()
    {
        // MEASURED: git does not strip an indented comment line either.
        string text = "konu\n\n# yorum\n  # girintili\nson";

        CommitMessageText.RemoveComments(text).ShouldBe("konu\n\n  # girintili\nson");
    }

    [Fact]
    public void Auto_yorum_karakterinde_VARSAYILANA_donulur()
    {
        // `auto` means the character git picks based on the message; there is no fixed answer.
        // Leaving the comment in is preferable to deleting the user's line on a wrong guess.
        CommitMessageText.ResolveCommentCharacter("auto").ShouldBe("#");
        CommitMessageText.ResolveCommentCharacter(null).ShouldBe("#");
        CommitMessageText.ResolveCommentCharacter("//").ShouldBe("//");
    }

    // ---- Draft ----

    [Fact]
    public async Task Taslak_yazilir_ve_geri_okunur()
    {
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "yarim kalan mesaj", Ct);

        PendingCommitMessage pending = await harness.Store.ReadAsync(harness.Path, Ct);

        pending.Text.ShouldBe("yarim kalan mesaj");
        pending.Source.ShouldBe(CommitMessageSource.Draft);
    }

    [Fact]
    public async Task Taslak_GIT_DIZININE_yazilir_ve_calisma_agacini_kirletmez()
    {
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "taslak", Ct);

        File.Exists(Path.Combine(harness.Path, ".git", CommitMessageStore.DraftFileName))
            .ShouldBeTrue();

        // A foreign file under `.git` does not bother git (measured) — but what the test needs to
        // verify is that the working tree stays clean.
        harness.Repository.Git("status", "--porcelain").ShouldBeEmpty();
    }

    [Fact]
    public async Task Bos_taslak_dosyayi_SILER()
    {
        // A user who deletes a half-written message must not see it again when they reopen the screen.
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "bir sey", Ct);
        await harness.Store.SaveDraftAsync(harness.Path, "   \n  ", Ct);

        File.Exists(Path.Combine(harness.Path, ".git", CommitMessageStore.DraftFileName))
            .ShouldBeFalse();

        (await harness.Store.ReadAsync(harness.Path, Ct)).Source.ShouldBe(CommitMessageSource.None);
    }

    [Fact]
    public async Task Taslak_worktree_BASINA_ayri()
    {
        // MERGE_MSG and the index are per worktree (P02-T06). Putting the draft in the common directory
        // would mix up the messages of a user working in two worktrees.
        using Harness harness = await CreateAsync();
        using TestRepository worktree = harness.Repository.AddWorkTree("yan-dal");

        await harness.Store.SaveDraftAsync(harness.Path, "ana mesaj", Ct);
        await harness.Store.SaveDraftAsync(worktree.Path, "worktree mesaji", Ct);

        (await harness.Store.ReadAsync(harness.Path, Ct)).Text.ShouldBe("ana mesaj");
        (await harness.Store.ReadAsync(worktree.Path, Ct)).Text.ShouldBe("worktree mesaji");
    }

    [Fact]
    public async Task Merge_mesaji_taslaktan_ONCE_gelir()
    {
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "eski taslak", Ct);

        CreateConflictingMerge(harness.Repository);

        PendingCommitMessage pending = await harness.Store.ReadAsync(harness.Path, Ct);

        pending.Source.ShouldBe(CommitMessageSource.Pending);
        pending.Text.ShouldStartWith("Merge branch");
    }

    [Fact]
    public async Task Merge_mesajindaki_YORUMLAR_temizlenir()
    {
        // 🔴 The quietest bug of the phase would be here: git's editor path does not let `# Conflicts:`
        // lines into the commit, our `--cleanup=whitespace` path would.
        // What is shown in the box = what is committed.
        using Harness harness = await CreateAsync();

        CreateConflictingMerge(harness.Repository);

        string raw = File.ReadAllText(Path.Combine(harness.Path, ".git", "MERGE_MSG"));
        raw.ShouldContain("# Conflicts:");

        PendingCommitMessage pending = await harness.Store.ReadAsync(harness.Path, Ct);

        pending.Text.ShouldNotContain("#");
        pending.Text.ShouldBe("Merge branch 'yan'");
    }

    [Fact]
    public async Task Merge_mesaji_DOSYANIN_KODLAMASIYLA_okunur()
    {
        // 🔴 MEASURED: git writes this file with raw bytes — in a repository whose `i18n.commitEncoding`
        // is Latin-5, a cherry-picked message lands with Latin-5 bytes. Had UTF-8 been assumed, a Turkish
        // message would turn into replacement characters (the same as P04-T07).
        using Harness harness = await CreateAsync();

        harness.Repository.Git("config", "--local", "i18n.commitEncoding", "ISO-8859-9");

        // 🔴 REQUIRED — the fixture below builds its bytes with Encoding.GetEncoding, and .NET does
        // not know "ISO-8859-9" until CodePagesEncodingProvider is registered. Production code
        // registers it through TextEncodings' static constructor, but this line runs BEFORE any
        // production code is touched. Without this call the test passed only when some earlier test
        // in the same assembly happened to trigger that constructor first: run alone, it threw
        // ArgumentException — an order-dependent test that was green by luck.
        TextEncodings.EnsureRegistered();

        string gitDirectory = Path.Combine(harness.Path, ".git");
        File.WriteAllBytes(
            Path.Combine(gitDirectory, "MERGE_MSG"),
            Encoding.GetEncoding("ISO-8859-9").GetBytes("Türkçe merge mesajı\n"));

        PendingCommitMessage pending = await harness.Store.ReadAsync(harness.Path, Ct);

        pending.Text.ShouldBe("Türkçe merge mesajı");
    }

    [Fact]
    public async Task Taslak_temizlenince_geri_gelmez()
    {
        using Harness harness = await CreateAsync();

        await harness.Store.SaveDraftAsync(harness.Path, "commit edilecek", Ct);
        await harness.Store.ClearDraftAsync(harness.Path, Ct);

        (await harness.Store.ReadAsync(harness.Path, Ct)).Source.ShouldBe(CommitMessageSource.None);
    }

    /// <summary>Starts a conflicting merge; <c>MERGE_MSG</c> is left behind.</summary>
    private static void CreateConflictingMerge(TestRepository repository)
    {
        repository.WriteFile("çakışan.txt", "taban\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "taban");

        repository.Git("checkout", "-b", "yan");
        repository.WriteFile("çakışan.txt", "yan dal\n");
        repository.Git("commit", "-am", "yan dal degisikligi");

        repository.Git("checkout", "main");
        repository.WriteFile("çakışan.txt", "ana dal\n");
        repository.Git("commit", "-am", "ana dal degisikligi");

        // A conflict is expected: `Git` throws on failure, which is why TryGit is used.
        repository.TryGit("merge", "yan");
    }
}
