using Avalonia.Controls;
using GitExt.Core.Diagnostics;
using GitExt.UI.Diagnostics;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The performance diagnostics window (P09-T03).
/// </summary>
/// <remarks>
/// NOT modal: the point of the diagnostics is to watch while using the application. Modal, it would be
/// impossible to do the very work we want to measure.
/// </remarks>
public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens the window and attaches the frame measurement through <paramref name="owner"/>.
    /// </summary>
    /// <remarks>
    /// The frame measurement attaches to the <b>main window</b> and not to the diagnostics window: what
    /// we want to measure is scrolling the graph, not the diagnostics panel drawing itself.
    /// </remarks>
    internal static Task ShowAsync(IPerformanceDiagnostics diagnostics, Window owner)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(owner);

        FrameTimeMonitor frames = new(owner);
        DiagnosticsViewModel model = new(diagnostics, frames);

        DiagnosticsWindow window = new() { DataContext = model };

        // The timer and the frame measurement are released on close; otherwise a timer firing once a
        // second would carry on running in the background even with the window closed.
        window.Closed += (_, _) => model.Dispose();

        window.Show(owner);

        return Task.CompletedTask;
    }
}
