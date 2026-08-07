using Avalonia.Controls;

namespace GitExt.UI.Views;

public partial class CommitDetailsView : UserControl
{
    /// <summary>
    /// Panele odaklanır (P08-T05).
    /// </summary>
    /// <remarks>
    /// Detay paneli salt okunur; odaklanabilen bir alt öğesi olmayabilir. Bu yüzden
    /// görünümün kendisi odaklanabilir yapıldı (<c>Focusable</c>, XAML'de) — aksi halde
    /// <c>F6</c> gezinmesi burayı atlar ve panelin içeriği klavyeyle hiç kaydırılamazdı.
    /// </remarks>
    public bool FocusPanel() => Focus();

    public CommitDetailsView()
    {
        InitializeComponent();
    }
}
