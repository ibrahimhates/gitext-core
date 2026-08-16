using System.Diagnostics;
using GitExt.Core.Git;

namespace GitExt.Core.Tests;

/// <summary>
/// Verifies that git is run on the host from inside a Flatpak sandbox
/// (P10-T10, ADR-0009).
/// </summary>
/// <remarks>
/// The gap these tests close is the wrapping being <b>silently incomplete</b>.
/// A missing working directory runs the command against the wrong repository; a missing environment
/// variable runs git without the user's configuration. Neither gives an error, they just produce the
/// wrong result.
/// </remarks>
public class SandboxLauncherTests
{
    private static ProcessStartInfo GitCommit(string workingDirectory = "/home/user/repo")
    {
        ProcessStartInfo info = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
        };

        info.ArgumentList.Add("commit");
        info.ArgumentList.Add("-m");
        info.ArgumentList.Add("mesaj içinde boşluk var");

        return info;
    }

    [Fact]
    public void Sandbox_disinda_hicbir_sey_degismiyor()
    {
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: false);

        info.FileName.ShouldBe("git");
        info.ArgumentList.ShouldBe(["commit", "-m", "mesaj içinde boşluk var"]);
    }

    [Fact]
    public void Sandbox_icinde_flatpak_spawn_ile_sarmalaniyor()
    {
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.FileName.ShouldBe("flatpak-spawn");
        info.ArgumentList[0].ShouldBe("--host");
    }

    [Fact]
    public void Calisma_dizini_argüman_olarak_aktariliyor()
    {
        // flatpak-spawn DOES NOT CARRY the calling process's working directory to the host side.
        // Unless it is passed, git runs in the host user's home directory — that is, against the wrong
        // repository. It gives no error, it just gives the wrong answer.
        ProcessStartInfo info = GitCommit("/home/user/projects/gitext-core");

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.ArgumentList.ShouldContain("--directory=/home/user/projects/gitext-core");
    }

    [Fact]
    public void Ortam_degiskenleri_aktariliyor()
    {
        // A process started with --host DOES NOT INHERIT the sandbox's environment. Unless everything
        // GitEnvironment sets up (LC_ALL, the GIT_* overrides, askpass) is passed on, the git on the
        // host runs with an entirely different configuration.
        ProcessStartInfo info = GitCommit();
        info.Environment["LC_ALL"] = "C";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.ArgumentList.ShouldContain("--env=LC_ALL=C");
        info.ArgumentList.ShouldContain("--env=GIT_TERMINAL_PROMPT=0");
    }

    [Fact]
    public void Komut_ve_argumanlari_sirasiyla_korunuyor()
    {
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        // The command name and its arguments must come AFTER the flags and in their own order.
        int gitIndex = info.ArgumentList.IndexOf("git");
        gitIndex.ShouldBeGreaterThan(0);

        info.ArgumentList.Skip(gitIndex).ShouldBe(["git", "commit", "-m", "mesaj içinde boşluk var"]);
    }

    [Fact]
    public void Bosluk_iceren_arguman_tek_parca_kaliyor()
    {
        // The arguments go through ArgumentList, not as a joined command line. Joined, commit messages
        // containing spaces would be split and git would take them for separate arguments.
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.ArgumentList.ShouldContain("mesaj içinde boşluk var");
    }

    [Fact]
    public void Sarmalama_iki_kez_uygulanmiyor()
    {
        // Wrapping a second time would produce "flatpak-spawn --host flatpak-spawn --host git":
        // it does not work and its error is incomprehensible. Wrappers being applied twice is a classic
        // accident, so this is idempotent.
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: true);
        List<string> afterFirst = [.. info.ArgumentList];

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.FileName.ShouldBe("flatpak-spawn");
        info.ArgumentList.ShouldBe(afterFirst);
        info.ArgumentList.Count(a => a == "--host").ShouldBe(1);
    }

    [Fact]
    public void Bu_makinede_sandbox_algilanmiyor()
    {
        // The tests do not run inside a Flatpak sandbox; were the detection to give a false positive,
        // every git call would be routed to a flatpak-spawn that does not exist.
        SandboxLauncher.IsSandboxed.ShouldBeFalse();
    }

    [Fact]
    public void Sandbox_disinda_host_dogrulamasi_sorunsuz_geciyor()
    {
        // Outside a sandbox this call must do nothing — otherwise the application would fail to start
        // on every system without flatpak-spawn.
        Should.NotThrow(SandboxLauncher.EnsureHostAccessible);
    }
}
