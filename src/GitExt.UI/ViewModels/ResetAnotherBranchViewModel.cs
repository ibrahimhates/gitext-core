using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

public sealed record ResetAnotherBranchCandidate(string Name, CommitId Tip);

public sealed partial class ResetAnotherBranchViewModel : ViewModelBase
{
    private readonly string _workingDirectory;
    private readonly IMergeBaseReader? _mergeBase;
    private CancellationTokenSource? _validation;

    public ResetAnotherBranchViewModel(
        string workingDirectory,
        CommitInfo target,
        IReadOnlyList<ResetAnotherBranchCandidate> branches,
        IMergeBaseReader? mergeBase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(branches);

        _workingDirectory = workingDirectory;
        _mergeBase = mergeBase;
        Target = target;
        Branches = branches;
    }

    public static IReadOnlyList<ResetAnotherBranchCandidate> Candidates(RepositoryRefs refs, CommitId target)
    {
        ArgumentNullException.ThrowIfNull(refs);

        return
        [
            .. refs.LocalBranches
                .Where(branch => refs.Head.IsDetached || !branch.IsCurrent)
                .Where(branch => branch.Ref.ObjectId != target)
                .Select(branch => new ResetAnotherBranchCandidate(branch.Name, branch.Ref.ObjectId)),
        ];
    }

    public CommitInfo Target { get; }

    public string TargetText => $"{Target.Id.ToShortString()} {Target.Subject}";

    public IReadOnlyList<ResetAnotherBranchCandidate> Branches { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReset))]
    public partial ResetAnotherBranchCandidate? SelectedBranch { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReset))]
    public partial bool Force { get; set; }

    [ObservableProperty]
    public partial bool CheckoutAfterReset { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReset))]
    public partial bool IsFastForward { get; private set; }

    public bool CanReset => SelectedBranch is not null && (Force || IsFastForward);

    public Task? Validation { get; private set; }

    partial void OnSelectedBranchChanged(ResetAnotherBranchCandidate? value) => Validation = ValidateAsync(value);

    private async Task ValidateAsync(ResetAnotherBranchCandidate? branch)
    {
        _validation?.Cancel();
        _validation?.Dispose();
        _validation = new CancellationTokenSource();
        CancellationToken token = _validation.Token;

        IsFastForward = false;

        if (branch is null || _mergeBase is null)
        {
            return;
        }

        try
        {
            CommitId? baseCommit = await _mergeBase
                .FindAsync(_workingDirectory, [branch.Tip.ToString(), Target.Id.ToString()], token)
                .ConfigureAwait(true);

            if (!token.IsCancellationRequested)
            {
                IsFastForward = baseCommit == branch.Tip;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Core.Git.GitException)
        {
            IsFastForward = false;
        }
    }
}

public interface IResetAnotherBranchPrompt
{
    Task<bool> ShowAsync(ResetAnotherBranchViewModel model);
}
