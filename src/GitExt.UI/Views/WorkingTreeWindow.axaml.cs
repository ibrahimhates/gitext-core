using Avalonia.Controls;
using GitExt.UI.Commands;
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

                // İzleyici uygulama ömrü boyunca yaşıyor, bu pencere ise kapanıyor:
                // abonelik bırakılmazsa kapalı bir ekran için `git status` çalışmaya
                // devam ederdi (P05-T14).
                model.Dispose();
            }
        };
    }

    /// <summary>Commit ekranını sahibinin üstünde <b>modal</b> açar.</summary>
    internal static Task Open(WorkingTreeViewModel viewModel, Window owner, ICommandRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(owner);

        WorkingTreeWindow window = new() { DataContext = viewModel };

        if (registry is not null)
        {
            window.GetControl<WorkingTreeView>("Files").AttachShortcuts(registry);
        }

        // Onay diyaloğu bu pencerenin üstünde açılacak; sahip pencere ancak burada belli
        // oluyor (P05-T15).
        viewModel.Confirmer = new DialogConfirmer(window);

        return window.ShowDialog(owner);
    }

    /// <summary>
    /// Onayı gerçek bir diyalogla soran uygulama (P05-T15).
    /// </summary>
    private sealed class DialogConfirmer : IDestructiveActionConfirmer
    {
        private readonly Window _owner;

        public DialogConfirmer(Window owner) => _owner = owner;

        public Task<ResetChangesDecision> ConfirmResetAsync(ResetChangesRequest request) =>
            ResetChangesDialog.ShowAsync(request, _owner);
    }
}
