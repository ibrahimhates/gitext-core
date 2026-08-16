using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T05 — remote name verification.
/// </summary>
/// <remarks>
/// <para>
/// The <b>real test</b> of this file is <see cref="Kurallarimiz_gercek_git_remote_add_ile_AYNI_cevabi_veriyor"/>.
/// The verification is pure C# (so as not to start a process on every keystroke) and any drift in it
/// would be silent.
/// </para>
/// <para>
/// 🔴 <b>The oracle is NOT <c>check-ref-format</c>, it is real <c>git remote add</c>.</b> Measured:
/// <c>check-ref-format --branch HEAD</c> rejects it but <c>git remote add HEAD …</c>
/// <b>accepts</b> it — branch rules do not apply here.
/// </para>
/// </remarks>
public class RemoteNameTests
{
    /// <summary>
    /// The names where the verification <b>deliberately</b> diverges from git.
    /// </summary>
    /// <remarks>
    /// Both were <b>accepted</b> by git in the measurement and we reject both of them:
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>refs/…</c>: git creates a remote that writes under
    ///     <c>refs/remotes/refs/remotes/x/*</c> — if the user copies a name out of <c>branch -a</c>
    ///     output they silently end up with a nested name.
    ///   </description></item>
    ///   <item><description>
    ///     A name starting with <c>-</c>: it only works in calls that use the <c>--</c> separator;
    ///     when the user types the same name in a terminal they get <c>unknown switch</c>.
    ///   </description></item>
    /// </list>
    /// </remarks>
    private static readonly string[] BilincliSapmalar = ["refs/remotes/x", "refs/heads/x", "-x", "-"];

    public static TheoryData<string> Corpus =>
    [
        "origin", "upstream", "a/b", "a/b/c", "türkçe", "büyük/KÜÇÜK", "v1.0", "a.b",
        "HEAD", "head", "@", "x@y", "1", "fork-1",
        "a b", "iki  bosluk", " basta-bosluk", "sonda-bosluk ",
        "a~b", "a^b", "a:b", "a?b", "a*b", "a[b", "a\\b", "a\u007Fb", "a\tb",
        ".gizli", "a/.gizli", "sonu.lock", "a/sonu.lock", "sonu.lockx",
        "a..b", "sonda.nokta.", "a/", "/basta", "a//b", "a/b/", "...", "a.", ".",
        "refs/remotes/x", "refs/heads/x", "-x", "-",
    ];

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Kurallarimiz_gercek_git_remote_add_ile_AYNI_cevabi_veriyor(string name)
    {
        using TestRepository repository = TestRepository.CreateEmpty();

        // The `--` separator: the name itself is being exercised, not git's flag parsing.
        bool gitAccepts =
            repository.TryGit("remote", "add", "--", name, "https://example.com/x.git").ExitCode == 0;

        bool weAccept = RemoteName.IsValid(name);

        if (BilincliSapmalar.Contains(name, StringComparer.Ordinal))
        {
            gitAccepts.ShouldBeTrue($"'{name}' sapma listesinde ama git artık reddediyor.");
            weAccept.ShouldBeFalse($"'{name}' bilinçli olarak reddedilmeliydi.");
            return;
        }

        weAccept.ShouldBe(
            gitAccepts,
            $"'{name}' için git {(gitAccepts ? "kabul" : "ret")} diyor, biz tersini diyoruz.");
    }

    [Fact]
    public void HEAD_dal_icin_gecersiz_ama_remote_icin_GECERLI()
    {
        // The reason this test exists: if `BranchName` were reused, a remote named `HEAD` — a name
        // git allows — would be rejected for no reason.
        BranchName.IsValid("HEAD").ShouldBeFalse();
        RemoteName.IsValid("HEAD").ShouldBeTrue();
    }

    [Fact]
    public void Refs_onekli_ad_reddediliyor_ve_gerekcesi_yaziliyor()
    {
        RemoteName.Validate("refs/remotes/origin")
            .ShouldBe(RemoteNameProblem.NestedRefsPrefix);

        RemoteName.Describe(RemoteNameProblem.NestedRefsPrefix)
            .ShouldContain("refs/");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Bos_ad_reddediliyor(string? name) =>
        RemoteName.Validate(name).ShouldBe(RemoteNameProblem.Empty);

    [Fact]
    public void Her_sorunun_kendi_metni_var()
    {
        // If they all collapsed into a single "name is invalid" message the user would not know
        // what to fix (the same rationale as the four-option text in P06-T02).
        RemoteNameProblem[] problems = Enum.GetValues<RemoteNameProblem>();

        problems
            .Select(RemoteName.Describe)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(problems.Length);
    }
}
