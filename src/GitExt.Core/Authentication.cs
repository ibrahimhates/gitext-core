using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Uzak depoya hangi yolla bağlanılıyor (P06-T09)?</summary>
public enum RemoteTransport
{
    /// <summary>Tanınmayan biçim.</summary>
    Unknown,

    /// <summary>Dosya sistemi yolu — kimlik doğrulama yok.</summary>
    Local,

    /// <summary><c>https://…</c> — kullanıcı adı + token.</summary>
    Https,

    /// <summary><c>git@host:yol</c> ya da <c>ssh://…</c> — anahtar.</summary>
    Ssh,
}

/// <summary>SSH agent'ın durumu (P06-T09).</summary>
/// <remarks>
/// <b>ÖLÇÜLDÜ — <c>ssh-add -l</c>'in çıkış kodu temiz bir teşhis kanalı:</b>
/// <c>2</c> agent yok · <c>1</c> agent var ama boş · <c>0</c> agent var ve anahtar yüklü.
/// </remarks>
public enum SshAgentState
{
    /// <summary><c>ssh-add</c> çalıştırılamadı ya da beklenmedik bir kod döndü.</summary>
    Unknown,

    /// <summary>Agent çalışmıyor (<c>SSH_AUTH_SOCK</c> yok).</summary>
    NotRunning,

    /// <summary>Agent çalışıyor ama hiç anahtar yüklü değil.</summary>
    Empty,

    /// <summary>Agent çalışıyor ve en az bir anahtar yüklü.</summary>
    HasKeys,
}

/// <summary>
/// Kimlik doğrulama başarısızlığının <b>neden</b> olduğu (P06-T09).
/// </summary>
public sealed record AuthenticationDiagnosis
{
    /// <summary>Bağlantı yolu.</summary>
    public required RemoteTransport Transport { get; init; }

    /// <summary>Uzak depo URL'si (parola maskelenmiş).</summary>
    public string? Url { get; init; }

    /// <summary>Kullanıcının <c>credential.helper</c> ayarı var mı?</summary>
    public bool HasCredentialHelper { get; init; }

    /// <summary>SSH agent durumu; HTTPS'te <see cref="SshAgentState.Unknown"/>.</summary>
    public SshAgentState Agent { get; init; }

    /// <summary>
    /// Kimlik bilgisi sorup <b>tekrar denemek</b> anlamlı mı?
    /// </summary>
    /// <remarks>
    /// Yalnızca HTTPS'te. SSH'ta sorulacak şey bir parola değil, <b>anahtar</b>; onu bir
    /// diyalogla çözemeyiz (ölçüm: agent'a anahtar eklemek ayrı bir iş ve kullanıcının
    /// kendi anahtar dosyasını gerektiriyor).
    /// </remarks>
    public bool CanRetryWithCredentials => Transport == RemoteTransport.Https;

    /// <summary>Kullanıcıya gösterilecek açıklama.</summary>
    public required string Explanation { get; init; }

    /// <summary>Çalıştırılabilir öneriler (komut ya da adım).</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];
}

/// <summary>Kullanıcıdan alınan HTTPS kimlik bilgisi (P06-T09).</summary>
/// <param name="Username">Kullanıcı adı.</param>
/// <param name="Secret">Parola ya da kişisel erişim token'ı.</param>
public sealed record GitCredentials(string Username, string Secret);

/// <summary>Kimlik doğrulama teşhisi (P06-T09).</summary>
public interface IAuthenticationDiagnostics
{
    /// <summary>Başarısız bir uzak işlemin <b>neden</b> başarısız olduğunu söyler.</summary>
    Task<AuthenticationDiagnosis> DiagnoseAsync(
        string workingDirectory,
        string? remote,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Kimlik doğrulama teşhisi (P06-T09).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Teşhis olmadan mesaj YANLIŞTI.</b> SSH tarafında git, kimlik ve ağ hatalarının
/// hepsine <c>Could not read from remote repository.</c> satırını ekliyor; sınıflandırıcı
/// bu satıra önce baktığı için <b>eksik SSH anahtarı</b> kullanıcıya <i>"uzak depo
/// bulunamadı"</i> diye gösteriliyordu. Sınıflandırma sırası düzeltildi; bu sınıf da
/// <b>ne yapılacağını</b> söylüyor.
/// </para>
/// <para>
/// Teşhis git'in metnine değil <b>ortama</b> bakıyor: uzak URL'nin biçimi, kullanıcının
/// <c>credential.helper</c> ayarı ve <c>ssh-add -l</c>'in çıkış kodu.
/// </para>
/// </remarks>
public sealed class AuthenticationDiagnostics : IAuthenticationDiagnostics
{
    private readonly IGitProcessRunner _runner;
    private readonly ISshAgentProbe _agent;

    public AuthenticationDiagnostics(IGitProcessRunner runner, ISshAgentProbe? agent = null)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
        _agent = agent ?? new SshAgentProbe();
    }

    public async Task<AuthenticationDiagnosis> DiagnoseAsync(
        string workingDirectory,
        string? remote,
        CancellationToken cancellationToken = default)
    {
        string? url = await ReadUrlAsync(workingDirectory, remote, cancellationToken).ConfigureAwait(false);
        RemoteTransport transport = ClassifyTransport(url);
        bool helper = await HasCredentialHelperAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        SshAgentState agent = transport == RemoteTransport.Ssh
            ? await _agent.ProbeAsync(cancellationToken).ConfigureAwait(false)
            : SshAgentState.Unknown;

        return new AuthenticationDiagnosis
        {
            Transport = transport,
            Url = GitRemoteUrl.Mask(url),
            HasCredentialHelper = helper,
            Agent = agent,
            Explanation = Explain(transport, helper, agent),
            Suggestions = Suggest(transport, helper, agent),
        };
    }

    /// <summary>Uzak URL'nin biçimine bakarak bağlantı yolunu belirler.</summary>
    /// <remarks>
    /// <c>git@host:yol</c> biçimi bir şema içermiyor; şeması olmayan ve <c>:</c> içeren
    /// adresler SCP kısayolu sayılıyor. Windows sürücü harfleri (<c>C:\…</c>) bu kurala
    /// takılmasın diye tek harfli önek ayrıca eleniyor.
    /// </remarks>
    internal static RemoteTransport ClassifyTransport(string? url)
    {
        if (url is not { Length: > 0 })
        {
            return RemoteTransport.Unknown;
        }

        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return RemoteTransport.Https;
        }

        if (url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("git+ssh://", StringComparison.OrdinalIgnoreCase))
        {
            return RemoteTransport.Ssh;
        }

        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith('/')
            || url.StartsWith('.')
            || url.StartsWith('~'))
        {
            return RemoteTransport.Local;
        }

        int colon = url.IndexOf(':', StringComparison.Ordinal);

        if (colon > 1 && !url.AsSpan(0, colon).Contains('/'))
        {
            return RemoteTransport.Ssh;
        }

        return url.Contains("://", StringComparison.Ordinal)
            ? RemoteTransport.Unknown
            : RemoteTransport.Local;
    }

    private async Task<string?> ReadUrlAsync(
        string workingDirectory,
        string? remote,
        CancellationToken cancellationToken)
    {
        if (remote is not { Length: > 0 })
        {
            return null;
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "remote", "get-url", "--", remote),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? result.GetStandardOutputText().Trim() : null;
    }

    private async Task<bool> HasCredentialHelperAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "config", "--get-all", "credential.helper"),
            cancellationToken).ConfigureAwait(false);

        // ÖLÇÜLDÜ: ayar yoksa çıkış kodu 1 ve çıktı boş. Boş DEĞER de anlamlı — kullanıcı
        // `credential.helper=` yazarak devralınan helper'ı bilerek iptal etmiş olabilir.
        return result.IsSuccess
            && result.GetStandardOutputText()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Trim().Length > 0);
    }

    private static string Explain(RemoteTransport transport, bool helper, SshAgentState agent) =>
        transport switch
        {
            RemoteTransport.Ssh => agent switch
            {
                SshAgentState.NotRunning =>
                    "The remote uses SSH but there is no SSH agent running in this session, "
                    + "so no key could be offered to the server.",
                SshAgentState.Empty =>
                    "The SSH agent is running but holds no keys.",
                SshAgentState.HasKeys =>
                    "The SSH agent holds keys but the server did not accept them — the key is "
                    + "probably not authorised for this repository.",
                _ =>
                    "The remote uses SSH and the server did not accept the key.",
            },
            RemoteTransport.Https when helper =>
                "The stored credential was rejected. The token may have expired or may not be authorised "
                + "yetmiyor olabilir.",
            RemoteTransport.Https =>
                "The remote requires authentication and there is no stored credential.",
            RemoteTransport.Local =>
                "A local path was unreachable; this is unrelated to authentication.",
            _ =>
                "Authentication failed.",
        };

    private static IReadOnlyList<string> Suggest(
        RemoteTransport transport,
        bool helper,
        SshAgentState agent) => transport switch
        {
            RemoteTransport.Ssh => agent switch
            {
                SshAgentState.NotRunning =>
                [
                    "eval \"$(ssh-agent -s)\"",
                    "ssh-add ~/.ssh/id_ed25519",
                ],
                SshAgentState.Empty => ["ssh-add ~/.ssh/id_ed25519"],
                SshAgentState.HasKeys =>
                [
                    "ssh -T git@<sunucu>",
                    "ssh-add -l",
                ],
                _ => ["ssh-add -l"],
            },

            // ⚠️ Bilinçli olarak `store` önerilmiyor: parolayı düz metin olarak diske yazar.
            RemoteTransport.Https when !helper =>
            [
                "git config --global credential.helper libsecret",
                "git config --global credential.helper cache",
            ],
            RemoteTransport.Https => ["git credential reject"],
            _ => [],
        };
}

/// <summary>SSH agent'ı yoklayan taraf (P06-T09).</summary>
/// <remarks>Ayrı bir arayüz: agent süreç dışında ve testte taklit edilmesi gerekiyor.</remarks>
public interface ISshAgentProbe
{
    Task<SshAgentState> ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>ssh-add -l</c> ile agent durumunu okur (P06-T09).
/// </summary>
public sealed class SshAgentProbe : ISshAgentProbe
{
    public async Task<SshAgentState> ProbeAsync(CancellationToken cancellationToken = default)
    {
        // `SSH_AUTH_SOCK` yoksa süreci hiç başlatmaya gerek yok.
        if (System.Environment.GetEnvironmentVariable("SSH_AUTH_SOCK") is not { Length: > 0 })
        {
            return SshAgentState.NotRunning;
        }

        try
        {
            using System.Diagnostics.Process process = new()
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ssh-add",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            process.StartInfo.ArgumentList.Add("-l");

            if (!process.Start())
            {
                return SshAgentState.Unknown;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // ÖLÇÜLDÜ: 2 agent yok · 1 agent boş · 0 anahtar var.
            return process.ExitCode switch
            {
                0 => SshAgentState.HasKeys,
                1 => SshAgentState.Empty,
                2 => SshAgentState.NotRunning,
                _ => SshAgentState.Unknown,
            };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // `ssh-add` kurulu değil.
            return SshAgentState.Unknown;
        }
    }
}

/// <summary>Uzak URL yardımcıları.</summary>
internal static class GitRemoteUrl
{
    /// <summary>URL'deki parolayı maskeler.</summary>
    internal static string? Mask(string? url) => Model.GitRemote.MaskCredentials(url);
}
