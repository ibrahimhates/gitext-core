using Avalonia.Input;
using GitExt.UI.Settings;

namespace GitExt.UI.Commands;

/// <summary>
/// The <b>single registry</b> for every command and shortcut in the application (P08-T01).
/// </summary>
/// <remarks>
/// Spreading shortcuts across XAML and code-behind <c>switch</c>es does not scale: entering
/// Phase 08 the gestures sat in <b>six separate files</b>, and in two different mechanisms on
/// top of that (<c>MenuItem.InputGesture</c> and hand-written tunneling handlers). Learning a
/// command's shortcut meant reading six files, and nobody was there to see the conflicts.
/// </remarks>
public interface ICommandRegistry
{
    /// <summary>Every defined command, in definition order.</summary>
    IReadOnlyList<CommandDefinition> Definitions { get; }

    /// <summary>Raised when a shortcut assignment or the set of definitions changes.</summary>
    event EventHandler? Changed;

    CommandDefinition? Find(string commandId);

    /// <summary>
    /// The command's <b>effective</b> shortcut: the user's if assigned, otherwise the default.
    /// </summary>
    KeyGesture? GetGesture(string commandId);

    /// <summary>Has the user changed this command's shortcut?</summary>
    bool IsCustomized(string commandId);

    /// <summary>
    /// Changes the shortcut. Passing <see langword="null"/> <b>removes</b> the shortcut —
    /// use <see cref="Reset"/> to go back to the default.
    /// </summary>
    void SetGesture(string commandId, KeyGesture? gesture);

    /// <summary>Resets the command to its default shortcut.</summary>
    void Reset(string commandId);

    /// <summary>Resets every shortcut to its default.</summary>
    void ResetAll();

    /// <summary>
    /// Commands that share the same gesture and whose contexts overlap.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>We have to compute this ourselves.</b> P08-T00/M10: when Avalonia finds two
    /// registrations for the same gesture it runs <b>only the first</b> and says nothing. The
    /// user reassigns a shortcut, it does not work, and they cannot see why.
    /// </remarks>
    IReadOnlyList<ShortcutConflict> Conflicts { get; }

    /// <summary>
    /// The id of the command bound to this gesture in the given context.
    /// </summary>
    /// <remarks>
    /// The context matches the requested set <b>exactly</b>; <see cref="CommandContext.Global"/>
    /// is not added automatically. Otherwise a global shortcut would run <b>twice</b> — once
    /// from the window binding and once from the panel dispatch.
    /// </remarks>
    string? Resolve(KeyGesture gesture, CommandContext context);

    /// <summary>The commands valid in the given context.</summary>
    IReadOnlyList<CommandDefinition> InContext(CommandContext context);
}

/// <inheritdoc cref="ICommandRegistry"/>
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly IReadOnlyList<CommandDefinition> _definitions;
    private readonly Dictionary<string, CommandDefinition> _byId;
    private readonly ISettingsStore _settings;

    public CommandRegistry(ISettingsStore settings)
        : this(settings, DefaultCommandScheme.Definitions)
    {
    }

    public CommandRegistry(ISettingsStore settings, IReadOnlyList<CommandDefinition> definitions)
    {
        _settings = settings;
        _definitions = definitions;

        // An id clash is a programming error, not a runtime state: if two commands share the
        // same id it becomes undefined which one the user's reassignment lands on.
        _byId = definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<CommandDefinition> Definitions => _definitions;

    public event EventHandler? Changed;

    public CommandDefinition? Find(string commandId) =>
        _byId.GetValueOrDefault(commandId);

    public KeyGesture? GetGesture(string commandId)
    {
        if (!_byId.TryGetValue(commandId, out CommandDefinition? definition))
        {
            return null;
        }

        if (!_settings.Current.Shortcuts.TryGetValue(commandId, out string? stored))
        {
            return definition.DefaultGesture;
        }

        if (stored.Length == 0)
        {
            // The user removed the shortcut deliberately. Falling back to the default would
            // undo that decision; that is why "none" and "unassigned" are kept apart.
            return null;
        }

        return TryParse(stored) ?? definition.DefaultGesture;
    }

    public bool IsCustomized(string commandId) =>
        _settings.Current.Shortcuts.ContainsKey(commandId);

    public void SetGesture(string commandId, KeyGesture? gesture)
    {
        if (!_byId.ContainsKey(commandId))
        {
            return;
        }

        _settings.Update(s => s.Shortcuts[commandId] = gesture?.ToString() ?? "");

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset(string commandId)
    {
        if (!_settings.Current.Shortcuts.ContainsKey(commandId))
        {
            return;
        }

        _settings.Update(s => s.Shortcuts.Remove(commandId));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ResetAll()
    {
        if (_settings.Current.Shortcuts.Count == 0)
        {
            return;
        }

        _settings.Update(s => s.Shortcuts.Clear());

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<ShortcutConflict> Conflicts
    {
        get
        {
            List<ShortcutConflict> conflicts = [];

            // Group by gesture, then look for pairs with overlapping contexts inside the group.
            // The same gesture doing different work in different panels is NOT A CONFLICT.
            IEnumerable<IGrouping<KeyGesture, CommandDefinition>> groups = _definitions
                .Select(d => (Definition: d, Gesture: GetGesture(d.Id)))
                .Where(pair => pair.Gesture is not null)
                .GroupBy(pair => pair.Gesture!, pair => pair.Definition);

            foreach (IGrouping<KeyGesture, CommandDefinition> group in groups)
            {
                List<CommandDefinition> members = [.. group];

                if (members.Count < 2)
                {
                    continue;
                }

                List<string> clashing = [];

                for (int i = 0; i < members.Count; i++)
                {
                    bool overlapsAny = members
                        .Where((_, j) => j != i)
                        .Any(other => CommandDefinition.ContextsOverlap(members[i].Context, other.Context));

                    if (overlapsAny)
                    {
                        clashing.Add(members[i].Id);
                    }
                }

                if (clashing.Count > 1)
                {
                    conflicts.Add(new ShortcutConflict(group.Key, clashing));
                }
            }

            return conflicts;
        }
    }

    public string? Resolve(KeyGesture gesture, CommandContext context)
    {
        foreach (CommandDefinition definition in _definitions)
        {
            if ((definition.Context & context) == CommandContext.None)
            {
                continue;
            }

            if (gesture.Equals(GetGesture(definition.Id)))
            {
                return definition.Id;
            }
        }

        return null;
    }

    public IReadOnlyList<CommandDefinition> InContext(CommandContext context) =>
        [.. _definitions.Where(d => (d.Context & context) != CommandContext.None)];

    /// <summary>
    /// Parses the gesture text from the settings file.
    /// </summary>
    /// <remarks>
    /// Forgiving: one hand-edited broken line only drops that command back to its default.
    /// <c>KeyGesture.Parse</c> throws on text it does not recognise (P08-T00/M06), and if that
    /// exception were not caught a single broken line would make <b>the app fail to start</b>.
    /// </remarks>
    private static KeyGesture? TryParse(string text)
    {
        try
        {
            return KeyGesture.Parse(text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }
}
