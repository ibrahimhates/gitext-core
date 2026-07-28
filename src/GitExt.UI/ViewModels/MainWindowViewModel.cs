using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Ana pencerenin ViewModel'ı.
/// </summary>
/// <remarks>
/// Faz 01'de yalnızca iskelet doğrulaması yapar: metin code-behind'dan değil binding ile gelir,
/// böylece MVVM zinciri (View → ViewModel → DI) ilk günden kurulmuş olur.
/// Gerçek içerik Faz 03'te (commit grafiği) gelecek.
/// </remarks>
public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Hello World";

    [ObservableProperty]
    public partial string Subtitle { get; set; } =
        "gitext-core — iskelet hazır. Sıradaki: Faz 02, Git çekirdek katmanı.";
}
