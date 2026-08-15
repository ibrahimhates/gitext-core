using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// Installs global shortcuts on the window and syncs menu labels with the registry (P08-T01).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This class exists because of a real defect.</b> MEASURED in P08-T00/M03:
/// <c>MenuItem.InputGesture</c> does <b>not run</b> the command, it only draws the label.
/// <c>MainWindow.axaml</c> had <c>InputGesture="F5"</c> and no other binding at all —
/// so <b>F5 was a dead key</b>: the menu showed the shortcut, pressing it did
/// nothing.
/// </para>
/// <para>
/// That is why two jobs happen together here: the gesture is written <b>from one source</b> to
/// both <c>KeyBindings</c> (which does the work) and <c>InputGesture</c> (the label). They
/// cannot be split; if they are, the menu's shortcut and the working shortcut drift silently.
/// </para>
/// </remarks>
public sealed class GlobalShortcuts : IDisposable
{
    private readonly Window _window;
    private readonly ICommandRegistry _registry;
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MenuItem> _menuItems = new(StringComparer.Ordinal);

    public GlobalShortcuts(Window window, ICommandRegistry registry)
    {
        _window = window;
        _registry = registry;

        _registry.Changed += OnRegistryChanged;
    }

    /// <summary>
    /// The router that forwards a command id to the place that runs it.
    /// </summary>
    /// <remarks>
    /// The command palette is fed from here: if the shortcut and the palette did not use the
    /// <b>same</b> execution path, a command could work in one and not in the other.
    /// </remarks>
    public CommandRouter Router { get; } = new();

    /// <summary>Binds a command to an id.</summary>
    public GlobalShortcuts Bind(string commandId, ICommand command)
    {
        if (_registry.Find(commandId) is null)
        {
            throw new ArgumentException($"Unknown command id: {commandId}", nameof(commandId));
        }

        _commands[commandId] = command;
        Router.Register(commandId, command);

        return this;
    }

    /// <summary>
    /// Binds a menu item to an id: takes the command and the <b>shown</b> gesture from the registry.
    /// </summary>
    public GlobalShortcuts BindMenu(string commandId, MenuItem item)
    {
        _menuItems[commandId] = item;

        if (_commands.TryGetValue(commandId, out ICommand? command) && item.Command is null)
        {
            item.Command = command;
        }

        return this;
    }

    /// <summary>Applies the bindings to the window. Repeats itself whenever the registry changes.</summary>
    public void Apply()
    {
        _window.KeyBindings.Clear();

        HashSet<KeyGesture> taken = [];

        foreach (CommandDefinition definition in _registry.InContext(CommandContext.Global))
        {
            KeyGesture? gesture = _registry.GetGesture(definition.Id);

            if (_menuItems.TryGetValue(definition.Id, out MenuItem? item))
            {
                // The label is always updated — even when the command is not bound, so the
                // shortcut in the menu and the one that actually runs never drift apart.
                item.InputGesture = gesture;
            }

            if (gesture is null || !_commands.TryGetValue(definition.Id, out ICommand? command))
            {
                continue;
            }

            // 🔴 Registering the same gesture twice silently kills the second (P08-T00/M10).
            // The conflict is already reported by the registry; not registering the second one
            // AT ALL here is more honest than "sometimes it works" behaviour.
            if (!taken.Add(gesture))
            {
                continue;
            }

            _window.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
        }
    }

    public void Dispose() => _registry.Changed -= OnRegistryChanged;

    private void OnRegistryChanged(object? sender, EventArgs e) => Apply();
}
