using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Commit ekranı (P05-T09).
/// </summary>
/// <remarks>
/// GitExtensions'ta karşılığı <c>FormCommit</c> ve <b>modal</b> açılıyor
/// (<c>GitUICommands.StartCommitDialog</c> → <c>form.ShowDialog(owner)</c>). Açılış biçimi de
/// yerleşim gibi takip ediliyor (CLAUDE.md § 9) — ayrıca modal olması doğru: kullanıcı bu
/// ekranda index'i değiştiriyor, arkadaki commit listesi eski durumu gösterirdi.
/// </remarks>
public partial class WorkingTreeWindow : Window
{
    public WorkingTreeWindow()
    {
        InitializeComponent();

        // 🔑 Taslak, pencere kapanırken KESİN olarak yazılıyor (P05-T13). Gecikmeli kayıt
        // (750 ms) henüz çalışmamış olabilir ve kullanıcının en son yazdığı satır — yani
        // tam da bırakıp gittiği yer — kaybolurdu.
        Closing += (_, _) =>
        {
            if (DataContext is WorkingTreeViewModel model)
            {
                _ = model.Message.FlushDraftAsync();
            }
        };
    }

    /// <summary>Commit ekranını sahibinin üstünde <b>modal</b> açar.</summary>
    internal static Task Open(WorkingTreeViewModel viewModel, Window owner)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(owner);

        return new WorkingTreeWindow { DataContext = viewModel }.ShowDialog(owner);
    }
}
