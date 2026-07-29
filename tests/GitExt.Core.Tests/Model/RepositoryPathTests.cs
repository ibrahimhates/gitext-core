using GitExt.Core.Model;

namespace GitExt.Core.Tests.Model;

public class RepositoryPathTests
{
    [Fact]
    public void Egik_cizgili_yol_oldugu_gibi_kalir()
    {
        RepositoryPath path = RepositoryPath.Parse("src/GitExt.Core/Program.cs");

        path.Value.ShouldBe("src/GitExt.Core/Program.cs");
    }

    [Fact]
    public void Ters_egik_cizgi_normallestirilir()
    {
        // Windows'tan gelen bir yol git'e ters eğik çizgiyle verilirse git onu
        // dosya adının parçası sanar ve dosya "bulunamaz".
        RepositoryPath path = RepositoryPath.Parse(@"src\GitExt.Core\Program.cs");

        path.Value.ShouldBe("src/GitExt.Core/Program.cs");
    }

    [Fact]
    public void Bastaki_ve_sondaki_egik_cizgiler_atilir()
    {
        RepositoryPath.Parse("/src/dosya.cs/").Value.ShouldBe("src/dosya.cs");
    }

    [Fact]
    public void Ad_uzanti_ve_ust_dizin_okunur()
    {
        RepositoryPath path = RepositoryPath.Parse("src/GitExt.Core/Program.cs");

        path.Name.ShouldBe("Program.cs");
        path.Extension.ShouldBe(".cs");
        path.Parent.Value.ShouldBe("src/GitExt.Core");
    }

    [Fact]
    public void Kokteki_dosyanin_ust_dizini_bostur()
    {
        RepositoryPath path = RepositoryPath.Parse("README.md");

        path.Name.ShouldBe("README.md");
        path.Parent.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Nokta_ile_baslayan_dosyada_uzanti_bos_kabul_edilir()
    {
        // ".gitignore" uzantısız bir dosyadır, uzantısı ".gitignore" değildir.
        RepositoryPath path = RepositoryPath.Parse(".gitignore");

        path.Name.ShouldBe(".gitignore");
        path.Extension.ShouldBe(string.Empty);
    }

    [Fact]
    public void Unicode_ve_boslukli_adlar_korunur()
    {
        const string awkward = "belgeler/çalışma günlüğü ÖĞÜŞİ.md";

        RepositoryPath path = RepositoryPath.Parse(awkward);

        path.Value.ShouldBe(awkward);
        path.Name.ShouldBe("çalışma günlüğü ÖĞÜŞİ.md");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("/")]
    [InlineData(@"C:\Users\test\dosya.cs")]
    public void Gecersiz_yollar_reddedilir(string? value)
    {
        RepositoryPath.TryParse(value, out _).ShouldBeFalse();
    }

    [Fact]
    public void Mutlak_yol_platformun_ayraciyla_uretilir()
    {
        RepositoryPath path = RepositoryPath.Parse("src/dosya.cs");
        string root = Path.Combine(Path.GetTempPath(), "repo");

        string absolute = path.ToAbsolutePath(root);

        absolute.ShouldBe(Path.Combine(root, "src", "dosya.cs"));
        // Platformun ayracını kullanmalı; Windows'ta ters eğik çizgi.
        absolute.ShouldContain(Path.DirectorySeparatorChar);
    }

    [Fact]
    public void Varsayilan_deger_bostur()
    {
        RepositoryPath path = default;

        path.IsEmpty.ShouldBeTrue();
        path.Value.ShouldBe(string.Empty);
    }
}
