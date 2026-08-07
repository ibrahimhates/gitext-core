namespace GitExt.UI.Settings;

/// <summary>
/// Oturumlar arası hatırlananları yazar ve okur (P08-T16).
/// </summary>
/// <remarks>
/// <para>
/// Ayrı bir sınıf çünkü <b>ne hatırlanacağı</b> bir ürün kararı, ayar dosyasının biçimi
/// değil. Burada tutulan üç şey var: son açılan depo, o depodaki son seçili commit ve
/// pencerenin boyutu (sonuncusu <c>MainWindow.Layout</c>'ta, pencereyi tanıdığı için).
/// </para>
/// <para>
/// <b>Seçili commit depo başına saklanıyor.</b> Tek bir "son seçili commit" tutmak,
/// depo değiştiren kullanıcıda anlamsız — üstelik SHA başka bir depoda hiç bulunmaz ve
/// geri yükleme sessizce hiçbir şey yapmazdı.
/// </para>
/// </remarks>
public sealed class SessionTracker
{
    /// <summary>
    /// Depo başına saklanan en fazla seçim kaydı.
    /// </summary>
    /// <remarks>
    /// Sınırsız bırakılsaydı ayar dosyası, kullanıcının bir kez açtığı her deponun kaydıyla
    /// zamanla büyürdü. Son açılanlar listesiyle aynı boyutta tutuluyor: daha eski bir
    /// deponun seçimini hatırlamanın zaten bir yolu yok.
    /// </remarks>
    public const int MaximumTrackedRepositories = 12;

    private readonly ISettingsStore _settings;

    public SessionTracker(ISettingsStore settings) => _settings = settings;

    /// <summary>Kapanışta açık olan depo; yoksa boş.</summary>
    public string LastRepository => _settings.Current.Session.LastRepository;

    public void RememberRepository(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _settings.Update(s => s.Session.LastRepository = workingDirectory);
    }

    /// <summary>Depo kapandığında çağrılır; sonraki açılışta karşılama ekranı gelir.</summary>
    public void ForgetRepository() =>
        _settings.Update(s => s.Session.LastRepository = "");

    public string? SelectedCommit(string workingDirectory) =>
        _settings.Current.Session.SelectedCommits.GetValueOrDefault(workingDirectory);

    public void RememberSelectedCommit(string workingDirectory, string sha)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(sha))
        {
            return;
        }

        _settings.Update(s =>
        {
            s.Session.SelectedCommits[workingDirectory] = sha;

            if (s.Session.SelectedCommits.Count <= MaximumTrackedRepositories)
            {
                return;
            }

            // Sıra bilgisi yok; en son yazılanı korumak için yalnızca güncel depo dışında
            // rastgele bir kayıt atılıyor. Kesin bir LRU tutmak, ayar dosyasına zaman damgası
            // eklemek demekti — hatırlanan bir seçim için fazla bedel.
            string? victim = s.Session.SelectedCommits.Keys
                .FirstOrDefault(k => !string.Equals(k, workingDirectory, StringComparison.Ordinal));

            if (victim is not null)
            {
                s.Session.SelectedCommits.Remove(victim);
            }
        });
    }
}
