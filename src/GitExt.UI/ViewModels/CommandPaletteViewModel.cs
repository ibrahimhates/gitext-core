using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.UI.Commands;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The command palette (P08-T04).
/// </summary>
/// <remarks>
/// It solves discoverability on its own: someone who knows a command's name can reach it without
/// knowing its shortcut — and <b>learns</b> the shortcut from the search result. For commands buried
/// in the menu this is the only realistic route to discovery.
/// </remarks>
public sealed partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly ICommandRegistry _registry;
    private readonly CommandRouter _router;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public CommandPaletteViewModel(ICommandRegistry registry, CommandRouter router)
    {
        _registry = registry;
        _router = router;

        Refresh();
    }

    public ObservableCollection<CommandPaletteItem> Results { get; } = [];

    public bool IsEmpty => Results.Count == 0;

    /// <summary>Runs the selected command.</summary>
    /// <returns><see langword="true"/> when it ran — only then should the palette close.</returns>
    public bool RunSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
        {
            return false;
        }

        CommandPaletteItem item = Results[SelectedIndex];

        // A command that cannot run is visible in the palette but DIMMED; even if selected it does not
        // run. Hiding it would be worse: the user searches for the command, does not find it, and thinks
        // it does not exist.
        return item.CanRun && _router.Run(item.CommandId);
    }

    /// <summary>Moves the selection through the list; wraps around at either end.</summary>
    /// <remarks>
    /// The wrapping is deliberate: the palette is a short list and the user keeps pressing down. Getting
    /// stuck at the end would be a silent wall to a user who has not noticed the list finished.
    /// </remarks>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            SelectedIndex = -1;

            return;
        }

        int next = (SelectedIndex + delta) % Results.Count;

        SelectedIndex = next < 0 ? next + Results.Count : next;
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        Results.Clear();

        foreach (CommandDefinition definition in _registry.Definitions.OrderBy(Rank).ThenBy(d => d.Title))
        {
            if (!Matches(definition))
            {
                continue;
            }

            Results.Add(new CommandPaletteItem(
                definition.Id,
                definition.Title,
                definition.Category.ToString(),
                _registry.GetGesture(definition.Id)?.ToString() ?? "",
                _router.CanRun(definition.Id)));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// The sort priority: runnable commands first.
    /// </summary>
    /// <remarks>
    /// With no repository open, "Push…" sitting at the top of the list meant the palette not working on
    /// the first try — the user pressing Enter sees nothing happen.
    /// </remarks>
    private int Rank(CommandDefinition definition) => _router.CanRun(definition.Id) ? 0 : 1;

    /// <summary>
    /// Query matching: it is enough for the letters to appear <b>in order</b>.
    /// </summary>
    /// <remarks>
    /// "dlo" → "Dal oluştur". A full substring search would meet a user typing an abbreviation with an
    /// empty result; fuzzy matching is what makes the palette useful.
    /// </remarks>
    private bool Matches(CommandDefinition definition)
    {
        if (Query.Length == 0)
        {
            return true;
        }

        return IsSubsequence(Query, definition.Title)
            || IsSubsequence(Query, definition.Id)
            || _registry.GetGesture(definition.Id)?.ToString()
                .Contains(Query, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Do <paramref name="needle"/>'s letters appear in order within <paramref name="haystack"/>?
    /// </summary>
    /// <remarks>
    /// The comparison is <b>culture-sensitive</b>: in Turkish, <c>I</c>/<c>ı</c> and <c>İ</c>/<c>i</c>
    /// are separate letters. An ordinal comparison would return nothing to a user typing "İşlem".
    /// </remarks>
    private static bool IsSubsequence(string needle, string haystack)
    {
        int index = 0;

        foreach (char c in haystack)
        {
            if (index < needle.Length
                && char.ToLower(c, System.Globalization.CultureInfo.CurrentCulture)
                    == char.ToLower(needle[index], System.Globalization.CultureInfo.CurrentCulture))
            {
                index++;
            }
        }

        return index == needle.Length;
    }
}

/// <summary>A row shown in the palette.</summary>
public sealed record CommandPaletteItem(
    string CommandId,
    string Title,
    string Category,
    string GestureText,
    bool CanRun);
