using Avalonia.Input;
using GitExt.UI.Localization;

namespace GitExt.UI.Commands;

/// <summary>
/// Whether a gesture can be assigned to a command (P08-T03).
/// </summary>
public enum GestureRejection
{
    /// <summary>Atanabilir.</summary>
    None,

    /// <summary>The key is only a modifier (Ctrl/Alt/Shift/Meta) — not a gesture.</summary>
    ModifierOnly,

    /// <summary>
    /// A letter/digit/arrow without a modifier cannot be assigned to a global command.
    /// </summary>
    BareKeyInGlobalContext,

    /// <summary>A key the application cannot handle.</summary>
    Unsupported,
}

/// <summary>
/// Filters the gestures the user is allowed to assign (P08-T03).
/// </summary>
/// <remarks>
/// <para>
/// Every restriction came out of the P08-T00 measurements; none of them are arbitrary.
/// </para>
/// <para>
/// 🔴 <b>Bare keys are forbidden in the global context.</b> MEASURED in M11+M12: a window-level
/// gesture takes the key from the focused control <b>unconditionally</b> and the command runs
/// even if the control sets <c>Handled=true</c>. If a global <c>S</c> were assignable the user
/// could never type "s" in a text box again — and never see why, because no error appears.
/// </para>
/// </remarks>
public static class GestureValidation
{
    /// <summary>Keys that do not count as a gesture when pressed on their own.</summary>
    private static readonly Key[] ModifierKeys =
    [
        Key.LeftCtrl, Key.RightCtrl,
        Key.LeftAlt, Key.RightAlt,
        Key.LeftShift, Key.RightShift,
        Key.LWin, Key.RWin,
        Key.System,
        Key.None,
    ];

    public static GestureRejection Validate(KeyGesture gesture, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        if (ModifierKeys.Contains(gesture.Key))
        {
            return GestureRejection.ModifierOnly;
        }

        if (gesture.Key is Key.Cancel or Key.Clear)
        {
            return GestureRejection.Unsupported;
        }

        if (context.HasFlag(CommandContext.Global)
            && gesture.KeyModifiers == KeyModifiers.None
            && !IsSafeBareKey(gesture.Key))
        {
            return GestureRejection.BareKeyInGlobalContext;
        }

        return GestureRejection.None;
    }

    /// <summary>
    /// Keys that can be global even without a modifier.
    /// </summary>
    /// <remarks>
    /// Function keys and <c>Escape</c> produce no text, so they cannot block typing —
    /// <c>F5</c> and <c>F1</c> are already in the scheme this way.
    /// </remarks>
    private static bool IsSafeBareKey(Key key) =>
        key is >= Key.F1 and <= Key.F24 or Key.Escape or Key.Pause or Key.PrintScreen;

    /// <summary>The explanation of the rejection reason shown to the user.</summary>
    public static string Describe(GestureRejection rejection) => rejection switch
    {
        GestureRejection.ModifierOnly =>
            Loc.T("gesture_validation.only_a_modifier_key_was_pressed_press_anothe"),
        GestureRejection.BareKeyInGlobalContext =>
            Loc.T("gesture_validation.a_key_without_a_modifier_cannot_be_assigned_")
            + Loc.T("gesture_validation.boxes_too_and_you_would_not_be_able_to_type_")
            + Loc.T("gesture_validation.function_keys_are_the_exception"),
        GestureRejection.Unsupported =>
            Loc.T("gesture_validation.this_key_cannot_be_used_as_a_shortcut"),
        _ => "",
    };
}
