using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Günlükteki tek bir satır (P06-T16).
/// </summary>
public sealed class CommandLogRowViewModel
{
    public required GitCommandLogEntry Entry { get; init; }

    public string Time => Entry.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public string CommandLine => Entry.CommandLine;

    /// <summary>
    /// Süre; milisaniyenin altındakiler de görünsün diye ondalık.
    /// </summary>
    /// <remarks>
    /// Süreyi göstermek panelin asıl işlerinden biri: bu projede bir okuma yolunun
    /// beklenenden yavaş olduğu birkaç kez ölçümle bulundu, ama kullanıcı için tek
    /// görünür yer burası.
    /// </remarks>
    public string Duration => Entry.Duration.TotalSeconds >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{Entry.Duration.TotalSeconds:0.00} sn")
        : string.Create(CultureInfo.InvariantCulture, $"{Entry.Duration.TotalMilliseconds:0} ms");

    /// <summary>
    /// Çıkış kodu; süreç hiç tamamlanmadıysa (iptal, zaman aşımı) tire.
    /// </summary>
    /// <remarks>
    /// ⚠️ <see langword="null"/> ile <c>0</c> aynı şey değil: ilki "bitmedi", ikincisi
    /// "başarıyla bitti". <c>0</c> yazmak iptal edilen bir komutu başarılı gösterirdi.
    /// </remarks>
    public string ExitCode => Entry.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "—";

    public bool IsSuccess => Entry.IsSuccess;

    /// <summary>stderr ya da tamamlanmama nedeni.</summary>
    public string Details => Entry.Details.TrimEnd();

    public bool HasDetails => Details.Length > 0;
}

/// <summary>
/// Git komut günlüğü paneli (P06-T16).
/// </summary>
/// <remarks>
/// <para>
/// Planın maddesi: <i>"Kullanıcı ne olduğunu her zaman görebilmeli."</i> Altyapı
/// P02-T05'te kurulmuştu (<see cref="IGitCommandLog"/>); burada gösterimi ve <b>canlı
/// akışı</b> geliyor.
/// </para>
/// <para>
/// ⚠️ <b>Kayıtlar arayüz iş parçacığında gelmiyor</b> — git süreçleri havuz iş
/// parçacıklarında çalışıyor. Koleksiyona doğrudan eklemek Avalonia'da çökme ya da sessiz
/// bozulma üretirdi; bu yüzden her kayıt <see cref="Dispatcher"/> üzerinden geçiyor.
/// </para>
/// <para>
/// 🔒 <b>Gizli değerler günlüğe girmiyor:</b> kimlik bilgisi komut satırına değil ortama
/// yazılıyor (P06-T09) ve <c>ToDisplayString</c> ortamı hiç yazmıyor.
/// </para>
/// </remarks>
public sealed class CommandLogViewModel : ViewModelBase, IDisposable
{
    private readonly IGitCommandLog _log;
    private readonly int _capacity;

    private bool _onlyFailures;
    private CommandLogRowViewModel? _selected;
    private bool _disposed;

    public CommandLogViewModel(IGitCommandLog log, int capacity = 500)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
        _capacity = capacity;

        if (log is InMemoryGitCommandLog memory)
        {
            // Panel açılmadan önce çalışmış komutlar da görünsün: açılışta boş bir liste,
            // "hiçbir şey çalışmadı" gibi okunurdu.
            foreach (GitCommandLogEntry entry in memory.Entries)
            {
                All.Add(new CommandLogRowViewModel { Entry = entry });
            }
        }

        _log.Recorded += OnRecorded;

        ClearCommand = new RelayCommand(Clear);

        Refresh();
    }

    /// <summary>Tüm kayıtlar, en yeniden en eskiye.</summary>
    private ObservableCollection<CommandLogRowViewModel> All { get; } = [];

    /// <summary>Ekranda gösterilen kayıtlar.</summary>
    public ObservableCollection<CommandLogRowViewModel> Rows { get; } = [];

    /// <summary>Yalnızca başarısız komutlar gösterilsin mi?</summary>
    public bool OnlyFailures
    {
        get => _onlyFailures;
        set
        {
            if (SetProperty(ref _onlyFailures, value))
            {
                Refresh();
            }
        }
    }

    public CommandLogRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(SelectedDetails));
                OnPropertyChanged(nameof(HasSelectedDetails));
            }
        }
    }

    /// <summary>Seçili kaydın ayrıntısı.</summary>
    public string SelectedDetails => Selected?.Details ?? string.Empty;

    public bool HasSelectedDetails => SelectedDetails.Length > 0;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Başarısız komut sayısı — süzme kutusunun yanında gösteriliyor.</summary>
    public int FailureCount => All.Count(row => !row.IsSuccess);

    /// <summary>Başarısız komut sayısının çevrilmiş metni (P11-T08).</summary>
    public string FailureCountText => Loc.F("command_log.failed_commands", FailureCount);

    public IRelayCommand ClearCommand { get; }

    private void OnRecorded(object? sender, GitCommandLogEntry entry)
    {
        // ⚠️ Bu çağrı arayüz iş parçacığında DEĞİL.
        Dispatcher.UIThread.Post(() => Append(entry));
    }

    private void Append(GitCommandLogEntry entry)
    {
        All.Insert(0, new CommandLogRowViewModel { Entry = entry });

        // Sınır olmadan uzun bir oturumda bellek şişer; günlüğün kendisi de halka tampon.
        while (All.Count > _capacity)
        {
            All.RemoveAt(All.Count - 1);
        }

        Refresh();
    }

    private void Clear()
    {
        All.Clear();
        Refresh();
    }

    private void Refresh()
    {
        Rows.Clear();

        foreach (CommandLogRowViewModel row in All)
        {
            if (!OnlyFailures || !row.IsSuccess)
            {
                Rows.Add(row);
            }
        }

        // Süzme sonrası seçim listede kalmayabilir; ayrıntı panelinin eski bir kaydı
        // göstermeye devam etmesi kullanıcıyı yanıltırdı.
        if (Selected is { } selected && !Rows.Contains(selected))
        {
            Selected = null;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(FailureCount));
        OnPropertyChanged(nameof(FailureCountText));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _log.Recorded -= OnRecorded;
    }
}

/// <summary>Komut günlüğü panelini gösteren taraf (P06-T16).</summary>
public interface ICommandLogPrompt
{
    Task ShowAsync(CommandLogViewModel model);
}
