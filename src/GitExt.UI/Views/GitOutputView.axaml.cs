using Avalonia.Controls;

namespace GitExt.UI.Views;

/// <summary>
/// Shows a git command's full output (P05-T07).
/// </summary>
/// <remarks>
/// A standalone component: it will be used both inside <see cref="GitOutputWindow"/> and, later,
/// embedded in the commit panel (P05-T12).
/// </remarks>
public partial class GitOutputView : UserControl
{
    public GitOutputView()
    {
        InitializeComponent();
    }
}
