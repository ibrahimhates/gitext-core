using GitExt.UI.Settings;

namespace GitExt.UI.Tests;

/// <summary>
/// Settings store that never touches the disk. For tests that <b>consume</b> settings.
/// </summary>
/// <remarks>
/// File behavior (atomic write, corrupt file, migration) is the subject of <c>SettingsStoreTests</c>;
/// mixing it in here would turn every command test into a file system test.
/// </remarks>
public sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; } = new();

    public int FlushCount { get; private set; }

    public event EventHandler? Changed;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Update(Action<AppSettings> change)
    {
        change(Current);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        FlushCount++;

        return Task.CompletedTask;
    }
}
