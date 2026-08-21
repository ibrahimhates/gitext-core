using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.UI.Commands;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The model of the shortcut editing screen (P08-T03).
/// </summary>
public sealed partial class ShortcutSettingsViewModel : ViewModelBase
{
    private readonly ICommandRegistry _registry;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private ShortcutRow? _selected;

    /// <summary>Are we currently waiting for a key?</summary>
    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>Why the last assignment attempt was rejected; empty on success.</summary>
    [ObservableProperty]
    private string _captureError = string.Empty;

    public ShortcutSettingsViewModel(ICommandRegistry registry)
    {
        _registry = registry;

        BeginCaptureCommand = new RelayCommand(BeginCapture, () => Selected is not null);
        CancelCaptureCommand = new RelayCommand(CancelCapture, () => IsCapturing);
        ClearCommand = new RelayCommand(Clear, () => Selected is not null);
        ResetCommand = new RelayCommand(ResetSelected, () => Selected is { IsCustomized: true });
        ResetAllCommand = new RelayCommand(ResetAll, () => Rows.Any(r => r.IsCustomized));

        Refresh();
    }

    public ObservableCollection<ShortcutRow> Rows { get; } = [];

    /// <summary>A human-readable summary of the conflicting assignments; empty when there is no conflict.</summary>
    public IReadOnlyList<string> ConflictMessages { get; private set; } = [];

    public bool HasConflicts => ConflictMessages.Count > 0;

    public IRelayCommand BeginCaptureCommand { get; }

    public IRelayCommand CancelCaptureCommand { get; }

    public IRelayCommand ClearCommand { get; }

    public IRelayCommand ResetCommand { get; }

    public IRelayCommand ResetAllCommand { get; }

    /// <summary>
    /// Assigns the captured key to the selected command.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when it was assigned. On rejection, <see cref="CaptureError"/> is filled
    /// in and <b>the capture continues</b>: the user must be able to try another key.
    /// </returns>
    /// <remarks>
    /// <b>A conflict DOES NOT BLOCK the assignment, it warns.</b> The user may want to conflict
    /// deliberately (they are about to change the old assignment, say). Blocking it would make it
    /// impossible to change two assignments one after the other. But it is not passed over silently
    /// either — measured in P08-T00/M10: Avalonia never runs the second, conflicting registration and
    /// the user could not see why.
    /// </remarks>
    public bool TryApplyCapture(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        if (Selected is not { } row)
        {
            return false;
        }

        GestureRejection rejection = GestureValidation.Validate(gesture, row.Context);

        if (rejection is GestureRejection.ModifierOnly)
        {
            // A modifier key on its own is not an error but an unfinished gesture: the user may have
            // pressed Ctrl and be holding it while reaching for the letter. It is silently ignored.
            return false;
        }

        if (rejection is not GestureRejection.None)
        {
            CaptureError = GestureValidation.Describe(rejection);

            return false;
        }

        _registry.SetGesture(row.CommandId, gesture);

        IsCapturing = false;
        CaptureError = string.Empty;

        Refresh();

        return true;
    }

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnSelectedChanged(ShortcutRow? value)
    {
        CancelCapture();

        BeginCaptureCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCapturingChanged(bool value)
    {
        CancelCaptureCommand.NotifyCanExecuteChanged();
    }

    private void BeginCapture()
    {
        CaptureError = string.Empty;
        IsCapturing = true;
    }

    private void CancelCapture()
    {
        IsCapturing = false;
        CaptureError = string.Empty;
    }

    /// <summary><b>Removes</b> the shortcut (it does not return to the default).</summary>
    private void Clear()
    {
        if (Selected is { } row)
        {
            _registry.SetGesture(row.CommandId, null);

            Refresh();
        }
    }

    private void ResetSelected()
    {
        if (Selected is { } row)
        {
            _registry.Reset(row.CommandId);

            Refresh();
        }
    }

    private void ResetAll()
    {
        _registry.ResetAll();

        Refresh();
    }

    private void Refresh()
    {
        string? selectedId = Selected?.CommandId;

        Rows.Clear();

        foreach (CommandDefinition definition in _registry.Definitions)
        {
            if (!Matches(definition))
            {
                continue;
            }

            Rows.Add(new ShortcutRow(
                definition.Id,
                definition.Title,
                definition.Category,
                definition.Context,
                _registry.GetGesture(definition.Id),
                _registry.IsCustomized(definition.Id)));
        }

        ConflictMessages =
        [
            .. _registry.Conflicts.Select(conflict =>
                $"{conflict.Gesture} is assigned to more than one command: "
                + string.Join(", ", conflict.CommandIds.Select(TitleOf))
                + Loc.T("shortcut_settings.only_the_first_one_works"))
        ];

        OnPropertyChanged(nameof(ConflictMessages));
        OnPropertyChanged(nameof(HasConflicts));

        Selected = Rows.FirstOrDefault(r => r.CommandId == selectedId);

        ResetAllCommand.NotifyCanExecuteChanged();
    }

    private string TitleOf(string commandId) =>
        _registry.Find(commandId)?.Title ?? commandId;

    private bool Matches(CommandDefinition definition)
    {
        if (Filter.Length == 0)
        {
            return true;
        }

        // The shortcut itself can be searched for too: "what is this key assigned to?" is the most
        // frequently asked question.
        return definition.Title.Contains(Filter, StringComparison.CurrentCultureIgnoreCase)
            || definition.Id.Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || _registry.GetGesture(definition.Id)?.ToString()
                .Contains(Filter, StringComparison.OrdinalIgnoreCase) == true;
    }
}

/// <summary>A row in the shortcut list.</summary>
public sealed record ShortcutRow(
    string CommandId,
    string Title,
    CommandCategory Category,
    CommandContext Context,
    KeyGesture? Gesture,
    bool IsCustomized)
{
    public string GestureText => Gesture?.ToString() ?? "—";

    /// <summary>The context's name as shown to the user.</summary>
    public string ContextText => Context switch
    {
        CommandContext.Global => Loc.T("shortcut.context_everywhere"),
        CommandContext.CommitList => Loc.T("shortcut.context_commit_list"),
        CommandContext.WorkingTree => Loc.T("shortcut_settings.working_tree"),
        CommandContext.Diff => Loc.T("shortcut.context_diff"),
        CommandContext.RefTree => Loc.T("shortcut.context_branch_panel"),
        _ => Context.ToString(),
    };
}
