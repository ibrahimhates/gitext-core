using Avalonia.Controls;

namespace GitExt.UI.Views;

public partial class CommitDetailsView : UserControl
{
    /// <summary>
    /// Focuses the panel (P08-T05).
    /// </summary>
    /// <remarks>
    /// The details panel is read-only; it may have no focusable child. That is why the view itself was
    /// made focusable (<c>Focusable</c>, in XAML) — otherwise <c>F6</c> navigation would skip it and the
    /// panel's content could never be scrolled from the keyboard.
    /// </remarks>
    public bool FocusPanel() => Focus();

    public CommitDetailsView()
    {
        InitializeComponent();
    }
}
