using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P05-T15 — the confirmation dialog's content and its GitExtensions layout.
/// </summary>
/// <remarks>
/// The dialog is <b>really built</b>: as measured in P05-T13, the properties of a control that has not
/// been built stay at their never-evaluated defaults and the test gives the wrong answer.
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
        // An "Are you sure?" asked without saying what will go pushes the user towards clicking rather
        // than thinking. P05-T08 handed this preview over to T15.
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
        // The behaviour in GitExtensions: when there is no new file in the selection the box is forced off.
        ResetChangesDialog dialog = Create(Request(modified: 3, untracked: 0));

        CheckBox box = dialog.GetControl<CheckBox>("DeleteUntrackedBox");

        box.IsChecked.ShouldNotBe(true);
        box.IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void YALNIZCA_yeni_dosya_varsa_kutu_zorla_isaretli()
    {
        // Again the behaviour in GitExtensions: when deleting is the only thing that can be done, the box
        // is ticked and disabled — unticked, "Reset" would do nothing.
        ResetChangesDialog dialog = Create(Request(modified: 0, untracked: 4));

        CheckBox box = dialog.GetControl<CheckBox>("DeleteUntrackedBox");

        box.IsChecked.ShouldBe(true);
        box.IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Bastirilamayan_islemde_bir_daha_sorma_GORUNMUYOR()
    {
        // 🔴 Offering this box on an operation with no backup would open a data-loss path the user would
        // never be warned about again.
        ResetChangesDialog dialog = Create(Request(canSuppress: false));

        dialog.GetControl<CheckBox>("DoNotAskAgainBox").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Stage_lenmis_kapsami_metinde_ACIKCA_soyleniyor()
    {
        ResetChangesDialog staged = Create(Request(includesStaged: true));
        ResetChangesDialog unstagedOnly = Create(Request(includesStaged: false));

        staged.GetControl<TextBlock>("MessageText").Text!.ShouldContain("including staged");
        unstagedOnly.GetControl<TextBlock>("MessageText").Text!.ShouldContain("Unstaged changes");
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
