using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T09 — kimlik doğrulama ekranının yerleşimi.
/// </summary>
public class AuthenticationLayoutTests
{
    private static Window Show(AuthenticationDiagnosis diagnosis)
    {
        AuthenticationWindow window = new()
        {
            DataContext = new AuthenticationViewModel(diagnosis),
            Width = 520,
            Height = 420,
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    [AvaloniaFact]
    public void SSH_te_kimlik_paneli_ve_dugmesi_GIZLI()
    {
        Window window = Show(new AuthenticationDiagnosis
        {
            Transport = RemoteTransport.Ssh,
            Agent = SshAgentState.NotRunning,
            Explanation = "SSH agent yok.",
            Suggestions = ["ssh-add ~/.ssh/id_ed25519"],
        });

        window.GetControl<StackPanel>("CredentialPanel").IsVisible.ShouldBeFalse();
        window.GetControl<Button>("SubmitButton").IsVisible.ShouldBeFalse();

        // Ne yapılacağı yine de ekranda.
        window.GetControl<StackPanel>("SuggestionPanel").IsVisible.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public void HTTPS_te_kimlik_paneli_GORUNUYOR_ve_gizli_alan_MASKELI()
    {
        Window window = Show(new AuthenticationDiagnosis
        {
            Transport = RemoteTransport.Https,
            Url = "https://example.com/x.git",
            Explanation = "Kimlik doğrulama gerekiyor.",
        });

        window.GetControl<StackPanel>("CredentialPanel").IsVisible.ShouldBeTrue();

        // 🔒 Parola alanı maskeli olmalı — omuz üstünden okunmasın.
        window.GetControl<TextBox>("SecretBox").PasswordChar.ShouldNotBe('\0');

        window.Close();
    }

    [AvaloniaFact]
    public void Kaydedilmedigi_kullaniciya_YAZILI()
    {
        // Kullanıcı token'ını girerken nereye gittiğini bilmeli.
        Window window = Show(new AuthenticationDiagnosis
        {
            Transport = RemoteTransport.Https,
            Explanation = "Kimlik doğrulama gerekiyor.",
        });

        (window.GetControl<TextBlock>("StorageNoticeText").Text ?? string.Empty)
            .ShouldContain("not stored anywhere");

        window.Close();
    }

    [AvaloniaFact]
    public void Yerel_yolda_ne_oneri_ne_alan_gosteriliyor()
    {
        Window window = Show(new AuthenticationDiagnosis
        {
            Transport = RemoteTransport.Local,
            Explanation = "Yerel bir yola erişilemedi.",
        });

        window.GetControl<StackPanel>("CredentialPanel").IsVisible.ShouldBeFalse();
        window.GetControl<StackPanel>("SuggestionPanel").IsVisible.ShouldBeFalse();

        window.Close();
    }
}
