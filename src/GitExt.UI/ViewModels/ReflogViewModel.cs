using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>A row in the reflog list (P07-T14).</summary>
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
    /// Is this commit no longer reachable from any branch?
    /// </summary>
    /// <remarks>
    /// This is what the reflog browser is really for: the user will find the commit they lost to
    /// <c>reset --hard</c> here. The marked rows are brought to the front.
    /// </remarks>
    public bool IsUnreachable => Entry.IsUnreachable;

    public string RecoveryCommand => Entry.RecoveryCommand;
}

/// <summary>
/// The reflog browser (P07-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>The phase's insurance policy.</b> Every operation in Phase 07 rewrites history; when the user
/// loses something, this is where they will get it back.
/// </para>
/// <para>
/// ⚠️ The "return here" command uses <b>the SHA, not the selector</b>: <c>HEAD@{3}</c> is a sliding
/// reference and points at a different commit as soon as another operation adds an entry to the reflog.
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

    /// <summary>The command to run to return to the selected entry — shown on screen.</summary>
    public string SelectedRecoveryCommand => Selected?.RecoveryCommand ?? string.Empty;

    /// <summary>Show only the unreachable ("lost") commits.</summary>
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

    /// <summary>The translated text of the unreachable commit count (P11-T08).</summary>
    public string UnreachableCountText => Loc.F("reflog.unreachable_count", UnreachableCount);

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
        OnPropertyChanged(nameof(UnreachableCountText));
    }

    private bool CanReturn() => Selected is not null && _reset is not null && !IsBusy;

    /// <remarks>
    /// It returns with <c>--hard</c>: what the user wants is "go back to how it was then". But that
    /// deletes uncommitted work, so a safety point is taken and the way back is shown — so the loss
    /// itself can be undone too.
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

/// <summary>The side that shows the reflog browser (P07-T14).</summary>
public interface IReflogPrompt
{
    Task ShowAsync(ReflogViewModel model);
}
