using System.Collections.Concurrent;

namespace GitExt.Core.Git;

/// <summary>
/// Aynı depoya yapılan yazma işlemlerini <b>seri</b> hâle getirir (P05-T01).
/// </summary>
public interface IGitWriteQueue
{
    /// <summary>
    /// İşlemi, aynı depoya yazan diğer işlemler bitene kadar bekleterek çalıştırır.
    /// </summary>
    /// <param name="gitDirectory">
    /// Deponun <b>git dizini</b> — <c>rev-parse --absolute-git-dir</c> çıktısı. Worktree'ler
    /// ayrı index'e sahip olduğu için ortak dizin (<c>--git-common-dir</c>) <b>değil</b>.
    /// </param>
    /// <param name="operation">Sıra geldiğinde çalıştırılacak yazma işlemi.</param>
    /// <param name="cancellationToken">Beklerken de geçerli olan iptal jetonu.</param>
    Task<T> RunAsync<T>(
        string gitDirectory,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="RunAsync{T}"/>
    Task RunAsync(
        string gitDirectory,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Depo başına tek yazar kuralını uygulayan kuyruk (P05-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ — neden gerekli:</b> git eşzamanlı yazmayı <b>beklemez</b>, anında düşer.
/// Aynı depoda 8 paralel <c>git add</c> çalıştırıldığında <b>7'si</b>
/// <c>fatal: Unable to create '…/index.lock': File exists</c> ile başarısız oldu.
/// </para>
/// <para>
/// <b>Kilit kapsamı git dizini, ortak dizin değil.</b> Ölçüldü: iki worktree'de eşzamanlı
/// <c>git add</c> çalıştırıldığında <b>hiç çakışma olmadı</b> — her worktree'nin kendi
/// index'i var (<c>.git/worktrees/&lt;ad&gt;/index</c>). Ortak dizinle anahtarlamak,
/// kullanıcının iki worktree'de paralel çalışmasını gereksiz yere engellerdi.
/// </para>
/// <para>
/// ⚠️ <b>Ref yazmaları farklı kapsamda:</b> dallar ve etiketler ortak dizinde yaşıyor
/// (Faz 02'de ölçüldü). Faz 06'da ref yazan komutlar gelince o işlemler için ortak dizin
/// anahtarı gerekecek — bu sınıf anahtarı dışarıdan aldığı için değişiklik gerektirmiyor.
/// </para>
/// <para>
/// <b>Okumalar kuyruğa GİRMEZ.</b> Ölçüldü: <c>index.lock</c> dosyası dururken
/// <c>status</c>, <c>log</c>, <c>diff</c>, <c>diff --cached</c>, <c>show</c> ve
/// <c>for-each-ref</c> sorunsuz çalışıyor (git opsiyonel kilidi alamayınca sessizce
/// vazgeçiyor).
/// </para>
/// </remarks>
public sealed class GitWriteQueue : IGitWriteQueue, IDisposable
{
    /// <summary>
    /// Yol karşılaştırması: Linux'ta büyük/küçük harf duyarlı, Windows ve macOS'ta değil.
    /// </summary>
    private static readonly StringComparer _pathComparer =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _queues = new(_pathComparer);

    private bool _disposed;

    /// <summary>O an kuyruk tutulan depo sayısı (teşhis için).</summary>
    public int TrackedRepositories => _queues.Count;

    public async Task<T> RunAsync<T>(
        string gitDirectory,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        SemaphoreSlim queue = _queues.GetOrAdd(Normalize(gitDirectory), _ => new SemaphoreSlim(1, 1));

        await queue.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            queue.Release();
        }
    }

    public Task RunAsync(
        string gitDirectory,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunAsync<object?>(
            gitDirectory,
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
    }

    /// <summary>
    /// Yolu anahtar olarak kullanılabilecek biçime getirir.
    /// </summary>
    /// <remarks>
    /// Aynı depoya iki farklı yazımla (<c>/repo/.git</c> ve <c>/repo/.git/</c>) gelen
    /// çağrılar aynı kuyruğa düşmeli; aksi hâlde serileştirme <b>sessizce</b> devre dışı
    /// kalırdı.
    /// </remarks>
    private static string Normalize(string gitDirectory)
    {
        string full = Path.GetFullPath(gitDirectory);

        return full.Length > 1
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (SemaphoreSlim queue in _queues.Values)
        {
            queue.Dispose();
        }

        _queues.Clear();
    }
}
