using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A single row in the log (P06-T16).
/// </summary>
public sealed class CommandLogRowViewModel
{
    public required GitCommandLogEntry Entry { get; init; }

    public string Time => Entry.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public string CommandLine => Entry.CommandLine;

    /// <summary>
    /// The duration; with a decimal so sub-millisecond values are visible too.
    /// </summary>
    /// <remarks>
    /// Showing the duration is one of the panel's main jobs: several times in this project a read path
    /// turned out to be slower than expected only through measurement, and this is the one place the
    /// user can see it.
    /// </remarks>
    public string Duration => Entry.Duration.TotalSeconds >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{Entry.Duration.TotalSeconds:0.00} sn")
        : string.Create(CultureInfo.InvariantCulture, $"{Entry.Duration.TotalMilliseconds:0} ms");

    /// <summary>
    /// The exit code; a dash when the process never completed (cancelled, timed out).
    /// </summary>
    /// <remarks>
    /// ⚠️ <see langword="null"/> and <c>0</c> are not the same thing: the first means "did not
    /// finish", the second "finished successfully". Writing <c>0</c> would show a cancelled command as
    /// a success.
    /// </remarks>
    public string ExitCode => Entry.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "—";

    public bool IsSuccess => Entry.IsSuccess;

    /// <summary>stderr ya da tamamlanmama nedeni.</summary>
    public string Details => Entry.Details.TrimEnd();

    public bool HasDetails => Details.Length > 0;
}

/// <summary>
/// The git command log panel (P06-T16).
/// </summary>
/// <remarks>
/// <para>
/// From the plan: <i>"The user must always be able to see what happened."</i> The infrastructure was
/// built in P02-T05 (<see cref="IGitCommandLog"/>); what arrives here is the display and the <b>live
/// stream</b>.
/// </para>
/// <para>
/// ⚠️ <b>The entries do not arrive on the UI thread</b> — git processes run on pool threads. Adding
/// to the collection directly would produce a crash or silent corruption in Avalonia; that is why
/// every entry goes through the <see cref="Dispatcher"/>.
/// </para>
/// <para>
/// 🔒 <b>Secrets never enter the log:</b> credentials are written to the environment, not to the
/// command line (P06-T09), and <c>ToDisplayString</c> never writes the environment.
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
            // Commands that ran before the panel opened should be visible too: an empty list at startup
            // would read as "nothing has run".
            foreach (GitCommandLogEntry entry in memory.Entries)
            {
                All.Add(new CommandLogRowViewModel { Entry = entry });
            }
        }

        _log.Recorded += OnRecorded;

        ClearCommand = new RelayCommand(Clear);

        Refresh();
    }

    /// <summary>All the entries, newest first.</summary>
    private ObservableCollection<CommandLogRowViewModel> All { get; } = [];

    /// <summary>The entries shown on screen.</summary>
    public ObservableCollection<CommandLogRowViewModel> Rows { get; } = [];

    /// <summary>Should only failed commands be shown?</summary>
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

    /// <summary>The detail of the selected entry.</summary>
    public string SelectedDetails => Selected?.Details ?? string.Empty;

    public bool HasSelectedDetails => SelectedDetails.Length > 0;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The number of failed commands — shown next to the filter box.</summary>
    public int FailureCount => All.Count(row => !row.IsSuccess);

    /// <summary>The translated text of the failed command count (P11-T08).</summary>
    public string FailureCountText => Loc.F("command_log.failed_commands", FailureCount);

    public IRelayCommand ClearCommand { get; }

    private void OnRecorded(object? sender, GitCommandLogEntry entry)
    {
        // ⚠️ This call is NOT on the UI thread.
        Dispatcher.UIThread.Post(() => Append(entry));
    }

    private void Append(GitCommandLogEntry entry)
    {
        All.Insert(0, new CommandLogRowViewModel { Entry = entry });

        // Without a limit, memory swells over a long session; the log itself is a ring buffer too.
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

        // After filtering, the selection may no longer be in the list; the detail panel carrying on
        // showing an old entry would mislead the user.
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

/// <summary>The side that shows the command log panel (P06-T16).</summary>
public interface ICommandLogPrompt
{
    Task ShowAsync(CommandLogViewModel model);
}
