using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.UI.Storage;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Karşılama ekranındaki son açılan depo girdisi (P03-T16).
/// </summary>
public sealed class RecentRepositoryItem
{
    public RecentRepositoryItem(string path, ICommand openCommand)
    {
        Path = path;
        OpenCommand = openCommand;

        // Klasör adı listeyi taramayı kolaylaştırır; tam yol ikinci satırda kalır.
        Name = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(Name))
        {
            Name = path;
        }
    }

    public string Path { get; }

    public string Name { get; }

    public ICommand OpenCommand { get; }

    /// <summary>
    /// Klasör hâlâ duruyor mu?
    /// </summary>
    /// <remarks>
    /// Kayıp girdiler listeden <b>silinmiyor</b>, soluk gösteriliyor: bağlı olmayan bir disk
    /// veya geçici olarak erişilemeyen bir ağ yolu, kullanıcının listesini kalıcı olarak
    /// budamak için yeterli sebep değil.
    /// </remarks>
    public bool Exists => Directory.Exists(Path);

    public override string ToString() => Path;
}

/// <summary>
/// Ana pencerenin ViewModel'ı.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IRecentRepositoryStore _recentStore;

    public MainWindowViewModel(CommitListViewModel commits, IRecentRepositoryStore recentStore)
    {
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(recentStore);

        Commits = commits;
        _recentStore = recentStore;

        OpenRecentCommand = new AsyncRelayCommand<string>(
            async path =>
            {
                if (!string.IsNullOrEmpty(path))
                {
                    await OpenRepositoryAsync(path).ConfigureAwait(true);
                }
            });

        CancelLoadingCommand = new AsyncRelayCommand(Commits.CancelLoadingAsync);

        RecentRepositories.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasRecentRepositories));

        Commits.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CommitListViewModel.IsLoading)
                or nameof(CommitListViewModel.Repository))
            {
                OnPropertyChanged(nameof(ShowWelcome));
            }
        };
    }

    /// <summary>Commit geçmişi listesi.</summary>
    public CommitListViewModel Commits { get; }

    /// <summary>Son açılan depolar, en yeni ilk sırada.</summary>
    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = [];

    /// <summary>
    /// Gösterilecek son açılan depo var mı?
    /// </summary>
    /// <remarks>
    /// Ayrı bir <see cref="bool"/> gerekli: <c>IsVisible</c>'a doğrudan <c>Count</c> bağlamak
    /// <b>çalışmıyor</b> — Avalonia <c>int</c>'i <c>bool</c>'a çevirmiyor ve bölüm sessizce
    /// hiç görünmüyordu (render'da yakalandı).
    /// </remarks>
    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    public ICommand OpenRecentCommand { get; }

    public ICommand CancelLoadingCommand { get; }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "Depo açılmadı.";

    /// <summary>
    /// Karşılama ekranı gösterilsin mi?
    /// </summary>
    /// <remarks>
    /// Açık depo yoksa ve bir şey yüklenmiyorsa. Yükleme sırasında gizlenmesi kasıtlı:
    /// aksi halde açılışta karşılama ekranı bir an görünüp kaybolur.
    /// </remarks>
    public bool ShowWelcome => Commits.Repository is null && !Commits.IsLoading;

    /// <summary>
    /// Uygulama açılışındaki depo seçimi (P03-T16).
    /// </summary>
    /// <param name="explicitPath">
    /// Komut satırında verilen yol; verilmediyse <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    /// <remarks>
    /// <para>
    /// Yol açıkça verildiyse açılamaması bir <b>hatadır</b> ve kullanıcıya söylenir — o yolu
    /// isteyen kullanıcıdır.
    /// </para>
    /// <para>
    /// Verilmediyse çalışma dizini <b>sessizce</b> denenir. Uygulama masaüstünden veya
    /// menüden başlatıldığında çalışma dizini rastgele bir yerdir; "burası depo değil"
    /// hatası göstermek anlamsız olur. Depo değilse karşılama ekranı açılır.
    /// </para>
    /// </remarks>
    public async Task StartAsync(string? explicitPath, CancellationToken cancellationToken = default)
    {
        await LoadRecentAsync(cancellationToken).ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            await OpenRepositoryAsync(explicitPath, cancellationToken).ConfigureAwait(true);
            return;
        }

        await TryOpenQuietlyAsync(Directory.GetCurrentDirectory(), cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Bir depoyu açar, başlığı günceller ve son açılanlara ekler.
    /// </summary>
    public async Task OpenRepositoryAsync(string path, CancellationToken cancellationToken = default)
    {
        await Commits.OpenAsync(path, cancellationToken).ConfigureAwait(true);

        Subtitle = Commits.Repository is { } repository
            ? $"{repository.WorkingDirectory} — {Commits.Rows.Count} commit"
            : Commits.ErrorMessage ?? "Depo açılamadı.";

        OnPropertyChanged(nameof(ShowWelcome));

        if (Commits.Repository is { } opened)
        {
            // Kullanıcının verdiği yol değil, git'in çözdüğü kök kaydedilir: alt klasörden
            // açıldığında listede iki farklı girdi oluşmasın.
            await AddRecentAsync(opened.WorkingDirectory, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Pencereye bırakılan yolları açmayı dener (P03-T16, sürükle-bırak).
    /// </summary>
    /// <remarks>
    /// Bırakılan şey bir <b>dosya</b> olabilir (kullanıcı dosya yöneticisinden bir dosyayı
    /// sürükler); o zaman bulunduğu klasör denenir. <c>git</c> zaten üst klasörlere doğru
    /// depo kökünü arar, yani deponun içindeki herhangi bir dosya yeterlidir.
    /// </remarks>
    /// <returns>Bir yol açılmaya çalışıldıysa <see langword="true"/>.</returns>
    public async Task<bool> TryOpenDroppedAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string directory = Directory.Exists(path)
                ? path
                : System.IO.Path.GetDirectoryName(path) ?? path;

            await OpenRepositoryAsync(directory, cancellationToken).ConfigureAwait(true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Depo olmayabilecek bir yolu, başarısızlığı hata olarak göstermeden dener.
    /// </summary>
    private async Task TryOpenQuietlyAsync(string path, CancellationToken cancellationToken)
    {
        await Commits.OpenAsync(path, cancellationToken).ConfigureAwait(true);

        if (Commits.Repository is { } repository)
        {
            Subtitle = $"{repository.WorkingDirectory} — {Commits.Rows.Count} commit";
            await AddRecentAsync(repository.WorkingDirectory, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            // Hata mesajı temizleniyor: kullanıcı bu klasörü açmayı istemedi.
            Commits.ErrorMessage = null;
            Subtitle = "Depo açılmadı.";
        }

        OnPropertyChanged(nameof(ShowWelcome));
    }

    private async Task LoadRecentAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> recent;

        try
        {
            recent = await _recentStore.LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        RecentRepositories.Clear();

        foreach (string path in recent)
        {
            RecentRepositories.Add(new RecentRepositoryItem(path, OpenRecentCommand));
        }
    }

    private async Task AddRecentAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            await _recentStore.AddAsync(workingDirectory, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        await LoadRecentAsync(cancellationToken).ConfigureAwait(true);
    }
}
