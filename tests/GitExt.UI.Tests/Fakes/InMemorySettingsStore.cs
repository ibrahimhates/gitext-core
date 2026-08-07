using GitExt.UI.Settings;

namespace GitExt.UI.Tests;

/// <summary>
/// Diske dokunmayan ayar deposu. Ayarları <b>tüketen</b> testler için.
/// </summary>
/// <remarks>
/// Dosya davranışı (atomik yazma, bozuk dosya, göç) <c>SettingsStoreTests</c>'in konusu;
/// buraya karıştırmak her komut testini bir dosya sistemi testine çevirirdi.
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
