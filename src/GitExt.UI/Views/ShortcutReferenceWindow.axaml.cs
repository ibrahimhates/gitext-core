using Avalonia.Controls;
using Avalonia.Input;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The shortcut reference screen (P08-T06, <c>F1</c>).
/// </summary>
public partial class ShortcutReferenceWindow : Window
{
    public ShortcutReferenceWindow()
    {
        InitializeComponent();

        Opened += (_, _) => FilterBox.Focus();
    }

    internal static Task ShowAsync(ShortcutReferenceViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        ShortcutReferenceWindow window = new() { DataContext = model };

        return window.ShowDialog(owner);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape closes it: the way out of a read-only list must not be the mouse.
        if (e.Key is Key.Escape)
        {
            Close();
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }
}
