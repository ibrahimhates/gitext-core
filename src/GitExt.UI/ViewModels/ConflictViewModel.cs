using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The row for a single conflicting file (P07-T03).
/// </summary>
public sealed class ConflictFileViewModel : ViewModelBase
{
    private bool _isResolved;

    public required ConflictedFile File { get; init; }

    public string Path => File.Path.Value;

    public string Name => File.Path.Name;

    /// <summary>The human-readable rendering of the conflict kind.</summary>
    /// <remarks>
    /// The texts carry the same meaning as the column values in GitExtensions'
    /// <c>FormResolveConflicts</c> (§ 9); there too the user is told <i>which side did what</i>.
    /// </remarks>
    public string Description => File.Kind switch
    {
        ConflictKind.BothModified => "Both modified",
        ConflictKind.BothAdded => "Both added",
        ConflictKind.BothDeleted => "Both deleted",
        ConflictKind.AddedByUs => "Added by us",
        ConflictKind.AddedByThem => "Added by them",
        ConflictKind.DeletedByUs => "Deleted by us",
        ConflictKind.DeletedByThem => "Deleted by them",
        _ => "Conflict",
    };

    /// <summary>
    /// Does the three-way text view make sense for this file?
    /// </summary>
    /// <remarks>
    /// In a presence conflict (one side deleted it) there are not two texts to merge; there is a
    /// <b>decision</b> to make. Showing an empty "theirs" panel would read as the file being empty —
    /// the UI counterpart of why <see langword="null"/> and an empty string are kept apart in
    /// P07-T02.
    /// </remarks>
    public bool SupportsThreeWay => File.IsContentConflict;

    public bool IsResolved
    {
        get => _isResolved;
        set => SetProperty(ref _isResolved, value);
    }
}

/// <summary>
/// The conflict resolution screen (P07-T03, P07-T05).
/// </summary>
/// <remarks>
/// <para>
/// The layout follows GitExtensions' <c>FormResolveConflicts</c> (§ 9): the list of conflicting files
/// on the left, the selected file's <b>Base | Ours | Theirs</b> panels on the right, and the action
/// buttons at the bottom.
/// </para>
/// <para>
/// 🔴 The continue button is enabled only when <b>everything is resolved</b>: in the measurement,
/// running <c>--continue</c> before resolving gave rc=128.
/// </para>
/// </remarks>
public sealed class ConflictViewModel : ViewModelBase
{
    private readonly string _workingDirectory;
    private readonly IConflictReader _reader;
    private readonly IConflictResolver _resolver;
    private readonly IMergeToolRunner? _tools;

    private ConflictFileViewModel? _selected;
    private ConflictProgress? _progress;
    private string _baseText = string.Empty;
    private string _oursText = string.Empty;
    private string _theirsText = string.Empty;
    private string _mergedText = string.Empty;
    private string? _error;
    private bool _isBusy;

    public ConflictViewModel(
        string workingDirectory,
        IConflictReader reader,
        IConflictResolver resolver,
        IMergeToolRunner? mergeTools = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(resolver);

        _workingDirectory = workingDirectory;
        _reader = reader;
        _resolver = resolver;
        _tools = mergeTools;

        TakeOursCommand = new AsyncRelayCommand(() => TakeSideAsync(ResolutionSide.Ours), CanResolve);
        TakeTheirsCommand = new AsyncRelayCommand(() => TakeSideAsync(ResolutionSide.Theirs), CanResolve);
        TakeBothCommand = new AsyncRelayCommand(TakeBothAsync, CanTakeBoth);
        SaveMergedCommand = new AsyncRelayCommand(SaveMergedAsync, CanResolve);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, CanResolve);
        OpenMergeToolCommand = new AsyncRelayCommand(OpenMergeToolAsync, () => CanResolve() && _tools is not null);
        ContinueCommand = new AsyncRelayCommand(ContinueAsync, () => Progress?.CanContinue == true);
        AbortCommand = new AsyncRelayCommand(AbortAsync, () => Progress?.Operation != InProgressOperation.None);
    }

    /// <summary>The conflicting files.</summary>
    public ObservableCollection<ConflictFileViewModel> Files { get; } = [];

    public ConflictFileViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ShowThreeWay));
                RaiseCanExecuteChanged();
                _ = LoadStagesAsync();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>Should the three-way panels be shown?</summary>
    public bool ShowThreeWay => Selected?.SupportsThreeWay == true;

    /// <summary>The common ancestor version.</summary>
    public string BaseText
    {
        get => _baseText;
        private set => SetProperty(ref _baseText, value);
    }

    public string OursText
    {
        get => _oursText;
        private set => SetProperty(ref _oursText, value);
    }

    public string TheirsText
    {
        get => _theirsText;
        private set => SetProperty(ref _theirsText, value);
    }

    /// <summary>The result as edited by the user.</summary>
    public string MergedText
    {
        get => _mergedText;
        set => SetProperty(ref _mergedText, value);
    }

    public ConflictProgress? Progress
    {
        get => _progress;
        private set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(RemainingText));
                OnPropertyChanged(nameof(ContinueCommandText));
                RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>A summary along the lines of "3 files still conflicting".</summary>
    public string RemainingText => Progress is null
        ? string.Empty
        : Progress.IsResolved
            ? "All conflicts resolved."
            : $"{Progress.RemainingCount} file(s) still conflicted.";

    /// <summary>The text of the continue command — it differs by operation.</summary>
    public string ContinueCommandText => Progress?.ContinueCommand ?? string.Empty;

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
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

    public IAsyncRelayCommand TakeOursCommand { get; }

    public IAsyncRelayCommand TakeTheirsCommand { get; }

    public IAsyncRelayCommand TakeBothCommand { get; }

    public IAsyncRelayCommand SaveMergedCommand { get; }

    public IAsyncRelayCommand RemoveCommand { get; }

    public IAsyncRelayCommand OpenMergeToolCommand { get; }

    public IAsyncRelayCommand ContinueCommand { get; }

    public IAsyncRelayCommand AbortCommand { get; }

    /// <summary>Populates the screen.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        string? previous = Selected?.Path;

        IReadOnlyList<ConflictedFile> files =
            await _reader.ReadAsync(_workingDirectory, cancellationToken).ConfigureAwait(true);

        Files.Clear();

        foreach (ConflictedFile file in files)
        {
            Files.Add(new ConflictFileViewModel { File = file });
        }

        Progress = await _resolver
            .GetProgressAsync(_workingDirectory, cancellationToken)
            .ConfigureAwait(true);

        // The selection is preserved: jumping back to the top of the list after every resolution would
        // lose the place of a user working through them in order.
        Selected = Files.FirstOrDefault(file =>
                       string.Equals(file.Path, previous, StringComparison.Ordinal))
                   ?? Files.FirstOrDefault();

        OnPropertyChanged(nameof(IsEmpty));
    }

    public bool IsEmpty => Files.Count == 0;

    private bool CanResolve() => Selected is not null && !IsBusy;

    private bool CanTakeBoth() => CanResolve() && Selected?.SupportsThreeWay == true;

    private void RaiseCanExecuteChanged()
    {
        TakeOursCommand.NotifyCanExecuteChanged();
        TakeTheirsCommand.NotifyCanExecuteChanged();
        TakeBothCommand.NotifyCanExecuteChanged();
        SaveMergedCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        OpenMergeToolCommand.NotifyCanExecuteChanged();
        ContinueCommand.NotifyCanExecuteChanged();
        AbortCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Reads the three versions of the selected file.</summary>
    private async Task LoadStagesAsync()
    {
        if (Selected is not { } selected)
        {
            BaseText = OursText = TheirsText = MergedText = string.Empty;
            return;
        }

        BaseText = await ReadStageAsync(selected, ConflictStage.Base).ConfigureAwait(true);
        OursText = await ReadStageAsync(selected, ConflictStage.Ours).ConfigureAwait(true);
        TheirsText = await ReadStageAsync(selected, ConflictStage.Theirs).ConfigureAwait(true);

        // The result panel starts from the state in the working tree — the file git put the conflict
        // markers into. The user's hand editing carries on from there.
        MergedText = ReadWorkingTree(selected);
    }

    private async Task<string> ReadStageAsync(ConflictFileViewModel file, ConflictStage stage)
    {
        // 🔴 With the stage absent we do not even attempt the read: `git show :2:<path>` gives a fatal.
        if (!file.File.HasStage(stage))
        {
            return string.Empty;
        }

        byte[]? content = await _reader
            .ReadStageAsync(_workingDirectory, file.File.Path, stage)
            .ConfigureAwait(true);

        return content is null ? string.Empty : Encoding.UTF8.GetString(content);
    }

    private string ReadWorkingTree(ConflictFileViewModel file)
    {
        try
        {
            string path = file.File.Path.ToAbsolutePath(_workingDirectory);
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private Task TakeSideAsync(ResolutionSide side) => RunAsync(async () =>
    {
        await _resolver
            .TakeSideAsync(_workingDirectory, Selected!.File.Path, side)
            .ConfigureAwait(true);
    });

    /// <remarks>
    /// "Take both" is not merging but <b>leaving the merge to the user</b>: the two sides are written
    /// one after the other and put into the result panel. We do not save it directly — appending two
    /// versions on top of each other without the user looking is almost never the right result.
    /// </remarks>
    private Task TakeBothAsync()
    {
        MergedText = OursText.TrimEnd('\n') + "\n" + TheirsText;
        return Task.CompletedTask;
    }

    private Task SaveMergedAsync() => RunAsync(async () =>
    {
        await _resolver.WriteResolvedAsync(
            _workingDirectory,
            Selected!.File.Path,
            Encoding.UTF8.GetBytes(MergedText)).ConfigureAwait(true);
    });

    private Task RemoveAsync() => RunAsync(async () =>
    {
        await _resolver
            .RemoveAsync(_workingDirectory, Selected!.File.Path)
            .ConfigureAwait(true);
    });

    private Task OpenMergeToolAsync() => RunAsync(async () =>
    {
        await _tools!
            .RunAsync(_workingDirectory, Selected!.File.Path)
            .ConfigureAwait(true);
    });

    private Task ContinueAsync() => RunAsync(async () =>
    {
        await _resolver.ContinueAsync(_workingDirectory).ConfigureAwait(true);
    });

    private Task AbortAsync() => RunAsync(async () =>
    {
        await _resolver.AbortAsync(_workingDirectory).ConfigureAwait(true);
    });

    /// <summary>The shared wrapper: error handling plus refresh.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        Error = null;

        try
        {
            await action().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (GitExt.Core.Git.GitException error)
        {
            Error = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>The side that shows the conflict resolution screen (P07-T03).</summary>
public interface IConflictPrompt
{
    Task ShowAsync(ConflictViewModel model);
}
