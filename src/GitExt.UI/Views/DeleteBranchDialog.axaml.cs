using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Dal silme onayı (P06-T03).
/// </summary>
/// <remarks>
/// İki turlu akış: ilk turda sıradan bir onay sorulur. git dalı <b>birleştirilmemiş</b>
/// diye reddederse diyalog ikinci kez, bu kez <b>kurtarma komutuyla</b> açılır.
/// Birleşmişliği önden hesaplamıyoruz çünkü ölçüldü — <c>git branch -d</c> upstream'ine
/// birleşmiş dalı da siliyor ve kendi hesabımız yanlış alarm üretirdi.
/// </remarks>
public partial class DeleteBranchDialog : Window
{
    private DeleteBranchDecision _decision = DeleteBranchDecision.Cancelled;
    private bool _isUnmerged;

    public DeleteBranchDialog()
    {
        InitializeComponent();

        ForceBox.IsCheckedChanged += (_, _) => UpdateButton();

        UpdateButton();
    }

    internal static async Task<DeleteBranchDecision> ShowAsync(
        DeleteBranchRequest request,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        DeleteBranchDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    internal void Apply(DeleteBranchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _isUnmerged = request.IsUnmerged;

        MessageText.Text = request.IsUnmerged
            ? $"Could not delete branch '{request.Name}'."
            : $"Are you sure you want to delete branch '{request.Name}'?";

        UnmergedPanel.IsVisible = request.IsUnmerged;

        // 🔴 Kurtarmanın tek güvenilir yolu bu komut: ölçüldü, silinen dalın KENDİ reflog'u
        // da siliniyor ve dal bu çalışma ağacında hiç checkout edilmemişse HEAD
        // reflog'unda da iz yok.
        RecoveryCommand.Text = request.LastCommitId is { Length: > 0 } id
            ? $"git branch {request.Name} {id}"
            : string.Empty;

        UpdateButton();
    }

    /// <summary>
    /// Birleştirilmemiş dalda <b>Sil</b>, kutu işaretlenmeden açılmıyor.
    /// </summary>
    /// <remarks>
    /// Onay kutusu yeterli, ayrı bir diyalog gerekmiyor: kurtarma komutu ekranda duruyor,
    /// yani işlem geri döndürülebilir (P05-T15'in "diyalog yalnızca geri getirilemeyen
    /// işlemler için" kuralı).
    /// </remarks>
    private void UpdateButton() =>
        DeleteButton.IsEnabled = !_isUnmerged || ForceBox.IsChecked == true;

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isUnmerged && ForceBox.IsChecked != true)
        {
            return;
        }

        _decision = new DeleteBranchDecision { Confirmed = true, Force = _isUnmerged };

        Close();
    }
}
