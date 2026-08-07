using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Kısayol düzenleme ekranı (P08-T03).
/// </summary>
/// <remarks>
/// Tuş yakalama kod arkasında olmak zorunda: yakalanan şey bir <see cref="KeyGesture"/> ve
/// onu üreten ham tuş olayı yalnızca burada görülüyor. Karar (atanabilir mi, çakışıyor mu)
/// ViewModel'da ve orada ayrıca test ediliyor.
/// </remarks>
public partial class ShortcutSettingsView : UserControl
{
    public ShortcutSettingsView()
    {
        InitializeComponent();

        // 🔴 TÜNEL fazı şart, iki ayrı sebeple:
        //   1. Yakalama sırasında basılan tuş önce listeye ulaşırsa `↓` seçimi kaydırır,
        //      `Space` satırı seçer ve kullanıcı o tuşları HİÇ atayamaz.
        //   2. Odak "Kısayol ata…" düğmesinde kalıyor; kabarma fazında `Space` ve `Enter`
        //      düğmeye gider ve yine atanamazdı.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private ShortcutSettingsViewModel? Model => DataContext as ShortcutSettingsViewModel;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { IsCapturing: true } model)
        {
            return;
        }

        // Escape yakalamadan çıkar; atanabilir bir kısayol olarak okunsaydı vazgeçmenin
        // klavyeyle yolu kalmazdı.
        if (e.Key is Key.Escape)
        {
            model.CancelCaptureCommand.Execute(null);
            e.Handled = true;

            return;
        }

        model.TryApplyCapture(new KeyGesture(e.Key, e.KeyModifiers));

        // Sonuç ne olursa olsun tüketiliyor: yakalama modundayken hiçbir tuş listeye
        // veya kısayol dağıtımına gitmemeli.
        e.Handled = true;
    }

}
