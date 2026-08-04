using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T03 — dal yeniden adlandırma ve silme diyalogları.
/// </summary>
public class BranchEditDialogTests
{
    private static RenameBranchDialog Rename(string current = "ozellik")
    {
        RenameBranchDialog dialog = new();
        dialog.Apply(new RenameBranchRequest { CurrentName = current });

        return dialog;
    }

    private static DeleteBranchDialog Delete(bool unmerged = false, string? lastCommit = null)
    {
        DeleteBranchDialog dialog = new();

        dialog.Apply(new DeleteBranchRequest
        {
            Name = "ozellik",
            IsUnmerged = unmerged,
            LastCommitId = lastCommit ?? (unmerged ? "1234567890abcdef" : null),
        });

        return dialog;
    }

    // ---- Yeniden adlandırma ----

    [AvaloniaFact]
    public void Yeni_ad_kutusu_MEVCUT_adla_dolu_geliyor()
    {
        // Yeniden adlandırma çoğunlukla küçük bir düzeltme; sıfırdan yazdırmak gereksiz iş.
        Rename("ozellik/eski").GetControl<TextBox>("NewNameTextBox").Text.ShouldBe("ozellik/eski");
    }

    [AvaloniaFact]
    public void Gecersiz_yeni_ad_NEDENIYLE_bildiriliyor()
    {
        RenameBranchDialog dialog = Rename();

        dialog.GetControl<TextBox>("NewNameTextBox").Text = "iki kelime";

        dialog.GetControl<Button>("RenameButton").IsEnabled.ShouldBeFalse();
        dialog.GetControl<TextBlock>("ValidationText").Text.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public void Gecerli_yeni_ad_dugmeyi_ACIYOR()
    {
        RenameBranchDialog dialog = Rename();

        dialog.GetControl<TextBox>("NewNameTextBox").Text = "ozellik/yeni";

        dialog.GetControl<Button>("RenameButton").IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Yeniden_adlandirmada_dugme_sirasi_GitExtensions_ile_ayni()
    {
        RenameBranchDialog dialog = Rename();

        Panel buttons = (Panel)dialog.GetControl<Button>("CancelButton").Parent!;

        buttons.Children.IndexOf(dialog.GetControl<Button>("CancelButton"))
            .ShouldBeLessThan(buttons.Children.IndexOf(dialog.GetControl<Button>("RenameButton")));
    }

    // ---- Silme ----

    [AvaloniaFact]
    public void Merge_edilmis_dalda_zorlama_paneli_GIZLI()
    {
        // Yanlış alarm: birleştirilmiş dalda "commit'ler kaybolacak" demek, uyarıyı
        // okunmaz hâle getirir.
        DeleteBranchDialog dialog = Delete(unmerged: false);

        dialog.GetControl<StackPanel>("UnmergedPanel").IsVisible.ShouldBeFalse();
        dialog.GetControl<Button>("DeleteButton").IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Birlestirilmemis_dalda_SIL_kutu_isaretlenmeden_KAPALI()
    {
        DeleteBranchDialog dialog = Delete(unmerged: true);

        dialog.GetControl<StackPanel>("UnmergedPanel").IsVisible.ShouldBeTrue();
        dialog.GetControl<Button>("DeleteButton").IsEnabled.ShouldBeFalse();

        dialog.GetControl<CheckBox>("ForceBox").IsChecked = true;

        dialog.GetControl<Button>("DeleteButton").IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Kurtarma_KOMUTU_calistirilabilir_bicimde_gosteriliyor()
    {
        // 🔴 ÖLÇÜLDÜ: silinen dalın KENDİ reflog'u da siliniyor ve dal bu çalışma ağacında
        // hiç checkout edilmemişse HEAD reflog'unda da iz yok — "reflog'dan geri
        // alabilirsiniz" demek her durumda DOĞRU DEĞİL. Komutun kendisi veriliyor.
        DeleteBranchDialog dialog = Delete(unmerged: true, lastCommit: "1234567890abcdef");

        string command = dialog.GetControl<TextBox>("RecoveryCommand").Text!;

        command.ShouldStartWith("git branch ");
        command.ShouldContain("ozellik");
        command.ShouldContain("1234567890abcdef");
    }

    [AvaloniaFact]
    public void Kurtarma_kutusu_SALT_OKUNUR()
    {
        // Kullanıcının yanlışlıkla düzenleyip kopyaladığı bir komut, kurtarma vaadini
        // sessizce boşa çıkarırdı.
        Delete(unmerged: true).GetControl<TextBox>("RecoveryCommand").IsReadOnly.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Reflog_un_de_silindigi_SOYLENIYOR()
    {
        // Kullanıcı komutu not almazsa gerçekten kaybedebilir; sebebi yazılmalı.
        Delete(unmerged: true).GetControl<TextBlock>("RecoveryNote").Text!.ShouldContain("reflog");
    }

    [AvaloniaFact]
    public void Silmede_dugme_sirasi_GitExtensions_ile_ayni()
    {
        DeleteBranchDialog dialog = Delete();

        Panel buttons = (Panel)dialog.GetControl<Button>("CancelButton").Parent!;

        buttons.Children.IndexOf(dialog.GetControl<Button>("CancelButton"))
            .ShouldBeLessThan(buttons.Children.IndexOf(dialog.GetControl<Button>("DeleteButton")));
    }
}
