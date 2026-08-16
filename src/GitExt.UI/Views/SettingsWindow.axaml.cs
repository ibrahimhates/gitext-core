using Avalonia.Controls;
using Avalonia.Input;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Ayarlar penceresi (P08-T15).
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    internal static async Task ShowAsync(SettingsViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        SettingsWindow window = new() { DataContext = model };

        // The git settings are read before the window opens: left until afterwards, the user would see
        // EMPTY fields for a moment, think they were unset and overwrite them.
        await model.LoadGitAsync();

        await window.ShowDialog(owner);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 🔴 Escape closes the window only when NO shortcut capture is under way. During a capture,
        // Escape means "cancel" (ShortcutSettingsView); had it closed the window here too, backing out
        // of a capture would also close the window.
        if (e.Key is Key.Escape && !IsCapturingShortcut)
        {
            Close();
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }

    private bool IsCapturingShortcut =>
        DataContext is SettingsViewModel { Shortcuts.IsCapturing: true };
}
