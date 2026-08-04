using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T01 — dal oluşturma diyaloğunun içeriği ve GitExtensions yerleşimi.
/// </summary>
/// <remarks>
/// Diyalog <b>gerçekten kuruluyor</b>: P05-T13'te ölçüldüğü gibi, kurulmamış bir denetimin
/// özellikleri değerlendirilmemiş varsayılanlarında kalır ve test yanlış cevap verir.
/// </remarks>
public class CreateBranchDialogTests
{
    private static CreateBranchDialog Create(
        string? startPoint = "abcdef1234",
        bool hasLocalChanges = false)
    {
        CreateBranchDialog dialog = new();

        dialog.Apply(new CreateBranchRequest
        {
            StartPoint = startPoint,
            StartPointLabel = startPoint is null ? "HEAD (mevcut dalın ucu)" : "abcdef12 — bir commit",
            HasLocalChanges = hasLocalChanges,
        });

        return dialog;
    }

    [AvaloniaFact]
    public void Checkout_kutusu_VARSAYILAN_isaretli()
    {
        // GitExtensions'ta `chkCheckoutAfterCreate.Checked = true` (§ 9). Kapalı gelseydi
        // kullanıcı "dal oluşturdum ama neden hâlâ eski daldayım" derdi.
        Create().GetControl<CheckBox>("CheckoutAfterCreateBox").IsChecked.ShouldBe(true);
    }

    [AvaloniaFact]
    public void Bos_adla_OLUSTUR_kapali_ama_HATA_YAZMIYOR()
    {
        // Henüz bir şey yazmamış kullanıcıya kırmızı hata göstermek onu azarlamaktır.
        CreateBranchDialog dialog = Create();

        dialog.GetControl<Button>("CreateButton").IsEnabled.ShouldBeFalse();
        dialog.GetControl<TextBlock>("ValidationText").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Gecersiz_ad_NEDENIYLE_birlikte_bildiriliyor()
    {
        // "Geçersiz ad" demek yazarken düzeltmeyi imkânsız kılar; hangi kuralın kırıldığı
        // söylenmeli.
        CreateBranchDialog dialog = Create();

        dialog.GetControl<TextBox>("BranchNameTextBox").Text = "iki kelime";

        dialog.GetControl<Button>("CreateButton").IsEnabled.ShouldBeFalse();

        TextBlock validation = dialog.GetControl<TextBlock>("ValidationText");
        validation.IsVisible.ShouldBeTrue();
        validation.Text.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public void Gecerli_ad_yazilinca_OLUSTUR_aciliyor()
    {
        CreateBranchDialog dialog = Create();

        dialog.GetControl<TextBox>("BranchNameTextBox").Text = "ozellik/yeni";

        dialog.GetControl<Button>("CreateButton").IsEnabled.ShouldBeTrue();
        dialog.GetControl<TextBlock>("ValidationText").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Revizyon_sozdizimi_ACIKCA_aciklaniyor()
    {
        // 🔴 Bu ad git'e göre GEÇERLİ (`check-ref-format --branch` 0 döndürüyor) ama
        // yazılandan başka bir ada çevriliyor. Kullanıcı neden reddedildiğini bilmezse
        // uygulamanın bozuk olduğunu düşünür.
        CreateBranchDialog dialog = Create();

        dialog.GetControl<TextBox>("BranchNameTextBox").Text = "@{-1}";

        dialog.GetControl<Button>("CreateButton").IsEnabled.ShouldBeFalse();
        dialog.GetControl<TextBlock>("ValidationText").Text!.ShouldContain("@{");
    }

    [AvaloniaFact]
    public void Kirli_agac_uyarisi_YALNIZCA_checkout_isaretliyken()
    {
        // ÖLÇÜLDÜ: `git branch` (checkout'suz) kirli ağaçta HER ZAMAN başarılı; uyarı
        // göstermek yanlış alarm olurdu.
        CreateBranchDialog dialog = Create(hasLocalChanges: true);

        dialog.GetControl<TextBlock>("DirtyWarning").IsVisible.ShouldBeTrue();

        dialog.GetControl<CheckBox>("CheckoutAfterCreateBox").IsChecked = false;

        dialog.GetControl<TextBlock>("DirtyWarning").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Temiz_agacta_uyari_YOK()
    {
        Create(hasLocalChanges: false).GetControl<TextBlock>("DirtyWarning").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Baslangic_noktasi_GOSTERILIYOR()
    {
        // "Bu revizyonda dal oluştur" derken hangi revizyon olduğunu söylememek,
        // kullanıcıyı tahmine zorlar.
        Create().GetControl<TextBox>("StartPointText").Text.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public void Orphan_grubu_YERINDE_ama_devre_disi()
    {
        // § 9 kural 2: uygulanmamış komut kaldırılmaz, devre dışı durur.
        Create().GetControl<StackPanel>("OrphanGroup").IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Dugme_sirasi_GitExtensions_ile_ayni()
    {
        CreateBranchDialog dialog = Create();

        Panel buttons = (Panel)dialog.GetControl<Button>("CancelButton").Parent!;

        int cancel = buttons.Children.IndexOf(dialog.GetControl<Button>("CancelButton"));
        int create = buttons.Children.IndexOf(dialog.GetControl<Button>("CreateButton"));

        cancel.ShouldBeLessThan(create);
    }

    [AvaloniaFact]
    public void Her_sorun_turu_KENDI_mesajini_veriyor()
    {
        // Tüm türler tek bir "geçersiz ad" metnine düşerse arayüz kullanıcıya yardım etmez.
        IReadOnlyList<string> messages =
        [
            .. Enum.GetValues<BranchNameProblem>().Select(CreateBranchDialog.Describe),
        ];

        messages.Distinct().Count().ShouldBe(messages.Count);
    }
}
