using System.Diagnostics.CodeAnalysis;

namespace GitExt.Core.Model;

/// <summary>
/// Depo köküne göre göreli bir yol; ayraç <b>her zaman</b> <c>/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Git yolları platformdan bağımsız olarak eğik çizgiyle ayırır — Windows'ta bile. Bu tip,
/// yolun git'e verilirken doğru biçimde, dosya sistemine verilirken platformun biçiminde
/// olmasını garanti eder.
/// </para>
/// <para>
/// Düz <see cref="string"/> kullanmak Windows'ta sessiz hatalara yol açar: <c>Path.Combine</c>
/// ters eğik çizgi üretir, git onu dosya adının parçası sanar ve dosya "bulunamaz".
/// </para>
/// </remarks>
public readonly record struct RepositoryPath : IComparable<RepositoryPath>
{
    private readonly string? _value;

    private RepositoryPath(string value)
    {
        _value = value;
    }

    /// <summary>Depo köküne göre göreli yol, <c>/</c> ayraçlı.</summary>
    public string Value => _value ?? string.Empty;

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>Yolun son bileşeni (dosya veya klasör adı).</summary>
    public string Name
    {
        get
        {
            string value = Value;
            int index = value.LastIndexOf('/');
            return index < 0 ? value : value[(index + 1)..];
        }
    }

    /// <summary>Üst dizin; kökteyse boş.</summary>
    public RepositoryPath Parent
    {
        get
        {
            string value = Value;
            int index = value.LastIndexOf('/');
            return index < 0 ? default : new RepositoryPath(value[..index]);
        }
    }

    /// <summary>Uzantı (nokta dahil); yoksa boş.</summary>
    public string Extension
    {
        get
        {
            string name = Name;
            int index = name.LastIndexOf('.');
            return index <= 0 ? string.Empty : name[index..];
        }
    }

    /// <summary>
    /// Git'ten gelen bir yolu ayrıştırır.
    /// </summary>
    /// <exception cref="ArgumentException">Yol boşsa veya mutlaksa.</exception>
    public static RepositoryPath Parse(string value)
    {
        if (!TryParse(value, out RepositoryPath path))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid repository-relative path.", nameof(value));
        }

        return path;
    }

    /// <summary>
    /// Git'ten gelen bir yolu ayrıştırmayı dener.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out RepositoryPath path)
    {
        path = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Windows'tan gelmiş olabilir; ayracı normalleştir.
        //
        // 🔴 Yalnızca Windows'ta (P05-T08'de ölçüldü): Linux'ta `\` dosya adında GEÇERLİ bir
        // karakterdir ve git onu olduğu gibi bildirir. Her platformda çevirmek,
        // `ters\slash.txt` adlı bir dosyayı `ters/slash.txt` yapıp yolu SESSİZCE yanlış
        // gösteriyordu — `.gitignore`'a eklemek hiçbir işe yaramıyordu çünkü üretilen desen
        // var olmayan bir alt dizini işaret ediyordu.
        string normalized = OperatingSystem.IsWindows()
            ? value.Replace('\\', '/').Trim('/')
            : value.Trim('/');

        if (normalized.Length == 0)
        {
            return false;
        }

        // Mutlak yollar ve sürücü harfleri depo-göreli değildir.
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            return false;
        }

        path = new RepositoryPath(normalized);
        return true;
    }

    /// <summary>
    /// Depo kökü verildiğinde mutlak dosya sistemi yolunu üretir.
    /// </summary>
    public string ToAbsolutePath(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        // Path.Combine platformun ayracını kullanır; git'in / ayraçlı yolunu
        // bileşenlerine ayırıp veriyoruz.
        return Path.GetFullPath(Path.Combine([repositoryRoot, .. Value.Split('/')]));
    }

    public int CompareTo(RepositoryPath other) =>
        string.CompareOrdinal(Value, other.Value);

    public override string ToString() => Value;
}
