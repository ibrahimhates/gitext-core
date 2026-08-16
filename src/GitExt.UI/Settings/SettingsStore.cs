using System.Text.Json;
using System.Text.Json.Nodes;
using GitExt.UI.Storage;

namespace GitExt.UI.Settings;

/// <summary>
/// The single access point for the user settings (P08-T14).
/// </summary>
public interface ISettingsStore
{
    /// <summary>The loaded settings. Before <see cref="LoadAsync"/> is called, these are the defaults.</summary>
    AppSettings Current { get; }

    /// <summary>Raised whenever the settings change.</summary>
    event EventHandler? Changed;

    /// <summary>Reads the file on disk. Called once at application startup.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the settings, raises <see cref="Changed"/> and <b>schedules</b> the save.
    /// </summary>
    /// <remarks>
    /// This is the only write path. Modifying <see cref="Current"/> directly skips both the
    /// notification and the save.
    /// </remarks>
    void Update(Action<AppSettings> change);

    /// <summary>Writes any pending save to disk immediately. Must be called on shutdown.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the settings in a single JSON file in the platform-standard configuration directory.
/// </summary>
/// <remarks>
/// 🔴 <b><see cref="IDisposable"/> MUST be implemented as well.</b> With only
/// <see cref="IAsyncDisposable"/> implemented, the
/// <c>Microsoft.Extensions.DependencyInjection</c> container throws an
/// <see cref="InvalidOperationException"/> saying
/// <c>"type only implements IAsyncDisposable"</c> when it is disposed <b>synchronously</b> — a test
/// caught this. The symptom was invisible because the application never disposes the container
/// today; it would have surfaced in the first code path that did.
/// </remarks>
public sealed class SettingsStore : ISettingsStore, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// How long the save is delayed.
    /// </summary>
    /// <remarks>
    /// Most settings change with a single click, but the <b>layout</b> does not: dragging a splitter
    /// produces dozens of changes a second. Writing each of them to disk would mean writing to the
    /// file continuously until the user lets go of the mouse.
    /// </remarks>
    public static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly string _filePath;
    private readonly TimeSpan _saveDelay;
    private readonly SettingsMigrator _migrator;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _scheduleLock = new();

    private AppSettings _current = new();
    private CancellationTokenSource? _pendingSave;
    private Task _pendingSaveTask = Task.CompletedTask;

    public SettingsStore(string? filePath = null, TimeSpan? saveDelay = null)
        : this(filePath, saveDelay, new SettingsMigrator())
    {
    }

    internal SettingsStore(string? filePath, TimeSpan? saveDelay, SettingsMigrator migrator)
    {
        _filePath = filePath ?? Path.Combine(RecentRepositoryStore.ConfigurationDirectory(), "settings.json");
        _saveDelay = saveDelay ?? DefaultSaveDelay;
        _migrator = migrator;
    }

    /// <summary>The name a file found to be corrupt is moved to.</summary>
    public string InvalidFilePath => _filePath + ".invalid";

    public AppSettings Current => _current;

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _current = await ReadAsync(cancellationToken).ConfigureAwait(false);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        change(_current);

        Changed?.Invoke(this, EventArgs.Empty);

        ScheduleSave();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task pending;

        lock (_scheduleLock)
        {
            // Cancel the pending delay: we want it to WRITE, not to wait.
            _pendingSave?.Cancel();
            pending = _pendingSaveTask;
        }

        await pending.ConfigureAwait(false);

        await WriteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync().ConfigureAwait(false);

        _writeLock.Dispose();
    }

    /// <summary>
    /// Synchronous shutdown: any pending save is written <b>blocking</b>.
    /// </summary>
    /// <remarks>
    /// Blocking is the right thing here: the alternative would be exiting without saving the setting
    /// the user just changed.
    /// </remarks>
    public void Dispose()
    {
        FlushAsync().GetAwaiter().GetResult();

        _writeLock.Dispose();
    }

    private void ScheduleSave()
    {
        lock (_scheduleLock)
        {
            _pendingSave?.Cancel();
            _pendingSave?.Dispose();

            CancellationTokenSource source = new();
            _pendingSave = source;

            _pendingSaveTask = DelayThenWriteAsync(source.Token);
        }
    }

    private async Task DelayThenWriteAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_saveDelay > TimeSpan.Zero)
            {
                await Task.Delay(_saveDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Either a new change arrived (which will schedule its own save) or FlushAsync cut the
            // wait short (in which case it will write itself). In both cases we do not write here.
            return;
        }

        await WriteAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<AppSettings> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        string text;

        try
        {
            text = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read is NOT corrupt: renaming it would lose the user's settings
            // over a temporary lock. We carry on with the defaults.
            return new AppSettings();
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }

        if (root is null)
        {
            PreserveInvalidFile();

            return new AppSettings();
        }

        JsonObject? migrated = _migrator.Migrate(root);

        if (migrated is null)
        {
            // A file from the future, or one that cannot be migrated. NOT CORRUPT — it is not touched
            // and not renamed; the defaults are simply used for this session.
            return new AppSettings();
        }

        try
        {
            return SettingsSerializer.Deserialize(migrated) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Valid JSON but not in the expected shape (`appearance` being an array, say).
            PreserveInvalidFile();

            return new AppSettings();
        }
    }

    /// <summary>
    /// Moves a corrupt file aside rather than deleting it.
    /// </summary>
    /// <remarks>
    /// The settings file is a file <b>the user writes</b>. Overwriting it would also make it
    /// impossible for them to see the line they broke while editing it by hand. This is where it
    /// parts company with the recent-repositories list (that is derived data and can be reset
    /// silently).
    /// </remarks>
    private void PreserveInvalidFile()
    {
        try
        {
            File.Move(_filePath, InvalidFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            string text = SettingsSerializer.Serialize(_current);

            // First to a temporary file, then moved into place. Writing directly leaves a HALF file
            // behind if the operation is interrupted mid-write (a crash, a power cut) — and that file
            // counts as "corrupt" on the next start, meaning the loss of all the settings.
            string temporary = _filePath + ".tmp";

            await File.WriteAllTextAsync(temporary, text, cancellationToken).ConfigureAwait(false);

            File.Move(temporary, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only home directory, a full disk. Failing to save a setting does not stop the app.
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
