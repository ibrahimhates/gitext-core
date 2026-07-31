using Avalonia.Controls;

namespace GitExt.UI.Views;

/// <summary>
/// Bir git komutunun tam çıktısını gösterir (P05-T07).
/// </summary>
/// <remarks>
/// Bağımsız bir bileşen: hem <see cref="GitOutputWindow"/> içinde hem de ileride commit
/// panelinin (P05-T12) içine gömülü olarak kullanılacak.
/// </remarks>
public partial class GitOutputView : UserControl
{
    public GitOutputView()
    {
        InitializeComponent();
    }
}
