using Avalonia.Controls;

namespace GitExt.UI.Commands;

/// <summary>
/// Focus navigation between panels (P08-T05).
/// </summary>
/// <remarks>
/// <para>
/// <c>Tab</c> walks control by control; in a large window moving from one panel to the next
/// means dozens of <c>Tab</c> presses. <c>F6</c> jumps panels — MEASURED in P08-T00/M09:
/// in Avalonia <c>F6</c> does <b>nothing</b> by default, so it is free.
/// </para>
/// <para>
/// 🔴 <b>Invisible panels are skipped.</b> Giving focus to a hidden panel means the user
/// <b>loses</b> focus: keys go nowhere and nothing changes on screen — the keyboard dies
/// silently (the same trap measured in Phase 03).
/// </para>
/// </remarks>
public sealed class PanelNavigator
{
    private readonly List<Panel> _panels = [];

    private sealed record Panel(string Id, Func<bool> IsAvailable, Func<bool> Focus);

    /// <summary>Adds a panel to the order. The order of addition is the navigation order.</summary>
    public PanelNavigator Add(string id, Func<bool> isAvailable, Func<bool> focus)
    {
        _panels.Add(new Panel(id, isAvailable, focus));

        return this;
    }

    /// <summary>The index of the panel that currently has focus; <c>-1</c> if none does.</summary>
    public int CurrentIndex(Func<string, bool> hasFocus) =>
        _panels.FindIndex(p => hasFocus(p.Id));

    /// <summary>Focuses a specific panel.</summary>
    public bool FocusPanel(string id)
    {
        Panel? panel = _panels.Find(p => p.Id == id);

        return panel is not null && panel.IsAvailable() && panel.Focus();
    }

    /// <summary>
    /// Moves focus to the next usable panel.
    /// </summary>
    /// <param name="hasFocus">Whether the given panel currently has focus.</param>
    /// <param name="delta"><c>1</c> for forward, <c>-1</c> for backward.</param>
    public bool Move(Func<string, bool> hasFocus, int delta)
    {
        if (_panels.Count == 0)
        {
            return false;
        }

        int start = CurrentIndex(hasFocus);

        // If focus is in no panel (in the menu, in the toolbar…) we go to the first panel: if
        // the user pressed F6 they mean "go to a panel", not "stay nowhere".
        if (start < 0)
        {
            start = delta > 0 ? -1 : 0;
        }

        for (int step = 1; step <= _panels.Count; step++)
        {
            int index = (start + (delta * step)) % _panels.Count;

            if (index < 0)
            {
                index += _panels.Count;
            }

            Panel candidate = _panels[index];

            if (candidate.IsAvailable() && candidate.Focus())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a control or its subtree has focus.
    /// </summary>
    /// <remarks>
    /// <c>IsFocused</c> is not enough: focus is almost always on an element <b>inside</b> the
    /// panel (a <c>ListBoxItem</c>, a <c>TextBox</c>). Same question as <c>:focus-within</c>.
    /// </remarks>
    public static bool ContainsFocus(Control? control) =>
        control is not null && control.IsKeyboardFocusWithin;
}
