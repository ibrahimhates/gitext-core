using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The shortcut editing screen (P08-T03).
/// </summary>
/// <remarks>
/// The key capture has to live in the code-behind: what is captured is a <see cref="KeyGesture"/> and
/// the raw key event producing it is only visible here. The decision (can it be assigned, does it
/// conflict) lives in the ViewModel and is tested separately there.
/// </remarks>
public partial class ShortcutSettingsView : UserControl
{
    public ShortcutSettingsView()
    {
        InitializeComponent();

        // 🔴 The TUNNEL phase is essential, for two separate reasons:
        //   1. If a key pressed during capture reaches the list first, `↓` moves the selection and
        //      `Space` selects a row, and the user can NEVER assign those keys.
        //   2. Focus stays on the "Assign shortcut…" button; in the bubbling phase `Space` and `Enter`
        //      go to the button and again could not be assigned.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private ShortcutSettingsViewModel? Model => DataContext as ShortcutSettingsViewModel;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { IsCapturing: true } model)
        {
            return;
        }

        // Escape leaves capture; read as an assignable shortcut, there would be no keyboard way to back
        // out.
        if (e.Key is Key.Escape)
        {
            model.CancelCaptureCommand.Execute(null);
            e.Handled = true;

            return;
        }

        model.TryApplyCapture(new KeyGesture(e.Key, e.KeyModifiers));

        // It is consumed whatever the outcome: while in capture mode no key may reach the list or the
        // shortcut dispatch.
        e.Handled = true;
    }

}
