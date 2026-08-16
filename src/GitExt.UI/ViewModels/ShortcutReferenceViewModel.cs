using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.UI.Commands;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The shortcut reference screen (P08-T06, <c>F1</c>).
/// </summary>
/// <remarks>
/// Grouped by context rather than by category: the user's question is "which keys work right now, where
/// I am?" — and the answer is exactly the context. Grouping by category would show the commit list's
/// shortcuts in the same list while you are in the diff panel.
/// </remarks>
public sealed partial class ShortcutReferenceViewModel : ViewModelBase
{
    private readonly ICommandRegistry _registry;

    [ObservableProperty]
    private string _filter = string.Empty;

    public ShortcutReferenceViewModel(ICommandRegistry registry)
    {
        _registry = registry;

        Refresh();
    }

    public List<ShortcutReferenceGroup> Groups { get; private set; } = [];

    /// <summary>Conflicting assignments; the user should see them here too.</summary>
    public IReadOnlyList<string> ConflictMessages { get; private set; } = [];

    public bool HasConflicts => ConflictMessages.Count > 0;

    partial void OnFilterChanged(string value) => Refresh();

    private void Refresh()
    {
        CommandContext[] order =
        [
            CommandContext.Global,
            CommandContext.CommitList,
            CommandContext.Diff,
            CommandContext.WorkingTree,
            CommandContext.RefTree,
        ];

        List<ShortcutReferenceGroup> groups = [];

        foreach (CommandContext context in order)
        {
            List<ShortcutRow> rows =
            [
                .. _registry.InContext(context)
                    .Where(Matches)
                    .Select(d => new ShortcutRow(
                        d.Id,
                        d.Title,
                        d.Category,
                        d.Context,
                        _registry.GetGesture(d.Id),
                        _registry.IsCustomized(d.Id)))

                    // Commands without a shortcut go at the END of the list: the screen answers the
                    // question "which key does what", it is not a command catalogue.
                    .OrderBy(r => r.Gesture is null)
                    .ThenBy(r => r.Title, StringComparer.CurrentCulture)
            ];

            if (rows.Count > 0)
            {
                groups.Add(new ShortcutReferenceGroup(ContextName(context), rows));
            }
        }

        Groups = groups;

        ConflictMessages =
        [
            .. _registry.Conflicts.Select(c =>
                $"{c.Gesture}: {string.Join(", ", c.CommandIds.Select(id => _registry.Find(id)?.Title ?? id))}"
                + Loc.T("shortcut_reference.only_the_first_one_works"))
        ];

        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(ConflictMessages));
        OnPropertyChanged(nameof(HasConflicts));
    }

    private bool Matches(CommandDefinition definition)
    {
        if (Filter.Length == 0)
        {
            return true;
        }

        return definition.Title.Contains(Filter, StringComparison.CurrentCultureIgnoreCase)
            || _registry.GetGesture(definition.Id)?.ToString()
                .Contains(Filter, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string ContextName(CommandContext context) => context switch
    {
        CommandContext.Global => "Her yerde",
        CommandContext.CommitList => "Commit listesi",
        CommandContext.Diff => Loc.T("shortcut_reference.diff_view"),
        CommandContext.WorkingTree => Loc.T("shortcut_reference.working_tree_commit_screen"),
        CommandContext.RefTree => "Dal paneli",
        _ => context.ToString(),
    };
}

/// <summary>The shortcuts grouped by context.</summary>
public sealed record ShortcutReferenceGroup(string Title, IReadOnlyList<ShortcutRow> Rows);
