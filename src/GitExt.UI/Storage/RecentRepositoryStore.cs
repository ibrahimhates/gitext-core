using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitExt.UI.Storage;

/// <summary>
/// One entry of the repository list (P03-T16, category P12-T02).
/// </summary>
/// <param name="Path">The repository's working directory.</param>
/// <param name="Category">
/// The category the user filed it under, or <see langword="null"/> when it is simply recent.
/// </param>
/// <remarks>
/// The category is what GitExtensions calls a <b>favourite</b>: on the dashboard a categorised
/// repository moves out of "Recent repositories" and into a group of its own, and it is
/// <b>never pruned</b> by the size cap.
/// </remarks>
public sealed record RecentRepository(string Path, string? Category = null)
{
    /// <summary>Is this a favourite (i.e. filed under a category)?</summary>
    public bool IsFavourite => !string.IsNullOrWhiteSpace(Category);
}

/// <summary>
/// Stores the list of recently opened repositories (P03-T16).
/// </summary>
public interface IRecentRepositoryStore
{
    /// <summary>Returns the repository list, most recently opened first.</summary>
    Task<IReadOnlyList<RecentRepository>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves a repository to the front of the list (or adds it) and saves.</summary>
    Task AddAsync(string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>Removes a repository from the list.</summary>
    Task RemoveAsync(string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Files a repository under a category, or removes it from one with <see langword="null"/>.
    /// </summary>
    Task SetCategoryAsync(
        string workingDirectory,
        string? category,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps recently opened repositories in a JSON file in the platform-standard config directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the forerunner of P08-T14 (settings infrastructure).</b> When the full settings
/// system arrives, this file will move into it; that's why a <b>version field is included
/// from day one</b> (ADR-0006: the settings format freezes at <c>v1.0.0</c>, a version field
/// cannot be added afterwards).
/// </para>
/// <para>
/// If the list cannot be read or is corrupt, it is <b>treated as empty</b>. Recently opened is
/// a convenience; the app failing to open because of a corrupt file is unacceptable.
/// </para>
/// </remarks>
public sealed class RecentRepositoryStore : IRecentRepositoryStore
{
    /// <summary>
    /// Maximum number of <b>uncategorised</b> repositories kept in the list.
    /// </summary>
    /// <remarks>
    /// Kept short so it fits in the menu and is easy to scan; an unbounded list would
    /// eventually fill up with repositories the user never opens. 🔴 Favourites are
    /// <b>outside</b> the cap: the user filed those deliberately, and silently dropping one
    /// because twelve other repositories were opened since would throw away a decision the
    /// user made — the exact opposite of what the category is for.
    /// </remarks>
    public const int MaximumCount = 12;

    /// <summary>
    /// The version written into the file.
    /// </summary>
    /// <remarks>
    /// v1 held plain path strings; v2 holds objects so a category can travel with the path. v1
    /// files are still read (see <see cref="ParseRepositories"/>) — an upgrade must not empty
    /// the user's list.
    /// </remarks>
    private const int SchemaVersion = 2;

    private readonly string _filePath;

    public RecentRepositoryStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(ConfigurationDirectory(), "recent-repositories.json");
    }

    /// <summary>
    /// Directory where settings files live (platform-standard location).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠️ MEASURED:</b> on Linux, if <c>XDG_CONFIG_HOME</c> points to a directory that
    /// <b>doesn't exist</b>, .NET's <see cref="Environment.SpecialFolder.ApplicationData"/>
    /// value returns an <b>empty string</b>. If the empty string is passed into
    /// <see cref="Path.Combine(string, string)"/>, the result becomes a <b>relative</b> path
    /// and the file gets written to the user's working directory — i.e. inside the repository
    /// they opened.
    /// </para>
    /// <para>
    /// (Other measured behaviors: if it points to an existing directory, that directory is
    /// returned; a <b>relative</b> value is ignored per the XDG specification, falling back to
    /// <c>~/.config</c>.)
    /// </para>
    /// </remarks>
    public static string ConfigurationDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrEmpty(root))
        {
            // XDG_CONFIG_HOME points somewhere that doesn't exist. We fall back to the XDG
            // spec's default ourselves.
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            root = string.IsNullOrEmpty(home)
                ? Path.Combine(Path.GetTempPath(), "gitext-core-config")
                : Path.Combine(home, ".config");
        }

        return Path.Combine(root, "gitext-core");
    }

    public async Task<IReadOnlyList<RecentRepository>> LoadAsync(
        CancellationToken cancellationToken = default) =>
        await ReadAsync(cancellationToken).ConfigureAwait(false);

    public async Task AddAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        IReadOnlyList<RecentRepository> current = await LoadAsync(cancellationToken).ConfigureAwait(false);

        // The category the repository already carries is kept: reopening a favourite must not
        // demote it back into "Recent repositories".
        RecentRepository? existing = current.FirstOrDefault(r => PathsEqual(r.Path, workingDirectory));

        List<RecentRepository> updated = [new RecentRepository(workingDirectory, existing?.Category)];

        // The same repository must not appear twice; reopening it moves it to the front.
        updated.AddRange(current.Where(r => !PathsEqual(r.Path, workingDirectory)));

        await WriteAsync(Trim(updated), cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RecentRepository> current = await LoadAsync(cancellationToken).ConfigureAwait(false);

        await WriteAsync(
            [.. current.Where(r => !PathsEqual(r.Path, workingDirectory))],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCategoryAsync(
        string workingDirectory,
        string? category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        IReadOnlyList<RecentRepository> current = await LoadAsync(cancellationToken).ConfigureAwait(false);

        string? cleaned = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        List<RecentRepository> updated =
        [
            .. current.Select(r => PathsEqual(r.Path, workingDirectory) ? r with { Category = cleaned } : r),
        ];

        // Filing a repository the list has never seen is legitimate: it is added rather than
        // silently ignored.
        if (!updated.Any(r => PathsEqual(r.Path, workingDirectory)))
        {
            updated.Insert(0, new RecentRepository(workingDirectory, cleaned));
        }

        await WriteAsync(Trim(updated), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the size cap — to the uncategorised entries only.
    /// </summary>
    private static IReadOnlyList<RecentRepository> Trim(IReadOnlyList<RecentRepository> repositories)
    {
        int kept = 0;
        List<RecentRepository> result = [];

        foreach (RecentRepository repository in repositories)
        {
            if (repository.IsFavourite)
            {
                result.Add(repository);
                continue;
            }


            if (kept >= MaximumCount)
            {
                continue;
            }

            kept++;
            result.Add(repository);
        }

        return result;
    }

    /// <summary>
    /// Whether two paths point to the same repository.
    /// </summary>
    /// <remarks>
    /// Comparison is case-insensitive on Windows and macOS, case-sensitive on Linux — that's
    /// the actual behavior of those file systems. Trailing separators are also normalized,
    /// otherwise <c>/repo</c> and <c>/repo/</c> would count as two separate entries.
    /// </remarks>
    private static bool PathsEqual(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private async Task<IReadOnlyList<RecentRepository>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            await using FileStream stream = File.OpenRead(_filePath);

            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ParseRepositories(document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable file: proceed as if the list didn't exist.
            return [];
        }
    }

    /// <summary>
    /// Reads the entries out of the file, in <b>both</b> schema versions.
    /// </summary>
    /// <remarks>
    /// 🔴 The document is walked by hand rather than deserialised into a type: v1 wrote
    /// <c>"repositories": ["/a", "/b"]</c> and v2 writes
    /// <c>"repositories": [{"path": "/a"}]</c>. A typed read of the v2 shape throws
    /// <see cref="JsonException"/> on a v1 file, and this class treats an unreadable file as an
    /// empty list — so upgrading would have <b>silently wiped</b> the user's repository list.
    /// <see cref="JsonDocument"/> is also trimming-safe (see <see cref="RecentJsonContext"/>).
    /// </remarks>
    private static IReadOnlyList<RecentRepository> ParseRepositories(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("repositories", out JsonElement repositories)
            || repositories.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<RecentRepository> result = [];

        foreach (JsonElement element in repositories.EnumerateArray())
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String when element.GetString() is { Length: > 0 } path:
                    // Schema v1.
                    result.Add(new RecentRepository(path));
                    break;

                case JsonValueKind.Object
                    when element.TryGetProperty("path", out JsonElement pathElement)
                        && pathElement.GetString() is { Length: > 0 } objectPath:

                    string? category = element.TryGetProperty("category", out JsonElement categoryElement)
                        && categoryElement.ValueKind == JsonValueKind.String
                            ? categoryElement.GetString()
                            : null;

                    result.Add(new RecentRepository(
                        objectPath,
                        string.IsNullOrWhiteSpace(category) ? null : category));
                    break;

                default:
                    // An entry we do not understand is skipped, not fatal.
                    break;
            }
        }

        return result;
    }

    private async Task WriteAsync(
        IReadOnlyList<RecentRepository> repositories,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            RecentFile file = new()
            {
                Version = SchemaVersion,
                Repositories = [.. repositories.Select(r => new RecentEntry
                {
                    Path = r.Path,
                    Category = r.Category,
                })],
            };

            await using FileStream stream = File.Create(_filePath);

            await JsonSerializer
                .SerializeAsync(stream, file, RecentJsonContext.Default.RecentFile, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only home directory, full disk, etc. Failing to save recently opened
            // repositories must not stop the app.
        }
    }
}

/// <summary>
/// Schema of the recently-opened file.
/// </summary>
internal sealed class RecentFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("repositories")]
    public IReadOnlyList<RecentEntry> Repositories { get; set; } = [];
}

/// <summary>One entry in the file (schema v2).</summary>
internal sealed class RecentEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string? Category { get; set; }
}

/// <summary>
/// Source-generated serialization context for <see cref="RecentFile"/>.
/// </summary>
/// <remarks>
/// <b>Reflective <c>JsonSerializer</c> overloads cannot be used:</b> they break trimming
/// (IL2026) and the publish <b>fails to build</b> with <c>PublishTrimmed</c>. This was found
/// by measurement — the reflective calls added in P03-T16 broke the publish, but build and
/// tests stayed green, so it was only caught when `dotnet publish` was actually tried.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RecentFile))]
internal sealed partial class RecentJsonContext : JsonSerializerContext;
