using System.Globalization;

namespace GitExt.Core.Git;

/// <summary>
/// A <c>git</c> version number.
/// </summary>
/// <remarks>
/// The output of <c>git --version</c> varies by platform:
/// <list type="bullet">
///   <item><c>git version 2.55.0</c></item>
///   <item><c>git version 2.39.5 (Apple Git-154)</c></item>
///   <item><c>git version 2.47.1.windows.1</c></item>
/// </list>
/// So only the leading numeric components are read; the remaining platform suffix is ignored.
/// </remarks>
public readonly record struct GitVersion(int Major, int Minor, int Patch)
    : IComparable<GitVersion>
{
    /// <summary>
    /// The lowest supported version (ADR-0002).
    /// </summary>
    /// <remarks>
    /// 2.30 (January 2021): <c>--porcelain=v2</c>, <c>for-each-ref</c> format support and
    /// <c>switch</c>/<c>restore</c> are all safely present here.
    /// </remarks>
    public static GitVersion Minimum { get; } = new(2, 30, 0);

    /// <summary>
    /// Parses the output of <c>git --version</c>.
    /// </summary>
    /// <returns><see langword="true"/> when it could be parsed.</returns>
    public static bool TryParse(string? output, out GitVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        // Skip the "git version " prefix; some builds may use a different prefix, so rather than
        // requiring it we advance to the first digit.
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
    /// Reads the leading run of digits; returns -1 when there is no digit.
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
