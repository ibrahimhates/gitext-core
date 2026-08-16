using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitExt.UI.Storage;

/// <summary>
/// Stores the list of recently opened repositories (P03-T16).
/// </summary>
public interface IRecentRepositoryStore
{
    /// <summary>Returns recently opened repositories, newest first.</summary>
    Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves a repository to the front of the list (or adds it) and saves.</summary>
    Task AddAsync(string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>Removes a repository from the list.</summary>
    Task RemoveAsync(string workingDirectory, CancellationToken cancellationToken = default);
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
    /// Maximum number of repositories kept in the list.
    /// </summary>
    /// <remarks>
    /// Kept short so it fits in the menu and is easy to scan; an unbounded list would
    /// eventually fill up with repositories the user never opens.
    /// </remarks>
    public const int MaximumCount = 12;

    private const int SchemaVersion = 1;

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

        // The same repository must not appear twice; reopening it moves it to the front.
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
                .DeserializeAsync(stream, RecentJsonContext.Default.RecentFile, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable file: proceed as if the list didn't exist.
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
    public IReadOnlyList<string> Repositories { get; set; } = [];
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
