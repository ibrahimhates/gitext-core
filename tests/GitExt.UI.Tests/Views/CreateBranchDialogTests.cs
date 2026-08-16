using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T01 — the create branch dialog's content and its GitExtensions layout.
/// </summary>
/// <remarks>
/// The dialog is <b>really built</b>: as measured in P05-T13, the properties of a control that has not
/// been built stay at their never-evaluated defaults and the test gives the wrong answer.
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
            StartPointLabel = startPoint is null ? "HEAD (tip of the current branch)" : "abcdef12 — bir commit",
            HasLocalChanges = hasLocalChanges,
        });

        return dialog;
    }

    [AvaloniaFact]
    public void Checkout_kutusu_VARSAYILAN_isaretli()
    {
        // In GitExtensions `chkCheckoutAfterCreate.Checked = true` (§ 9). Arriving unticked, the user
        // would say "I created a branch, so why am I still on the old one".
        Create().GetControl<CheckBox>("CheckoutAfterCreateBox").IsChecked.ShouldBe(true);
    }

    [AvaloniaFact]
    public void Bos_adla_OLUSTUR_kapali_ama_HATA_YAZMIYOR()
    {
        // Showing a red error to a user who has not typed anything yet is telling them off.
        CreateBranchDialog dialog = Create();

        dialog.GetControl<Button>("CreateButton").IsEnabled.ShouldBeFalse();
        dialog.GetControl<TextBlock>("ValidationText").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Gecersiz_ad_NEDENIYLE_birlikte_bildiriliyor()
    {
        // Saying "invalid name" makes it impossible to fix while typing; which rule was broken has to be
        // said.
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
        // 🔴 This name is VALID as far as git is concerned (`check-ref-format --branch` returns 0) but it
        // is translated to a name other than the one typed. Unless the user knows why it was rejected,
        // they think the application is broken.
        CreateBranchDialog dialog = Create();

        dialog.GetControl<TextBox>("BranchNameTextBox").Text = "@{-1}";

        dialog.GetControl<Button>("CreateButton").IsEnabled.ShouldBeFalse();
        dialog.GetControl<TextBlock>("ValidationText").Text!.ShouldContain("@{");
    }

    [AvaloniaFact]
    public void Kirli_agac_uyarisi_YALNIZCA_checkout_isaretliyken()
    {
        // MEASURED: `git branch` (without a checkout) ALWAYS succeeds on a dirty tree; showing a warning
        // would be a false alarm.
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
        // Saying "Create branch at this revision" without saying which revision forces the user to guess.
        Create().GetControl<TextBox>("StartPointText").Text.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public void Orphan_grubu_YERINDE_ama_devre_disi()
    {
        // § 9 rule 2: an unimplemented command is not removed, it stays disabled.
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
        // If every kind falls back to a single "invalid name" text, the UI does not help the user.
        IReadOnlyList<string> messages =
        [
            .. Enum.GetValues<BranchNameProblem>().Select(CreateBranchDialog.Describe),
        ];

        messages.Distinct().Count().ShouldBe(messages.Count);
    }
}
