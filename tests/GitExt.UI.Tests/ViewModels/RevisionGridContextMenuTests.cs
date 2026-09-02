using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P14-T02 — the commit list's context menu, against GitExtensions itself.
/// </summary>
/// <remarks>
/// <para>
/// The order is not a matter of taste: it is <c>RevisionGridControl.Designer.cs</c>'s
/// <c>mainContextMenu.Items.AddRange</c>, copied here as a literal list. A menu that drifts from it
/// is a menu the user has to relearn, so the drift has to turn a test red.
/// </para>
/// <para>
/// Which entries are visible comes from <c>RevisionGridControl.ContextMenuOpening</c>.
/// </para>
/// </remarks>
public class RevisionGridContextMenuTests
{
    /// <summary>
    /// <c>mainContextMenu.Items.AddRange</c>, verbatim (RevisionGridControl.Designer.cs:125).
    /// </summary>
    private static readonly RevisionGridMenuId[] _gitExtensionsOrder =
    [
        RevisionGridMenuId.MarkRevisionAsBad,
        RevisionGridMenuId.MarkRevisionAsGood,
        RevisionGridMenuId.BisectSkipRevision,
        RevisionGridMenuId.StopBisect,
        RevisionGridMenuId.SepBisect,
        RevisionGridMenuId.CopyToClipboard,
        RevisionGridMenuId.SepCopy,
        RevisionGridMenuId.ApplyStash,
        RevisionGridMenuId.PopStash,
        RevisionGridMenuId.DropStash,
        RevisionGridMenuId.SepStash,
        RevisionGridMenuId.CheckoutBranch,
        RevisionGridMenuId.PushBranch,
        RevisionGridMenuId.MergeBranch,
        RevisionGridMenuId.RebaseOn,
        RevisionGridMenuId.ResetCurrentBranchToHere,
        RevisionGridMenuId.SepBranch,
        RevisionGridMenuId.ResetChanges,
        RevisionGridMenuId.Commit,
        RevisionGridMenuId.CreateNewBranch,
        RevisionGridMenuId.ResetAnotherBranchToHere,
        RevisionGridMenuId.RenameBranch,
        RevisionGridMenuId.DeleteBranch,
        RevisionGridMenuId.SepBranchModification,
        RevisionGridMenuId.CreateTag,
        RevisionGridMenuId.DeleteTag,
        RevisionGridMenuId.SepCommit,
        RevisionGridMenuId.CheckoutRevision,
        RevisionGridMenuId.RevertCommit,
        RevisionGridMenuId.CherryPickCommit,
        RevisionGridMenuId.ArchiveRevision,
        RevisionGridMenuId.ManipulateCommit,
        RevisionGridMenuId.SepCompare,
        RevisionGridMenuId.Compare,
        RevisionGridMenuId.SepNavigate,
        RevisionGridMenuId.Navigate,
        RevisionGridMenuId.SelectInLeftPanel,
        RevisionGridMenuId.View,
        RevisionGridMenuId.RunScript,
        RevisionGridMenuId.OpenBuildReport,
        RevisionGridMenuId.OpenPullRequestPage,
        RevisionGridMenuId.OtherActions,
    ];

    private static RevisionGridRef Local(string name, bool isCurrent = false) =>
        new(name, RevisionGridRefKind.LocalBranch, isCurrent);

    private static RevisionGridRef Remote(string name, bool tracked = false) =>
        new(name, RevisionGridRefKind.RemoteBranch, IsTrackedByLocalBranch: tracked);

    private static RevisionGridRef Tag(string name) =>
        new(name, RevisionGridRefKind.Tag);

    private static RevisionGridMenuContext Context(
        IReadOnlyList<RevisionGridRef>? refs = null,
        string? currentBranch = "main") =>
        new()
        {
            CommitHash = "1234567890abcdef1234567890abcdef12345678",
            Refs = refs ?? [],
            CurrentBranch = currentBranch,
        };

    private static RevisionGridMenuEntry Find(
        IReadOnlyList<RevisionGridMenuEntry> menu,
        RevisionGridMenuId id) =>
        menu.First(entry => entry.Id == id);

    [Fact]
    public void Menu_sirasi_GitExtensions_ile_birebir()
    {
        RevisionGridContextMenu.Order.ShouldBe(_gitExtensionsOrder);
    }

    [Fact]
    public void Her_ogenin_bir_metni_var()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(Context());

        foreach (RevisionGridMenuEntry entry in menu.Where(item => !item.IsSeparator))
        {
            entry.Header.ShouldNotBeNullOrWhiteSpace(entry.Id.ToString());
            entry.Header.ShouldNotStartWith("revision_grid.");
        }
    }

    [Fact]
    public void Bisect_disindayken_bisect_ogeleri_gizli()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(Context());

        Find(menu, RevisionGridMenuId.MarkRevisionAsBad).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.MarkRevisionAsGood).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.BisectSkipRevision).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.StopBisect).IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Bisect_sirasinda_dort_oge_de_gorunur()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context() with { InTheMiddleOfBisect = true });

        Find(menu, RevisionGridMenuId.MarkRevisionAsBad).IsVisible.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.MarkRevisionAsGood).IsVisible.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.BisectSkipRevision).IsVisible.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.StopBisect).IsVisible.ShouldBeTrue();
    }

    [Fact]
    public void Yapay_satirda_sifirla_ve_commit_gorunur_kopyalama_gizli()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context() with { IsArtificial = true });

        Find(menu, RevisionGridMenuId.ResetChanges).IsVisible.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.Commit).IsVisible.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.CopyToClipboard).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.CheckoutRevision).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.ArchiveRevision).IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Sirasan_committe_sifirla_ve_commit_gizli()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(Context());

        Find(menu, RevisionGridMenuId.ResetChanges).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.Commit).IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Zula_satirinda_zula_ogeleri_gorunur()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context() with { IsStash = true });

        Find(menu, RevisionGridMenuId.ApplyStash).IsVisible.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.PopStash).IsVisible.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.DropStash).IsVisible.ShouldBeTrue();
    }

    [Fact]
    public void Zula_disinda_zula_ogeleri_gizli()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(Context());

        Find(menu, RevisionGridMenuId.ApplyStash).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.PopStash).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.DropStash).IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Refi_olmayan_committe_checkout_alt_menusu_gizli()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(Context());

        Find(menu, RevisionGridMenuId.CheckoutBranch).IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Checkout_alt_menusu_o_committeki_dallari_listeler()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(
            Context([Local("main", isCurrent: true), Local("feature"), Remote("origin/feature")]));

        RevisionGridMenuEntry checkout = Find(menu, RevisionGridMenuId.CheckoutBranch);

        checkout.IsVisible.ShouldBeTrue();

        string[] names =
            [.. checkout.Children.Where(c => !c.IsSeparator).Select(c => c.Argument!)];

        names.ShouldContain("feature");
        names.ShouldContain("origin/feature");
    }

    [Fact]
    public void Checkout_alt_menusunde_bulunulan_dal_yok()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(
            Context([Local("main", isCurrent: true), Local("feature")]));

        RevisionGridMenuEntry checkout = Find(menu, RevisionGridMenuId.CheckoutBranch);

        checkout.Children
            .Where(c => !c.IsSeparator)
            .Select(c => c.Argument)
            .ShouldNotContain("main");
    }

    [Fact]
    public void Checkout_alt_menusunde_yereller_ile_uzaklar_ayrac_ile_ayrilir()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(
            Context([Local("feature"), Remote("origin/feature")]));

        RevisionGridMenuEntry checkout = Find(menu, RevisionGridMenuId.CheckoutBranch);

        checkout.Children.Any(c => c.IsSeparator).ShouldBeTrue();
    }

    [Fact]
    public void Birlestirme_alt_menusu_ref_yoksa_commit_ozetini_gosterir()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context(currentBranch: "main"));

        RevisionGridMenuEntry merge = Find(menu, RevisionGridMenuId.MergeBranch);

        merge.IsVisible.ShouldBeTrue();

        RevisionGridMenuEntry only = merge.Children.Single(c => !c.IsSeparator);

        only.Id.ShouldBe(RevisionGridMenuId.DynamicCommitHash);
        only.Argument.ShouldBe("1234567890abcdef1234567890abcdef12345678");
    }

    [Fact]
    public void Birlestirme_alt_menusunde_bulunulan_dal_yok()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(
            Context([Local("main", isCurrent: true), Local("feature")]));

        Find(menu, RevisionGridMenuId.MergeBranch).Children
            .Where(c => !c.IsSeparator)
            .Select(c => c.Argument)
            .ShouldNotContain("main");
    }

    [Fact]
    public void Etiket_silme_yalniz_etiket_varken_gorunur()
    {
        IReadOnlyList<RevisionGridMenuEntry> withoutTag =
            RevisionGridContextMenu.Build(Context([Local("feature")]));

        Find(withoutTag, RevisionGridMenuId.DeleteTag).IsVisible.ShouldBeFalse();

        IReadOnlyList<RevisionGridMenuEntry> withTag =
            RevisionGridContextMenu.Build(Context([Tag("v1.0")]));

        RevisionGridMenuEntry delete = Find(withTag, RevisionGridMenuId.DeleteTag);

        delete.IsVisible.ShouldBeTrue();
        delete.Children.Single(c => !c.IsSeparator).Argument.ShouldBe("v1.0");
    }

    [Fact]
    public void Bulunulan_dal_silinemez_ama_oge_gorunur_kalir()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context([Local("main", isCurrent: true)]));

        RevisionGridMenuEntry delete = Find(menu, RevisionGridMenuId.DeleteBranch);

        delete.IsVisible.ShouldBeTrue();
        delete.Children.Where(c => !c.IsSeparator).ShouldBeEmpty();
    }

    [Fact]
    public void Ciplak_depoda_calisma_agaci_islemleri_gizli()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(
            Context([Local("feature")]) with { IsBareRepository = true });

        Find(menu, RevisionGridMenuId.CheckoutBranch).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.CheckoutRevision).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.CherryPickCommit).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.DeleteBranch).IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Derleme_raporu_ve_pull_request_yalniz_varken_gorunur()
    {
        IReadOnlyList<RevisionGridMenuEntry> plain = RevisionGridContextMenu.Build(Context());

        Find(plain, RevisionGridMenuId.OpenBuildReport).IsVisible.ShouldBeFalse();
        Find(plain, RevisionGridMenuId.OpenPullRequestPage).IsVisible.ShouldBeFalse();

        IReadOnlyList<RevisionGridMenuEntry> rich = RevisionGridContextMenu.Build(
            Context() with { HasBuildReport = true, HasPullRequest = true });

        Find(rich, RevisionGridMenuId.OpenBuildReport).IsVisible.ShouldBeTrue();
        Find(rich, RevisionGridMenuId.OpenPullRequestPage).IsVisible.ShouldBeTrue();
    }

    [Fact]
    public void Ref_uzerine_tiklaninca_gelismis_ogeler_diger_eylemlere_taşinir()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(
            Context([Local("feature")]) with { RefClicked = true, ClickedRef = "feature" });

        RevisionGridMenuEntry other = Find(menu, RevisionGridMenuId.OtherActions);

        other.Children.ShouldNotBeEmpty();
        other.Children.Select(c => c.Id).ShouldContain(RevisionGridMenuId.CherryPickCommit);

        menu.Select(entry => entry.Id).ShouldNotContain(RevisionGridMenuId.CherryPickCommit);
    }

    [Fact]
    public void Ust_uste_gelen_ayraclar_teke_iner()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(Context());

        RevisionGridMenuEntry[] shown = [.. menu.Where(entry => entry.IsVisible)];

        for (int index = 1; index < shown.Length; index++)
        {
            (shown[index - 1].IsSeparator && shown[index].IsSeparator).ShouldBeFalse();
        }

        shown.Length.ShouldBeGreaterThan(0);
        shown[0].IsSeparator.ShouldBeFalse();
        shown[^1].IsSeparator.ShouldBeFalse();
    }

    [Fact]
    public void Izlenen_uzak_dal_checkout_listesinde_iki_kez_gecmez()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(
            Context([Local("feature"), Remote("origin/feature", tracked: true)]));

        RevisionGridMenuEntry merge = Find(menu, RevisionGridMenuId.MergeBranch);

        merge.Children
            .Where(c => !c.IsSeparator)
            .Select(c => c.Argument)
            .ShouldNotContain("origin/feature");
    }

    [Fact]
    public void Zula_ogeleri_artik_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context() with { IsStash = true });

        Find(menu, RevisionGridMenuId.ApplyStash).IsEnabled.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.PopStash).IsEnabled.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.DropStash).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Yapay_satirda_sifirla_ve_commit_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context() with { IsArtificial = true });

        Find(menu, RevisionGridMenuId.ResetChanges).IsEnabled.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.Commit).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Etiket_ogeleri_artik_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context([Tag("v1.0")]));

        Find(menu, RevisionGridMenuId.CreateTag).IsEnabled.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.DeleteTag).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Etiketsiz_satirda_etiket_sil_gizli_etiket_olustur_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu = RevisionGridContextMenu.Build(Context());

        Find(menu, RevisionGridMenuId.DeleteTag).IsVisible.ShouldBeFalse();
        Find(menu, RevisionGridMenuId.CreateTag).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Etiket_sil_alt_menusu_satirdaki_etiketlerden_dolar()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context([Tag("v1.0"), Tag("v1.1")]));

        Find(menu, RevisionGridMenuId.DeleteTag).Children
            .Select(child => child.Argument)
            .ShouldBe(["v1.0", "v1.1"]);
    }

    [Fact]
    public void Yapay_satirda_etiket_olusturulamaz()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context() with { IsArtificial = true });

        // There is no commit to tag on the working tree / index rows.
        Find(menu, RevisionGridMenuId.CreateTag).IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Karsilastirma_ogeleri_artik_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> compare =
            Find(RevisionGridContextMenu.Build(Context()), RevisionGridMenuId.Compare).Children;

        Find(compare, RevisionGridMenuId.CompareToBranch).IsEnabled.ShouldBeTrue();
        Find(compare, RevisionGridMenuId.SelectAsBase).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Taban_secilmeden_tabanla_karsilastir_devre_disi_ama_gorunur()
    {
        IReadOnlyList<RevisionGridMenuEntry> compare =
            Find(RevisionGridContextMenu.Build(Context()), RevisionGridMenuId.Compare).Children;

        RevisionGridMenuEntry entry = Find(compare, RevisionGridMenuId.CompareToBase);

        // GitExtensions disables it (`compareToBaseToolStripMenuItem.Enabled = …`); it does not
        // remove it, so the submenu keeps the same shape either way.
        entry.IsVisible.ShouldBeTrue();
        entry.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Taban_secildikten_sonra_tabanla_karsilastir_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> compare = Find(
                RevisionGridContextMenu.Build(Context() with { HasBaseCommitToCompare = true }),
                RevisionGridMenuId.Compare)
            .Children;

        Find(compare, RevisionGridMenuId.CompareToBase).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Difftool_hala_devre_disi()
    {
        // There is no difftool runner in Core; the entry keeps its place but cannot be pressed.
        IReadOnlyList<RevisionGridMenuEntry> compare = Find(
                RevisionGridContextMenu.Build(Context() with { SelectedCount = 2 }),
                RevisionGridMenuId.Compare)
            .Children;

        Find(compare, RevisionGridMenuId.OpenCommitsWithDiffTool).IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Commite_git_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> navigate =
            Find(RevisionGridContextMenu.Build(Context()), RevisionGridMenuId.Navigate).Children;

        Find(navigate, RevisionGridMenuId.GoToCommit).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Gezinme_ogeleri_artik_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> navigate =
            Find(RevisionGridContextMenu.Build(Context()), RevisionGridMenuId.Navigate).Children;

        Find(navigate, RevisionGridMenuId.GoToCurrentRevision).IsEnabled.ShouldBeTrue();
        Find(navigate, RevisionGridMenuId.GoToMergeBaseCommit).IsEnabled.ShouldBeTrue();
        Find(navigate, RevisionGridMenuId.GoToFirstParentCommit).IsEnabled.ShouldBeTrue();
        Find(navigate, RevisionGridMenuId.GoToLastParentCommit).IsEnabled.ShouldBeTrue();
        Find(navigate, RevisionGridMenuId.NavigateBackward).IsEnabled.ShouldBeTrue();
        Find(navigate, RevisionGridMenuId.NavigateForward).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Hizli_arama_ogeleri_hala_devre_disi()
    {
        // The search box moves to the next match; "previous/next match" as menu commands need a
        // match cursor the list does not keep yet.
        IReadOnlyList<RevisionGridMenuEntry> navigate =
            Find(RevisionGridContextMenu.Build(Context()), RevisionGridMenuId.Navigate).Children;

        Find(navigate, RevisionGridMenuId.QuickSearchNext).IsEnabled.ShouldBeFalse();
        Find(navigate, RevisionGridMenuId.QuickSearchPrevious).IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Bisect_ogeleri_artik_etkin()
    {
        IReadOnlyList<RevisionGridMenuEntry> menu =
            RevisionGridContextMenu.Build(Context() with { InTheMiddleOfBisect = true });

        Find(menu, RevisionGridMenuId.MarkRevisionAsBad).IsEnabled.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.MarkRevisionAsGood).IsEnabled.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.BisectSkipRevision).IsEnabled.ShouldBeTrue();
        Find(menu, RevisionGridMenuId.StopBisect).IsEnabled.ShouldBeTrue();
    }
}
