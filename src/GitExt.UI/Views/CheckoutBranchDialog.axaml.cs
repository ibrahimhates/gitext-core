using Avalonia.Controls;
using GitExt.Core;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

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
        TargetLabel.Text = request.IsDetached ? Loc.T("checkout_branch_dialog.axaml.commit_to_check_out") : Loc.T("checkout_branch_dialog.axaml.branch_to_switch_to");
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
                Loc.T("checkout_branch_dialog.axaml.the_changes_are_carried_to_the_new_branch_if")
                + Loc.T("checkout_branch_dialog.axaml.and_nothing_changes"),
            LocalChangesAction.Merge =>
                Loc.T("checkout_branch_dialog.axaml.the_changes_are_merged_into_the_target_if_co")
                + Loc.T("checkout_branch_dialog.axaml.happens_but_the_files_are_left_unresolved"),
            LocalChangesAction.Stash =>
                Loc.T("checkout_branch_dialog.axaml.the_changes_are_stashed_along_with_untracked")
                + Loc.T("checkout_branch_dialog.axaml.nothing_is_lost_you_can_restore_it_later"),
            LocalChangesAction.Discard =>
                Loc.T("checkout_branch_dialog.axaml.changes_in_tracked_files_are_discarded_untra")
                + Loc.T("checkout_branch_dialog.axaml.files_are_left_alone_the_discarded_content_i")
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
