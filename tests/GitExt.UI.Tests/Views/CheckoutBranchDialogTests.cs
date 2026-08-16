using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T02 — the checkout branch dialog's content and its GitExtensions layout.
/// </summary>
public class CheckoutBranchDialogTests
{
    private static CheckoutBranchDialog Create(
        bool hasLocalChanges = true,
        bool isDetached = false)
    {
        CheckoutBranchDialog dialog = new();

        dialog.Apply(new CheckoutRequest
        {
            Target = isDetached ? "abcdef1234" : "ozellik",
            TargetLabel = isDetached ? "abcdef12 — bir commit" : "ozellik",
            IsDetached = isDetached,
            HasLocalChanges = hasLocalChanges,
        });

        return dialog;
    }

    [AvaloniaFact]
    public void Secenek_sirasi_GitExtensions_ile_ayni()
    {
        // `flpnlLocalOptions`: rbDontChange, rbMerge, rbStash, rbReset (§ 9).
        CheckoutBranchDialog dialog = Create();

        Panel group = (Panel)dialog.GetControl<RadioButton>("KeepOption").Parent!;

        IReadOnlyList<int> order =
        [
            group.Children.IndexOf(dialog.GetControl<RadioButton>("KeepOption")),
            group.Children.IndexOf(dialog.GetControl<RadioButton>("MergeOption")),
            group.Children.IndexOf(dialog.GetControl<RadioButton>("StashOption")),
            group.Children.IndexOf(dialog.GetControl<RadioButton>("DiscardOption")),
        ];

        order.ShouldBe(order.OrderBy(i => i).ToArray());
    }

    [AvaloniaFact]
    public void Varsayilan_secim_DOKUNMA()
    {
        // A destructive option as the default would lose data for a user who presses Enter.
        CheckoutBranchDialog dialog = Create();

        dialog.GetControl<RadioButton>("KeepOption").IsChecked.ShouldBe(true);
        dialog.GetControl<RadioButton>("DiscardOption").IsChecked.ShouldNotBe(true);
    }

    [AvaloniaFact]
    public void Temiz_agacta_yerel_degisiklik_grubu_GIZLI()
    {
        // All four options would give the same result; asking is nothing but noise.
        Create(hasLocalChanges: false)
            .GetControl<StackPanel>("LocalChangesGroup").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Kirli_agacta_grup_GORUNUYOR()
    {
        Create(hasLocalChanges: true)
            .GetControl<StackPanel>("LocalChangesGroup").IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Her_secenek_NE_YAPACAGINI_yaziyor()
    {
        // 🔴 The labels are not enough: in the measurement "reset" DOES NOT TOUCH untracked files but
        // deletes tracked unstaged content irrecoverably; "stash" preserves both. If that difference is
        // invisible at the moment of choosing, the user takes the wrong option for an innocent one.
        CheckoutBranchDialog dialog = Create();
        TextBlock hint = dialog.GetControl<TextBlock>("ActionHint");

        List<string> texts = [];

        foreach (string name in (string[])["KeepOption", "MergeOption", "StashOption", "DiscardOption"])
        {
            dialog.GetControl<RadioButton>(name).IsChecked = true;
            hint.Text.ShouldNotBeNullOrWhiteSpace();
            texts.Add(hint.Text!);
        }

        texts.Distinct().Count().ShouldBe(4);
    }

    [AvaloniaFact]
    public void Atma_secenegi_YIKICI_oldugunu_soyluyor()
    {
        CheckoutBranchDialog dialog = Create();

        dialog.GetControl<RadioButton>("DiscardOption").IsChecked = true;

        string hint = dialog.GetControl<TextBlock>("ActionHint").Text!;

        hint.ShouldContain("DISCARDED");

        // That it is backed up has to be said too: destructive, but with a way back (the P05-T15 rule).
        hint.ShouldContain("backed up", Case.Insensitive);
    }

    [AvaloniaFact]
    public void Detached_gecis_ACIKCA_uyariyor()
    {
        // Commits made on a detached HEAD appear on no branch; not saying so costs the user work.
        Create(isDetached: true).GetControl<TextBlock>("DetachWarning").IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Dala_gecerken_detached_uyarisi_YOK()
    {
        Create(isDetached: false).GetControl<TextBlock>("DetachWarning").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Hedef_GOSTERILIYOR()
    {
        Create().GetControl<TextBox>("TargetText").Text.ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public void Dugme_sirasi_GitExtensions_ile_ayni()
    {
        CheckoutBranchDialog dialog = Create();

        Panel buttons = (Panel)dialog.GetControl<Button>("CancelButton").Parent!;

        int cancel = buttons.Children.IndexOf(dialog.GetControl<Button>("CancelButton"));
        int ok = buttons.Children.IndexOf(dialog.GetControl<Button>("CheckoutButton"));

        cancel.ShouldBeLessThan(ok);
    }
}
