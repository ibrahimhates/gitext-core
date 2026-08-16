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
    public void Ters_egik_cizgi_YALNIZCA_Windowsta_normallestirilir()
    {
        // If a path coming from Windows is handed to git with backslashes, git thinks they are part
        // of the file name and the file is "not found" — converting is mandatory there.
        //
        // 🔴 But on Linux `\` is a VALID character in a file name (measured in P05-T08) and git
        // reports it as-is. Converting on every platform SILENTLY turned the path of a file named
        // `ters\slash.txt` into `ters/slash.txt`.
        RepositoryPath path = RepositoryPath.Parse(@"src\GitExt.Core\Program.cs");

        path.Value.ShouldBe(
            OperatingSystem.IsWindows()
                ? "src/GitExt.Core/Program.cs"
                : @"src\GitExt.Core\Program.cs");
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
        // ".gitignore" is a file without an extension; its extension is not ".gitignore".
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
        // Must use the platform's separator; on Windows that is the backslash.
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
