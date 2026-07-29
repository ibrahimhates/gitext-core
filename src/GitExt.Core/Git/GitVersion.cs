using System.Globalization;

namespace GitExt.Core.Git;

/// <summary>
/// Bir <c>git</c> sürüm numarası.
/// </summary>
/// <remarks>
/// <c>git --version</c> çıktısı platforma göre değişir:
/// <list type="bullet">
///   <item><c>git version 2.55.0</c></item>
///   <item><c>git version 2.39.5 (Apple Git-154)</c></item>
///   <item><c>git version 2.47.1.windows.1</c></item>
/// </list>
/// Bu yüzden yalnızca baştaki sayısal bileşenler okunur; kalan platform eki yok sayılır.
/// </remarks>
public readonly record struct GitVersion(int Major, int Minor, int Patch)
    : IComparable<GitVersion>
{
    /// <summary>
    /// Desteklenen en düşük sürüm (ADR-0002).
    /// </summary>
    /// <remarks>
    /// 2.30 (Ocak 2021): <c>--porcelain=v2</c>, <c>for-each-ref</c> format desteği ve
    /// <c>switch</c>/<c>restore</c> burada güvenle mevcut.
    /// </remarks>
    public static GitVersion Minimum { get; } = new(2, 30, 0);

    /// <summary>
    /// <c>git --version</c> çıktısını ayrıştırır.
    /// </summary>
    /// <returns>Ayrıştırılabildiyse <see langword="true"/>.</returns>
    public static bool TryParse(string? output, out GitVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        // "git version " önekini atla; bazı derlemeler farklı önek kullanabilir, o yüzden
        // öneki zorunlu tutmak yerine ilk rakama kadar ilerliyoruz.
        ReadOnlySpan<char> span = output.AsSpan().Trim();
        int start = span.IndexOfAnyInRange('0', '9');
        if (start < 0)
        {
            return false;
        }

        span = span[start..];

        int major = ReadComponent(ref span);
        if (major < 0)
        {
            return false;
        }

        int minor = TryConsumeSeparator(ref span) ? ReadComponent(ref span) : 0;
        int patch = TryConsumeSeparator(ref span) ? ReadComponent(ref span) : 0;

        version = new GitVersion(major, Math.Max(minor, 0), Math.Max(patch, 0));
        return true;
    }

    private static bool TryConsumeSeparator(ref ReadOnlySpan<char> span)
    {
        if (span.Length > 0 && span[0] == '.')
        {
            span = span[1..];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Baştaki ardışık rakamları okur; rakam yoksa -1 döner.
    /// </summary>
    private static int ReadComponent(ref ReadOnlySpan<char> span)
    {
        int length = 0;
        while (length < span.Length && char.IsAsciiDigit(span[length]))
        {
            length++;
        }

        if (length == 0)
        {
            return -1;
        }

        int value = int.Parse(span[..length], CultureInfo.InvariantCulture);
        span = span[length..];
        return value;
    }

    public int CompareTo(GitVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(GitVersion left, GitVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(GitVersion left, GitVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(GitVersion left, GitVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(GitVersion left, GitVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}
