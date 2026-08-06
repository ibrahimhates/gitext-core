using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T09 — kimlik doğrulama akışı.
/// </summary>
/// <remarks>
/// Ölçümün üç sessiz noktası: SSH'ta kimlik ve ağ hatalarının <b>ikisinin de</b>
/// <c>Could not read from remote repository.</c> satırını taşıması, <c>ssh-add -l</c>'in
/// çıkış kodunun temiz bir teşhis kanalı olması, ve <c>GIT_ASKPASS</c>'ın
/// <c>GIT_TERMINAL_PROMPT=0</c> iken de çalışması.
/// </remarks>
public class AuthenticationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------- sınıflandırma

    /// <remarks>
    /// 🔴 <b>Bu satırlar gerçek git çıktısından alındı.</b> P06-T09'a kadar üçü de
    /// <see cref="GitFailureKind.RemoteUnreachable"/> diye sınıflandırılıyordu: kullanıcıya
    /// "uzak depo bulunamadı" denirdi, o da adresini kurcalardı — oysa adres doğru.
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
        // Düzeltmenin eski davranışı yutmadığının kanıtı: sebebi kimlik ya da ağ OLMAYAN
        // bir "okuyamadım" hâlâ kendi türünde.
        const string text =
            "fatal: '/olmayan/yol' does not appear to be a git repository\n"
            + "fatal: Could not read from remote repository.\n";

        GitFailureClassifier.Classify(text).ShouldBe(GitFailureKind.RemoteUnreachable);
    }

    // ------------------------------------------------------------- teşhis

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

        // SSH'ta kimlik bilgisi sormak anlamsız — istenen şey bir anahtar.
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

        result.Explanation.ShouldContain("yetkili değil");
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

        // ⚠️ `store` bilinçli olarak önerilmiyor: parolayı düz metin diske yazar.
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
        // `credential.helper=` yazmak, devralınan helper'ı iptal etmenin git'teki yolu;
        // "helper var" demek kullanıcıya yanlış öneri verirdi.
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
        // 🔴 Parolanın argümana konmaması kritik: komut satırı aynı makinedeki her sürece
        // `ps` ile görünür. Bu test gizli değerin argümanlarda OLMADIĞINI ve buna rağmen
        // git'e ULAŞTIĞINI birlikte gösteriyor.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        // Kimlik isteyen sahte bir uzak taraf: `git credential fill` askpass'i çağırıyor.
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

        // "Komutu göster" alanı ve komut günlüğü ekranda: gizli değer oraya sızmamalı.
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

            // Gizli değer betiğin İÇİNDE değil, ortamda.
            File.ReadAllText(path).ShouldNotContain("COK-GIZLI-DEGER-42");
        }

        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Komuta_ozel_ortam_GitEnvironment_in_bosalttigi_degiskeni_GERI_koyuyor()
    {
        // GitEnvironment `GIT_ASKPASS`i bilerek boşaltıyor (grafik araçlar açılmasın diye).
        // Komuta özel ortam EN SONA uygulanmazsa kimlik doğrulama akışı hiç çalışmazdı.
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
