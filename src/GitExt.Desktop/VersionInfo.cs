using System.Reflection;

namespace GitExt.Desktop;

/// <summary>
/// The application's own version (P10-T01).
/// </summary>
/// <remarks>
/// <para>
/// The version is derived from the git tag with MinVer (ADR-0006) and embedded into
/// <see cref="AssemblyInformationalVersionAttribute"/> at build time. It is read here,
/// never written by hand anywhere.
/// </para>
/// <para>
/// The packaging scripts use this value too: the output of <c>gitext-core --version</c> must be the
/// same as the version in the produced package's file name. Having the script compute the version
/// separately would create a second source that could silently diverge from this one.
/// </para>
/// </remarks>
internal static class VersionInfo
{
    internal const string Flag = "--version";

    /// <summary>
    /// Version — <c>1.0.0</c> or <c>1.0.1-alpha.0.3</c>. Build metadata (+sha) is dropped.
    /// </summary>
    internal static string Version { get; } = ReadInformationalVersion();

    /// <summary>
    /// The full SHA of the commit the version was derived from; <c>null</c> if there is none.
    /// </summary>
    /// <remarks>
    /// MinVer writes the commit SHA into the build metadata part of the version (<c>+sha</c>).
    /// In bug reports, knowing which commit was running is more precise information than the version
    /// number: in pre-releases the same version number maps to several commits.
    /// </remarks>
    internal static string? Commit { get; } = ReadCommit();

    internal static int Run()
    {
        Console.WriteLine($"gitext-core {Version}");

        if (Commit is not null)
        {
            Console.WriteLine($"commit      {Commit}");
        }

        return 0;
    }

    private static string ReadInformationalVersion()
    {
        string? raw = typeof(VersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(raw))
        {
            // The attribute is always generated; landing here means the build configuration is
            // broken. Silently inventing "1.0.0" would make an output packaged with the wrong
            // version look correct.
            return "bilinmiyor";
        }

        int plus = raw.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? raw : raw[..plus];
    }

    private static string? ReadCommit()
    {
        string? raw = typeof(VersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        int plus = raw?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        return plus < 0 ? null : raw![(plus + 1)..];
    }
}
