using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Git komut günlüğü penceresi (P06-T16).
/// </summary>
/// <remarks>
/// Modal DEĞİL: kullanıcı günlüğü açık tutup uygulamayı kullanmaya devam edebilmeli —
/// panelin amacı zaten olan biteni canlı izlemek.
/// </remarks>
public partial class CommandLogWindow : Window
{
    public CommandLogWindow()
    {
        InitializeComponent();
    }

    internal static Task ShowAsync(CommandLogViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        CommandLogWindow window = new() { DataContext = model };

        // Pencere kapanınca abonelik bırakılıyor; aksi halde günlük ViewModel'i sonsuza
        // kadar canlı tutardı.
        window.Closed += (_, _) => model.Dispose();

        window.Show(owner);

        return Task.CompletedTask;
    }
}
