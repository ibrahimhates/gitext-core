using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P14-T05 — remembering the local-changes action, as GitExtensions' <c>FormCheckoutBranch</c> does.
/// </summary>
/// <remarks>
/// The rule worth testing is the refusal: <c>rbReset_CheckedChanged</c> disables the box while
/// "discard" is selected. A remembered "discard" would throw away uncommitted work on every later
/// branch switch, without asking again.
/// </remarks>
public class CheckoutDefaultActionTests
{
    private static CheckoutRequest Dirty(LocalChangesAction defaultAction = LocalChangesAction.Keep) =>
        new()
        {
            Target = "feature",
            TargetLabel = "feature",
            HasLocalChanges = true,
            DefaultAction = defaultAction,
        };

    [AvaloniaFact]
    public void Hatirlanan_eylem_secili_acilir()
    {
        CheckoutBranchDialog dialog = new();

        dialog.Apply(Dirty(LocalChangesAction.Stash));

        dialog.SelectedActionForTest.ShouldBe(LocalChangesAction.Stash);
    }

    [AvaloniaFact]
    public void Varsayilan_keep_ile_acilir()
    {
        CheckoutBranchDialog dialog = new();

        dialog.Apply(Dirty());

        dialog.SelectedActionForTest.ShouldBe(LocalChangesAction.Keep);
    }

    [AvaloniaFact]
    public void Sil_seciliyken_varsayilan_yap_kutusu_kapali()
    {
        CheckoutBranchDialog dialog = new();

        dialog.Apply(Dirty());

        dialog.SetOptionForTest(LocalChangesAction.Stash);
        dialog.DefaultBoxEnabledForTest.ShouldBeTrue();

        dialog.SetOptionForTest(LocalChangesAction.Discard);
        dialog.DefaultBoxEnabledForTest.ShouldBeFalse();
        dialog.DefaultBoxCheckedForTest.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Sile_gecince_isaretli_kutu_kaldirilir()
    {
        CheckoutBranchDialog dialog = new();

        dialog.Apply(Dirty());

        dialog.SetOptionForTest(LocalChangesAction.Merge);
        dialog.DefaultBoxCheckedForTest = true;

        dialog.SetOptionForTest(LocalChangesAction.Discard);

        dialog.DefaultBoxCheckedForTest.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Temiz_agacta_yerel_degisiklik_grubu_gizli()
    {
        CheckoutBranchDialog dialog = new();

        dialog.Apply(new CheckoutRequest
        {
            Target = "feature",
            TargetLabel = "feature",
            HasLocalChanges = false,
        });

        dialog.LocalChangesVisibleForTest.ShouldBeFalse();
    }
}
