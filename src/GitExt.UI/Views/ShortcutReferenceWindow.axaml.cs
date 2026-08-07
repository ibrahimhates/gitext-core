using Avalonia.Controls;
using Avalonia.Input;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Kısayol referans ekranı (P08-T06, <c>F1</c>).
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
        // Escape kapatır: salt okunur bir listeden çıkmanın yolu fare olmamalı.
        if (e.Key is Key.Escape)
        {
            Close();
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }
}
