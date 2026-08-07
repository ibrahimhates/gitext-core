using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Çakışan tek bir dosyanın satırı (P07-T03).
/// </summary>
public sealed class ConflictFileViewModel : ViewModelBase
{
    private bool _isResolved;

    public required ConflictedFile File { get; init; }

    public string Path => File.Path.Value;

    public string Name => File.Path.Name;

    /// <summary>Çakışma türünün insan okunur karşılığı.</summary>
    /// <remarks>
    /// Metinler GitExtensions <c>FormResolveConflicts</c>'in sütun değerleriyle aynı
    /// anlamda (§ 9); orada da kullanıcıya <i>hangi tarafın ne yaptığı</i> söyleniyor.
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
    /// Üç yollu metin görünümü bu dosya için anlamlı mı?
    /// </summary>
    /// <remarks>
    /// Varlık çakışmasında (bir taraf silmiş) birleştirilecek iki metin yok; verilecek bir
    /// <b>karar</b> var. Boş bir "theirs" paneli göstermek, dosyanın boş olduğu gibi
    /// okunurdu — P07-T02'de <see langword="null"/> ile boş dizinin ayrılma gerekçesinin
    /// arayüzdeki karşılığı.
    /// </remarks>
    public bool SupportsThreeWay => File.IsContentConflict;

    public bool IsResolved
    {
        get => _isResolved;
        set => SetProperty(ref _isResolved, value);
    }
}

/// <summary>
/// Çakışma çözüm ekranı (P07-T03, P07-T05).
/// </summary>
/// <remarks>
/// <para>
/// Yerleşim GitExtensions <c>FormResolveConflicts</c>'i takip ediyor (§ 9): solda çakışan
/// dosyaların listesi, sağda seçili dosyanın <b>Base | Ours | Theirs</b> panelleri, altta
/// eylem düğmeleri.
/// </para>
/// <para>
/// 🔴 Devam düğmesi yalnızca <b>hepsi çözüldüğünde</b> etkin: ölçümde çözülmeden
/// <c>--continue</c> çalıştırmak rc=128 veriyordu.
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

    /// <summary>Çakışan dosyalar.</summary>
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

    /// <summary>Üç yollu paneller gösterilsin mi?</summary>
    public bool ShowThreeWay => Selected?.SupportsThreeWay == true;

    /// <summary>Ortak ata sürümü.</summary>
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

    /// <summary>Kullanıcının düzenlediği sonuç.</summary>
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

    /// <summary>"3 dosyada çakışma kaldı" gibi bir özet.</summary>
    public string RemainingText => Progress is null
        ? string.Empty
        : Progress.IsResolved
            ? "All conflicts resolved."
            : $"{Progress.RemainingCount} file(s) still conflicted.";

    /// <summary>Devam komutunun metni — işleme göre değişiyor.</summary>
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

    /// <summary>Ekranı doldurur.</summary>
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

        // Seçim korunuyor: her çözümden sonra listenin başına atlamak, sırayla ilerleyen
        // kullanıcının yerini kaybettirirdi.
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

    /// <summary>Seçili dosyanın üç sürümünü okur.</summary>
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

        // Sonuç paneli çalışma ağacındaki hâlle başlıyor — git'in çakışma işaretlerini
        // koyduğu dosya. Kullanıcının elle düzenlemesi buradan devam ediyor.
        MergedText = ReadWorkingTree(selected);
    }

    private async Task<string> ReadStageAsync(ConflictFileViewModel file, ConflictStage stage)
    {
        // 🔴 Aşama yoksa okumaya kalkmıyoruz: `git show :2:<yol>` fatal veriyor.
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
    /// "İkisini de al" birleştirme değil <b>birleştirmeyi kullanıcıya bırakma</b>: iki
    /// taraf art arda yazılıyor ve sonuç paneline konuyor. Doğrudan kaydetmiyoruz —
    /// kullanıcı bakmadan iki sürümün üst üste eklenmesi neredeyse hiçbir zaman doğru
    /// sonuç değil.
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

    /// <summary>Ortak sarmalayıcı: hata yakalama + yenileme.</summary>
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

/// <summary>Çakışma çözüm ekranını gösteren taraf (P07-T03).</summary>
public interface IConflictPrompt
{
    Task ShowAsync(ConflictViewModel model);
}
