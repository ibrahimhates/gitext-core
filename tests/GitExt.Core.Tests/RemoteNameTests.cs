using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T05 — uzak depo adı doğrulaması.
/// </summary>
/// <remarks>
/// <para>
/// Bu dosyanın <b>asıl testi</b> <see cref="Kurallarimiz_gercek_git_remote_add_ile_AYNI_cevabi_veriyor"/>.
/// Doğrulama saf C# (her tuş vuruşunda süreç başlatmamak için) ve sapması sessiz olurdu.
/// </para>
/// <para>
/// 🔴 <b>Oracle <c>check-ref-format</c> DEĞİL, gerçek <c>git remote add</c>.</b> Ölçüldü:
/// <c>check-ref-format --branch HEAD</c> reddediyor ama <c>git remote add HEAD …</c>
/// <b>kabul ediyor</b> — dal kuralları burada geçerli değil.
/// </para>
/// </remarks>
public class RemoteNameTests
{
    /// <summary>
    /// Doğrulamanın <b>bilinçli olarak</b> git'ten ayrıldığı adlar.
    /// </summary>
    /// <remarks>
    /// Her ikisi de ölçümde git tarafından <b>kabul edildi</b> ve ikisini de biz reddediyoruz:
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>refs/…</c>: git <c>refs/remotes/refs/remotes/x/*</c> altına yazan bir remote
    ///     oluşturuyor — kullanıcı <c>branch -a</c> çıktısından ad kopyalarsa sessizce
    ///     iç içe bir ad elde ediyor.
    ///   </description></item>
    ///   <item><description>
    ///     <c>-</c> ile başlayan ad: yalnızca <c>--</c> ayracı kullanılan çağrılarda
    ///     çalışıyor; kullanıcı aynı adı terminalde yazdığında <c>unknown switch</c> alıyor.
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

        // `--` ayracı: adın kendisi sınanıyor, git'in bayrak ayrıştırması değil.
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
        // Bu testin varlık sebebi: `BranchName` yeniden kullanılsaydı `HEAD` adlı bir uzak
        // depo — git'in izin verdiği bir ad — sebepsiz reddedilirdi.
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
        // Hepsi tek bir "ad geçersiz" metnine düşseydi kullanıcı ne düzelteceğini bilemezdi
        // (P06-T02'deki dört seçenek metninin aynı gerekçesi).
        RemoteNameProblem[] problems = Enum.GetValues<RemoteNameProblem>();

        problems
            .Select(RemoteName.Describe)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(problems.Length);
    }
}
