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
        TargetLabel.Text = request.IsDetached ? "Geçilecek commit" : "Geçilecek dal";
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
                "Değişiklikler yeni dala taşınmaya çalışılır; taşınamazsa geçiş reddedilir "
                + "ve hiçbir şey değişmez.",
            LocalChangesAction.Merge =>
                "Değişiklikler hedefe birleştirilmeye çalışılır. Çakışma çıkarsa geçiş yine "
                + "yapılır ama dosyalar çözülmemiş hâlde kalır.",
            LocalChangesAction.Stash =>
                "Değişiklikler takip edilmeyen dosyalarla birlikte bir stash'e alınır; "
                + "hiçbir şey kaybolmaz, sonradan geri alabilirsiniz.",
            LocalChangesAction.Discard =>
                "⚠️ Takip edilen dosyalardaki değişiklikler ATILIR. Takip edilmeyen "
                + "dosyalara dokunulmaz. Atılan içerik yedeklenir, işlemden sonra geri "
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
