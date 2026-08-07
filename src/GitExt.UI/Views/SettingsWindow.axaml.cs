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

        // Git ayarları pencere açılmadan önce okunuyor: sonrasına kalsaydı kullanıcı bir an
        // için BOŞ alanlar görür ve "ayarlanmamış" sanıp üstüne yazardı.
        await model.LoadGitAsync();

        await window.ShowDialog(owner);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 🔴 Escape yalnızca kısayol yakalama YOKKEN pencereyi kapatır. Yakalama sırasında
        // Escape "vazgeç" anlamına geliyor (ShortcutSettingsView); burada da kapatsaydık
        // kullanıcı yakalamadan çıkarken pencereyi de kapatmış olurdu.
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
