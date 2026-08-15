using GitExt.Core.Git;

namespace GitExt.Core.Tests.Git;

/// <summary>
/// Verifies the candidate paths for the <c>git</c> executable (P02-T02, P10-T19).
/// </summary>
/// <remarks>
/// <para>
/// When this list is incomplete the result is "git not found" — that is, the application does not open
/// at all on a machine that has git installed. In P10-T19 it was measured under Wine with <b>real Git
/// for Windows</b>: git installed via Scoop and Chocolatey was not found, because neither of them uses
/// Git for Windows' installation path.
/// </para>
/// <para>
/// The Windows list is tested independently of the platform. A test that only runs on Windows would
/// never be executed in this project, which is developed on Linux — and that is exactly why the gap
/// went unnoticed.
/// </para>
/// </remarks>
public class GitExecutableDiscoveryTests
{
    private static List<string> Candidates(bool windows, string? explicitPath = null) =>
        [.. GitExecutable.EnumerateCandidates(explicitPath, windows)];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Acik_yol_verildiginde_baska_hicbir_sey_denenmiyor(bool windows)
    {
        // Silently falling back to another git produces behaviour differences that are very hard to
        // diagnose: 2.47 may be running while the user pointed at 2.30.
        Candidates(windows, "/opt/ozel/git").ShouldBe(["/opt/ozel/git"]);
    }

    [Theory]
    [InlineData(true, "git.exe")]
    [InlineData(false, "git")]
    public void Ilk_aday_her_zaman_PATH_uzerinden(bool windows, string expected)
    {
        // The most common case and the one the user expects; trying it first is both correct and fast.
        Candidates(windows)[0].ShouldBe(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Adaylar_arasinda_tekrar_yok(bool windows)
    {
        // A duplicate candidate means making the same failing call twice — it slows down startup
        // when git is not installed.
        List<string> candidates = Candidates(windows);

        candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(candidates.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hicbir_aday_bos_degil(bool windows)
    {
        // Environment.GetFolderPath returns an empty string for undefined folders;
        // letting that leak into the candidate list would produce a meaningless error in Process.Start.
        Candidates(windows).ShouldAllBe(c => !string.IsNullOrWhiteSpace(c));
    }

    [Fact]
    public void Windows_paket_yoneticisi_yollari_kapsaniyor()
    {
        // 🔴 Measured in P10-T19 under Wine with REAL Git for Windows (MinGit 2.47.1):
        // before these paths were added, a user who installed git via Scoop or Chocolatey got
        // "git not found" from the application. After they were added both were found and a
        // real repository could be read.
        string joined = string.Join("|", Candidates(windows: true));

        joined.ShouldContain("scoop", Case.Insensitive);
        joined.ShouldContain("chocolatey", Case.Insensitive);
    }

    [Fact]
    public void Windows_adaylari_git_for_windows_konumunu_iceriyor()
    {
        // Git for Windows can be installed without being added to PATH; this is the installer's default path.
        string joined = string.Join("|", Candidates(windows: true));

        joined.ShouldContain("Git", Case.Sensitive);
        joined.ShouldContain("cmd", Case.Insensitive);
    }

    [Fact]
    public void Windows_adaylarinin_tamami_exe_uzantili()
    {
        // A path without an extension cannot be executed on Windows. Measured (P10-T19): discovery
        // does not accept a file like `git.bat` either, it only looks for `git.exe`.
        Candidates(windows: true)
            .ShouldAllBe(c => c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unix_yollari_kapsaniyor()
    {
        List<string> candidates = Candidates(windows: false);

        // Homebrew (Apple Silicon), Homebrew (Intel) and the classic Unix location.
        candidates.ShouldContain("/opt/homebrew/bin/git");
        candidates.ShouldContain("/usr/local/bin/git");
        candidates.ShouldContain("/usr/bin/git");
    }

    [Fact]
    public void Unix_adaylarinda_exe_uzantisi_yok()
    {
        Candidates(windows: false)
            .ShouldAllBe(c => !c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }
}
