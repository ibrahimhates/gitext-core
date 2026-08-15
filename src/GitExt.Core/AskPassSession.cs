using System.Runtime.Versioning;

namespace GitExt.Core;

/// <summary>
/// Passes the credential supplied by the user to <c>git</c> <b>safely</b>
/// (P06-T09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not an argument or a config value?</b> Putting the password into an argument
/// like <c>-c credential.helper='!echo …'</c> would make it visible to <b>every process</b>
/// on the same machine via <c>ps</c>. And <c>credential.helper store</c>, which writes to
/// disk, leaves the password in plain text (that is why it is not on the suggestion list).
/// </para>
/// <para>
/// <b>MEASURED — <c>GIT_ASKPASS</c> works even when <c>GIT_TERMINAL_PROMPT=0</c>.</b>
/// git calls the script twice and passes the prompt as an argument:
/// <c>Username for 'https://github.com': </c> and
/// <c>Password for 'https://deneme@github.com': </c> — so the two are told apart <b>from the
/// prompt text</b>. Secrets go into the process <b>environment</b>, not inside the script;
/// the environment is visible only to the same user via <c>/proc/&lt;pid&gt;/environ</c>.
/// </para>
/// <para>
/// <b>MEASURED — the credential is not stored anywhere.</b> With no <c>credential.helper</c>
/// setting git never creates <c>~/.git-credentials</c>; the value dies with the process.
/// </para>
/// </remarks>
public sealed class AskPassSession : IDisposable
{
    /// <summary>Variable the user name is read from.</summary>
    internal const string UsernameVariable = "GITEXT_ASKPASS_USERNAME";

    /// <summary>Variable the secret value is read from.</summary>
    internal const string SecretVariable = "GITEXT_ASKPASS_SECRET";

    private readonly string _scriptPath;
    private bool _disposed;

    private AskPassSession(string scriptPath, IReadOnlyDictionary<string, string> environment)
    {
        _scriptPath = scriptPath;
        Environment = environment;
    }

    /// <summary>Environment variables to add to the command.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>
    /// Sets up a temporary askpass script that carries the credential.
    /// </summary>
    /// <remarks>
    /// The script is written with owner-only read/write/execute permissions (<c>0700</c>) and is
    /// deleted by <see cref="Dispose"/>. It holds <b>no</b> secret value itself; it only reads
    /// from the environment.
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

            // some git versions may prefer `SSH_ASKPASS` as well; binding both to the same
            // script keeps the behaviour predictable.
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
            // Failing to delete it breaks nothing; there is nothing secret inside the script.
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
    /// Tells apart whether the user name or the password is being asked by looking at the
    /// prompt text — the measured strings are <c>Username for '…'</c> and <c>Password for '…'</c>.
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
