using System.Globalization;
using Avalonia.Data.Converters;
using GitExt.Core.Model;
using GitExt.UI.Localization;
using GitExt.UI.Settings;

namespace GitExt.UI.ViewModels;

public sealed record RevisionGridDisplay
{
    public bool ShowRemoteBranches { get; init; } = true;

    public bool ShowTags { get; init; } = true;

    public bool DrawNonRelativesGray { get; init; } = true;

    public bool ShowAuthorDate { get; init; } = true;

    public bool RelativeDate { get; init; } = true;

    public bool ShowCommitBody { get; init; } = true;

    public bool ShowGraphColumn { get; init; } = true;

    public bool ShowAuthorNameColumn { get; init; } = true;

    public bool ShowDateColumn { get; init; } = true;

    public bool ShowIdColumn { get; init; } = true;

    public static RevisionGridDisplay From(RevisionGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new RevisionGridDisplay
        {
            ShowRemoteBranches = settings.ShowRemoteBranches,
            ShowTags = settings.ShowTags,
            DrawNonRelativesGray = settings.DrawNonRelativesGray,
            ShowAuthorDate = settings.ShowAuthorDate,
            RelativeDate = settings.RelativeDate,
            ShowCommitBody = settings.ShowCommitBody,
            ShowGraphColumn = settings.ShowGraphColumn,
            ShowAuthorNameColumn = settings.ShowAuthorNameColumn,
            ShowDateColumn = settings.ShowDateColumn,
            ShowIdColumn = settings.ShowIdColumn,
        };
    }

    public void CopyTo(RevisionGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.ShowRemoteBranches = ShowRemoteBranches;
        settings.ShowTags = ShowTags;
        settings.DrawNonRelativesGray = DrawNonRelativesGray;
        settings.ShowAuthorDate = ShowAuthorDate;
        settings.RelativeDate = RelativeDate;
        settings.ShowCommitBody = ShowCommitBody;
        settings.ShowGraphColumn = ShowGraphColumn;
        settings.ShowAuthorNameColumn = ShowAuthorNameColumn;
        settings.ShowDateColumn = ShowDateColumn;
        settings.ShowIdColumn = ShowIdColumn;
    }

    public bool? IsChecked(RevisionGridMenuId id) => id switch
    {
        RevisionGridMenuId.ShowRemoteBranches => ShowRemoteBranches,
        RevisionGridMenuId.ShowTags => ShowTags,
        RevisionGridMenuId.DrawNonRelativesGray => DrawNonRelativesGray,
        RevisionGridMenuId.ShowAuthorDate => ShowAuthorDate,
        RevisionGridMenuId.ShowRelativeDate => RelativeDate,
        RevisionGridMenuId.ShowCommitMessageBody => ShowCommitBody,
        RevisionGridMenuId.ShowRevisionGraphColumn => ShowGraphColumn,
        RevisionGridMenuId.ShowAuthorNameColumn => ShowAuthorNameColumn,
        RevisionGridMenuId.ShowDateColumn => ShowDateColumn,
        RevisionGridMenuId.ShowIdColumn => ShowIdColumn,
        _ => null,
    };

    public RevisionGridDisplay? Toggle(RevisionGridMenuId id) => id switch
    {
        RevisionGridMenuId.ShowRemoteBranches => this with { ShowRemoteBranches = !ShowRemoteBranches },
        RevisionGridMenuId.ShowTags => this with { ShowTags = !ShowTags },
        RevisionGridMenuId.DrawNonRelativesGray => this with { DrawNonRelativesGray = !DrawNonRelativesGray },
        RevisionGridMenuId.ShowAuthorDate => this with { ShowAuthorDate = !ShowAuthorDate },
        RevisionGridMenuId.ShowRelativeDate => this with { RelativeDate = !RelativeDate },
        RevisionGridMenuId.ShowCommitMessageBody => this with { ShowCommitBody = !ShowCommitBody },
        RevisionGridMenuId.ShowRevisionGraphColumn => this with { ShowGraphColumn = !ShowGraphColumn },
        RevisionGridMenuId.ShowAuthorNameColumn => this with { ShowAuthorNameColumn = !ShowAuthorNameColumn },
        RevisionGridMenuId.ShowDateColumn => this with { ShowDateColumn = !ShowDateColumn },
        RevisionGridMenuId.ShowIdColumn => this with { ShowIdColumn = !ShowIdColumn },
        _ => null,
    };

    public bool Shows(RefBadge badge)
    {
        ArgumentNullException.ThrowIfNull(badge);

        return badge.Kind switch
        {
            RefBadgeKind.RemoteBranch => ShowRemoteBranches,
            RefBadgeKind.Tag => ShowTags,
            _ => true,
        };
    }

    public string FormatDate(CommitInfo commit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(commit);

        DateTimeOffset when = ShowAuthorDate ? commit.Author.When : commit.Committer.When;

        return RelativeDate
            ? Relative(now - when)
            : when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    }

    public static string Relative(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        double days = span.TotalDays;

        return span.TotalMinutes < 1 ? Count("second", (int)span.TotalSeconds)
            : span.TotalHours < 1 ? Count("minute", (int)span.TotalMinutes)
            : days < 1 ? Count("hour", (int)span.TotalHours)
            : days < 7 ? Count("day", (int)days)
            : days < 30 ? Count("week", (int)(days / 7))
            : days < 365 ? Count("month", (int)(days / 30))
            : Count("year", (int)(days / 365));

        static string Count(string unit, int value) => value == 1
            ? Loc.T($"revision_grid.relative_date.{unit}_ago")
            : Loc.F($"revision_grid.relative_date.{unit}s_ago", value);
    }
}

public sealed class RevisionGridDateText : IMultiValueConverter
{
    public static RevisionGridDateText Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count >= 2 && values[0] is CommitRowViewModel row
            ? (values[1] as RevisionGridDisplay ?? new RevisionGridDisplay()).FormatDate(row.Commit, DateTimeOffset.Now)
            : null;
}

public sealed class RevisionGridBadgeVisible : IMultiValueConverter
{
    public static RevisionGridBadgeVisible Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count < 2 || values[0] is not RefBadge badge || values[1] is not RevisionGridDisplay display
            || display.Shows(badge);
}
