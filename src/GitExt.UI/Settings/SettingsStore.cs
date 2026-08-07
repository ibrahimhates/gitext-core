using System.Text.Json;
using System.Text.Json.Nodes;
using GitExt.UI.Storage;

namespace GitExt.UI.Settings;

/// <summary>
/// Kullanıcı ayarlarının tek erişim noktası (P08-T14).
/// </summary>
public interface ISettingsStore
{
    /// <summary>Yüklü ayarlar. <see cref="LoadAsync"/> çağrılmadan önce varsayılanlardır.</summary>
    AppSettings Current { get; }

    /// <summary>Ayarlar her değiştiğinde tetiklenir.</summary>
    event EventHandler? Changed;

    /// <summary>Diskteki dosyayı okur. Uygulama açılışında bir kez çağrılır.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ayarları değiştirir, <see cref="Changed"/>'i tetikler ve kaydı <b>zamanlar</b>.
    /// </summary>
    /// <remarks>
    /// Tek yazma yolu budur. Doğrudan <see cref="Current"/> üzerinde değişiklik yapmak
    /// bildirimi de kaydı da atlar.
    /// </remarks>
    void Update(Action<AppSettings> change);

    /// <summary>Bekleyen kaydı hemen diske yazar. Kapanışta çağrılmalıdır.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Ayarları platform standardı yapılandırma dizinindeki tek JSON dosyasında tutar.
/// </summary>
/// <remarks>
/// 🔴 <b><see cref="IDisposable"/> de uygulanmak ZORUNDA.</b> Yalnızca
/// <see cref="IAsyncDisposable"/> uygulandığında <c>Microsoft.Extensions.DependencyInjection</c>
/// konteyneri <b>senkron</b> kapatılırken
/// <c>"type only implements IAsyncDisposable"</c> diye <see cref="InvalidOperationException"/>
/// atıyor — bir test bunu yakaladı. Uygulama bugün konteyneri hiç kapatmadığı için belirti
/// görünmüyordu; kapatan ilk kod yolunda çıkacaktı.
/// </remarks>
public sealed class SettingsStore : ISettingsStore, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Kaydın gecikme süresi.
    /// </summary>
    /// <remarks>
    /// Ayarların çoğu tek tıkla değişir ama <b>düzen</b> değişmez: bir ayırıcıyı sürüklemek
    /// saniyede onlarca değişiklik üretir. Her birini diske yazmak, kullanıcı fareyi
    /// bırakana kadar sürekli dosya yazmak demekti.
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

    /// <summary>Bozuk bulunan dosyanın taşındığı ad.</summary>
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
            // Bekleyen gecikmeyi iptal et: beklemesini değil, YAZMASINI istiyoruz.
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
    /// Senkron kapanış: bekleyen kayıt <b>bloke edilerek</b> yazılır.
    /// </summary>
    /// <remarks>
    /// Bloke etmek burada doğru: alternatif, kullanıcının az önce değiştirdiği ayarı
    /// kaydetmeden çıkmak olurdu.
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
            // Ya yeni bir değişiklik geldi (o kendi kaydını zamanlayacak) ya da FlushAsync
            // beklemeyi kesti (o zaten kendisi yazacak). İki durumda da burada yazmıyoruz.
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
            // Okunamayan dosya bozuk DEĞİL: yeniden adlandırmak, geçici bir kilit yüzünden
            // kullanıcının ayarlarını kaybettirirdi. Varsayılanlarla devam edilir.
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
            // Gelecekten gelen veya göç edilemeyen dosya. BOZUK DEĞİL — dokunulmuyor,
            // yeniden adlandırılmıyor; yalnızca bu oturumda varsayılanlar kullanılıyor.
            return new AppSettings();
        }

        try
        {
            return SettingsSerializer.Deserialize(migrated) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Geçerli JSON ama beklenen şekilde değil (ör. `appearance` bir dizi).
            PreserveInvalidFile();

            return new AppSettings();
        }
    }

    /// <summary>
    /// Bozuk dosyayı silmek yerine yanına taşır.
    /// </summary>
    /// <remarks>
    /// Ayar dosyası <b>kullanıcının yazdığı</b> bir dosya. Üstüne yazmak, elle uğraşıp
    /// bozduğu satırı görmesini de imkânsız kılardı. Son açılanlar listesinden (o türetilmiş
    /// veri, sessizce sıfırlanabilir) ayrıldığı yer burası.
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

            // Önce geçici dosyaya, sonra yerine taşı. Doğrudan yazmak, yazma sırasında
            // kesilen bir işlemde (çökme, elektrik) YARIM bir dosya bırakır — ki o dosya
            // sonraki açılışta "bozuk" sayılıp bütün ayarların kaybı demektir.
            string temporary = _filePath + ".tmp";

            await File.WriteAllTextAsync(temporary, text, cancellationToken).ConfigureAwait(false);

            File.Move(temporary, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Salt okunur ev dizini, dolu disk. Ayarı kaydedememek uygulamayı durdurmaz.
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
