using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P05-T15 — onay diyaloğunun içeriği ve GitExtensions yerleşimi.
/// </summary>
/// <remarks>
/// Diyalog <b>gerçekten kuruluyor</b>: P05-T13'te ölçüldüğü gibi, kurulmamış bir denetimin
/// özellikleri değerlendirilmemiş varsayılanlarında kalır ve test yanlış cevap verir.
/// </remarks>
public class ResetChangesDialogTests
{
    private static ResetChangesRequest Request(
        int modified = 2,
        int untracked = 1,
        bool includesStaged = false,
        bool canSuppress = true)
    {
        return new ResetChangesRequest
        {
            ModifiedPaths = [.. Enumerable.Range(1, modified)
                .Select(i => RepositoryPath.Parse($"src/dosya{i}.cs"))],
            UntrackedPaths = [.. Enumerable.Range(1, untracked)
                .Select(i => RepositoryPath.Parse($"yeni{i}.cs"))],
            IncludesStaged = includesStaged,
            CanSuppress = canSuppress,
        };
    }

    private static ResetChangesDialog Create(ResetChangesRequest request)
    {
        ResetChangesDialog dialog = new();
        dialog.Apply(request);
        return dialog;
    }

    [AvaloniaFact]
    public void Etkilenecek_dosyalar_GERCEKTEN_listeleniyor()
    {
        // "Emin misiniz?" sorusu neyin gideceğini söylemeden sorulduğunda kullanıcıyı
        // düşünmeye değil tıklamaya yönlendirir. P05-T08 bu önizlemeyi T15'e devretmişti.
        ResetChangesDialog dialog = Create(Request(modified: 2, untracked: 1));

        IReadOnlyList<string> items =
            [.. dialog.GetControl<ItemsControl>("AffectedList").ItemsSource!.Cast<string>()];

        items.Count.ShouldBe(3);
        items.ShouldContain("M  src/dosya1.cs");
        items.ShouldContain("?  yeni1.cs");
    }

    [AvaloniaFact]
    public void Cok_uzun_liste_kirpiliyor_ama_SAYI_soyleniyor()
    {
        ResetChangesDialog dialog = Create(Request(modified: 500, untracked: 0));

        IReadOnlyList<string> items =
            [.. dialog.GetControl<ItemsControl>("AffectedList").ItemsSource!.Cast<string>()];

        items.Count.ShouldBe(ResetChangesDialog.PreviewLimit + 1);
        items[^1].ShouldContain("460");
    }

    [AvaloniaFact]
    public void Yeni_dosya_yoksa_silme_kutusu_kapali_ve_devre_disi()
    {
        // GitExtensions'taki davranış: seçimde yeni dosya yoksa kutu zorla kapalı.
        ResetChangesDialog dialog = Create(Request(modified: 3, untracked: 0));

        CheckBox box = dialog.GetControl<CheckBox>("DeleteUntrackedBox");

        box.IsChecked.ShouldNotBe(true);
        box.IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void YALNIZCA_yeni_dosya_varsa_kutu_zorla_isaretli()
    {
        // Yine GitExtensions'taki davranış: yapılabilecek tek şey silmek olduğunda kutu
        // işaretli ve devre dışı — kapalıyken "Reset" hiçbir şey yapmazdı.
        ResetChangesDialog dialog = Create(Request(modified: 0, untracked: 4));

        CheckBox box = dialog.GetControl<CheckBox>("DeleteUntrackedBox");

        box.IsChecked.ShouldBe(true);
        box.IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Bastirilamayan_islemde_bir_daha_sorma_GORUNMUYOR()
    {
        // 🔴 Yedeği olmayan bir işlemde bu kutuyu sunmak, kullanıcının bir daha asla
        // uyarılmayacağı bir veri kaybı yolunu açmak olurdu.
        ResetChangesDialog dialog = Create(Request(canSuppress: false));

        dialog.GetControl<CheckBox>("DoNotAskAgainBox").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Stage_lenmis_kapsami_metinde_ACIKCA_soyleniyor()
    {
        ResetChangesDialog staged = Create(Request(includesStaged: true));
        ResetChangesDialog unstagedOnly = Create(Request(includesStaged: false));

        staged.GetControl<TextBlock>("MessageText").Text!.ShouldContain("stage'lenmiş");
        unstagedOnly.GetControl<TextBlock>("MessageText").Text!.ShouldContain("stage'lenmemiş");
    }

    [AvaloniaFact]
    public void Dugme_sirasi_GitExtensions_ile_ayni()
    {
        // `flowLayoutPanel1`: btnCancel, btnReset (§ 9).
        ResetChangesDialog dialog = Create(Request());

        Panel buttons = (Panel)dialog.GetControl<Button>("CancelButton").Parent!;

        int cancel = buttons.Children.IndexOf(dialog.GetControl<Button>("CancelButton"));
        int reset = buttons.Children.IndexOf(dialog.GetControl<Button>("ResetButton"));

        cancel.ShouldBeLessThan(reset);
    }
}
