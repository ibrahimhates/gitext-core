using System.Windows.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// Routes a command id to the place that actually runs it (P08-T04).
/// </summary>
/// <remarks>
/// <para>
/// Shortcut dispatch and the command palette have to use the <b>same</b> execution path. If
/// they were separate, a command could work from the palette but not from the shortcut — or
/// the other way round — and nobody would know which one was right.
/// </para>
/// <para>
/// There are two sources: global commands are a plain <see cref="ICommand"/>, panel commands
/// live in that panel's <see cref="ShortcutDispatcher"/>. The split is not accidental — panel
/// commands read view state (selected rows, focus) and that state exists only in the panel.
/// </para>
/// </remarks>
public sealed class CommandRouter
{
    private readonly Dictionary<string, ICommand> _global = new(StringComparer.Ordinal);
    private readonly List<ShortcutDispatcher> _panels = [];

    public void Register(string commandId, ICommand command) => _global[commandId] = command;

    public void Register(ShortcutDispatcher dispatcher)
    {
        if (!_panels.Contains(dispatcher))
        {
            _panels.Add(dispatcher);
        }
    }

    public IReadOnlyDictionary<string, ICommand> GlobalCommands => _global;

    /// <summary>
    /// Can the command be executed right now?
    /// </summary>
    /// <remarks>
    /// For panel commands this asks "is it bound", not "is it meaningful right now": the latter
    /// only shows up when it runs (with no selection the handler returns <c>false</c>).
    /// </remarks>
    public bool CanRun(string commandId) =>
        (_global.TryGetValue(commandId, out ICommand? command) && command.CanExecute(null))
        || _panels.Any(p => p.BoundCommands.Contains(commandId));

    /// <summary>Runs the command.</summary>
    /// <returns><see langword="true"/> if it ran.</returns>
    public bool Run(string commandId)
    {
        if (_global.TryGetValue(commandId, out ICommand? command))
        {
            if (!command.CanExecute(null))
            {
                return false;
            }

            command.Execute(null);

            return true;
        }

        foreach (ShortcutDispatcher panel in _panels)
        {
            if (panel.TryInvoke(commandId))
            {
                return true;
            }
        }

        return false;
    }
}
