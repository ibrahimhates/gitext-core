using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Listede görünen tek bir uzak depo satırı (P06-T05).
/// </summary>
public sealed class RemoteRowViewModel
{
    public RemoteRowViewModel(GitRemote remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        Remote = remote;
    }

    public GitRemote Remote { get; }

    public string Name => Remote.Name;

    /// <summary>
    /// Listede gösterilen URL — <b>parola maskeli</b>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Maskeleme yalnızca burada. Düzenleme kutusuna maskelenmiş değer konulsaydı
    /// kullanıcı <c>***</c>'ı kaydeder ve parolasını bozardı.
    /// </remarks>
    public string DisplayUrl => Remote.Url is { } url
        ? GitRemote.MaskCredentials(url)
        : Loc.T("remotes.no_url_configured");

    public override string ToString() => Name;
}

/// <summary>
/// Uzak depo yönetimi ekranı (P06-T05).
/// </summary>
/// <remarks>
/// <para>
/// Yerleşim GitExtensions <c>FormRemotes</c>'tan (§ 9): solda liste, sağda
/// <i>Url → Name → Separate Push Url → Push Url</i> sırası, altta <i>Save changes</i>,
/// listenin sağında <i>New</i>/<i>Delete</i>.
/// </para>
/// <para>
/// 🔴 <b>Düzenlenen değerler HAM config değerleri.</b> <c>git remote get-url</c>
/// <c>insteadOf</c> kısayollarını çözerek veriyor; o değeri kutuya koyup kaydetmek
/// kullanıcının kısayolunu sessizce yok ederdi (ölçüldü).
/// </para>
/// </remarks>
public sealed partial class RemotesViewModel : ViewModelBase
{
    private readonly IRemoteReader _reader;
    private readonly IRemoteWriter _writer;
    private readonly IRemoteRemovalConfirmer? _removalConfirmer;

    private string _workingDirectory = string.Empty;
    private RemoteRowViewModel? _selected;
    private string _name = string.Empty;
    private string _url = string.Empty;
    private string _pushUrl = string.Empty;
    private bool _separatePushUrl;
    private string? _notice;
    private string? _warning;
    private bool _isBusy;

    public RemotesViewModel(
        IRemoteReader reader,
        IRemoteWriter writer,
        IRemoteRemovalConfirmer? removalConfirmer = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);

        _reader = reader;
        _writer = writer;
        _removalConfirmer = removalConfirmer;

        NewCommand = new RelayCommand(BeginNew);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => Selected is not null && !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
    }

    /// <summary>Yapılandırılmış uzak depolar.</summary>
    public ObservableCollection<RemoteRowViewModel> Remotes { get; } = [];

    /// <summary>Seçili satır; <see langword="null"/> ise "yeni" modundayız.</summary>
    public RemoteRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                LoadEditor(value?.Remote);
                DeleteCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsExisting));
                OnPropertyChanged(nameof(HasMultipleUrls));
                OnPropertyChanged(nameof(MultipleUrlNotice));
            }
        }
    }

    /// <summary>Düzenlenen ad.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(NameProblem));
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Düzenlenen fetch URL'si — <b>ham</b>.</summary>
    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Düzenlenen push URL'si — <b>ham</b>.</summary>
    public string PushUrl
    {
        get => _pushUrl;
        set
        {
            if (SetProperty(ref _pushUrl, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Push için ayrı URL kullanılsın mı? (<c>checkBoxSepPushUrl</c>)</summary>
    public bool SeparatePushUrl
    {
        get => _separatePushUrl;
        set
        {
            if (SetProperty(ref _separatePushUrl, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Son işlemin sonucu.</summary>
    public string? Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    /// <summary>
    /// git'in <b>çıkış kodu 0 ile</b> verdiği uyarı.
    /// </summary>
    /// <remarks>
    /// Ayrı bir alan çünkü bu bir hata değil: işlem başarılı ama <b>yarım</b>. Ölçüldü:
    /// varsayılan olmayan fetch refspec'i yeniden adlandırmada güncellenmiyor ve bunu
    /// yalnızca stderr söylüyor.
    /// </remarks>
    public string? Warning
    {
        get => _warning;
        private set
        {
            if (SetProperty(ref _warning, value))
            {
                OnPropertyChanged(nameof(HasWarning));
            }
        }
    }

    /// <summary>
    /// Bileşik koşul XAML'de değil burada: Faz 03'te bileşik bağlamanın sessizce yanlış
    /// davrandığı ölçülmüştü.
    /// </summary>
    public bool HasWarning => !string.IsNullOrEmpty(Warning);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                DeleteCommand.NotifyCanExecuteChanged();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Var olan bir remote düzenleniyor mu (yoksa yeni mi ekleniyor)?</summary>
    public bool IsExisting => Selected is not null;

    /// <summary>Seçili remote'ta birden çok URL var mı?</summary>
    public bool HasMultipleUrls =>
        Selected?.Remote is { } remote && (remote.FetchUrls.Count > 1 || remote.PushUrls.Count > 1);

    /// <summary>
    /// Çoklu URL durumunda kullanıcıya gösterilen açıklama.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: bu durumda <c>git remote set-url</c> <i>"has multiple values"</i> deyip
    /// çıkış kodu 128 ile duruyor. Tek satırlık kutu bu remote'u temsil edemez.
    /// </remarks>
    public string? MultipleUrlNotice => HasMultipleUrls
        ? Loc.T("remotes.this_remote_has_multiple_urls_configured_it_")
          + Loc.T("remotes.configured_urls") + string.Join(", ", AllUrls())
        : null;

    /// <summary>Ad doğrulaması — kullanıcı yazarken.</summary>
    public string? NameProblem => RemoteName.Validate(Name) is { } problem
        && problem != RemoteNameProblem.Empty
            ? RemoteName.Describe(problem)
            : null;

    public bool CanSave =>
        !IsBusy
        && !HasMultipleUrls
        && RemoteName.IsValid(Name)
        && !string.IsNullOrWhiteSpace(Url)
        && (!SeparatePushUrl || !string.IsNullOrWhiteSpace(PushUrl));

    public ICommand NewCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>Ekranı bir depo için doldurur.</summary>
    public async Task LoadAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _workingDirectory = workingDirectory;

        await ReloadAsync(select: null, cancellationToken).ConfigureAwait(true);
    }

    private async Task ReloadAsync(string? select, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitRemote> remotes =
            await _reader.ReadAllAsync(_workingDirectory, cancellationToken).ConfigureAwait(true);

        Remotes.Clear();

        foreach (GitRemote remote in remotes)
        {
            Remotes.Add(new RemoteRowViewModel(remote));
        }

        Selected = select is null
            ? Remotes.FirstOrDefault()
            : Remotes.FirstOrDefault(row => string.Equals(row.Name, select, StringComparison.Ordinal))
              ?? Remotes.FirstOrDefault();
    }

    private void BeginNew()
    {
        Selected = null;
        Name = string.Empty;
        Url = string.Empty;
        PushUrl = string.Empty;
        SeparatePushUrl = false;
        Notice = null;
        Warning = null;
    }

    private void LoadEditor(GitRemote? remote)
    {
        _name = remote?.Name ?? string.Empty;
        _url = remote?.Url ?? string.Empty;
        _pushUrl = remote is { PushUrls.Count: > 0 } ? remote.PushUrls[0] : string.Empty;
        _separatePushUrl = remote?.HasSeparatePushUrl ?? false;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(PushUrl));
        OnPropertyChanged(nameof(SeparatePushUrl));
        OnPropertyChanged(nameof(NameProblem));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private IEnumerable<string> AllUrls() =>
        Selected is null
            ? []
            : Selected.Remote.FetchUrls
                .Concat(Selected.Remote.PushUrls)
                .Select(GitRemote.MaskCredentials);

    private async Task SaveAsync()
    {
        if (!CanSave)
        {
            return;
        }

        IsBusy = true;
        Notice = null;
        Warning = null;

        try
        {
            if (Selected is null)
            {
                await _writer
                    .AddAsync(_workingDirectory, new RemoteAddOptions { Name = Name, Url = Url })
                    .ConfigureAwait(true);

                if (SeparatePushUrl)
                {
                    await _writer
                        .SetUrlAsync(_workingDirectory, Name, RemoteUrlKind.Push, PushUrl)
                        .ConfigureAwait(true);
                }

                Notice = $"'{Name}' eklendi.";
            }
            else
            {
                await ApplyChangesAsync(Selected.Remote).ConfigureAwait(true);
            }

            await ReloadAsync(Name).ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Notice = Loc.GitError(error);
        }
        catch (ArgumentException error)
        {
            Notice = error.Message;
        }
        catch (InvalidOperationException error)
        {
            Notice = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyChangesAsync(GitRemote original)
    {
        List<string> done = [];

        // Sıra önemli: önce ad değişikliği, sonra URL'ler. Ters sırada URL'ler ESKİ ada
        // yazılır ve yeniden adlandırma onları taşımadan önce iki kez yazma yapılırdı.
        if (!string.Equals(original.Name, Name, StringComparison.Ordinal))
        {
            RemoteRenameResult rename = await _writer
                .RenameAsync(_workingDirectory, original.Name, Name)
                .ConfigureAwait(true);

            done.Add($"'{original.Name}' → '{Name}'");

            if (rename.Warnings.Count > 0)
            {
                // Çıkış kodu 0 ama iş yarım kaldı; sessizce geçilemez.
                Warning = string.Join(" · ", rename.Warnings);
            }
        }

        if (!string.Equals(original.Url ?? string.Empty, Url, StringComparison.Ordinal))
        {
            await _writer
                .SetUrlAsync(_workingDirectory, Name, RemoteUrlKind.Fetch, Url)
                .ConfigureAwait(true);

            done.Add(Loc.T("remotes.url_updated"));
        }

        string originalPush = original.PushUrls.Count > 0 ? original.PushUrls[0] : string.Empty;

        if (SeparatePushUrl && !string.Equals(originalPush, PushUrl, StringComparison.Ordinal))
        {
            await _writer
                .SetUrlAsync(_workingDirectory, Name, RemoteUrlKind.Push, PushUrl)
                .ConfigureAwait(true);

            done.Add(Loc.T("remotes.the_push_url_was_updated"));
        }
        else if (!SeparatePushUrl && originalPush.Length > 0)
        {
            await _writer
                .RemoveUrlAsync(_workingDirectory, Name, RemoteUrlKind.Push, originalPush)
                .ConfigureAwait(true);

            done.Add(Loc.T("remotes.the_separate_push_url_was_removed"));
        }

        Notice = done.Count == 0 ? Loc.T("remotes.no_changes") : string.Join(", ", done) + ".";
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        IsBusy = true;
        Warning = null;

        try
        {
            // 🔴 Plan SİLMEDEN ÖNCE okunuyor ve kullanıcıya gösteriliyor: silme sonrası
            // bu bilgilerin hiçbiri okunamıyor (ölçüldü).
            RemoteRemovalPlan plan = await _writer
                .PrepareRemovalAsync(_workingDirectory, row.Name)
                .ConfigureAwait(true);

            if (_removalConfirmer is not null)
            {
                bool confirmed = await _removalConfirmer
                    .ConfirmAsync(new RemoteRemovalRequest
                    {
                        Name = row.Name,
                        TrackingBranchCount = plan.TrackingBranches.Count,
                        AffectedBranches = [.. plan.AffectedBranches.Select(pair => pair.Branch)],
                        IsPushDefault = plan.IsPushDefault,
                        RecoveryCommands = plan.RecoveryCommands,
                    })
                    .ConfigureAwait(true);

                if (!confirmed)
                {
                    return;
                }
            }

            await _writer.RemoveAsync(_workingDirectory, row.Name).ConfigureAwait(true);

            Notice = $"'{row.Name}' removed."
                + (plan.TrackingBranches.Count > 0
                    ? $" {plan.TrackingBranches.Count} remote-tracking branches were deleted too."
                    : string.Empty);

            await ReloadAsync(select: null).ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Notice = Loc.GitError(error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
