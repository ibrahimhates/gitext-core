using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GitExt.UI.Updates;

/// <summary>
/// A released version, for comparing "is there anything newer?" (P13-T01).
/// </summary>
/// <remarks>
/// <para>
/// A small semver reader rather than <see cref="Version"/>: <see cref="Version"/> knows nothing
/// about the pre-release part, and this application's own version is a pre-release most of the
/// time (MinVer produces <c>0.1.2-alpha.0.3</c> between tags — ADR-0006). Comparing those with
/// <see cref="Version"/> would either throw or silently drop the suffix.
/// </para>
/// <para>
/// 🔴 The rule that matters here is semver's own: <b>a pre-release is OLDER than the release of
/// the same number</b>. Without it, someone running <c>0.1.2-alpha.0.3</c> would be told that
/// <c>0.1.2</c> is "not newer" — and the one build that most needs the notification would never
/// get it.
/// </para>
/// </remarks>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string PreRelease)
    : IComparable<ReleaseVersion>
{
    /// <summary>Is this a pre-release (<c>-alpha.1</c>)?</summary>
    public bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>
    /// Reads a version. Accepts a leading <c>v</c> and drops build metadata (<c>+sha</c>).
    /// </summary>
    /// <remarks>
    /// Anything it cannot read comes back as <see langword="false"/> and the caller stays silent.
    /// A version string that cannot be understood is not a reason to claim an update — telling the
    /// user about a release that may not exist is worse than telling them nothing.
    /// </remarks>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ReleaseVersion? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();

        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        // Build metadata (`+sha`) says nothing about precedence — semver ignores it too.
        int plus = value.IndexOf('+', StringComparison.Ordinal);

        if (plus >= 0)
        {
            value = value[..plus];
        }

        string preRelease = string.Empty;
        int dash = value.IndexOf('-', StringComparison.Ordinal);

        if (dash >= 0)
        {
            preRelease = value[(dash + 1)..];
            value = value[..dash];
        }

        string[] parts = value.Split('.');

        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        int[] numbers = new int[3];

        for (int index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
            {
                return false;
            }
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int number = Major.CompareTo(other.Major);

        if (number != 0)
        {
            return number;
        }

        number = Minor.CompareTo(other.Minor);

        if (number != 0)
        {
            return number;
        }

        number = Patch.CompareTo(other.Patch);

        if (number != 0)
        {
            return number;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>
    /// Compares the pre-release parts by semver's rules.
    /// </summary>
    /// <remarks>
    /// No pre-release beats any pre-release; otherwise the dot-separated identifiers are compared
    /// one by one, numbers numerically and the rest as text, and a shorter list loses.
    /// </remarks>
    private static int ComparePreRelease(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0)
        {
            return 0;
        }

        // 🔴 The rule the whole thing hangs on: 1.0.0-alpha < 1.0.0.
        if (left.Length == 0)
        {
            return 1;
        }

        if (right.Length == 0)
        {
            return -1;
        }

        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');

        for (int index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            bool leftNumeric = int.TryParse(
                leftParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int leftNumber);

            bool rightNumeric = int.TryParse(
                rightParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int rightNumber);

            int result = (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),

                // Semver: a numeric identifier always ranks lower than an alphanumeric one.
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftParts[index], rightParts[index]),
            };

            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    public override string ToString() =>
        IsPreRelease
            ? $"{Major}.{Minor}.{Patch}-{PreRelease}"
            : $"{Major}.{Minor}.{Patch}";
}
