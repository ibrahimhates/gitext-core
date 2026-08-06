using System.Collections.Concurrent;

namespace GitExt.Core.Git;

/// <summary>
/// Depoyu <b>değiştiren</b> git komutlarını çalıştırır (P05-T03).
/// </summary>
/// <remarks>
/// Yazma yolunun tek girişi burasıdır: serileştirme (P05-T01) ve kilit çakışmasında yeniden
/// deneme (P05-T02) burada birleştirilmiştir. Çağıranların ikisini elle kurması gerekmez —
/// birini unutmak sessiz bir hata sınıfı olurdu.
/// </remarks>
public interface IGitWriter
{
    /// <summary>
    /// Yazma komutunu çalıştırır; sıraya girer ve kilit çakışmasında yeniden dener.
    /// </summary>
    /// <param name="workingDirectory">Komutun çalıştırılacağı çalışma dizini.</param>
    /// <param name="arguments">git argümanları.</param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Yazma komutunu, o çağrıya özel ortam değişkenleriyle çalıştırır (P06-T09).
    /// </summary>
    /// <remarks>
    /// Tek kullanım yeri kimlik doğrulama: <c>GIT_ASKPASS</c> ve onun okuyacağı gizli
    /// değer. Parola argüman olarak geçirilemez — komut satırı <c>ps</c> ile herkese
    /// görünür (bkz. <c>AskPassSession</c>).
    /// </remarks>
    Task<GitResult> RunWithEnvironmentAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        IProgress<GitProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Yazma komutunu <b>stdin</b>'e veri vererek çalıştırır.
    /// </summary>
    /// <remarks>
    /// Yamalar ve commit mesajları argüman olarak <b>geçirilemez</b>: uzunluk sınırı ve
    /// kabuk yorumlaması riski var (ADR-0002).
    /// </remarks>
    /// <param name="workingDirectory">Çalışma dizini.</param>
    /// <param name="arguments">git argümanları.</param>
    /// <param name="standardInput">stdin'e yazılacak metin.</param>
    /// <param name="encoding">
    /// Metnin hangi kodlamayla baytlanacağı; varsayılan UTF-8. Yamalarda bu, <b>dosyanın
    /// kodlaması</b> olmalıdır — git yamayı çalışma ağacındaki baytlarla karşılaştırıyor.
    /// </param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string standardInput,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IGitWriter"/>
public sealed class GitWriter : IGitWriter
{
    /// <summary>
    /// Bir yazma komutunun süreç sınırı. <see cref="GitCommand"/> varsayılanı 2 dakika.
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ (P05-T07):</b> 2 dakika yetmiyor. Bu, tek bir komutun değil <b>yazma
    /// yolunun</b> özelliği: yazma komutları kullanıcının hook'larını çalıştırır ve hook
    /// keyfi bir iş yapabilir — <c>pre-commit</c> test takımı, <c>pre-push</c> derleme.
    /// Sınırı tek tek komut seçeneklerine koymak, aynı sayıyı <c>commit</c>, <c>push</c>,
    /// <c>rebase</c>, <c>merge</c> için ayrı ayrı tekrarlamak olurdu.
    /// <para>
    /// Sınıra takıldığında süreç öldürülüyor; ölçüldü — commit <b>oluşmuyor</b> ve geride
    /// <c>index.lock</c> <b>kalmıyor</b> (git kilidi hook'tan sonra alıyor, P05-T02 ile aynı
    /// bulgu). Yani zaman aşımı veri kaybettirmiyor, sadece işi bitirmiyor.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultWriteTimeout = TimeSpan.FromMinutes(10);

    private readonly IGitProcessRunner _runner;
    private readonly IGitWriteQueue _queue;
    private readonly GitLockRetryOptions _retryOptions;
    private readonly TimeSpan _writeTimeout;

    /// <summary>
    /// Çalışma dizini → git dizini eşlemesi.
    /// </summary>
    /// <remarks>
    /// Kuyruk anahtarı git dizinidir (worktree'ler ayrı index'e sahip). Her yazmada
    /// <c>rev-parse</c> çalıştırmak ~1 ms; yine de önbellekleniyor çünkü satır seviyesinde
    /// staging'de (P05-T04) çağrı sayısı yükselecek.
    /// </remarks>
    private readonly ConcurrentDictionary<string, string> _gitDirectories =
        new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    public GitWriter(
        IGitProcessRunner runner,
        IGitWriteQueue queue,
        GitLockRetryOptions? retryOptions = null,
        TimeSpan? writeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(queue);

        _runner = runner;
        _queue = queue;
        _retryOptions = retryOptions ?? GitLockRetryOptions.Default;
        _writeTimeout = writeTimeout ?? DefaultWriteTimeout;
    }

    public Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(workingDirectory, arguments, null, null, null, cancellationToken);

    public Task<GitResult> RunWithEnvironmentAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        IProgress<GitProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(workingDirectory, arguments, null, environment, progress, cancellationToken);

    public Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string standardInput,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(standardInput);

        return RunCoreAsync(
            workingDirectory,
            arguments,
            (encoding ?? System.Text.Encoding.UTF8).GetBytes(standardInput),
            null,
            null,
            cancellationToken);
    }

    private async Task<GitResult> RunCoreAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte>? standardInput,
        IReadOnlyDictionary<string, string>? environment,
        IProgress<GitProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        string gitDirectory = await ResolveGitDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return await _queue.RunAsync(
            gitDirectory,
            token => GitLockRetry.RunAsync(
                // ⚠️ `RunCheckedAsync` — `RunAsync` başarısız çıkışta HATA FIRLATMIYOR,
                // yalnızca logluyor. Yazma yolunda bu, başarısız bir commit'in başarılı
                // sayılması demekti; bir test yakaladı (boş mesajlı commit reddedildi ama
                // akış devam edip `rev-parse HEAD`'i ayrıştırmaya çalıştı).
                // Kilit yeniden denemesi de buna bağlı: hata fırlamazsa retry hiç tetiklenmez.
                inner => _runner.RunCheckedAsync(
                    BuildCommand(workingDirectory, arguments, standardInput, environment, progress),
                    inner),
                _retryOptions,
                token),
            cancellationToken).ConfigureAwait(false);
    }

    private GitCommand BuildCommand(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte>? standardInput,
        IReadOnlyDictionary<string, string>? environment,
        IProgress<GitProgress>? progress) =>
        new()
        {
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            StandardInput = standardInput,
            Environment = environment,
            Progress = progress,

            // ⚠️ Yazma komutu: `GIT_OPTIONAL_LOCKS=0` uygulanmaz. Bu bayrak yalnızca
            // "opsiyonel" kilitleri kapatır; yazmanın gerçek kilidi zaten alınmak zorunda.
            IsReadOnly = false,

            Timeout = _writeTimeout,
        };

    private async Task<string> ResolveGitDirectoryAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (_gitDirectories.TryGetValue(workingDirectory, out string? cached))
        {
            return cached;
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--absolute-git-dir"],
            },
            cancellationToken).ConfigureAwait(false);

        string gitDirectory = result.GetStandardOutputText().Trim();

        if (gitDirectory.Length == 0)
        {
            // Kuyruk anahtarsız kalmasın: çalışma dizini yedek anahtar olur. Serileştirme
            // biraz geniş olur ama HİÇ olmamasından iyidir.
            gitDirectory = workingDirectory;
        }

        _gitDirectories[workingDirectory] = gitDirectory;

        return gitDirectory;
    }
}
