using GitExt.Core.Git;

namespace GitExt.Core.Tests.Git;

/// <summary>
/// <c>git</c> çalıştırılabilirinin aday yollarını doğrular (P02-T02, P10-T19).
/// </summary>
/// <remarks>
/// <para>
/// Bu liste eksik olduğunda sonuç "git bulunamadı" — yani uygulama, git'i kurulu olan
/// bir makinede hiç açılmıyor. P10-T19'da Wine altında <b>gerçek Git for Windows</b> ile
/// ölçüldü: Scoop ve Chocolatey ile kurulmuş git bulunamıyordu, çünkü ikisi de Git for
/// Windows'un kurulum yolunu kullanmıyor.
/// </para>
/// <para>
/// Windows listesi platformdan bağımsız olarak test ediliyor. Yalnızca Windows'ta koşan
/// bir test, Linux'ta geliştirilen bu projede hiç çalıştırılmazdı — ve tam da bu yüzden
/// eksik fark edilmemişti.
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
        // Sessizce başka bir git'e düşmek, teşhisi çok zor davranış farkları üretir:
        // kullanıcı 2.30'u işaret etmişken 2.47 çalışıyor olabilir.
        Candidates(windows, "/opt/ozel/git").ShouldBe(["/opt/ozel/git"]);
    }

    [Theory]
    [InlineData(true, "git.exe")]
    [InlineData(false, "git")]
    public void Ilk_aday_her_zaman_PATH_uzerinden(bool windows, string expected)
    {
        // En yaygın ve kullanıcının beklediği durum; ilk denenmesi hem doğru hem hızlı.
        Candidates(windows)[0].ShouldBe(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Adaylar_arasinda_tekrar_yok(bool windows)
    {
        // Tekrar eden aday, aynı başarısız çağrının iki kez yapılması demek — git
        // kurulu değilken açılışı yavaşlatıyor.
        List<string> candidates = Candidates(windows);

        candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(candidates.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hicbir_aday_bos_degil(bool windows)
    {
        // Environment.GetFolderPath tanımsız klasörler için boş string döndürüyor;
        // bunun aday listesine sızması Process.Start'ta anlamsız bir hata üretirdi.
        Candidates(windows).ShouldAllBe(c => !string.IsNullOrWhiteSpace(c));
    }

    [Fact]
    public void Windows_paket_yoneticisi_yollari_kapsaniyor()
    {
        // 🔴 P10-T19'da Wine altında GERÇEK Git for Windows (MinGit 2.47.1) ile ölçüldü:
        // bu yollar eklenmeden önce Scoop veya Chocolatey ile git kurmuş kullanıcıda
        // uygulama "git bulunamadı" diyordu. Eklendikten sonra ikisi de bulundu ve
        // gerçek bir depo okunabildi.
        string joined = string.Join("|", Candidates(windows: true));

        joined.ShouldContain("scoop", Case.Insensitive);
        joined.ShouldContain("chocolatey", Case.Insensitive);
    }

    [Fact]
    public void Windows_adaylari_git_for_windows_konumunu_iceriyor()
    {
        // Git for Windows PATH'e eklenmeden kurulabiliyor; bu, kurulumun varsayılan yolu.
        string joined = string.Join("|", Candidates(windows: true));

        joined.ShouldContain("Git", Case.Sensitive);
        joined.ShouldContain("cmd", Case.Insensitive);
    }

    [Fact]
    public void Windows_adaylarinin_tamami_exe_uzantili()
    {
        // Uzantısız bir yol Windows'ta çalıştırılamaz. Ölçüldü (P10-T19): keşif
        // `git.bat` gibi bir dosyayı da kabul etmiyor, yalnızca `git.exe` arıyor.
        Candidates(windows: true)
            .ShouldAllBe(c => c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unix_yollari_kapsaniyor()
    {
        List<string> candidates = Candidates(windows: false);

        // Homebrew (Apple Silicon), Homebrew (Intel) ve klasik Unix konumu.
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
