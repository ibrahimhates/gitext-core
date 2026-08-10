using Avalonia.Controls;
using GitExt.Core.Diagnostics;
using GitExt.UI.Diagnostics;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Performans teşhis penceresi (P09-T03).
/// </summary>
/// <remarks>
/// Modal DEĞİL: teşhisin amacı uygulamayı kullanırken izlemek. Modal olsaydı, ölçmek
/// istediğimiz işi yapmak imkânsız olurdu.
/// </remarks>
public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Pencereyi açar ve kare ölçümünü <paramref name="owner"/> üzerinden bağlar.
    /// </summary>
    /// <remarks>
    /// Kare ölçümü teşhis penceresine değil <b>ana pencereye</b> bağlanıyor: ölçmek
    /// istediğimiz şey grafiğin kaydırılması, teşhis panelinin kendi çizimi değil.
    /// </remarks>
    internal static Task ShowAsync(IPerformanceDiagnostics diagnostics, Window owner)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(owner);

        FrameTimeMonitor frames = new(owner);
        DiagnosticsViewModel model = new(diagnostics, frames);

        DiagnosticsWindow window = new() { DataContext = model };

        // Kapanışta zamanlayıcı ve kare ölçümü bırakılıyor; aksi halde pencere kapansa
        // bile saniyede bir tetiklenen bir zamanlayıcı arka planda çalışmaya devam ederdi.
        window.Closed += (_, _) => model.Dispose();

        window.Show(owner);

        return Task.CompletedTask;
    }
}
