using System.Diagnostics.CodeAnalysis;

namespace GitExt.Core.Model;

/// <summary>
/// Bir Git nesne kimliği (SHA).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu tip bilinçli olarak <see cref="string"/> değildir.</b> Bir SHA'yı düz metin olarak
/// taşımak iki hata sınıfını davet eder: kısa ve tam SHA'ların karıştırılması, ve yanlışlıkla
/// bir dosya yolunun veya ref adının SHA bekleyen bir parametreye geçirilmesi. İkisi de
/// derleme zamanında yakalanabilecekken çalışma zamanına kalır.
/// </para>
/// <para>
/// Hem SHA-1 (40 onaltılık karakter) hem SHA-256 (64 karakter) depoları desteklenir.
/// Kısaltılmış SHA'lar da geçerlidir (en az 4 karakter, <c>git</c>'in kabul ettiği alt sınır).
/// </para>
/// </remarks>
public readonly record struct CommitId : IComparable<CommitId>
{
    /// <summary><c>git</c>'in kabul ettiği en kısa benzersiz önek uzunluğu.</summary>
    public const int MinimumLength = 4;

    private const int Sha1Length = 40;
    private const int Sha256Length = 64;

    private readonly string? _value;

    private CommitId(string value)
    {
        _value = value;
    }

    /// <summary>Onaltılık gösterim, küçük harf.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Değer atanmamış mı?</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>
    /// Tam uzunlukta bir SHA mı (SHA-1 için 40, SHA-256 için 64)?
    /// </summary>
    /// <remarks>
    /// Kısaltılmış SHA'lar zamanla belirsizleşebilir (depo büyüdükçe önek çakışır), bu yüzden
    /// kalıcı olarak saklanacak kimlikler tam olmalıdır.
    /// </remarks>
    public bool IsFull => _value?.Length is Sha1Length or Sha256Length;

    /// <summary>
    /// Metinden bir <see cref="CommitId"/> üretir.
    /// </summary>
    /// <exception cref="FormatException">Değer geçerli bir onaltılık SHA değilse.</exception>
    public static CommitId Parse(string value)
    {
        if (!TryParse(value, out CommitId id))
        {
            throw new FormatException(
                $"'{value}' geçerli bir Git nesne kimliği değil. "
                + $"En az {MinimumLength}, en fazla {Sha256Length} onaltılık karakter bekleniyor.");
        }

        return id;
    }

    /// <summary>
    /// Metinden bir <see cref="CommitId"/> üretmeyi dener.
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

        // git SHA'ları küçük harf üretir; büyük harfli girdiyi normalleştir ki
        // karşılaştırmalar tutarlı olsun.
        id = new CommitId(span.ToString().ToLowerInvariant());
        return true;
    }

    /// <summary>
    /// Kullanıcıya gösterilecek kısa biçim.
    /// </summary>
    /// <param name="length">Karakter sayısı. Değer zaten daha kısaysa olduğu gibi döner.</param>
    /// <remarks>
    /// Varsayılan 7, <c>git log --oneline</c> ile aynı. Bu <b>yalnızca gösterim içindir</b>;
    /// kısaltılmış değer git'e geri verilmemeli.
    /// </remarks>
    public string ToShortString(int length = 7)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, MinimumLength);

        string value = Value;
        return value.Length <= length ? value : value[..length];
    }

    /// <summary>
    /// Bu kimlik, verilen kimliğin öneki mi?
    /// </summary>
    /// <remarks>
    /// Kısaltılmış bir SHA'nın tam bir SHA'ya karşılık gelip gelmediğini anlamak için.
    /// Eşitlik karşılaştırması bunu yapmaz — <c>abc1234</c> ile tam SHA'sı eşit değildir.
    /// </remarks>
    public bool IsPrefixOf(CommitId other) =>
        !IsEmpty && other.Value.StartsWith(Value, StringComparison.Ordinal);

    public int CompareTo(CommitId other) =>
        string.CompareOrdinal(Value, other.Value);

    public override string ToString() => Value;
}
