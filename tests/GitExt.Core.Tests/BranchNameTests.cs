using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T01 — dal adı doğrulaması.
/// </summary>
/// <remarks>
/// <para>
/// Bu dosyanın <b>asıl testi</b> <see cref="Kurallarimiz_gercek_git_ile_AYNI_cevabi_veriyor"/>:
/// doğrulama saf C# olduğu için git'ten sapabilir, ve sapma sessizdir — kullanıcı ya
/// geçerli bir adı reddedilmiş görür ya da git'in reddedeceği bir adı yazıp hata alır.
/// Ayrık test aynı adları hem bize hem gerçek <c>git check-ref-format --branch</c>'a verir.
/// </para>
/// <para>
/// <b>Neden `--branch`?</b> Ölçüldü: <c>git branch</c>'ın kendisi de bu kuralları uyguluyor
/// (<c>--</c> ayracından sonra bile <c>HEAD</c> ve <c>-x</c> reddediliyor), oysa
/// <c>--allow-onelevel refs/heads/&lt;ad&gt;</c> ikisini de <b>kabul ediyor</b>.
/// </para>
/// </remarks>
public class BranchNameTests
{
    /// <summary>
    /// Ayrık testin adları. Her satır ölçümde bir davranış gösterdi.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>@{…}</c> içeren adlar burada <b>yok</b>: git onları doğrulamıyor
    /// <b>çeviriyor</b>, yani "geçerli/geçersiz" ekseninde karşılaştırılamazlar.
    /// Onların davranışı <see cref="Revizyon_sozdizimi_git_te_BASKA_bir_ada_ceviriliyor"/>
    /// testinde ayrıca sabitlendi.
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
        // 🔴 ÖLÇÜLDÜ — bu testin varlık sebebi: `check-ref-format --branch` DOĞRULAMIYOR,
        // ÇEVİRİYOR. `@{-1}` "bir önceki dal" demek; çıkış kodu 0 geliyor ama çıktı
        // yazılan ad DEĞİL. Doğrulamayı buna dayandırsaydık kullanıcı "geçerli" yazısını
        // görür, sonra bambaşka bir dal adı oluşurdu (ya da "zaten var" hatası alırdı).
        using TestRepository repository = TestRepository.CreateEmpty();

        repository.WriteFile("a.txt", "a\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");
        repository.Git("branch", "ikinci");
        repository.Git("switch", "ikinci");
        repository.Git("switch", "-");

        // git: geçerli DİYOR ve `ikinci`ye çeviriyor…
        repository.TryGit("check-ref-format", "--branch", "@{-1}").ExitCode.ShouldBe(0);
        repository.Git("check-ref-format", "--branch", "@{-1}").Trim().ShouldBe("ikinci");

        // …biz reddediyoruz.
        BranchName.Validate("@{-1}").ShouldBe(BranchNameProblem.RevisionSyntax);
        BranchName.Validate("x@{u}").ShouldBe(BranchNameProblem.RevisionSyntax);
    }

    [Fact]
    public void Tam_ref_adi_yapistirmak_reddediliyor()
    {
        // 🔴 ÖLÇÜLDÜ: git bunu hata SAYMIYOR — `git branch refs/heads/x`
        // `refs/heads/refs/heads/x` oluşturuyor. Yani ayrık test bu adı "git kabul ediyor"
        // diye işaretler; reddi bizim kendi kararımız.
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
        // Tür önemli çünkü arayüz kullanıcıya "neden" olduğunu söylüyor; hepsini
        // "geçersiz ad" diye göstermek yazarken düzeltmeyi imkânsız kılar.
        BranchName.Validate(name).ShouldBe(expected);
    }
}
