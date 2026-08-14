using System.Reflection;
using GitExt.Desktop;

namespace GitExt.UI.Tests;

/// <summary>
/// Sürümün git tag'inden türetildiğini doğrular (P10-T01, ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// Bu testlerin kapattığı boşluk: sürüm yanlış olduğunda <b>hiçbir şey kırılmaz.</b>
/// Build yeşil, testler yeşil, paket üretiliyor — sadece adı yanlış. Ölçüldü (P10-T00):
/// sığ klonda MinVer sessizce <c>0.0.0-alpha.0</c> üretiyor ve o değerle yayın yapılabiliyor.
/// </para>
/// </remarks>
public class VersionInfoTests
{
    [Fact]
    public void Surum_derleme_sirasinda_gomulmus_olmali()
    {
        // "bilinmiyor" — özniteliğin hiç üretilmediği durum. Buraya düşmek, sürümleme
        // altyapısının sessizce devre dışı kaldığı anlamına gelir.
        VersionInfo.Version.ShouldNotBe("bilinmiyor");
        VersionInfo.Version.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Surum_gercekten_MinVer_tarafindan_turetilmis_olmali()
    {
        // 🔴 Bu test bir SABOTAJ DOĞRULAMASININ sonucu. MinVer devre dışı bırakıldığında
        // (MinVerSkip=true) diğer tüm testler geçmeye devam etti: SDK varsayılanı `1.0.0`
        // geçerli semver, tüm derlemelerde tutarlı ve sha'sı bile yerinde. Yani sürümleme
        // sessizce kapansa uygulama "gitext-core 1.0.0" derdi ve kimse fark etmezdi.
        //
        // MinVer çalıştığının tek kesin işareti, kendi ürettiği MinVerVersion değeri —
        // Directory.Build.props bunu derlemeye gömüyor.
        string? evidence = typeof(VersionInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MinVerVersion")?.Value;

        evidence.ShouldNotBeNullOrWhiteSpace(
            "MinVerVersion özniteliği yok — sürüm MinVer ile türetilmemiş. "
            + "Sürümleme altyapısı devre dışı kalmış olabilir.");

        // Gömülen sürüm ile uygulamanın bildirdiği sürüm aynı olmalı.
        evidence.ShouldBe(VersionInfo.Version);
    }

    [Fact]
    public void Surum_build_metadatasini_icermemeli()
    {
        // MinVer sürümün sonuna "+<sha>" ekliyor. Paket adlarında ve kullanıcıya
        // gösterilen metinde bunun yeri yok; '+' çoğu paket formatında geçersiz karakter.
        VersionInfo.Version.ShouldNotContain("+");
    }

    [Fact]
    public void Commit_sha_ayri_olarak_okunabilmeli()
    {
        // Hata raporlarında hangi commit'in çalıştığı, sürüm numarasından daha kesin:
        // ön sürümlerde aynı numara birden çok commit'e denk gelebiliyor.
        VersionInfo.Commit.ShouldNotBeNull();
        VersionInfo.Commit!.Length.ShouldBe(40);
        VersionInfo.Commit.ShouldAllBe(c => Uri.IsHexDigit(c));
    }

    [Fact]
    public void Surum_semver_bicinminde_olmali()
    {
        // MAJOR.MINOR.PATCH ile başlamalı (ADR-0006). Ardından ön sürüm eki gelebilir.
        string core = VersionInfo.Version.Split('-')[0];
        string[] parts = core.Split('.');

        parts.Length.ShouldBe(3);
        parts.ShouldAllBe(p => p.Length > 0 && p.All(char.IsAsciiDigit));
    }

    [Fact]
    public void Hicbir_derlemede_elle_yazilmis_surum_kalmamali()
    {
        // ADR-0006: "Sürüm hiçbir dosyaya elle yazılmaz." Directory.Build.props'taki
        // VersionPrefix P10-T01'de kaldırıldı; geri eklenirse MinVer'i sessizce ezmez
        // ama iki kaynak oluşur. Bu test, tüm derlemelerin AYNI sürümü taşıdığını
        // doğrulayarak o ayrışmayı yakalar.
        string[] assemblies = ["GitExt.Core", "GitExt.Graph", "GitExt.UI", "gitext-core"];

        List<string> versions = [];

        foreach (string name in assemblies)
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == name)
                ?? Assembly.Load(name);

            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            informational.ShouldNotBeNullOrWhiteSpace($"{name} sürüm özniteliği taşımıyor.");
            versions.Add(informational!);
        }

        versions.Distinct().Count().ShouldBe(
            1,
            $"Derlemeler farklı sürümler taşıyor: {string.Join(", ", versions.Distinct())}");
    }
}
