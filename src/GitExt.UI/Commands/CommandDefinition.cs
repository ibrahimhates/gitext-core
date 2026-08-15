using Avalonia.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// The context in which the shortcut is valid (P08-T01).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Context is not decoration, it is a requirement.</b> MEASURED in P08-T00: a gesture put
/// into <c>Window.KeyBindings</c> is <b>unconditionally global</b> — the focused control can
/// neither see it (M12: the command still ran even when the control set <c>Handled=true</c>)
/// nor keep it for itself (M11: binding <c>Down</c> left the <c>ListBox</c> selection frozen).
/// </para>
/// <para>
/// That is why arrow keys, bare letters and editing keys are <b>never</b> <see cref="Global"/>;
/// they are bound to a context, and that context's view dispatches them from its own
/// tunneling handler.
/// </para>
/// </remarks>
[Flags]
public enum CommandContext
{
    /// <summary>Nowhere — can only be invoked from the command palette.</summary>
    None = 0,

    /// <summary>Everywhere while the application is open. <b>Works inside a text box too.</b></summary>
    Global = 1,

    /// <summary>Commit listesi odaktayken.</summary>
    CommitList = 2,

    /// <summary>While the working tree (staging) panel has focus.</summary>
    WorkingTree = 4,

    /// <summary>While the diff view has focus.</summary>
    Diff = 8,

    /// <summary>Dal/ref paneli odaktayken.</summary>
    RefTree = 16,
}

/// <summary>
/// Category used for grouping on the shortcut screen and in the command palette.
/// </summary>
public enum CommandCategory
{
    Repository,
    Commit,
    Branch,
    Remote,
    History,
    View,
    Navigation,
    Tools,
    Help,
}

/// <summary>
/// The <b>persistent</b> definition of a command (P08-T01).
/// </summary>
/// <param name="Id">
/// Persistent id. <b>Never changes</b>: it is the key under which the user's reassigned
/// shortcuts are stored in the settings file; if it changes the user silently loses them.
/// </param>
/// <param name="Title">The name shown in the menu, in the palette and on the shortcut screen.</param>
/// <param name="Category">Grouping.</param>
/// <param name="Context">The context(s) in which the shortcut is valid.</param>
/// <param name="DefaultGesture">
/// Default shortcut. When <see langword="null"/> the command has no default shortcut
/// (menu/palette only). The user can assign one later.
/// </param>
public sealed record CommandDefinition(
    string Id,
    string Title,
    CommandCategory Category,
    CommandContext Context,
    KeyGesture? DefaultGesture)
{
    /// <summary>
    /// Whether two contexts <b>can be active at the same time</b>.
    /// </summary>
    /// <remarks>
    /// The core of conflict detection. <see cref="CommandContext.Global"/> conflicts with all;
    /// two different panel contexts (e.g. commit list and working tree) do <b>not</b> conflict —
    /// focus cannot be in both, so the same gesture can do different work in each.
    /// GitExtensions is the same: <c>Ctrl+D</c> means something different from panel to panel.
    /// </remarks>
    public static bool ContextsOverlap(CommandContext left, CommandContext right) =>
        left.HasFlag(CommandContext.Global)
        || right.HasFlag(CommandContext.Global)
        || (left & right) != CommandContext.None;
}

/// <summary>A pair of commands that share the same gesture and whose contexts overlap.</summary>
/// <param name="Gesture">The conflicting gesture.</param>
/// <param name="CommandIds">Ids of the conflicting commands, in definition order.</param>
public sealed record ShortcutConflict(KeyGesture Gesture, IReadOnlyList<string> CommandIds);
