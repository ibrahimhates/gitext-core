using Avalonia.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// Dispatches a panel's context shortcuts (P08-T01).
/// </summary>
/// <remarks>
/// <para>
/// Views do not read the key event themselves; they hand the gesture here and a command runs
/// in return. That way "which key does what" has exactly one answer:
/// <see cref="ICommandRegistry"/>.
/// </para>
/// <para>
/// <b>Why not a window binding?</b> P08-T00/M11: a gesture put into <c>Window.KeyBindings</c>
/// steals the key from the focused control unconditionally. Had panel shortcuts (bare
/// <c>S</c>, <c>PgDn</c>, <c>Delete</c>…) been put there, list navigation and text boxes
/// would have stopped working. That is why panel gestures are dispatched from the panel's
/// <b>own tunneling handler</b> — what the code already did, now from a central source.
/// </para>
/// </remarks>
public sealed class ShortcutDispatcher
{
    private readonly ICommandRegistry _registry;
    private readonly CommandContext _context;
    private readonly Dictionary<string, Func<bool>> _handlers = new(StringComparer.Ordinal);

    public ShortcutDispatcher(ICommandRegistry registry, CommandContext context)
    {
        _registry = registry;
        _context = context;
    }

    /// <summary>
    /// Binds a command to a handler.
    /// </summary>
    /// <param name="commandId">Id of the command to bind.</param>
    /// <param name="handler">
    /// Returns <b>whether the event was consumed</b>. Returning <see langword="false"/> means
    /// "I did not handle this key, let it travel on" — swallowing <c>Alt+↓</c> at the end of a
    /// file would be a silent wall for the user.
    /// </param>
    public void Bind(string commandId, Func<bool> handler)
    {
        // Binding to an undefined id would be a silent failure: the shortcut never runs and
        // nobody can see why.
        if (_registry.Find(commandId) is null)
        {
            throw new ArgumentException($"Unknown command id: {commandId}", nameof(commandId));
        }

        _handlers[commandId] = handler;
    }

    /// <summary>Binds a handler that always consumes the event.</summary>
    public void Bind(string commandId, Action handler) =>
        Bind(commandId, () =>
        {
            handler();

            return true;
        });

    /// <summary>
    /// Resolves the key event and runs it if bound.
    /// </summary>
    /// <returns><see langword="true"/> if the event was consumed.</returns>
    public bool Handle(KeyEventArgs e)
    {
        if (e.Key is Key.None)
        {
            return false;
        }

        string? commandId = _registry.Resolve(new KeyGesture(e.Key, e.KeyModifiers), _context);

        if (commandId is null || !_handlers.TryGetValue(commandId, out Func<bool>? handler))
        {
            return false;
        }

        if (!handler())
        {
            return false;
        }

        e.Handled = true;

        return true;
    }

    /// <summary>The commands that actually have a handler in this context.</summary>
    public IReadOnlyCollection<string> BoundCommands => _handlers.Keys;

    /// <summary>
    /// Runs the command as if the key had been pressed (the command palette's path).
    /// </summary>
    /// <returns><see langword="true"/> if it is bound and the handler took the work.</returns>
    public bool TryInvoke(string commandId) =>
        _handlers.TryGetValue(commandId, out Func<bool>? handler) && handler();
}
