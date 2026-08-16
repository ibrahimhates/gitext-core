using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitExt.Core.Git;

/// <summary>
/// A <c>git</c> executable that has been located and whose version has been validated.
/// </summary>
/// <remarks>
/// If an instance of this type exists, it means <c>git</c> was found and verified to satisfy the
/// <see cref="GitVersion.Minimum"/> requirement. Moving that validation into the type system
/// prevents the "is git even there" check from being repeated at every call site.
/// </remarks>
public sealed class GitExecutable
{
    private GitExecutable(string path, GitVersion version)
    {
        Path = path;
        Version = version;
    }

    /// <summary>Full path of the executable.</summary>
    public string Path { get; }

    /// <summary>The validated version.</summary>
    public GitVersion Version { get; }

    /// <summary>
    /// Searches the system for <c>git</c>, reads its version and validates it.
    /// </summary>
    /// <param name="explicitPath">
    /// Explicit path coming from user settings. If supplied, no search is performed.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GitNotFoundException">When the executable cannot be found.</exception>
    /// <exception cref="GitVersionTooOldException">If the version is too old.</exception>
    public static async Task<GitExecutable> LocateAsync(
        string? explicitPath = null,
        CancellationToken cancellationToken = default)
    {
        // If we are inside a sandbox, host access is verified to actually be possible FIRST.
        // If it is unreachable we stop here; silently falling back to a git inside the sandbox
        // causes commits to be silently dropped in repositories that have hooks (ADR-0009).
        SandboxLauncher.EnsureHostAccessible();

        List<string> attempted = [];

        foreach (string candidate in EnumerateCandidates(explicitPath))
        {
            attempted.Add(candidate);

            GitVersion? version = await TryReadVersionAsync(candidate, cancellationToken)
                .ConfigureAwait(false);

            if (version is not { } found)
            {
                continue;
            }

            if (found < GitVersion.Minimum)
            {
                throw new GitVersionTooOldException(found, candidate);
            }

            return new GitExecutable(candidate, found);
        }

        throw new GitNotFoundException(
            "No runnable 'git' was found. Git must be installed and on your PATH. "
            + $"Paths tried: {string.Join(", ", attempted)}");
    }

    /// <summary>
    /// Produces the candidate paths, in the order they are tried.
    /// </summary>
    internal static IEnumerable<string> EnumerateCandidates(string? explicitPath) =>
        EnumerateCandidates(explicitPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

    /// <summary>
    /// Candidate generation; the target platform can be supplied from outside.
    /// </summary>
    /// <remarks>
    /// Split out for testability. If the Windows candidate list could only be validated by a
    /// test that runs on Windows, it would never be exercised in this project, which is
    /// developed on Linux — and the gap found in P10-T19 (Scoop/Chocolatey paths) could not
    /// have been caught.
    /// </remarks>
    internal static IEnumerable<string> EnumerateCandidates(string? explicitPath, bool windows)
    {
        // If the user gave an explicit path, try only that one — silently falling back to a
        // different git leads to behavioural differences that are hard to diagnose.
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return explicitPath;
            yield break;
        }

        // Via PATH: the most common case and the one the user expects.
        yield return windows ? "git.exe" : "git";

        if (windows)
        {
            // Git for Windows default locations. It can be installed without being added to PATH.
            foreach (string root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     })
            {
                if (!string.IsNullOrEmpty(root))
                {
                    yield return System.IO.Path.Combine(root, "Git", "cmd", "git.exe");
                }
            }

            // git installed via package managers (P10-T19). These do NOT use the Git for
            // Windows installation path, and looking only at the list above produced
            // "git not found" for users who installed git with Scoop or Chocolatey.
            //
            // Normally both are added to PATH, so the first candidate already matches. These
            // paths are for the cases where PATH is incomplete: when the application is opened
            // via a shortcut or by a launcher that does not inherit PATH.
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(userProfile))
            {
                // Scoop: per-user installation, under shims/.
                yield return System.IO.Path.Combine(userProfile, "scoop", "shims", "git.exe");
                yield return System.IO.Path.Combine(userProfile, "scoop", "apps", "git", "current", "cmd", "git.exe");
            }

            // Chocolatey: system-wide, defaults to C:\ProgramData\chocolatey.
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            if (!string.IsNullOrEmpty(programData))
            {
                yield return System.IO.Path.Combine(programData, "chocolatey", "bin", "git.exe");
                // Scoop's global installation also lands here.
                yield return System.IO.Path.Combine(programData, "scoop", "shims", "git.exe");
            }
        }
        else
        {
            // Homebrew (Apple Silicon and Intel), Nix and the classic Unix locations.
            yield return "/opt/homebrew/bin/git";
            yield return "/usr/local/bin/git";
            yield return "/usr/bin/git";
        }
    }

    /// <summary>
    /// Runs the candidate and reads its version; returns <see langword="null"/> if it cannot be run.
    /// </summary>
    private static async Task<GitVersion?> TryReadVersionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add("--version");
        startInfo.Environment["LC_ALL"] = "C";

        // If we are inside a Flatpak sandbox, the git on the host is searched for (ADR-0009).
        // Finding the git inside the sandbox would mean treating a git that cannot see the
        // user's hooks and configuration as "found".
        SandboxLauncher.RewriteForHost(startInfo);

        try
        {
            using Process process = new() { StartInfo = startInfo };

            if (!process.Start())
            {
                return null;
            }

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken)
                .ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            return GitVersion.TryParse(output, out GitVersion version) ? version : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                       or FileNotFoundException
                                       or DirectoryNotFoundException)
        {
            // No executable at the candidate path — try the next one.
            return null;
        }
    }
}
