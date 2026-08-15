using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.UI.Commands;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Kısayol düzenleme ekranının modeli (P08-T03).
/// </summary>
public sealed partial class ShortcutSettingsViewModel : ViewModelBase
{
    private readonly ICommandRegistry _registry;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private ShortcutRow? _selected;

    /// <summary>Şu an tuş bekliyor muyuz?</summary>
    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>Son atama denemesinin reddedilme sebebi; başarılıysa boş.</summary>
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

    /// <summary>Çakışan atamaların insan okunur özeti; çakışma yoksa boş.</summary>
    public IReadOnlyList<string> ConflictMessages { get; private set; } = [];

    public bool HasConflicts => ConflictMessages.Count > 0;

    public IRelayCommand BeginCaptureCommand { get; }

    public IRelayCommand CancelCaptureCommand { get; }

    public IRelayCommand ClearCommand { get; }

    public IRelayCommand ResetCommand { get; }

    public IRelayCommand ResetAllCommand { get; }

    /// <summary>
    /// Yakalanan tuşu seçili komuta atar.
    /// </summary>
    /// <returns>
    /// Atandıysa <see langword="true"/>. Reddedildiyse <see cref="CaptureError"/> doldurulur
    /// ve <b>yakalama sürer</b>: kullanıcı başka bir tuş deneyebilmeli.
    /// </returns>
    /// <remarks>
    /// <b>Çakışma atamayı ENGELLEMEZ, uyarır.</b> Kullanıcı bilerek çakıştırmak isteyebilir
    /// (ör. eski atamayı birazdan değiştirecek). Engellemek, iki atamayı sırayla değiştirmeyi
    /// imkânsız kılardı. Ama sessiz de kalınmıyor — P08-T00/M10'da ölçüldü: Avalonia çakışan
    /// ikinci kaydı hiç çalıştırmıyor ve kullanıcı sebebini göremezdi.
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
            // Değiştirici tuşun kendisi hata değil, henüz tamamlanmamış bir jest:
            // kullanıcı Ctrl'ye basıp harfi bekletiyor olabilir. Sessizce yok sayılıyor.
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

    /// <summary>Kısayolu <b>kaldırır</b> (varsayılana dönmez).</summary>
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
                + " — only the first one works.")
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

        // Kısayolun kendisiyle de aranabiliyor: "bu tuş neye atanmış?" en sık sorulan soru.
        return definition.Title.Contains(Filter, StringComparison.CurrentCultureIgnoreCase)
            || definition.Id.Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || _registry.GetGesture(definition.Id)?.ToString()
                .Contains(Filter, StringComparison.OrdinalIgnoreCase) == true;
    }
}

/// <summary>Kısayol listesinin bir satırı.</summary>
public sealed record ShortcutRow(
    string CommandId,
    string Title,
    CommandCategory Category,
    CommandContext Context,
    KeyGesture? Gesture,
    bool IsCustomized)
{
    public string GestureText => Gesture?.ToString() ?? "—";

    /// <summary>Bağlamın kullanıcıya gösterilen adı.</summary>
    public string ContextText => Context switch
    {
        CommandContext.Global => "Her yerde",
        CommandContext.CommitList => "Commit listesi",
        CommandContext.WorkingTree => "Working tree",
        CommandContext.Diff => "Diff",
        CommandContext.RefTree => "Dal paneli",
        _ => Context.ToString(),
    };
}
