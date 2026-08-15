using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T01 — branch name validation.
/// </summary>
/// <remarks>
/// <para>
/// The <b>real test</b> of this file is <see cref="Kurallarimiz_gercek_git_ile_AYNI_cevabi_veriyor"/>:
/// because the validation is pure C# it can drift from git, and the drift is silent — the user
/// either sees a valid name rejected or types a name git will reject and gets an error.
/// The differential test feeds the same names to both us and real <c>git check-ref-format --branch</c>.
/// </para>
/// <para>
/// <b>Why `--branch`?</b> Measured: <c>git branch</c> itself also applies these rules
/// (even after the <c>--</c> separator <c>HEAD</c> and <c>-x</c> are rejected), whereas
/// <c>--allow-onelevel refs/heads/&lt;name&gt;</c> <b>accepts both</b> of them.
/// </para>
/// </remarks>
public class BranchNameTests
{
    /// <summary>
    /// The names of the differential test. Every line showed a behaviour during measurement.
    /// </summary>
    /// <remarks>
    /// ⚠️ Names containing <c>@{…}</c> are <b>not</b> here: git does not validate them,
    /// it <b>translates</b> them, so they cannot be compared on a "valid/invalid" axis.
    /// Their behaviour is pinned separately in the
    /// <see cref="Revizyon_sozdizimi_git_te_BASKA_bir_ada_ceviriliyor"/> test.
    /// </remarks>
    public static TheoryData<string> Corpus =>
    [
        "iyi", "feature/x", "a/b/c/d", "türkçe", "büyük/KÜÇÜK", "v1.0", "@", "x@y",
        "1", "-", "--", "-x", "HEAD", "head", "HEADX",
        "a b", "iki  bosluk", " basta-bosluk", "sonda-bosluk ",
        "a~b", "a^b", "a:b", "a?b", "a*b", "a[b", "a\\b", "a\u007Fb", "a\tb",
        ".gizli", "a/.gizli", "sonu.lock", "a/sonu.lock", "sonu.lockx",
        "a..b", "a.b", "sonda.nokta.", "a/", "/basta", "a//b", "a/b/",
        "...", "a.", ".", "@@", "x/@", "d.lock/e",
    ];

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Kurallarimiz_gercek_git_ile_AYNI_cevabi_veriyor(string name)
    {
        using TestRepository repository = TestRepository.CreateEmpty();

        bool gitAccepts = repository.TryGit("check-ref-format", "--branch", name).ExitCode == 0;

        BranchName.IsValid(name).ShouldBe(
            gitAccepts,
            $"'{name}' için git {(gitAccepts ? "kabul" : "ret")} diyor, biz tersini diyoruz.");
    }

    [Fact]
    public void Revizyon_sozdizimi_git_te_BASKA_bir_ada_ceviriliyor()
    {
        // 🔴 MEASURED — the reason this test exists: `check-ref-format --branch` does NOT VALIDATE,
        // it TRANSLATES. `@{-1}` means "the previous branch"; the exit code is 0 but the output
        // is NOT the name that was written. Had we based validation on this, the user would see
        // "valid", then a completely different branch name would be created (or they would get an "already exists" error).
        using TestRepository repository = TestRepository.CreateEmpty();

        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");
        repository.Git("branch", "ikinci");
        repository.Git("switch", "ikinci");
        repository.Git("switch", "-");

        // git: SAYS it is valid and translates it to `ikinci`…
        repository.TryGit("check-ref-format", "--branch", "@{-1}").ExitCode.ShouldBe(0);
        repository.Git("check-ref-format", "--branch", "@{-1}").Trim().ShouldBe("ikinci");

        // …we reject it.
        BranchName.Validate("@{-1}").ShouldBe(BranchNameProblem.RevisionSyntax);
        BranchName.Validate("x@{u}").ShouldBe(BranchNameProblem.RevisionSyntax);
    }

    [Fact]
    public void Tam_ref_adi_yapistirmak_reddediliyor()
    {
        // 🔴 MEASURED: git does NOT treat this as an error — `git branch refs/heads/x`
        // creates `refs/heads/refs/heads/x`. So the differential test marks this name as "git accepts it";
        // rejecting it is our own decision.
        BranchName.Validate("refs/heads/x").ShouldBe(BranchNameProblem.NestedRefsPrefix);
    }

    [Fact]
    public void Bos_ad_reddediliyor()
    {
        BranchName.Validate(null).ShouldBe(BranchNameProblem.Empty);
        BranchName.Validate("").ShouldBe(BranchNameProblem.Empty);
        BranchName.Validate("   ").ShouldBe(BranchNameProblem.Empty);
    }

    [Theory]
    [InlineData("-x", BranchNameProblem.LeadingDash)]
    [InlineData("HEAD", BranchNameProblem.ReservedHead)]
    [InlineData("a b", BranchNameProblem.ForbiddenCharacter)]
    [InlineData("a~b", BranchNameProblem.ForbiddenCharacter)]
    [InlineData(".gizli", BranchNameProblem.InvalidSegment)]
    [InlineData("sonu.lock", BranchNameProblem.InvalidSegment)]
    [InlineData("a//b", BranchNameProblem.EmptySegment)]
    [InlineData("a/", BranchNameProblem.EmptySegment)]
    [InlineData("a..b", BranchNameProblem.InvalidDot)]
    [InlineData("sonda.", BranchNameProblem.InvalidDot)]
    public void Sorun_TURU_dogru_bildiriliyor(string name, BranchNameProblem expected)
    {
        // The kind matters because the UI tells the user the "why"; showing all of them as
        // "invalid name" makes fixing it while typing impossible.
        BranchName.Validate(name).ShouldBe(expected);
    }
}
