using System.Reflection;

namespace GitExt.Desktop;

/// <summary>
/// Uygulamanın kendi sürümü (P10-T01).
/// </summary>
/// <remarks>
/// <para>
/// Sürüm git tag'inden MinVer ile türetiliyor (ADR-0006) ve derleme sırasında
/// <see cref="AssemblyInformationalVersionAttribute"/> içine gömülüyor. Burada
/// okunuyor, hiçbir yerde elle yazılmıyor.
/// </para>
/// <para>
/// Paketleme betikleri de bu değeri kullanıyor: <c>gitext-core --version</c> çıktısı,
/// üretilen paketin dosya adındaki sürümle aynı olmak zorunda. Betiğin sürümü ayrı
/// hesaplaması, ikisinin sessizce ayrışabileceği ikinci bir kaynak yaratırdı.
/// </para>
/// </remarks>
internal static class VersionInfo
{
    internal const string Flag = "--version";

    /// <summary>
    /// Sürüm — <c>1.0.0</c> veya <c>1.0.1-alpha.0.3</c>. Build metadata'sı (+sha) atılır.
    /// </summary>
    internal static string Version { get; } = ReadInformationalVersion();

    /// <summary>
    /// Sürümün türetildiği commit'in tam SHA'sı; yoksa <c>null</c>.
    /// </summary>
    /// <remarks>
    /// MinVer commit SHA'sını sürümün build metadata bölümüne (<c>+sha</c>) yazıyor.
    /// Hata raporlarında hangi commit'in çalıştığını bilmek, sürüm numarasından daha
    /// kesin bir bilgi: ön sürümlerde aynı sürüm numarası birden çok commit'e denk gelir.
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
            // Öznitelik her zaman üretiliyor; buraya düşmek derleme yapılandırmasının
            // bozulduğu anlamına gelir. Sessizce "1.0.0" uydurmak, yanlış sürümle
            // paketlenmiş bir çıktıyı doğru göstermek olurdu.
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
