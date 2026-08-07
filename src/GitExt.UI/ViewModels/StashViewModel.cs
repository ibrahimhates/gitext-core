using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;

namespace GitExt.UI.ViewModels;

/// <summary>Listedeki bir stash satırı (P07-T13).</summary>
public sealed class StashRowViewModel
{
    public required StashEntry Entry { get; init; }

    public string Selector => Entry.ShortSelector;

    public string Message => Entry.Message;

    public string Timestamp =>
        Entry.Timestamp == default
            ? string.Empty
            : Entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Takip edilmeyen dosyalar da içeride mi?</summary>
    /// <remarks>
    /// Gösteriliyor çünkü uygulandığında ortaya çıkacak dosyaları kullanıcı önceden
    /// bilmeli — özellikle <c>pop</c> geri alınamadığı için.
    /// </remarks>
    public string Badge => Entry.IncludesUntracked ? "＋untracked" : string.Empty;

    public bool HasBadge => Entry.IncludesUntracked;
}

/// <summary>
/// Stash ekranı (P07-T13).
/// </summary>
/// <remarks>
/// Yerleşim GitExtensions <c>FormStash</c>'ten (§ 9): solda stash listesi, sağda seçili
/// olanın diff'i, altta <i>Apply</i> / <i>Pop</i> / <i>Drop</i> / <i>Branch</i>.
/// </remarks>
public sealed class StashViewModel : ViewModelBase
{
    private readonly string _workingDirectory;
    private readonly IStashWriter _stash;

    private StashRowViewModel? _selected;
    private string _diff = string.Empty;
    private string _newMessage = string.Empty;
    private bool _includeUntracked;
    private bool _keepIndex;
    private string? _error;
    private string? _notice;
    private bool _isBusy;

    public StashViewModel(string workingDirectory, IStashWriter stash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(stash);

        _workingDirectory = workingDirectory;
        _stash = stash;

        PushCommand = new AsyncRelayCommand(PushAsync, () => !IsBusy);
        ApplyCommand = new AsyncRelayCommand(() => ApplyAsync(drop: false), CanUseSelected);
        PopCommand = new AsyncRelayCommand(() => ApplyAsync(drop: true), CanUseSelected);
        DropCommand = new AsyncRelayCommand(DropAsync, CanUseSelected);
        BranchCommand = new AsyncRelayCommand(BranchAsync, CanBranch);
    }

    public ObservableCollection<StashRowViewModel> Rows { get; } = [];

    public StashRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                RaiseCanExecuteChanged();
                _ = LoadDiffAsync();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Seçili stash'in diff'i — uygulamadan önce önizleme.</summary>
    public string Diff
    {
        get => _diff;
        private set => SetProperty(ref _diff, value);
    }

    /// <summary>Yeni stash'in mesajı.</summary>
    public string NewMessage
    {
        get => _newMessage;
        set => SetProperty(ref _newMessage, value);
    }

    public bool IncludeUntracked
    {
        get => _includeUntracked;
        set => SetProperty(ref _includeUntracked, value);
    }

    public bool KeepIndex
    {
        get => _keepIndex;
        set => SetProperty(ref _keepIndex, value);
    }

    /// <summary>Yeni dal adı — <c>stash branch</c> için.</summary>
    public string BranchName { get; set; } = string.Empty;

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Hata olmayan ama söylenmesi gereken şeyler.</summary>
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
                RaiseCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand PushCommand { get; }

    public IAsyncRelayCommand ApplyCommand { get; }

    public IAsyncRelayCommand PopCommand { get; }

    public IAsyncRelayCommand DropCommand { get; }

    public IAsyncRelayCommand BranchCommand { get; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StashEntry> entries =
            await _stash.ListAsync(_workingDirectory, cancellationToken).ConfigureAwait(true);

        Rows.Clear();

        foreach (StashEntry entry in entries)
        {
            Rows.Add(new StashRowViewModel { Entry = entry });
        }

        Selected = Rows.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool CanUseSelected() => Selected is not null && !IsBusy;

    private bool CanBranch() => CanUseSelected() && BranchName.Trim().Length > 0;

    private void RaiseCanExecuteChanged()
    {
        PushCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        PopCommand.NotifyCanExecuteChanged();
        DropCommand.NotifyCanExecuteChanged();
        BranchCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadDiffAsync()
    {
        if (Selected is not { } selected)
        {
            Diff = string.Empty;
            return;
        }

        Diff = await _stash
            .ShowAsync(_workingDirectory, selected.Entry.Selector)
            .ConfigureAwait(true);
    }

    private Task PushAsync() => RunAsync(async () =>
    {
        bool stashed = await _stash.PushAsync(
            _workingDirectory,
            new StashPushOptions
            {
                Message = NewMessage.Trim() is { Length: > 0 } message ? message : null,
                IncludeUntracked = IncludeUntracked,
                KeepIndex = KeepIndex,
            }).ConfigureAwait(true);

        // "Kenara konacak bir şey yoktu" sessizce geçilmiyor: kullanıcı olmayan bir
        // girdiyi arardı.
        Notice = stashed ? null : "Nothing to stash — the working tree is clean.";

        if (stashed)
        {
            NewMessage = string.Empty;
        }
    });

    private Task ApplyAsync(bool drop) => RunAsync(async () =>
    {
        StashApplyResult result = await _stash
            .ApplyAsync(_workingDirectory, Selected!.Entry.Selector, drop)
            .ConfigureAwait(true);

        List<string> notices = [];

        if (result.HasConflicts)
        {
            notices.Add($"{result.ConflictedPaths.Count} file(s) conflicted.");

            // 🔴 ÖLÇÜLDÜ: pop çakışırsa girdi DÜŞMÜYOR. Söylenmezse kullanıcı ya iki kez
            // uygular ya da elle silerken kaybeder.
            if (drop && result.EntryKept)
            {
                notices.Add("The stash entry was kept because the merge conflicted.");
            }
        }

        // 🔴 ÖLÇÜLDÜ: `--index` uygulanamadığında stage'lenmiş/stage'lenmemiş ayrımı
        // sessizce kayboluyor. Sessiz kalmak, kullanıcının hazırladığı index'i fark
        // etmeden kaybetmesi demekti.
        if (!result.IndexRestored)
        {
            notices.Add("The staged/unstaged split could not be restored; everything is unstaged.");
        }

        Notice = notices.Count > 0 ? string.Join(" ", notices) : null;
    });

    private Task DropAsync() => RunAsync(async () =>
    {
        await _stash
            .DropAsync(_workingDirectory, Selected!.Entry.Selector)
            .ConfigureAwait(true);
    });

    private Task BranchAsync() => RunAsync(async () =>
    {
        await _stash
            .BranchAsync(_workingDirectory, Selected!.Entry.Selector, BranchName.Trim())
            .ConfigureAwait(true);

        BranchName = string.Empty;
    });

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        Error = null;
        Notice = null;

        try
        {
            await action().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Error = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Stash ekranını gösteren taraf (P07-T13).</summary>
public interface IStashPrompt
{
    Task ShowAsync(StashViewModel model);
}
