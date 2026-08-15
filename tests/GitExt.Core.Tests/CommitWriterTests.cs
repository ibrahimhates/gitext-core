using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T06 — Creating commits.
/// </summary>
/// <remarks>
/// The message is passed via <b>stdin</b>: giving it as an argument would hit the length limit and
/// would expose the user's text to shell interpretation (ADR-0002).
/// </remarks>
public class CommitWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        CommitWriter Writer,
        CommitWriter Impatient,
        GitWriteQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        public string Message() => Repository.Git("log", "-1", "--format=%B");

        public string Subject() => Repository.Git("log", "-1", "--format=%s").Trim();

        public int CommitCount() =>
            int.Parse(Repository.Git("rev-list", "--count", "HEAD").Trim());
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(
            repository,
            new CommitWriter(new GitWriter(runner, queue), runner),

            // Same repository, short write timeout: for the slow-hook test (the default is 10 minutes).
            new CommitWriter(
                new GitWriter(runner, queue, writeTimeout: TimeSpan.FromSeconds(2)), runner),
            queue);
    }

    private static void Stage(Harness harness, string name, string content)
    {
        harness.Repository.WriteFile(name, content);
        harness.Repository.Git("add", "-A");
    }

    [Fact]
    public async Task Commit_olusturulur_ve_kimligi_donulur()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "ilk commit", cancellationToken: Ct);

        // The identity is not parsed from `git commit` output (that is human-readable); it is read separately.
        result.Id.Value.ShouldBe(harness.Repository.Git("rev-parse", "HEAD").Trim());
        harness.Subject().ShouldBe("ilk commit");
    }

    [Fact]
    public async Task Coksatirli_mesaj_govdesiyle_korunur()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "Konu satiri\n\nGovde birinci.\nGovde ikinci.", cancellationToken: Ct);

        // The `%B` output appends its own separator to the end of the message; the comparison is done
        // with trimming (the message itself is correct, the difference comes from the way it is read).
        harness.Message().Trim().ShouldBe("Konu satiri\n\nGovde birinci.\nGovde ikinci.");
    }

    [Fact]
    public async Task Diyez_ile_baslayan_satirlar_SILINMEZ()
    {
        // 🔴 A classic trap: in some modes git treats `#` lines as comments and strips them. In that case
        // a line like "fixes bug #123" would silently disappear.
        // `--cleanup=whitespace` is passed explicitly (to be independent of the user's `commit.cleanup`
        // setting).
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.Git("config", "commit.cleanup", "scissors");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "Konu\n\n#123 numarali issue\nGovde", cancellationToken: Ct);

        harness.Message().ShouldContain("#123 numarali issue");
    }

    [Fact]
    public async Task ASCII_disi_mesaj_bozulmaz()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "Türkçe konu: şğüıöç İĞÜŞÖÇ", cancellationToken: Ct);

        harness.Subject().ShouldBe("Türkçe konu: şğüıöç İĞÜŞÖÇ");
    }

    [Fact]
    public async Task Bos_mesaj_REDDEDILIR()
    {
        // MEASURED: git exits 1 ("Aborting commit due to empty commit message").
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await Should.ThrowAsync<GitException>(
            harness.Writer.CommitAsync(harness.Repository.Path, "", cancellationToken: Ct));
    }

    [Fact]
    public async Task Bos_mesaja_ACIKCA_izin_verilebilir()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path,
            "",
            new CommitOptions { AllowEmptyMessage = true },
            Ct);

        harness.CommitCount().ShouldBe(1);
    }

    [Fact]
    public async Task Amend_son_commiti_degistirir_yenisini_eklemez()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "ilk\n");
        await harness.Writer.CommitAsync(harness.Repository.Path, "ilk mesaj", cancellationToken: Ct);

        Stage(harness, "a.txt", "ikinci\n");
        await harness.Writer.CommitAsync(
            harness.Repository.Path, "duzeltilmis mesaj", new CommitOptions { Amend = true }, Ct);

        harness.CommitCount().ShouldBe(1);
        harness.Subject().ShouldBe("duzeltilmis mesaj");
    }

    [Fact]
    public async Task Signoff_satiri_eklenir()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", new CommitOptions { SignOff = true }, Ct);

        harness.Message().ShouldContain("Signed-off-by:");
    }

    [Fact]
    public async Task Yazar_degistirilebilir_committer_kendimiz_kalir()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path,
            "konu",
            new CommitOptions { Author = "Baska Kisi <baska@ornek.com>" },
            Ct);

        harness.Repository.Git("log", "-1", "--format=%an <%ae>").Trim()
            .ShouldBe("Baska Kisi <baska@ornek.com>");

        // The committer must NOT CHANGE: who committed is a separate fact.
        harness.Repository.Git("log", "-1", "--format=%cn").Trim().ShouldNotBe("Baska Kisi");
    }

    [Fact]
    public async Task Degisiklik_yokken_commit_REDDEDILIR()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");
        await harness.Writer.CommitAsync(harness.Repository.Path, "ilk", cancellationToken: Ct);

        await Should.ThrowAsync<GitException>(
            harness.Writer.CommitAsync(harness.Repository.Path, "bos", cancellationToken: Ct));
    }

    [Fact]
    public async Task Bos_commite_ACIKCA_izin_verilebilir()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");
        await harness.Writer.CommitAsync(harness.Repository.Path, "ilk", cancellationToken: Ct);

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "bos commit", new CommitOptions { AllowEmpty = true }, Ct);

        harness.CommitCount().ShouldBe(2);
    }

    // ---- Hooks ----

    [Fact]
    public async Task Basarisiz_pre_commit_hooku_commiti_DURDURUR()
    {
        // In ADR-0002 hook support was the main rationale for choosing the CLI; here is what it buys.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("pre-commit", "echo 'hook reddetti' >&2\nexit 1\n");

        GitException exception = await Should.ThrowAsync<GitException>(
            harness.Writer.CommitAsync(harness.Repository.Path, "konu", cancellationToken: Ct));

        // The hook's output must be able to reach the user (P05-T07 will carry this into the UI).
        exception.StandardError.ShouldContain("hook reddetti");
    }

    [Fact]
    public async Task Hooklar_ACIKCA_atlanabilir()
    {
        // `--no-verify` is OFF by default; when it is on the UI will show a visible warning (P05-T15).
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("pre-commit", "exit 1\n");

        await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", new CommitOptions { SkipHooks = true }, Ct);

        harness.CommitCount().ShouldBe(1);
    }

    [Fact]
    public async Task Commit_msg_hookunun_mesaj_degisikligi_yansir()
    {
        // The `commit-msg` hook can edit the message file in place; the result must end up in the commit.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("commit-msg", "echo '\\nEk-Satir: hook' >> \"$1\"\n");

        await harness.Writer.CommitAsync(harness.Repository.Path, "konu", cancellationToken: Ct);

        harness.Message().ShouldContain("Ek-Satir: hook");
    }

    // ---- P05-T07: capturing hook output ----

    [Fact]
    public async Task Basarili_committe_bile_hook_ciktisi_TASINIR()
    {
        // 🔴 This was the real gap: when the commit succeeded, the output of `git commit` was never
        // returned; the warnings of a successful `pre-commit` disappeared silently.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("pre-commit", "echo 'UYARI: iki TODO satiri var'\nexit 0\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        result.HasOutput.ShouldBeTrue();
        result.Output.ShouldContain("UYARI: iki TODO satiri var");
        harness.CommitCount().ShouldBe(1);
    }

    [Fact]
    public async Task Hookun_STDOUTU_da_yakalanir()
    {
        // MEASURED: git redirects the hook's stdout to stderr (stdout_to_stderr).
        // Looking only at stderr is therefore ENOUGH — but this is not an assumption, it is a measurement;
        // it is pinned here. If it ever changes, hooks that write with `echo` disappear silently.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook(
            "pre-commit",
            "echo 'SADECE-STDOUT'\necho 'SADECE-STDERR' >&2\nexit 1\n");

        GitException exception = await Should.ThrowAsync<GitException>(
            harness.Writer.CommitAsync(harness.Repository.Path, "konu", cancellationToken: Ct));

        exception.StandardError.ShouldContain("SADECE-STDOUT");
        exception.StandardError.ShouldContain("SADECE-STDERR");
    }

    [Fact]
    public async Task Hooksuz_basarili_committe_cikti_YOKTUR()
    {
        // Counter-evidence: if the output were always non-empty, a "the hook spoke" indicator would be meaningless.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        result.HasOutput.ShouldBeFalse();
    }

    [Fact]
    public async Task Mesaji_degistiren_hook_sonucta_BILDIRILIR()
    {
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("commit-msg", "echo 'Change-Id: I0001' >> \"$1\"\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        result.MessageChanged.ShouldBeTrue();
        result.Message.ShouldContain("Change-Id: I0001");
        result.RequestedMessage.ShouldBe("konu");
    }

    [Fact]
    public async Task Prepare_commit_msg_hooku_da_mesaji_degistirebilir()
    {
        // MEASURED: `prepare-commit-msg` runs even when the message is given with `-F -`
        // (source=message) and it can edit the file.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("prepare-commit-msg", "echo 'Hazirlik-Satiri' >> \"$1\"\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        result.MessageChanged.ShouldBeTrue();
        result.Message.ShouldContain("Hazirlik-Satiri");
    }

    [Fact]
    public async Task No_verify_prepare_commit_msgi_ATLAMAZ()
    {
        // 🔴 MEASURED: `--no-verify` skips only `pre-commit` and `commit-msg`.
        // If it is understood as "skip the hooks", it goes unnoticed that the message can still change.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("prepare-commit-msg", "echo 'Hazirlik-Satiri' >> \"$1\"\n");
        harness.Repository.InstallHook("pre-commit", "exit 1\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", new CommitOptions { SkipHooks = true }, Ct);

        result.Message.ShouldContain("Hazirlik-Satiri");
        result.MessageChanged.ShouldBeTrue();
    }

    [Fact]
    public async Task Hook_mesaja_dokunmazsa_degisiklik_BILDIRILMEZ()
    {
        // Counter-evidence: `--cleanup=whitespace`'s own normalization must not count as a "change",
        // otherwise the indicator lights up on every commit and loses its meaning.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "Konu satiri   \n\nGovde.\n\n\n", cancellationToken: Ct);

        result.MessageChanged.ShouldBeFalse();
    }

    [Fact]
    public async Task Post_commit_hooku_commiti_BOZMAZ_ama_ciktisi_gorunur()
    {
        // MEASURED: even if `post-commit` exits 9, git returns 0 — the commit has already been created.
        // The user must still be able to see what the hook said.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("post-commit", "echo 'POST: bildirim gonderilemedi' >&2\nexit 9\n");

        CommitResult result = await harness.Writer.CommitAsync(
            harness.Repository.Path, "konu", cancellationToken: Ct);

        harness.CommitCount().ShouldBe(1);
        result.Output.ShouldContain("POST: bildirim gonderilemedi");
    }

    [Fact]
    public async Task Yavas_hook_zaman_asimina_takilirsa_commit_OLUSMAZ()
    {
        // MEASURED: when the process is killed the commit is not created and no `index.lock` is left behind
        // (git takes the lock AFTER the hook). So a timeout does not lose data.
        using Harness harness = await CreateAsync();
        Stage(harness, "a.txt", "icerik\n");

        harness.Repository.InstallHook("pre-commit", "sleep 30\n");

        GitException exception = await Should.ThrowAsync<GitException>(
            harness.Impatient.CommitAsync(harness.Repository.Path, "konu", cancellationToken: Ct));

        exception.Kind.ShouldBe(GitFailureKind.Timeout);

        // `--all`: there is no HEAD yet, `rev-list --count HEAD` blows up in this case.
        harness.Repository.Git("rev-list", "--count", "--all").Trim().ShouldBe("0");
        File.Exists(Path.Combine(harness.Repository.Path, ".git", "index.lock")).ShouldBeFalse();
    }
}
