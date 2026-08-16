using System.Diagnostics;

namespace GitExt.Core.Git;

/// <summary>
/// If the application is running inside a Flatpak sandbox, makes <c>git</c> run on the host
/// (P10-T10, ADR-0009).
/// </summary>
/// <remarks>
/// <para>
/// The whole rationale of ADR-0002 is to reach <b>the user's own git</b>: hooks, credential
/// helpers, LFS, <c>.gitconfig</c>. A git inside the sandbox can see none of these.
/// </para>
/// <para>
/// 🔴 <b>Measured (P10-T10):</b> in an environment that has <c>git</c> but no <c>python3</c> —
/// that is, the exact equivalent of a runtime that embeds git — running <c>git commit</c> in a
/// repository with a <c>pre-commit</c> hook written in Python:
/// <b>the commit was not made, but the exit code was 0.</b> The only symptom was a single line
/// <c>env</c> wrote to stderr. The UI would say "committed" and the user would only notice it
/// later — on a push that sends nothing.
/// </para>
/// <para>
/// That is why, inside a sandbox, git is run <b>on the host</b>. If <c>flatpak-spawn</c> is
/// missing the application fails <b>loudly</b>; silently falling back to a git inside the
/// sandbox would bring back the bug described above.
/// </para>
/// </remarks>
public static class SandboxLauncher
{
    /// <summary>
    /// The info file Flatpak always creates inside the sandbox.
    /// </summary>
    /// <remarks>
    /// This file is used rather than an environment variable (<c>FLATPAK_ID</c>): environment
    /// variables can be cleared while being passed to child processes and can be faked by the
    /// user. The existence of the file is guaranteed by the sandbox itself.
    /// </remarks>
    private const string FlatpakInfoPath = "/.flatpak-info";

    private const string SpawnExecutable = "flatpak-spawn";

    /// <summary>
    /// Is the application running inside a Flatpak sandbox?
    /// </summary>
    public static bool IsSandboxed { get; } = File.Exists(FlatpakInfoPath);

    /// <summary>
    /// While inside a sandbox, rewrites a process so that it runs on the host.
    /// Outside a sandbox, <paramref name="startInfo"/> is left as it is.
    /// </summary>
    /// <remarks>
    /// The working directory is passed as an argument: <c>flatpak-spawn</c> does not carry the
    /// calling process's working directory over to the host side, the process on the host starts
    /// in its own directory. When this is skipped, commands would run against the wrong repository.
    /// </remarks>
    public static void RewriteForHost(ProcessStartInfo startInfo) =>
        RewriteForHost(startInfo, IsSandboxed);

    /// <summary>
    /// The wrapping itself; the sandbox state can be supplied from outside.
    /// </summary>
    /// <remarks>
    /// Split out for testability: the correctness of the wrapping must be verifiable without
    /// setting up a real Flatpak sandbox. If the wrapping is wrong — a missing environment
    /// variable, a lost working directory — the result would be git silently running against the
    /// wrong repository or the wrong configuration.
    /// </remarks>
    internal static void RewriteForHost(ProcessStartInfo startInfo, bool sandboxed)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (!sandboxed)
        {
            return;
        }

        // If it is already wrapped, do not wrap again: "flatpak-spawn --host flatpak-spawn
        // --host git" does not work and its error is incomprehensible too. There is only one
        // call site today, but applying a wrapper like this twice is a classic accident.
        if (string.Equals(startInfo.FileName, SpawnExecutable, StringComparison.Ordinal))
        {
            return;
        }

        List<string> hostArguments = ["--host"];

        // Environment variables must be forwarded explicitly too: a process started with --host
        // does NOT inherit the sandbox's environment. If everything GitEnvironment sets up
        // (LC_ALL, GIT_* overrides, authentication variables) is not forwarded here, the git on
        // the host runs with a completely different configuration.
        foreach ((string name, string? value) in startInfo.Environment)
        {
            if (value is not null)
            {
                hostArguments.Add($"--env={name}={value}");
            }
        }

        if (!string.IsNullOrEmpty(startInfo.WorkingDirectory))
        {
            hostArguments.Add($"--directory={startInfo.WorkingDirectory}");
        }

        hostArguments.Add(startInfo.FileName);
        hostArguments.AddRange(startInfo.ArgumentList);

        startInfo.FileName = SpawnExecutable;
        startInfo.ArgumentList.Clear();

        foreach (string argument in hostArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    /// <summary>
    /// Verifies that, while inside a sandbox, host access is actually possible.
    /// </summary>
    /// <exception cref="GitNotFoundException">
    /// If we are inside a sandbox but <c>flatpak-spawn</c> does not work.
    /// </exception>
    /// <remarks>
    /// Called once at startup. Stopping here instead of silently falling back to a git inside
    /// the sandbox is deliberate: ADR-0009 explicitly forbids the silent fallback.
    /// </remarks>
    public static void EnsureHostAccessible()
    {
        if (!IsSandboxed)
        {
            return;
        }

        ProcessStartInfo probe = new()
        {
            FileName = SpawnExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        probe.ArgumentList.Add("--host");
        probe.ArgumentList.Add("true");

        try
        {
            using Process process = Process.Start(probe)
                ?? throw new GitNotFoundException(SandboxFailureMessage);

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new GitNotFoundException(SandboxFailureMessage);
            }
        }
        catch (Exception ex) when (ex is not GitNotFoundException)
        {
            throw new GitNotFoundException(SandboxFailureMessage, ex);
        }
    }

    private const string SandboxFailureMessage =
        "Running inside a Flatpak sandbox but the host 'git' is unreachable "
        + "(flatpak-spawn --host failed). gitext-core has to use your git so that your hooks and "
        + "credential helpers keep working "
        + "(ADR-0002, ADR-0009). Required permission: --talk-name=org.freedesktop.Flatpak. "
        + "We do not fall back to a git inside the sandbox: it was measured that in repositories with hooks the commit "
        + "is silently not made while the exit code is still 0.";
}
