using Avalonia.Controls;
using GitExt.Core;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Dala geçme diyaloğu (P06-T02).
/// </summary>
/// <remarks>
/// GitExtensions'ta karşılığı <c>FormCheckoutBranch</c>; "Local changes" grubunun sırası
/// oradan alındı (§ 9): <i>Don't change · Merge · Stash · Reset</i>.
/// </remarks>
public partial class CheckoutBranchDialog : Window
{
    private CheckoutDecision _decision = CheckoutDecision.Cancelled;

    public CheckoutBranchDialog()
    {
        InitializeComponent();

        foreach (RadioButton option in Options)
        {
            option.IsCheckedChanged += (_, _) => UpdateHint();
        }

        UpdateHint();
    }

    private RadioButton[] Options => [KeepOption, MergeOption, StashOption, DiscardOption];

    /// <summary>Diyaloğu modal açar ve kullanıcının kararını döndürür.</summary>
    internal static async Task<CheckoutDecision> ShowAsync(CheckoutRequest request, Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        CheckoutBranchDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    /// <summary>İsteği diyalog üzerine yansıtır.</summary>
    internal void Apply(CheckoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TargetText.Text = request.TargetLabel;
        TargetLabel.Text = request.IsDetached ? "Commit to check out" : "Branch to switch to";
        DetachWarning.IsVisible = request.IsDetached;

        // Temiz ağaçta dört seçenek de aynı sonucu verir; sormak yalnızca gürültü olur.
        LocalChangesGroup.IsVisible = request.HasLocalChanges;

        UpdateHint();
    }

    /// <summary>
    /// Seçilen eylemin <b>ne yapacağını</b> yazar.
    /// </summary>
    /// <remarks>
    /// Etiketler tek başına yetmiyor: ölçümde "sıfırla" takip edilmeyen dosyalara
    /// <b>dokunmuyor</b> ama takip edilen stage'lenmemiş içeriği <b>geri getirilemez</b>
    /// biçimde siliyor, "stash" ise ikisini de koruyor. Bu fark seçim anında görünmezse
    /// kullanıcı yanlış seçeneği masum sanır.
    /// </remarks>
    private void UpdateHint()
    {
        ActionHint.Text = SelectedAction switch
        {
            LocalChangesAction.Keep =>
                "The changes are carried to the new branch; if they cannot be, the checkout is refused "
                + "and nothing changes.",
            LocalChangesAction.Merge =>
                "The changes are merged into the target. If conflicts appear the checkout still "
                + "happens but the files are left unresolved.",
            LocalChangesAction.Stash =>
                "The changes are stashed along with untracked files; "
                + "nothing is lost, you can restore it later.",
            LocalChangesAction.Discard =>
                "⚠️ Changes in tracked files are DISCARDED. Untracked "
                + "files are left alone. The discarded content is backed up, and after the operation you can "
                + "alabilirsiniz.",
            _ => string.Empty,
        };
    }

    private LocalChangesAction SelectedAction
    {
        get
        {
            if (MergeOption.IsChecked == true)
            {
                return LocalChangesAction.Merge;
            }

            if (StashOption.IsChecked == true)
            {
                return LocalChangesAction.Stash;
            }

            return DiscardOption.IsChecked == true
                ? LocalChangesAction.Discard
                : LocalChangesAction.Keep;
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnCheckoutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _decision = new CheckoutDecision
        {
            Confirmed = true,

            // Grup gizliyse ağaç temiz; eylem sormanın anlamı yok.
            LocalChanges = LocalChangesGroup.IsVisible ? SelectedAction : LocalChangesAction.Keep,
        };

        Close();
    }
}
