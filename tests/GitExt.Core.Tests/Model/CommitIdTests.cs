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
        // git artık SHA-256 depoları destekliyor; 40 karakter varsayımı yanlış olurdu.
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
        // git küçük harf üretir; karşılaştırmaların tutarlı olması için normalleştiriyoruz.
        CommitId upper = CommitId.Parse(FullSha1.ToUpperInvariant());

        upper.Value.ShouldBe(FullSha1);
        upper.ShouldBe(CommitId.Parse(FullSha1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]                       // MinimumLength'ten kısa
    [InlineData("zzzzzzz")]                   // onaltılık değil
    [InlineData("src/GitExt.Core/Program.cs")] // dosya yolu — tip sisteminin engellediği hata
    [InlineData("HEAD")]                       // ref adı
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
        // 40 ile 64 arasındaki uzunluklar SHA-1 için geçersiz görünse de, SHA-256 depolarında
        // meşru bir kısaltmadır. "40 değilse geçersiz" varsayımı yanlış olurdu.
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
        // git log --oneline ile aynı.
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
        // Ama eşit DEĞİLLER — kısaltılmış bir SHA'yı tam SHA yerine kullanmak hatadır.
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
