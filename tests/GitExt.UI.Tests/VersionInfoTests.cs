using System.Reflection;
using GitExt.Desktop;

namespace GitExt.UI.Tests;

/// <summary>
/// Verifies that the version is derived from the git tag (P10-T01, ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// The gap these tests close: when the version is wrong, <b>nothing breaks.</b> The build is green,
/// the tests are green, the package is produced — only its name is wrong. Measured (P10-T00): on a
/// shallow clone MinVer silently produces <c>0.0.0-alpha.0</c> and a release can go out with it.
/// </para>
/// </remarks>
public class VersionInfoTests
{
    [Fact]
    public void Surum_derleme_sirasinda_gomulmus_olmali()
    {
        // "unknown" — the case where the attribute was never emitted at all. Landing here means
        // the versioning infrastructure has silently been disabled.
        VersionInfo.Version.ShouldNotBe("unknown");
        VersionInfo.Version.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Surum_gercekten_MinVer_tarafindan_turetilmis_olmali()
    {
        // 🔴 This test is the result of a SABOTAGE VERIFICATION. When MinVer was disabled
        // (MinVerSkip=true) every other test kept passing: the SDK default `1.0.0` is valid
        // semver, consistent across all assemblies, and even its sha is in place. So if versioning
        // silently switched off, the app would say "gitext-core 1.0.0" and nobody would notice.
        //
        // The only certain sign that MinVer ran is the MinVerVersion value it produces itself —
        // Directory.Build.props embeds that into the assembly.
        string? evidence = typeof(VersionInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MinVerVersion")?.Value;

        evidence.ShouldNotBeNullOrWhiteSpace(
            "MinVerVersion özniteliği yok — sürüm MinVer ile türetilmemiş. "
            + "Sürümleme altyapısı devre dışı kalmış olabilir.");

        // The embedded version and the version the application reports must be the same.
        evidence.ShouldBe(VersionInfo.Version);
    }

    [Fact]
    public void Surum_build_metadatasini_icermemeli()
    {
        // MinVer appends "+<sha>" to the end of the version. It has no place in package names or
        // in text shown to the user; '+' is an invalid character in most package formats.
        VersionInfo.Version.ShouldNotContain("+");
    }

    [Fact]
    public void Commit_sha_ayri_olarak_okunabilmeli()
    {
        // In bug reports, which commit was running is more precise than the version number:
        // in pre-releases the same number can map to more than one commit.
        VersionInfo.Commit.ShouldNotBeNull();
        VersionInfo.Commit!.Length.ShouldBe(40);
        VersionInfo.Commit.ShouldAllBe(c => Uri.IsHexDigit(c));
    }

    [Fact]
    public void Surum_semver_bicinminde_olmali()
    {
        // Must start with MAJOR.MINOR.PATCH (ADR-0006). A pre-release suffix may follow.
        string core = VersionInfo.Version.Split('-')[0];
        string[] parts = core.Split('.');

        parts.Length.ShouldBe(3);
        parts.ShouldAllBe(p => p.Length > 0 && p.All(char.IsAsciiDigit));
    }

    [Fact]
    public void Hicbir_derlemede_elle_yazilmis_surum_kalmamali()
    {
        // ADR-0006: "The version is never written by hand into any file." The VersionPrefix in
        // Directory.Build.props was removed in P10-T01; if it is put back it does not silently
        // override MinVer, but there would be two sources. This test catches that divergence by
        // verifying that all assemblies carry the SAME version.
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
