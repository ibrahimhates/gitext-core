using System.Runtime.Versioning;

namespace GitExt.Core;

/// <summary>
/// Kullanıcının verdiği kimlik bilgisini <c>git</c>'e <b>güvenli biçimde</b> geçirir
/// (P06-T09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden argüman ya da yapılandırma değil?</b> Parolayı <c>-c credential.helper='!echo …'</c>
/// gibi bir argümana koymak, onu aynı makinedeki <b>her sürece</b> <c>ps</c> ile görünür
/// yapardı. Diske yazan <c>credential.helper store</c> ise parolayı düz metin bırakır
/// (bu yüzden öneri listesinde de yok).
/// </para>
/// <para>
/// <b>ÖLÇÜLDÜ — <c>GIT_ASKPASS</c>, <c>GIT_TERMINAL_PROMPT=0</c> iken de çalışıyor.</b>
/// git betiği iki kez çağırıyor ve istemi argüman olarak veriyor:
/// <c>Username for 'https://github.com': </c> ve
/// <c>Password for 'https://deneme@github.com': </c> — yani ikisi <b>istem metninden</b>
/// ayırt ediliyor. Gizli değerler betiğin içine değil, sürecin <b>ortamına</b> konuyor;
/// ortam <c>/proc/&lt;pid&gt;/environ</c> üzerinden yalnızca aynı kullanıcıya görünür.
/// </para>
/// <para>
/// <b>ÖLÇÜLDÜ — kimlik hiçbir yere kaydedilmiyor.</b> <c>credential.helper</c> ayarı yokken
/// git <c>~/.git-credentials</c> oluşturmuyor; bu oturumdaki değer süreçle birlikte gidiyor.
/// </para>
/// </remarks>
public sealed class AskPassSession : IDisposable
{
    /// <summary>Kullanıcı adının okunacağı değişken.</summary>
    internal const string UsernameVariable = "GITEXT_ASKPASS_USERNAME";

    /// <summary>Gizli değerin okunacağı değişken.</summary>
    internal const string SecretVariable = "GITEXT_ASKPASS_SECRET";

    private readonly string _scriptPath;
    private bool _disposed;

    private AskPassSession(string scriptPath, IReadOnlyDictionary<string, string> environment)
    {
        _scriptPath = scriptPath;
        Environment = environment;
    }

    /// <summary>Komuta eklenecek ortam değişkenleri.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>
    /// Kimlik bilgisini taşıyan geçici bir askpass betiği kurar.
    /// </summary>
    /// <remarks>
    /// Betik yalnızca sahibinin okuyup çalıştırabileceği izinlerle (<c>0700</c>) yazılıyor
    /// ve <see cref="Dispose"/> ile siliniyor. İçinde gizli bir değer <b>yok</b>; yalnızca
    /// ortamdan okuyor.
    /// </remarks>
    public static AskPassSession Create(GitCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"gitext-askpass-{Guid.NewGuid():N}{(OperatingSystem.IsWindows() ? ".cmd" : ".sh")}");

        File.WriteAllText(path, OperatingSystem.IsWindows() ? WindowsScript : PosixScript);

        if (!OperatingSystem.IsWindows())
        {
            MakeExecutable(path);
        }

        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["GIT_ASKPASS"] = path,
            [UsernameVariable] = credentials.Username,
            [SecretVariable] = credentials.Secret,

            // git bazı sürümlerde `SSH_ASKPASS`i de tercih edebiliyor; ikisini de aynı
            // betiğe bağlamak davranışı öngörülebilir kılıyor.
            ["SSH_ASKPASS"] = path,
        };

        return new AskPassSession(path, environment);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            File.Delete(_scriptPath);
        }
        catch (IOException)
        {
            // Silinememesi işlevi bozmuyor; betikte gizli bir şey yok.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void MakeExecutable(string path) => File.SetUnixFileMode(
        path,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    /// <remarks>
    /// İstem metnine bakarak kullanıcı adı mı parola mı istendiğini ayırıyor — ölçülen
    /// metinler <c>Username for '…'</c> ve <c>Password for '…'</c>.
    /// </remarks>
    private const string PosixScript =
        "#!/bin/sh\n"
        + "case \"$1\" in\n"
        + "  *[Uu]sername*) printf '%s\\n' \"$" + UsernameVariable + "\" ;;\n"
        + "  *) printf '%s\\n' \"$" + SecretVariable + "\" ;;\n"
        + "esac\n";

    private const string WindowsScript =
        "@echo off\r\n"
        + "echo %1 | findstr /i \"username\" >nul\r\n"
        + "if %errorlevel%==0 (echo %" + UsernameVariable + "%) else (echo %" + SecretVariable + "%)\r\n";
}
