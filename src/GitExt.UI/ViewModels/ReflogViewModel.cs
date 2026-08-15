using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>Reflog listesindeki bir satır (P07-T14).</summary>
public sealed class ReflogRowViewModel
{
    public required ReflogEntry Entry { get; init; }

    public string ShortId => Entry.ShortId;

    public string Selector => Entry.Selector;

    public string Message => Entry.Message;

    public string Subject => Entry.Subject;

    public string Timestamp =>
        Entry.Timestamp == default
            ? string.Empty
            : Entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Bu commit'e artık hiçbir daldan erişilemiyor mu?
    /// </summary>
    /// <remarks>
    /// Reflog tarayıcısının asıl işi bu: kullanıcı <c>reset --hard</c> ile kaybettiği
    /// commit'i burada bulacak. İşaretli satırlar öne çıkarılıyor.
    /// </remarks>
    public bool IsUnreachable => Entry.IsUnreachable;

    public string RecoveryCommand => Entry.RecoveryCommand;
}

/// <summary>
/// Reflog tarayıcısı (P07-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Fazın sigortası.</b> Faz 07'deki her işlem geçmişi yeniden yazıyor; kullanıcı bir
/// şeyi kaybettiğinde onu buradan geri alacak.
/// </para>
/// <para>
/// ⚠️ "Buraya dön" komutu <b>seçiciyi değil SHA'yı</b> kullanıyor: <c>HEAD@{3}</c> kayan
/// bir referans ve yeni bir işlem reflog'a girdi eklediğinde başka bir commit'i gösterir.
/// </para>
/// </remarks>
public sealed class ReflogViewModel : ViewModelBase
{
    private readonly string _workingDirectory;
    private readonly IReflogReader _reflog;
    private readonly IResetWriter? _reset;

    private ReflogRowViewModel? _selected;
    private bool _onlyUnreachable;
    private string? _error;
    private string? _notice;
    private bool _isBusy;

    public ReflogViewModel(string workingDirectory, IReflogReader reflog, IResetWriter? reset = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(reflog);

        _workingDirectory = workingDirectory;
        _reflog = reflog;
        _reset = reset;

        ReturnCommand = new AsyncRelayCommand(ReturnAsync, CanReturn);
    }

    private ObservableCollection<ReflogRowViewModel> All { get; } = [];

    public ObservableCollection<ReflogRowViewModel> Rows { get; } = [];

    public ReflogRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedRecoveryCommand));
                ReturnCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>Seçili girdiye dönmek için çalıştırılacak komut — ekranda gösteriliyor.</summary>
    public string SelectedRecoveryCommand => Selected?.RecoveryCommand ?? string.Empty;

    /// <summary>Yalnızca erişilemeyen ("kayıp") commit'leri göster.</summary>
    public bool OnlyUnreachable
    {
        get => _onlyUnreachable;
        set
        {
            if (SetProperty(ref _onlyUnreachable, value))
            {
                ApplyFilter();
            }
        }
    }

    public int UnreachableCount => All.Count(row => row.IsUnreachable);

    public bool IsEmpty => Rows.Count == 0;

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public string? Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ReturnCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand ReturnCommand { get; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReflogEntry> entries = await _reflog
            .ReadAsync(_workingDirectory, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        All.Clear();

        foreach (ReflogEntry entry in entries)
        {
            All.Add(new ReflogRowViewModel { Entry = entry });
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Clear();

        foreach (ReflogRowViewModel row in All)
        {
            if (!OnlyUnreachable || row.IsUnreachable)
            {
                Rows.Add(row);
            }
        }

        if (Selected is { } selected && !Rows.Contains(selected))
        {
            Selected = null;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(UnreachableCount));
    }

    private bool CanReturn() => Selected is not null && _reset is not null && !IsBusy;

    /// <remarks>
    /// <c>--hard</c> ile dönülüyor: kullanıcının istediği şey "o andaki hâle dön". Ama bu
    /// commit'lenmemiş işi siler, o yüzden güvenlik noktası alınıp geri dönüş komutu
    /// gösteriliyor — kaybın kendisi de geri alınabilir olsun.
    /// </remarks>
    private async Task ReturnAsync()
    {
        IsBusy = true;
        Error = null;
        Notice = null;

        try
        {
            SafetyPoint point = await _reset!.ResetAsync(
                _workingDirectory,
                new ResetOptions { Target = Selected!.Entry.ObjectId, Mode = ResetMode.Hard })
                .ConfigureAwait(true);

            Notice = point.IsFullyRecoverable
                ? $"Moved to {Selected.ShortId}. To undo: {point.RecoveryCommand}"
                : $"Moved to {Selected.ShortId}. To undo: {point.RecoveryCommand} "
                  + "(uncommitted changes were discarded and cannot be restored).";

            await RefreshAsync().ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Error = Loc.GitError(error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Reflog tarayıcısını gösteren taraf (P07-T14).</summary>
public interface IReflogPrompt
{
    Task ShowAsync(ReflogViewModel model);
}
