using System.Runtime.CompilerServices;

namespace GitExt.Core.Tests.Fixtures;

/// <summary>
/// Isolates the git configuration of the <b>whole test process</b> from the machine's.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TestRepository"/> already runs its own fixture commands hermetically, but the code
/// <b>under test</b> starts git itself (<c>GitProcessRunner</c>), and that child process inherits
/// the environment of the test process. So the machine's system and user configuration was reaching
/// exactly the half of the calls whose answers the assertions are about — while the fixture half
/// stayed clean. That asymmetry is invisible until a machine somewhere configures something.
/// </para>
/// <para>
/// 🔴 MEASURED (macOS CI): the git installed there carries <c>credential.helper = osxkeychain</c> in
/// the <b>system</b> configuration. <c>HasCredentialHelper</c> therefore came back true in a
/// repository where no helper was configured, and <c>HTTPS_te_helper_yoksa_kimlik_SORULABILIR</c>
/// failed on macOS alone. The same door lets a developer's own <c>~/.gitconfig</c> — hooks,
/// templates, <c>init.defaultBranch</c> — change the test results.
/// </para>
/// <para>
/// ⚠️ <c>HOME</c> is deliberately NOT redirected, although <see cref="TestRepository"/> does exactly
/// that for its own child processes. MEASURED: setting <c>HOME</c> from inside the process with
/// <c>Environment.SetEnvironmentVariable</c> makes
/// <c>Environment.GetFolderPath(LocalApplicationData)</c> and <c>ApplicationData</c> return an
/// <b>empty string</b> (<c>UserProfile</c> follows along, the XDG pair does not) — and a test that
/// checks the git discovery paths broke on that. The environment of THIS process is shared with
/// .NET's own APIs; only git-specific variables may be touched here.
/// </para>
/// <para>
/// <c>GIT_CONFIG_GLOBAL</c> needs git 2.32 while the minimum is 2.30 (ADR-0002); older versions
/// ignore it, which costs nothing — the system configuration, where the macOS helper actually comes
/// from, is closed off by <c>GIT_CONFIG_NOSYSTEM</c> on every version.
/// </para>
/// </remarks>
internal static class TestEnvironment
{
    /// <summary>
    /// Runs before the first test: a module initializer is guaranteed to have finished before any
    /// member of this assembly is touched, so no test can start git ahead of it.
    /// </summary>
    /// <remarks>
    /// CA2255 warns against module initializers in <b>libraries</b>, where an unexpected side effect
    /// would hit the consumer. This assembly is a test executable and the side effect — an isolated
    /// environment — is the very point.
    /// </remarks>
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Isolate()
    {
        // An EMPTY file, not /dev/null: that device does not exist on Windows, and the same
        // variable has to work on all three platforms.
        string emptyConfig = Path.Combine(
            Path.GetTempPath(),
            "gitext-test-gitconfig-"
            + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            File.WriteAllText(emptyConfig, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the file the global configuration stays visible; the system one — the actual
            // macOS cause — is closed off by the variable below in any case.
            return;
        }

        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", emptyConfig);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                File.Delete(emptyConfig);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover empty file in the temp folder is not worth failing a test run for.
            }
        };
    }
}
