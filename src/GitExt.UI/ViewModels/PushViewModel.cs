using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>Tab on the push screen (P06-T08).</summary>
/// <remarks>
/// Order follows GitExtensions' <c>FormPush</c> <c>TabControlTagBranch</c> (§ 9):
/// <i>Push branches · Push tags · Push multiple branches</i>.
/// </remarks>
public enum PushTab
{
    /// <summary>A single branch.</summary>
    Branch,

    /// <summary>Tags.</summary>
    Tag,

    /// <summary>Multiple branches — <b>remote branch deletion</b> is here too.</summary>
    MultipleBranches,
}

/// <summary>
/// A row on the "Multiple branches" tab (P06-T08).
/// </summary>
/// <remarks>
/// Columns follow GitExtensions' <c>BranchGrid</c>: <i>Local Branch · Remote Branch ·
/// Ahead/Behind · Push · Force · Delete Remote Branch</i>.
/// </remarks>
public sealed class PushBranchRowViewModel : ViewModelBase
{
    private bool _push;
    private bool _delete;

    public required string LocalBranch { get; init; }

    public required string RemoteBranch { get; init; }

    /// <summary>Position relative to upstream; empty if there's no upstream.</summary>
    public required string AheadBehind { get; init; }

    /// <summary>Does this branch exist on the remote? If not, it can't be deleted.</summary>
    public required bool ExistsOnRemote { get; init; }

    /// <summary>Lease anchor — tip of the remote tracking ref when the screen was opened.</summary>
    public string? RemoteTipObjectId { get; init; }

    public bool Push
    {
        get => _push;
        set
        {
            if (SetProperty(ref _push, value) && value)
            {
                Delete = false;
            }
        }
    }

    /// <summary>
    /// Delete the remote branch (<c>--delete</c>).
    /// </summary>
    /// <remarks>
    /// Cannot be checked at the same time as pushing: git doesn't accept both for a single
    /// refspec, and the user's intent would also stay ambiguous.
    /// </remarks>
    public bool Delete
    {
        get => _delete;
        set
        {
            if (SetProperty(ref _delete, value) && value)
            {
                Push = false;
            }
        }
    }

    public bool CanDelete => ExistsOnRemote;
}

/// <summary>
/// Push screen (P06-T08).
/// </summary>
/// <remarks>
/// <para>
/// 🔑 <b>No bare <c>--force</c>.</b> Plan decision: it silently deletes someone else's commits.
/// The screen <b>keeps</b> a <i>Force push</i> checkbox like GitExtensions does, but it's
/// disabled with a written reason — leaving its place empty would make the user think "this
/// program just can't force push", whereas <see cref="ForceWithLease"/> is exactly its safe form.
/// </para>
/// <para>
/// 🔴 <b>The lease anchor freezes when the screen opens.</b> <see cref="LeaseNotice"/> tells the
/// user which tip the decision is based on. Rationale:
/// <see cref="PushOptions.ForceWithLease"/> — a bare <c>--force-with-lease</c> drops its
/// protection after an intervening fetch, and in this project a fetch can also happen without
/// the user asking for it (automatic refresh).
/// </para>
/// </remarks>
public sealed class PushViewModel : ViewModelBase
{
    private readonly IRemoteReader _remotes;
    private readonly IPushWriter _push;
    private readonly IAuthenticationDiagnostics? _diagnostics;
    private readonly IAuthenticationPrompt? _authentication;

    private string _workingDirectory = string.Empty;
    private string? _selectedRemote;
    private string _currentBranch = string.Empty;
    private PushTab _tab;
    private string? _sourceBranch;
    private string _remoteBranch = string.Empty;
    private string? _selectedTag;
    private bool _allTags;
    private bool _setUpstream;
    private bool _forceWithLease;
    private bool _showOptions;
    private bool _isBusy;
    private string? _notice;
    private string? _warning;
    private string? _advice;
    private PushPlan? _plan;
    private IReadOnlyList<BranchInfo> _localBranches = [];
    private string? _progressText;
    private double? _progressPercent;
    private CancellationTokenSource? _cancellation;

    public PushViewModel(
        IRemoteReader remotes,
        IPushWriter push,
        IAuthenticationDiagnostics? diagnostics = null,
        IAuthenticationPrompt? authentication = null)
    {
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(push);

        _remotes = remotes;
        _push = push;
        _diagnostics = diagnostics;
        _authentication = authentication;

        RunCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
        CancelCommand = new RelayCommand(Cancel, () => CanCancel);
    }

    /// <summary>Configured remotes.</summary>
    public ObservableCollection<string> Remotes { get; } = [];

    /// <summary>Local branches (source selection).</summary>
    public ObservableCollection<string> SourceBranches { get; } = [];

    /// <summary>Tags that can be pushed.</summary>
    public ObservableCollection<string> Tags { get; } = [];

    /// <summary>Rows of the "Multiple branches" tab.</summary>
    public ObservableCollection<PushBranchRowViewModel> Rows { get; } = [];

    public string? SelectedRemote
    {
        get => _selectedRemote;
        set
        {
            if (SetProperty(ref _selectedRemote, value))
            {
                ReloadPlan();
            }
        }
    }

    public string CurrentBranch
    {
        get => _currentBranch;
        private set => SetProperty(ref _currentBranch, value);
    }

    public PushTab Tab
    {
        get => _tab;
        set
        {
            if (SetProperty(ref _tab, value))
            {
                OnPropertyChanged(nameof(IsBranchTab));
                OnPropertyChanged(nameof(IsTagTab));
                OnPropertyChanged(nameof(IsMultipleTab));
                RaisePreview();
            }
        }
    }

    public bool IsBranchTab
    {
        get => Tab == PushTab.Branch;
        set { if (value) { Tab = PushTab.Branch; } }
    }

    public bool IsTagTab
    {
        get => Tab == PushTab.Tag;
        set { if (value) { Tab = PushTab.Tag; } }
    }

    public bool IsMultipleTab
    {
        get => Tab == PushTab.MultipleBranches;
        set { if (value) { Tab = PushTab.MultipleBranches; } }
    }

    /// <summary>Local branch to push.</summary>
    public string? SourceBranch
    {
        get => _sourceBranch;
        set
        {
            if (SetProperty(ref _sourceBranch, value))
            {
                if (value is { Length: > 0 })
                {
                    // The destination name follows the source; the user changes it if they want.
                    RemoteBranch = value;
                }

                ReloadPlan();
            }
        }
    }

    /// <summary>Destination branch name on the remote (free text in GitExtensions too).</summary>
    public string RemoteBranch
    {
        get => _remoteBranch;
        set
        {
            if (SetProperty(ref _remoteBranch, value))
            {
                OnPropertyChanged(nameof(LeaseNotice));
                OnPropertyChanged(nameof(HasLeaseNotice));
                RaisePreview();
            }
        }
    }

    public string? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (SetProperty(ref _selectedTag, value))
            {
                RaisePreview();
            }
        }
    }

    /// <summary><c>--tags</c>: all of them instead of a single tag.</summary>
    public bool AllTags
    {
        get => _allTags;
        set
        {
            if (SetProperty(ref _allTags, value))
            {
                RaisePreview();
            }
        }
    }

    /// <summary>
    /// <c>--set-upstream</c> (called <i>"Replace tracking reference"</i> in GitExtensions).
    /// </summary>
    public bool SetUpstream
    {
        get => _setUpstream;
        set
        {
            if (SetProperty(ref _setUpstream, value))
            {
                RaisePreview();
            }
        }
    }

    public bool ForceWithLease
    {
        get => _forceWithLease;
        set
        {
            if (SetProperty(ref _forceWithLease, value))
            {
                OnPropertyChanged(nameof(LeaseNotice));
                OnPropertyChanged(nameof(HasLeaseNotice));
                RaisePreview();
            }
        }
    }

    /// <summary>The <i>"Show options"</i> link, like in GitExtensions.</summary>
    public bool ShowOptions
    {
        get => _showOptions;
        set => SetProperty(ref _showOptions, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRun));
                OnPropertyChanged(nameof(CanCancel));
                RunCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

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

    public bool HasWarning => !string.IsNullOrEmpty(Warning);

    /// <summary>Line telling what to do in case of a rejection.</summary>
    /// <remarks>
    /// GitExtensions detects a rejection by <b>applying a regular expression to the
    /// human-readable output</b> (<c>FormPush.cs</c>). For us the reason comes from the
    /// porcelain line; the suggestion also changes based on the reason — we don't settle for
    /// just saying "push failed".
    /// </remarks>
    public string? Advice
    {
        get => _advice;
        private set
        {
            if (SetProperty(ref _advice, value))
            {
                OnPropertyChanged(nameof(HasAdvice));
            }
        }
    }

    public bool HasAdvice => !string.IsNullOrEmpty(Advice);

    /// <summary>Would a new branch be created on the remote?</summary>
    public bool WouldCreateRemoteBranch =>
        _plan is not null && !_plan.RemoteBranches.Contains(RemoteBranch, StringComparer.Ordinal);

    /// <summary>Which tip the lease will base its decision on.</summary>
    public string? LeaseNotice
    {
        get
        {
            if (!ForceWithLease)
            {
                return null;
            }

            string? anchor = LeaseAnchor;

            return anchor is null
                ? Loc.T("push.this_branch_does_not_exist_on_the_remote_the")
                : $"The decision is based on the remote tip as you see it right now: "
                  + $"{anchor[..Math.Min(10, anchor.Length)]}. If someone else "
                  + Loc.T("push.pushes_something_the_push_is_rejected");
        }
    }

    public bool HasLeaseNotice => LeaseNotice is not null;

    /// <summary>
    /// Why isn't there a bare <c>--force</c>?
    /// </summary>
    public static string ForceDisabledReason =>
        Loc.T("push.a_bare_force_is_not_offered_it_deletes_remot")
        + Loc.T("push.force_with_lease_does_the_same_thing_but_sto");

    /// <summary>Command that will run ("show the command" principle).</summary>
    public string CommandPreview =>
        SelectedRemote is not { Length: > 0 } ? string.Empty : PushWriter.Describe(BuildOptions());

    public bool CanRun => !IsBusy
        && SelectedRemote is { Length: > 0 }
        && Tab switch
        {
            PushTab.Branch => SourceBranch is { Length: > 0 } && RemoteBranch.Length > 0,
            PushTab.Tag => AllTags || SelectedTag is { Length: > 0 },
            _ => Rows.Any(row => row.Push || row.Delete),
        };

    public IAsyncRelayCommand RunCommand { get; }

    /// <summary>Live progress text; empty while no operation is running.</summary>
    public string? ProgressText
    {
        get => _progressText;
        private set
        {
            if (SetProperty(ref _progressText, value))
            {
                OnPropertyChanged(nameof(HasProgress));
            }
        }
    }

    public bool HasProgress => !string.IsNullOrEmpty(ProgressText);

    /// <summary>Percentage; <see langword="null"/> if git doesn't report one (indeterminate bar).</summary>
    public double? ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (SetProperty(ref _progressPercent, value))
            {
                OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    public bool IsProgressIndeterminate => ProgressPercent is null;

    /// <summary>
    /// Cancels the running operation (P06-T10).
    /// </summary>
    /// <remarks>
    /// 🔑 The process is <b>actually killed</b> (<c>Kill(entireProcessTree: true)</c>) —
    /// merely giving up waiting would leave a git process still running in the background.
    /// Measured: a fetch interrupted midway leaves no lock behind and <c>fsck</c> stays clean.
    /// </remarks>
    public IRelayCommand CancelCommand { get; }

    public bool CanCancel => IsBusy;

    private void Cancel() => _cancellation?.Cancel();

    private IProgress<GitProgress> CreateProgress() => new Progress<GitProgress>(step =>
    {
        ProgressText = step.Describe();
        ProgressPercent = step.Percent;
    });


    /// <summary>Fills in the screen for a repository.</summary>
    public async Task LoadAsync(
        string workingDirectory,
        string currentBranch,
        IReadOnlyList<BranchInfo> localBranches,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(localBranches);

        _workingDirectory = workingDirectory;
        _localBranches = localBranches;
        CurrentBranch = currentBranch;

        Remotes.Clear();

        foreach (GitRemote remote in await _remotes
                     .ReadAllAsync(workingDirectory, cancellationToken).ConfigureAwait(true))
        {
            Remotes.Add(remote.Name);
        }

        SourceBranches.Clear();

        foreach (BranchInfo branch in localBranches)
        {
            SourceBranches.Add(branch.Name);
        }

        _selectedRemote = Remotes.FirstOrDefault(name =>
            string.Equals(name, "origin", StringComparison.Ordinal)) ?? Remotes.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedRemote));

        SourceBranch = SourceBranches.FirstOrDefault(name =>
            string.Equals(name, currentBranch, StringComparison.Ordinal))
            ?? SourceBranches.FirstOrDefault();

        await ReloadPlanAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Refreshes the plan when the selection changes.
    /// </summary>
    /// <remarks>
    /// 🔑 The anchor must belong to the <b>selected branch</b>. If not refreshed,
    /// <see cref="LeaseAnchor"/> falls onto the safe side (no anchor → the lease flag isn't
    /// written → forcing isn't attempted), but the user would have no way to see that the box
    /// they checked isn't working.
    /// </remarks>
    private void ReloadPlan() => _ = ReloadPlanSafeAsync();

    private async Task ReloadPlanSafeAsync()
    {
        try
        {
            await ReloadPlanAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Warning = Loc.GitError(error);
        }
    }

    /// <summary>Refreshes the lease anchor and the rows.</summary>
    private async Task ReloadPlanAsync(CancellationToken cancellationToken)
    {
        if (SelectedRemote is not { Length: > 0 } remote || SourceBranch is not { Length: > 0 } branch)
        {
            return;
        }

        _plan = await _push
            .PlanAsync(_workingDirectory, remote, branch, cancellationToken)
            .ConfigureAwait(true);

        // The most common thing the user wants: when pushing a branch that has no upstream,
        // the box should come pre-checked (GitExtensions does this too). Otherwise a bare
        // `git push` would fail again — measured, exit code 128.
        SetUpstream = !_plan.HasUpstream;

        Tags.Clear();

        foreach (string tag in _plan.Tags)
        {
            Tags.Add(tag);
        }

        SelectedTag = Tags.FirstOrDefault();

        BuildRows();

        OnPropertyChanged(nameof(LeaseNotice));
        OnPropertyChanged(nameof(HasLeaseNotice));
        OnPropertyChanged(nameof(WouldCreateRemoteBranch));
        RaisePreview();
    }

    private void BuildRows()
    {
        Rows.Clear();

        if (_plan is null)
        {
            return;
        }

        foreach (BranchInfo branch in _localBranches)
        {
            bool exists = _plan.RemoteBranches.Contains(branch.Name, StringComparer.Ordinal);

            Rows.Add(new PushBranchRowViewModel
            {
                LocalBranch = branch.Name,
                RemoteBranch = branch.Upstream is { Length: > 0 } upstream
                    ? upstream
                    : $"{_plan.Remote}/{branch.Name}",
                AheadBehind = DescribeTracking(branch.Tracking, branch.Upstream),
                ExistsOnRemote = exists,
            });
        }
    }

    private static string DescribeTracking(UpstreamTracking tracking, string? upstream)
    {
        if (upstream is not { Length: > 0 })
        {
            return "takip yok";
        }

        if (tracking.IsGone)
        {
            return Loc.T("push.upstream_was_deleted");
        }

        return tracking.IsUpToDate ? "up to date" : $"↑{tracking.Ahead} ↓{tracking.Behind}";
    }

    /// <summary>Lease anchor: the frozen tip of the selected target's remote tracking ref.</summary>
    private string? LeaseAnchor =>
        _plan is not null && string.Equals(_plan.RemoteBranch, RemoteBranch, StringComparison.Ordinal)
            ? _plan.RemoteTipObjectId
            : null;

    private PushOptions BuildOptions()
    {
        List<PushSpec> refs = [];
        PushTagMode tags = PushTagMode.None;

        switch (Tab)
        {
            case PushTab.Branch:
                if (SourceBranch is { Length: > 0 } source && RemoteBranch.Length > 0)
                {
                    refs.Add(new PushSpec(source, RemoteBranch)
                    {
                        ExpectedRemoteObjectId = LeaseAnchor,
                    });
                }

                break;

            case PushTab.Tag:
                if (AllTags)
                {
                    tags = PushTagMode.All;
                }
                else if (SelectedTag is { Length: > 0 } tag)
                {
                    refs.Add(new PushSpec($"refs/tags/{tag}", $"refs/tags/{tag}"));
                }

                break;

            case PushTab.MultipleBranches:
            default:
                foreach (PushBranchRowViewModel row in Rows)
                {
                    if (row.Delete)
                    {
                        refs.Add(new PushSpec(string.Empty, row.LocalBranch, Delete: true));
                    }
                    else if (row.Push)
                    {
                        refs.Add(new PushSpec(row.LocalBranch, row.LocalBranch));
                    }
                }

                break;
        }

        return new PushOptions
        {
            Remote = SelectedRemote ?? string.Empty,
            Refs = refs,
            SetUpstream = SetUpstream && Tab == PushTab.Branch,
            ForceWithLease = ForceWithLease && Tab == PushTab.Branch,
            Tags = tags,
        };
    }

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(CommandPreview));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(WouldCreateRemoteBranch));
        RunCommand.NotifyCanExecuteChanged();
    }

    private async Task RunAsync()
    {
        if (!CanRun)
        {
            return;
        }

        IsBusy = true;
        Notice = null;
        Warning = null;
        Advice = null;
        ProgressText = null;
        ProgressPercent = null;

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        try
        {
            PushResult result = await _push
                .PushAsync(
                    _workingDirectory,
                    BuildOptions() with { Progress = CreateProgress() },
                    _cancellation.Token)
                .ConfigureAwait(true);

            Report(result);
        }
        catch (OperationCanceledException)
        {
            Notice = Loc.T("push.the_operation_was_cancelled");
        }
        catch (GitException error) when (error.Kind == GitFailureKind.AuthenticationRequired)
        {
            await HandleAuthenticationAsync(error).ConfigureAwait(true);
        }
        catch (GitException error)
        {
            Warning = Loc.GitError(error);
        }
        finally
        {
            IsBusy = false;
            ProgressText = null;
            ProgressPercent = null;
        }
    }

    /// <summary>
    /// Handles an authentication error (P06-T09).
    /// </summary>
    /// <remarks>
    /// 🔑 The raw <c>stderr</c> isn't shown: the same line is written both for a missing SSH
    /// key and for a hostname that can't be resolved (measured). Diagnostics look at the
    /// <b>environment</b> and only for HTTPS does it ask for credentials and retry — over SSH
    /// what's needed is a key, and that isn't solved by a dialog.
    /// </remarks>
    private async Task HandleAuthenticationAsync(GitException error)
    {
        if (_diagnostics is null || _authentication is null)
        {
            Warning = Loc.GitError(error);
            return;
        }

        AuthenticationDiagnosis diagnosis = await _diagnostics
            .DiagnoseAsync(_workingDirectory, SelectedRemote)
            .ConfigureAwait(true);

        GitCredentials? credentials = await _authentication
            .ShowAsync(new AuthenticationViewModel(diagnosis))
            .ConfigureAwait(true);

        if (credentials is null)
        {
            Warning = diagnosis.Explanation;
            Advice = diagnosis.Suggestions.Count > 0
                ? "Deneyebilecekleriniz: " + string.Join(" · ", diagnosis.Suggestions)
                : null;

            return;
        }

        try
        {
            PushResult result = await _push
                .PushAsync(_workingDirectory, BuildOptions() with { Credentials = credentials })
                .ConfigureAwait(true);

            Report(result);
        }
        catch (GitException retry)
        {
            Warning = Loc.GitError(retry);
        }
    }

    private void Report(PushResult result)
    {
        Notice = Summarize(result);

        if (result.Rejected.Count == 0)
        {
            return;
        }

        // 🔴 On partial success we report both what went through and what was rejected: saying
        // "push failed" would push the user to retry a push that had already gone through
        // (measured — even with exit code 1 the other branch had actually gone through).
        Warning = (result.IsPartial
                ? Loc.T("push.some_were_pushed_some_were_rejected")
                : Loc.T("push.the_push_was_rejected"))
            + string.Join(", ", result.Rejected.Select(row => row.ShortDestination));

        Advice = DescribeAdvice(result);
    }

    private static string DescribeAdvice(PushResult result)
    {
        PushRejectionKind kind = result.Rejected[0].Rejection ?? PushRejectionKind.Unknown;

        string advice = kind switch
        {
            PushRejectionKind.Behind =>
                Loc.T("push.the_remote_has_commits_you_do_not_have_fetch")
                + Loc.T("push.if_you_rewrote_history_deliberately_use_the_"),
            PushRejectionKind.StaleLease =>
                Loc.T("push.the_remote_branch_changed_since_you_opened_t")
                + Loc.T("push.fetch_see_what_changed_then_try_again"),
            PushRejectionKind.RemoteRejected =>
                Loc.T("push.the_remote_rejected_it_it_may_be_a_protected"),
            _ => Loc.T("push.the_reason_for_the_rejection_was_not_recogni"),
        };

        return result.RemoteMessages.Count > 0
            ? advice + " Uzak depo diyor ki: " + string.Join(" · ", result.RemoteMessages)
            : advice;
    }

    private static string Summarize(PushResult result)
    {
        if (result.Refs.Count == 0)
        {
            return Loc.T("push.no_changes");
        }

        List<string> parts = [];

        foreach (IGrouping<PushRefStatus, PushRefResult> group in result.Refs.GroupBy(row => row.Status))
        {
            string label = group.Key switch
            {
                PushRefStatus.Created => "yeni",
                PushRefStatus.FastForward => Loc.T("push.updated"),
                PushRefStatus.Forced => Loc.T("push.force_updated"),
                PushRefStatus.Deleted => "silindi",
                PushRefStatus.UpToDate => Loc.T("push.already_up_to_date"),
                _ => "reddedildi",
            };

            parts.Add($"{group.Count()} {label} ({string.Join(", ", group.Select(row => row.ShortDestination))})");
        }

        return (result.DryRun ? "Deneme — " : string.Empty) + string.Join(" · ", parts);
    }
}
