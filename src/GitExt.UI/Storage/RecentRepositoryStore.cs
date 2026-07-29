using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitExt.UI.Storage;

/// <summary>
/// Son açılan depoların listesini saklar (P03-T16).
/// </summary>
public interface IRecentRepositoryStore
{
    /// <summary>Son açılanları, en yeni ilk sırada döndürür.</summary>
    Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Bir depoyu listenin başına taşır (veya ekler) ve kaydeder.</summary>
    Task AddAsync(string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>Bir depoyu listeden çıkarır.</summary>
    Task RemoveAsync(string workingDirectory, CancellationToken cancellationToken = default);
}

/// <summary>
/// Son açılan depoları platform standardı yapılandırma dizinindeki JSON dosyasında tutar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu, P08-T14'ün (ayarlar altyapısı) habercisi.</b> Tam ayar sistemi geldiğinde bu dosya
/// onun içine taşınacak; o yüzden şemaya <b>ilk günden sürüm alanı</b> konuyor (ADR-0006:
/// ayar formatı <c>v1.0.0</c>'da donuyor, sürüm alanı sonradan eklenemez).
/// </para>
/// <para>
/// Liste okunamazsa veya bozuksa <b>boş kabul edilir</b>. Son açılanlar kolaylıktır;
/// bozuk bir dosya yüzünden uygulamanın açılmaması kabul edilemez.
/// </para>
/// </remarks>
public sealed class RecentRepositoryStore : IRecentRepositoryStore
{
    /// <summary>
    /// Listede tutulan en fazla depo sayısı.
    /// </summary>
    /// <remarks>
    /// Menüye sığması ve göz taraması kolay olsun diye kısa tutuldu; sınırsız bir liste
    /// zamanla kullanıcının hiç açmadığı depolarla dolar.
    /// </remarks>
    public const int MaximumCount = 12;

    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;

    public RecentRepositoryStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(ConfigurationDirectory(), "recent-repositories.json");
    }

    /// <summary>
    /// Ayar dosyalarının bulunduğu dizin (platform standardı konumda).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠️ ÖLÇÜLDÜ:</b> Linux'ta <c>XDG_CONFIG_HOME</c> <b>var olmayan</b> bir dizini
    /// gösteriyorsa .NET'in <see cref="Environment.SpecialFolder.ApplicationData"/> değeri
    /// <b>boş dize</b> döner. Boş dize <see cref="Path.Combine(string, string)"/>'e girerse
    /// sonuç <b>göreli</b> bir yol olur ve dosya kullanıcının çalışma dizinine —
    /// yani açtığı deponun içine — yazılır.
    /// </para>
    /// <para>
    /// (Ölçülen diğer davranışlar: var olan bir dizin gösteriyorsa o dizin döner; <b>göreli</b>
    /// bir değer XDG şartnamesi gereği yok sayılır ve <c>~/.config</c>'e düşülür.)
    /// </para>
    /// </remarks>
    public static string ConfigurationDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrEmpty(root))
        {
            // XDG_CONFIG_HOME var olmayan bir yeri gösteriyor. XDG şartnamesindeki
            // varsayılana kendimiz düşüyoruz.
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            root = string.IsNullOrEmpty(home)
                ? Path.Combine(Path.GetTempPath(), "gitext-core-config")
                : Path.Combine(home, ".config");
        }

        return Path.Combine(root, "gitext-core");
    }

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        RecentFile? file = await ReadAsync(cancellationToken).ConfigureAwait(false);

        return file?.Repositories ?? [];
    }

    public async Task AddAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        IReadOnlyList<string> current = await LoadAsync(cancellationToken).ConfigureAwait(false);

        List<string> updated = [workingDirectory];

        // Aynı depo iki kez görünmemeli; yeniden açmak onu başa taşır.
        updated.AddRange(current.Where(p => !PathsEqual(p, workingDirectory)));

        if (updated.Count > MaximumCount)
        {
            updated.RemoveRange(MaximumCount, updated.Count - MaximumCount);
        }

        await WriteAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> current = await LoadAsync(cancellationToken).ConfigureAwait(false);

        await WriteAsync(
            [.. current.Where(p => !PathsEqual(p, workingDirectory))],
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// İki yolun aynı depoyu gösterip göstermediği.
    /// </summary>
    /// <remarks>
    /// Karşılaştırma Windows ve macOS'ta büyük/küçük harf duyarsız, Linux'ta duyarlıdır —
    /// dosya sistemlerinin gerçek davranışı bu. Sondaki ayraç da normalleştirilir, aksi halde
    /// <c>/depo</c> ve <c>/depo/</c> iki ayrı girdi olurdu.
    /// </remarks>
    private static bool PathsEqual(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private async Task<RecentFile?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            await using FileStream stream = File.OpenRead(_filePath);

            return await JsonSerializer
                .DeserializeAsync<RecentFile>(stream, _serializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Bozuk veya okunamayan dosya: liste yokmuş gibi devam edilir.
            return null;
        }
    }

    private async Task WriteAsync(IReadOnlyList<string> repositories, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            RecentFile file = new() { Version = SchemaVersion, Repositories = repositories };

            await using FileStream stream = File.Create(_filePath);

            await JsonSerializer
                .SerializeAsync(stream, file, _serializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Salt okunur ev dizini, dolu disk vb. Son açılanları kaydedememek
            // uygulamayı durdurmaz.
        }
    }

    private sealed class RecentFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("repositories")]
        public IReadOnlyList<string> Repositories { get; set; } = [];
    }
}
