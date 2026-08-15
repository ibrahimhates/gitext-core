using GitExt.Core.Git;

namespace GitExt.Core.Tests.Git;

public class GitVersionTests
{
    [Theory]
    // Linux
    [InlineData("git version 2.55.0", 2, 55, 0)]
    // macOS — Apple suffix
    [InlineData("git version 2.39.5 (Apple Git-154)", 2, 39, 5)]
    // Windows — platform suffix
    [InlineData("git version 2.47.1.windows.1", 2, 47, 1)]
    // A version with no patch component
    [InlineData("git version 2.30", 2, 30, 0)]
    // A trailing line ending
    [InlineData("git version 2.43.0\n", 2, 43, 0)]
    public void Bilinen_surum_ciktilarini_ayristirir(string output, int major, int minor, int patch)
    {
        GitVersion.TryParse(output, out GitVersion version).ShouldBeTrue();

        version.ShouldBe(new GitVersion(major, minor, patch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("command not found")]
    public void Gecersiz_girdide_basarisiz_olur(string? output)
    {
        GitVersion.TryParse(output, out _).ShouldBeFalse();
    }

    [Fact]
    public void Surumler_sayisal_olarak_karsilastirilir()
    {
        // Had a text comparison been used, "2.9" > "2.10" would come out.
        (new GitVersion(2, 9, 0) < new GitVersion(2, 10, 0)).ShouldBeTrue();
        (new GitVersion(2, 30, 0) >= GitVersion.Minimum).ShouldBeTrue();
        (new GitVersion(2, 29, 9) < GitVersion.Minimum).ShouldBeTrue();
    }

    [Fact]
    public void Bu_makinedeki_git_minimum_surumu_saglar()
    {
        // Because the fixtures rely on real git, this is a precondition of the test environment.
        GitVersion.TryParse(RunGitVersion(), out GitVersion version).ShouldBeTrue();

        version.ShouldBeGreaterThanOrEqualTo(GitVersion.Minimum);
    }

    private static string RunGitVersion()
    {
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
