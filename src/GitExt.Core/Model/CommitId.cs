using System.Diagnostics.CodeAnalysis;

namespace GitExt.Core.Model;

/// <summary>
/// A Git object id (SHA).
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is deliberately not a <see cref="string"/>.</b> Carrying a SHA as plain text invites
/// two classes of bug: confusing short and full SHAs, and accidentally passing a file path or a ref
/// name to a parameter expecting a SHA. Both could be caught at compile time but would otherwise be
/// left to run time.
/// </para>
/// <para>
/// Both SHA-1 (40 hex characters) and SHA-256 (64 characters) repositories are supported. Abbreviated
/// SHAs are valid too (at least 4 characters, the lower bound <c>git</c> accepts).
/// </para>
/// </remarks>
public readonly record struct CommitId : IComparable<CommitId>
{
    /// <summary>The shortest unique prefix length <c>git</c> accepts.</summary>
    public const int MinimumLength = 4;

    private const int Sha1Length = 40;
    private const int Sha256Length = 64;

    private readonly string? _value;

    private CommitId(string value)
    {
        _value = value;
    }

    /// <summary>The hexadecimal representation, in lower case.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Has no value been assigned?</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>
    /// Is it a full-length SHA (40 for SHA-1, 64 for SHA-256)?
    /// </summary>
    /// <remarks>
    /// Abbreviated SHAs can become ambiguous over time (as the repository grows, prefixes collide), so
    /// ids to be stored permanently have to be full.
    /// </remarks>
    public bool IsFull => _value?.Length is Sha1Length or Sha256Length;

    /// <summary>
    /// Produces a <see cref="CommitId"/> from text.
    /// </summary>
    /// <exception cref="FormatException">When the value is not a valid hexadecimal SHA.</exception>
    public static CommitId Parse(string value)
    {
        if (!TryParse(value, out CommitId id))
        {
            throw new FormatException(
                $"'{value}' is not a valid Git object id. "
                + $"Between {MinimumLength} and {Sha256Length} hexadecimal characters are expected.");
        }

        return id;
    }

    /// <summary>
    /// Tries to produce a <see cref="CommitId"/> from text.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out CommitId id)
    {
        id = default;

        if (value is null)
        {
            return false;
        }

        ReadOnlySpan<char> span = value.AsSpan().Trim();

        if (span.Length is < MinimumLength or > Sha256Length)
        {
            return false;
        }

        foreach (char c in span)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        // git produces lower-case SHAs; normalise upper-case input so comparisons stay consistent.
        id = new CommitId(span.ToString().ToLowerInvariant());
        return true;
    }

    /// <summary>
    /// The short form to show the user.
    /// </summary>
    /// <param name="length">The number of characters. When the value is already shorter it is returned as is.</param>
    /// <remarks>
    /// The default is 7, the same as <c>git log --oneline</c>. This is <b>for display only</b>; the
    /// abbreviated value must not be handed back to git.
    /// </remarks>
    public string ToShortString(int length = 7)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, MinimumLength);

        string value = Value;
        return value.Length <= length ? value : value[..length];
    }

    /// <summary>
    /// Is this id a prefix of the given one?
    /// </summary>
    /// <remarks>
    /// For working out whether an abbreviated SHA corresponds to a full one. An equality comparison
    /// does not do this — <c>abc1234</c> is not equal to its full SHA.
    /// </remarks>
    public bool IsPrefixOf(CommitId other) =>
        !IsEmpty && other.Value.StartsWith(Value, StringComparison.Ordinal);

    public int CompareTo(CommitId other) =>
        string.CompareOrdinal(Value, other.Value);

    public override string ToString() => Value;
}
