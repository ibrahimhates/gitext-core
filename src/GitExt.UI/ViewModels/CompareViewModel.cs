using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// What is being compared with what (P04-T16).
/// </summary>
public enum CompareTarget
{
    /// <summary>Between two arbitrary revisions.</summary>
    Revisions,

    /// <summary>Between a revision and the working tree.</summary>
    WorkingTree,
}

/// <summary>
/// The ViewModel of the <b>separate window</b> comparing two revisions (P04-T16).
/// </summary>
/// <remarks>
/// <para>
/// The window is <b>modeless</b> and more than one can be open at a time; each has its own
/// <see cref="CompareViewModel"/>. The decision came from looking at GitExtensions: there too
/// <c>FormDiff</c> is opened with <b><c>Show()</c></b> rather than <c>ShowDialog</c>.
/// </para>
/// <para>
/// <see cref="DiffViewModel"/> is <b>reused</b> to show the diff — that component was written
/// deliberately independent of the main window in P04-T08, for exactly this reason.
/// </para>
/// </remarks>
public sealed partial class CompareViewModel : ViewModelBase
{
    public CompareViewModel(IDiffReader reader, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        WorkingDirectory = workingDirectory;
        Diff = new DiffViewModel(reader);
    }

    public string WorkingDirectory { get; }

    public DiffViewModel Diff { get; }

    /// <summary>The comparison's left side (the base).</summary>
    [ObservableProperty]
    public partial string FromRevision { get; private set; } = string.Empty;

    /// <summary>The right side; empty in a working tree comparison.</summary>
    [ObservableProperty]
    public partial string ToRevision { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial CompareTarget Target { get; private set; }

    /// <summary>The window title.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = Loc.T("compare.compare");

    /// <summary>Compares two commits.</summary>
    public Task CompareAsync(
        CommitId from,
        CommitId to,
        CancellationToken cancellationToken = default) =>
        CompareAsync(from.Value, to.Value, cancellationToken);

    /// <summary>Compares two revisions (commit, branch, tag).</summary>
    public Task CompareAsync(
        string from,
        string to,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        FromRevision = from;
        ToRevision = to;
        Target = CompareTarget.Revisions;
        Title = $"{Shorten(from)} ↔ {Shorten(to)}";

        return Diff.ShowRangeAsync(WorkingDirectory, from, to, Title, cancellationToken: cancellationToken);
    }

    /// <summary>Compares a revision <b>with the working tree</b>.</summary>
    public Task CompareWithWorkingTreeAsync(
        string from,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);

        FromRevision = from;
        ToRevision = string.Empty;
        Target = CompareTarget.WorkingTree;
        Title = $"{Shorten(from)} ↔ working tree";

        return Diff.ShowRangeAsync(WorkingDirectory, from, null, Title, cancellationToken: cancellationToken);
    }

    /// <summary>Re-reads the same comparison.</summary>
    /// <remarks>
    /// Needed for a working tree comparison: the user can edit the file and leave the window open.
    /// </remarks>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        Target == CompareTarget.WorkingTree
            ? CompareWithWorkingTreeAsync(FromRevision, cancellationToken)
            : CompareAsync(FromRevision, ToRevision, cancellationToken);

    /// <summary>Abbreviates SHAs; leaves branch/tag names alone.</summary>
    private static string Shorten(string revision) =>
        revision.Length == 40 && revision.All(Uri.IsHexDigit) ? revision[..8] : revision;
}
