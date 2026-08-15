using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T09 — authentication flow.
/// </summary>
/// <remarks>
/// The three silent points of the measurement: over SSH <b>both</b> credential and network
/// errors carry the <c>Could not read from remote repository.</c> line, <c>ssh-add -l</c>'s
/// exit code is a clean diagnostic channel, and <c>GIT_ASKPASS</c> works even when
/// <c>GIT_TERMINAL_PROMPT=0</c> is set.
/// </remarks>
public class AuthenticationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ----------------------------------------------------- classification

    /// <remarks>
    /// 🔴 <b>These lines were taken from real git output.</b> Until P06-T09 all three were
    /// classified as <see cref="GitFailureKind.RemoteUnreachable"/>: the user was told
    /// "remote repository not found", so they went fiddling with the address — but the address is right.
    /// </remarks>
    [Theory]
    [InlineData(
        "git@github.com: Permission denied (publickey).\n"
        + "fatal: Could not read from remote repository.\n",
        GitFailureKind.AuthenticationRequired)]
    [InlineData(
        "fatal: could not read Username for 'https://github.com': terminal prompts disabled\n",
        GitFailureKind.AuthenticationRequired)]
    [InlineData(
        "remote: Invalid username or token. Password authentication is not supported.\n"
        + "fatal: Authentication failed for 'https://github.com/x/y.git/'\n",
        GitFailureKind.AuthenticationRequired)]
    [InlineData(
        "ssh: Could not resolve hostname nosuchhost.invalid: Name or service not known\n"
        + "fatal: Could not read from remote repository.\n",
        GitFailureKind.NetworkFailure)]
    [InlineData(
        "fatal: unable to access 'https://example.invalid/x.git/': Could not resolve host: example.invalid\n",
        GitFailureKind.NetworkFailure)]
    public void SSH_hatalari_dogru_siniflandiriliyor(string standardError, GitFailureKind expected) =>
        GitFailureClassifier.Classify(standardError).ShouldBe(expected);

    [Fact]
    public void Gercekten_ulasilamayan_remote_hala_RemoteUnreachable()
    {
        // Proof that the fix did not swallow the old behaviour: an "I could not read" whose
        // cause is NEITHER credentials NOR network is still its own kind.
        const string text =
            "fatal: '/olmayan/yol' does not appear to be a git repository\n"
            + "fatal: Could not read from remote repository.\n";

        GitFailureClassifier.Classify(text).ShouldBe(GitFailureKind.RemoteUnreachable);
    }

    // ---------------------------------------------------------- diagnosis

    [Theory]
    [InlineData("https://github.com/x/y.git", RemoteTransport.Https)]
    [InlineData("http://example.com/x.git", RemoteTransport.Https)]
    [InlineData("git@github.com:x/y.git", RemoteTransport.Ssh)]
    [InlineData("ssh://git@github.com/x/y.git", RemoteTransport.Ssh)]
    [InlineData("/srv/git/x.git", RemoteTransport.Local)]
    [InlineData("../uzak.git", RemoteTransport.Local)]
    [InlineData("file:///srv/git/x.git", RemoteTransport.Local)]
    public void Baglanti_yolu_URL_bicimiyle_belirleniyor(string url, RemoteTransport expected) =>
        AuthenticationDiagnostics.ClassifyTransport(url).ShouldBe(expected);

    private static async Task<(TestRepository Repository, AuthenticationDiagnostics Diagnostics)>
        CreateAsync(SshAgentState agent = SshAgentState.NotRunning)
    {
        TestRepository repository = TestRepository.CreateWithSingleCommit();

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);

        return (repository, new AuthenticationDiagnostics(
            new GitProcessRunner(executable),
            new FakeAgentProbe(agent)));
    }

    private sealed class FakeAgentProbe(SshAgentState state) : ISshAgentProbe
    {
        public Task<SshAgentState> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(state);
    }

    [Fact]
    public async Task SSH_te_agent_yoksa_ONERI_agent_baslatmak()
    {
        (TestRepository repository, AuthenticationDiagnostics diagnostics) = await CreateAsync();
        using TestRepository _ = repository;

        repository.Git("remote", "add", "origin", "git@github.com:x/y.git");

        AuthenticationDiagnosis result =
            await diagnostics.DiagnoseAsync(repository.Path, "origin", Ct);

        result.Transport.ShouldBe(RemoteTransport.Ssh);
        result.Agent.ShouldBe(SshAgentState.NotRunning);
        result.Explanation.ShouldContain("SSH agent");
        result.Suggestions.ShouldContain("ssh-add ~/.ssh/id_ed25519");

        // Asking for a credential over SSH is meaningless — what is wanted is a key.
        result.CanRetryWithCredentials.ShouldBeFalse();
    }

    [Fact]
    public async Task SSH_te_agent_DOLUYSA_mesaj_anahtarin_yetkisiz_oldugunu_soyluyor()
    {
        (TestRepository repository, AuthenticationDiagnostics diagnostics) =
            await CreateAsync(SshAgentState.HasKeys);
        using TestRepository _ = repository;

        repository.Git("remote", "add", "origin", "git@github.com:x/y.git");

        AuthenticationDiagnosis result =
            await diagnostics.DiagnoseAsync(repository.Path, "origin", Ct);

        result.Explanation.ShouldContain("not authorised");
    }

    [Fact]
    public async Task HTTPS_te_helper_yoksa_kimlik_SORULABILIR()
    {
        (TestRepository repository, AuthenticationDiagnostics diagnostics) = await CreateAsync();
        using TestRepository _ = repository;

        repository.Git("remote", "add", "origin", "https://example.com/x.git");

        AuthenticationDiagnosis result =
            await diagnostics.DiagnoseAsync(repository.Path, "origin", Ct);

        result.Transport.ShouldBe(RemoteTransport.Https);
        result.HasCredentialHelper.ShouldBeFalse();
        result.CanRetryWithCredentials.ShouldBeTrue();

        // ⚠️ `store` is deliberately not recommended: it writes the password to disk in plain text.
        result.Suggestions.ShouldNotContain(suggestion => suggestion.Contains("store", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HTTPS_te_helper_VARSA_mesaj_token_a_isaret_ediyor()
    {
        (TestRepository repository, AuthenticationDiagnostics diagnostics) = await CreateAsync();
        using TestRepository _ = repository;

        repository.Git("remote", "add", "origin", "https://example.com/x.git");
        repository.Git("config", "credential.helper", "cache");

        AuthenticationDiagnosis result =
            await diagnostics.DiagnoseAsync(repository.Path, "origin", Ct);

        result.HasCredentialHelper.ShouldBeTrue();
        result.Explanation.ShouldContain("Token");
    }

    [Fact]
    public async Task Bos_credential_helper_ayari_VAR_sayilmiyor()
    {
        // Writing `credential.helper=` is git's way of cancelling an inherited helper;
        // saying "there is a helper" would give the user the wrong advice.
        (TestRepository repository, AuthenticationDiagnostics diagnostics) = await CreateAsync();
        using TestRepository _ = repository;

        repository.Git("remote", "add", "origin", "https://example.com/x.git");
        repository.Git("config", "credential.helper", "");

        AuthenticationDiagnosis result =
            await diagnostics.DiagnoseAsync(repository.Path, "origin", Ct);

        result.HasCredentialHelper.ShouldBeFalse();
    }

    [Fact]
    public async Task Teshis_URL_deki_PAROLAYI_maskeliyor()
    {
        (TestRepository repository, AuthenticationDiagnostics diagnostics) = await CreateAsync();
        using TestRepository _ = repository;

        repository.Git("remote", "add", "origin", "https://kullanici:gizliparola@example.com/x.git");

        AuthenticationDiagnosis result =
            await diagnostics.DiagnoseAsync(repository.Path, "origin", Ct);

        result.Url.ShouldNotBeNull();
        result.Url!.ShouldNotContain("gizliparola");
    }

    // ---------------------------------------------------------- askpass

    [Fact]
    public async Task ASKPASS_kimligi_gercekten_gecirilyor_ve_komut_satirinda_GORUNMUYOR()
    {
        // 🔴 Keeping the password out of the arguments is critical: the command line is visible
        // to every process on the same machine via `ps`. This test shows both that the secret is
        // NOT in the arguments and that it nevertheless REACHES git.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        // A fake remote that asks for credentials: `git credential fill` invokes askpass.
        using AskPassSession session = AskPassSession.Create(new GitCredentials("deneme", "gizli-token"));

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);

        GitCommand command = new()
        {
            WorkingDirectory = repository.Path,
            Arguments = ["credential", "fill"],
            StandardInput = System.Text.Encoding.UTF8.GetBytes("protocol=https\nhost=example.com\n\n"),
            Environment = session.Environment,
        };

        GitResult result = await runner.RunAsync(command, Ct);
        string output = result.GetStandardOutputText();

        output.ShouldContain("username=deneme");
        output.ShouldContain("password=gizli-token");

        // The "show command" field and the command log are on screen: the secret must not leak there.
        command.ToDisplayString().ShouldNotContain("gizli-token");
        command.ToDisplayString().ShouldBe("git credential fill");
    }

    [Fact]
    public void ASKPASS_betigi_yalnizca_SAHIBINE_okunur_ve_sonra_SILINIYOR()
    {
        string path;

        using (AskPassSession session = AskPassSession.Create(new GitCredentials("kullanici", "COK-GIZLI-DEGER-42")))
        {
            path = session.Environment["GIT_ASKPASS"];

            File.Exists(path).ShouldBeTrue();

            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(path).ShouldBe(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            // The secret is not INSIDE the script, it is in the environment.
            File.ReadAllText(path).ShouldNotContain("COK-GIZLI-DEGER-42");
        }

        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Komuta_ozel_ortam_GitEnvironment_in_bosalttigi_degiskeni_GERI_koyuyor()
    {
        // GitEnvironment deliberately clears `GIT_ASKPASS` (so that graphical tools do not open).
        // If the command-specific environment were not applied LAST, the authentication flow would never work.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using AskPassSession session = AskPassSession.Create(new GitCredentials("kullanici", "sir"));

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);

        GitResult withEnvironment = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["credential", "fill"],
                StandardInput = System.Text.Encoding.UTF8.GetBytes("protocol=https\nhost=e.com\n\n"),
                Environment = session.Environment,
            },
            Ct);

        withEnvironment.GetStandardOutputText().ShouldContain("username=kullanici");

        GitResult without = await runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = repository.Path,
                Arguments = ["credential", "fill"],
                StandardInput = System.Text.Encoding.UTF8.GetBytes("protocol=https\nhost=e.com\n\n"),
            },
            Ct);

        without.GetStandardOutputText().ShouldNotContain("username=kullanici");
    }
}
