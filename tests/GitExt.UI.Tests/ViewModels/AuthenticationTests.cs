using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P06-T09 — kimlik doğrulama akışı (arayüz tarafı).
/// </summary>
public class AuthenticationTests
{
    private static GitException AuthFailure() => new(
        GitFailureKind.AuthenticationRequired,
        "Kimlik doğrulama gerekiyor.",
        "git push",
        128,
        "git@github.com: Permission denied (publickey).\nfatal: Could not read from remote repository.\n");

    private static AuthenticationDiagnosis SshDiagnosis() => new()
    {
        Transport = RemoteTransport.Ssh,
        Url = "git@github.com:x/y.git",
        Agent = SshAgentState.NotRunning,
        Explanation = "SSH agent yok.",
        Suggestions = ["ssh-add ~/.ssh/id_ed25519"],
    };

    private static AuthenticationDiagnosis HttpsDiagnosis() => new()
    {
        Transport = RemoteTransport.Https,
        Url = "https://example.com/x.git",
        Explanation = "Kimlik doğrulama gerekiyor.",
        Suggestions = ["git config --global credential.helper libsecret"],
    };

    private static (PushViewModel Model, FakePushWriter Push, FakeAuthenticationPrompt Prompt) Create(
        AuthenticationDiagnosis diagnosis)
    {
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        FakePushWriter push = new() { Failure = AuthFailure() };
        FakeAuthenticationPrompt prompt = new();

        return (
            new PushViewModel(remotes, push, new FakeAuthenticationDiagnostics(diagnosis), prompt),
            push,
            prompt);
    }

    private static Task LoadAsync(PushViewModel model) => model.LoadAsync(
        "/depo",
        "main",
        [FakeGitData.LocalBranch("main", FakeGitData.Sha(1), isCurrent: true) with { Upstream = "origin/main" }]);

    // ----------------------------------------------------------- ViewModel

    [AvaloniaFact]
    public void SSH_te_kimlik_ALANLARI_gosterilmiyor()
    {
        // 🔑 SSH'ta istenen şey bir parola değil, anahtar. Alan göstermek yanlış bir vaat
        // olurdu: kullanıcı bir şey yazar, hiçbir işe yaramaz.
        AuthenticationViewModel model = new(SshDiagnosis());

        model.CanEnterCredentials.ShouldBeFalse();
        model.CanSubmit.ShouldBeFalse();
        model.Suggestions.ShouldBe(["ssh-add ~/.ssh/id_ed25519"]);
    }

    [AvaloniaFact]
    public void HTTPS_te_kimlik_alanlari_DOLDURULABILIYOR()
    {
        AuthenticationViewModel model = new(HttpsDiagnosis());

        model.CanEnterCredentials.ShouldBeTrue();
        model.CanSubmit.ShouldBeFalse("boş alanla gönderilemez");

        model.Username = "kullanici";
        model.CanSubmit.ShouldBeFalse("gizli değer de gerekli");

        model.Secret = "token";
        model.CanSubmit.ShouldBeTrue();

        model.SubmitCommand.Execute(null);

        model.Result.ShouldBe(new GitCredentials("kullanici", "token"));
    }

    [AvaloniaFact]
    public void Iptal_edilirse_sonuc_BOS()
    {
        AuthenticationViewModel model = new(HttpsDiagnosis());

        model.Result.ShouldBeNull();
    }

    // ------------------------------------------------------------- akış

    [AvaloniaFact]
    public async Task Kimlik_hatasinda_ekran_ACILIYOR_ve_kimlikle_TEKRAR_deneniyor()
    {
        (PushViewModel model, FakePushWriter push, FakeAuthenticationPrompt prompt) =
            Create(HttpsDiagnosis());

        prompt.Credentials = new GitCredentials("kullanici", "token");

        await LoadAsync(model);

        // İlk deneme kimlik hatası verir, ikincisi (kimlikle) başarılı olsun.
        push.FailUntilCredentialsGiven = true;

        await model.RunCommand.ExecuteAsync(null);

        prompt.Shown.ShouldNotBeNull();
        push.Pushed.Count.ShouldBe(2, "biri kimliksiz, biri kimlikle");
        push.Pushed[0].Credentials.ShouldBeNull();
        push.Pushed[1].Credentials.ShouldBe(new GitCredentials("kullanici", "token"));

        model.HasWarning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Iptal_edilirse_TESHIS_gosteriliyor_ham_hata_DEGIL()
    {
        // 🔴 Ham stderr'i göstermek yanlış yönlendirirdi: aynı satır
        // ("Could not read from remote repository.") hem eksik anahtarda hem çözülemeyen
        // sunucu adında yazılıyor.
        (PushViewModel model, _, FakeAuthenticationPrompt prompt) = Create(SshDiagnosis());

        prompt.Credentials = null;

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasWarning.ShouldBeTrue();
        model.Warning!.ShouldBe("SSH agent yok.");
        model.Warning!.ShouldNotContain("Could not read from remote repository");
        model.Advice!.ShouldContain("ssh-add");
    }

    [AvaloniaFact]
    public async Task Kimlik_ekrani_YOKSA_akis_cokmuyor()
    {
        // Teşhis bağımlılıkları isteğe bağlı; eksikken eski davranış (ham mesaj) sürüyor.
        FakeRemoteReader remotes = new();
        remotes.Remotes.Add(new GitRemote { Name = "origin", FetchUrls = ["https://example.com/a.git"] });

        PushViewModel model = new(remotes, new FakePushWriter { Failure = AuthFailure() });

        await LoadAsync(model);
        await model.RunCommand.ExecuteAsync(null);

        model.HasWarning.ShouldBeTrue();
    }

    private sealed class FakeAuthenticationDiagnostics(AuthenticationDiagnosis diagnosis)
        : IAuthenticationDiagnostics
    {
        public Task<AuthenticationDiagnosis> DiagnoseAsync(
            string workingDirectory,
            string? remote,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(diagnosis);
    }

    private sealed class FakeAuthenticationPrompt : IAuthenticationPrompt
    {
        public AuthenticationViewModel? Shown { get; private set; }

        public GitCredentials? Credentials { get; set; }

        public Task<GitCredentials?> ShowAsync(AuthenticationViewModel model)
        {
            Shown = model;
            return Task.FromResult(Credentials);
        }
    }
}
