using GitExt.Core.Model;

namespace GitExt.Core.Tests.Model;

public class CommitIdTests
{
    private const string FullSha1 = "1e2d3c4b5a69788796a5b4c3d2e1f00112233445";
    private const string FullSha256 =
        "1e2d3c4b5a69788796a5b4c3d2e1f001122334451e2d3c4b5a69788796a5b4c3";

    [Fact]
    public void Tam_sha1_ayristirilir()
    {
        CommitId id = CommitId.Parse(FullSha1);

        id.Value.ShouldBe(FullSha1);
        id.IsFull.ShouldBeTrue();
        id.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Sha256_depolari_desteklenir()
    {
        // git now supports SHA-256 repositories; a 40-character assumption would be wrong.
        CommitId id = CommitId.Parse(FullSha256);

        id.IsFull.ShouldBeTrue();
        id.Value.Length.ShouldBe(64);
    }

    [Fact]
    public void Kisaltilmis_sha_gecerlidir_ama_tam_degildir()
    {
        CommitId id = CommitId.Parse("1e2d3c4");

        id.IsFull.ShouldBeFalse();
        id.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Buyuk_harfli_girdi_normallestirilir()
    {
        // git produces lowercase; we normalise so that comparisons stay consistent.
        CommitId upper = CommitId.Parse(FullSha1.ToUpperInvariant());

        upper.Value.ShouldBe(FullSha1);
        upper.ShouldBe(CommitId.Parse(FullSha1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]                       // shorter than MinimumLength
    [InlineData("zzzzzzz")]                   // not hexadecimal
    [InlineData("src/GitExt.Core/Program.cs")] // file path — the mistake the type system prevents
    [InlineData("HEAD")]                       // ref name
    [InlineData("main")]
    public void Gecersiz_girdiler_reddedilir(string? value)
    {
        CommitId.TryParse(value, out _).ShouldBeFalse();
    }

    [Fact]
    public void Cok_uzun_deger_reddedilir()
    {
        CommitId.TryParse(new string('a', 65), out _).ShouldBeFalse();
    }

    [Fact]
    public void Kisaltilmis_sha256_gecerlidir()
    {
        // Lengths between 40 and 64 look invalid for SHA-1, yet in SHA-256 repositories they are a
        // legitimate abbreviation. The "invalid unless 40" assumption would be wrong.
        CommitId.TryParse(new string('a', 42), out CommitId id).ShouldBeTrue();

        id.IsFull.ShouldBeFalse();
    }

    [Fact]
    public void Parse_gecersiz_girdide_aciklayici_hata_firlatir()
    {
        FormatException exception = Should.Throw<FormatException>(() => CommitId.Parse("HEAD"));

        exception.Message.ShouldContain("HEAD");
    }

    [Fact]
    public void Kisa_gosterim_varsayilan_olarak_yedi_karakter()
    {
        // Same as git log --oneline.
        CommitId.Parse(FullSha1).ToShortString().ShouldBe("1e2d3c4");
    }

    [Fact]
    public void Kisa_gosterim_deger_zaten_kisaysa_oldugu_gibi_doner()
    {
        CommitId.Parse("1e2d").ToShortString(7).ShouldBe("1e2d");
    }

    [Fact]
    public void Onek_kontrolu_esitlikten_farklidir()
    {
        CommitId abbreviated = CommitId.Parse("1e2d3c4");
        CommitId full = CommitId.Parse(FullSha1);

        abbreviated.IsPrefixOf(full).ShouldBeTrue();
        // But they are NOT equal — using an abbreviated SHA in place of a full SHA is a bug.
        abbreviated.ShouldNotBe(full);
    }

    [Fact]
    public void Varsayilan_deger_bostur()
    {
        CommitId id = default;

        id.IsEmpty.ShouldBeTrue();
        id.Value.ShouldBe(string.Empty);
        id.ToString().ShouldBe(string.Empty);
    }
}
