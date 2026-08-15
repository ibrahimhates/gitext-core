using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.UI.Commands;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Kısayol referans ekranı (P08-T06, <c>F1</c>).
/// </summary>
/// <remarks>
/// Bağlama göre gruplanıyor, kategoriye göre değil: kullanıcının sorusu "şu an, buradayken
/// hangi tuşlar çalışıyor?" — ve cevap tam olarak bağlamdır. Kategoriye göre gruplamak,
/// diff panelindeyken commit listesi kısayollarını aynı listede gösterirdi.
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

    /// <summary>Çakışan atamalar; kullanıcı burada da görmeli.</summary>
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

                    // Kısayolu olmayan komutlar listenin SONUNDA: ekran "hangi tuş ne yapar"
                    // sorusuna cevap veriyor, komut kataloğu değil.
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

/// <summary>Bağlama göre gruplanmış kısayollar.</summary>
public sealed record ShortcutReferenceGroup(string Title, IReadOnlyList<ShortcutRow> Rows);
