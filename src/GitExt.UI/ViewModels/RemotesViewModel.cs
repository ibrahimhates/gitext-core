using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A single remote row as shown in the list (P06-T05).
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
    /// The URL shown in the list — <b>with the password masked</b>.
    /// </summary>
    /// <remarks>
    /// ⚠️ The masking is only here. Had the masked value been put into the edit box, the user would
    /// save <c>***</c> and break their own password.
    /// </remarks>
    public string DisplayUrl => Remote.Url is { } url
        ? GitRemote.MaskCredentials(url)
        : Loc.T("remotes.no_url_configured");

    public override string ToString() => Name;
}

/// <summary>
/// The remote management screen (P06-T05).
/// </summary>
/// <remarks>
/// <para>
/// The layout comes from GitExtensions <c>FormRemotes</c> (§ 9): the list on the left, the
/// <i>Url → Name → Separate Push Url → Push Url</i> order on the right, <i>Save changes</i> at the
/// bottom, and <i>New</i>/<i>Delete</i> to the right of the list.
/// </para>
/// <para>
/// 🔴 <b>The values being edited are RAW config values.</b> <c>git remote get-url</c> gives them with
/// <c>insteadOf</c> shortcuts already resolved; putting that value into the box and saving it would
/// silently destroy the user's shortcut (measured).
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

    /// <summary>The configured remotes.</summary>
    public ObservableCollection<RemoteRowViewModel> Remotes { get; } = [];

    /// <summary>The selected row; when <see langword="null"/> we are in "new" mode.</summary>
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

    /// <summary>The name being edited.</summary>
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

    /// <summary>The fetch URL being edited — <b>raw</b>.</summary>
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

    /// <summary>The push URL being edited — <b>raw</b>.</summary>
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

    /// <summary>Should a separate URL be used for pushing? (<c>checkBoxSepPushUrl</c>)</summary>
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

    /// <summary>The result of the last operation.</summary>
    public string? Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    /// <summary>
    /// A warning git gave <b>alongside exit code 0</b>.
    /// </summary>
    /// <remarks>
    /// A separate field, because this is not an error: the operation succeeded but is <b>half
    /// done</b>. Measured: a non-default fetch refspec is not updated on a rename, and only stderr
    /// says so.
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
    /// The compound condition lives here rather than in XAML: in Phase 03 a compound binding was
    /// measured behaving silently wrong.
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

    /// <summary>Is an existing remote being edited (or a new one added)?</summary>
    public bool IsExisting => Selected is not null;

    /// <summary>Does the selected remote have more than one URL?</summary>
    public bool HasMultipleUrls =>
        Selected?.Remote is { } remote && (remote.FetchUrls.Count > 1 || remote.PushUrls.Count > 1);

    /// <summary>
    /// The explanation shown to the user in the multiple-URL case.
    /// </summary>
    /// <remarks>
    /// MEASURED: in this case <c>git remote set-url</c> says <i>"has multiple values"</i> and stops
    /// with exit code 128. A single-line box cannot represent this remote.
    /// </remarks>
    public string? MultipleUrlNotice => HasMultipleUrls
        ? Loc.T("remotes.this_remote_has_multiple_urls_configured_it_")
          + Loc.T("remotes.configured_urls") + string.Join(", ", AllUrls())
        : null;

    /// <summary>Name validation — while the user types.</summary>
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

    /// <summary>Populates the screen for a repository.</summary>
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

        // The order matters: the name change first, then the URLs. In the reverse order the URLs are
        // written to the OLD name, and there would be two writes before the rename moved them.
        if (!string.Equals(original.Name, Name, StringComparison.Ordinal))
        {
            RemoteRenameResult rename = await _writer
                .RenameAsync(_workingDirectory, original.Name, Name)
                .ConfigureAwait(true);

            done.Add($"'{original.Name}' → '{Name}'");

            if (rename.Warnings.Count > 0)
            {
                // Exit code 0 but the job was left half done; it cannot be passed over silently.
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
            // 🔴 The plan is read BEFORE the deletion and shown to the user: after the deletion none of
            // this information can be read (measured).
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
