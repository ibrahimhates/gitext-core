using Avalonia.Input;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P08-T03 — shortcut reassignment and conflict detection.
/// </summary>
public class ShortcutSettingsTests
{
    private static ShortcutSettingsViewModel Create(out CommandRegistry registry)
    {
        registry = TestCommands.Registry();

        return new ShortcutSettingsViewModel(registry);
    }

    private static ShortcutSettingsViewModel Create() => Create(out _);

    private static void Select(ShortcutSettingsViewModel model, string commandId) =>
        model.Selected = model.Rows.Single(r => r.CommandId == commandId);

    [Fact]
    public void Butun_komutlar_listeleniyor()
    {
        ShortcutSettingsViewModel model = Create();

        model.Rows.Count.ShouldBe(DefaultCommandScheme.Definitions.Count);
    }

    [Fact]
    public void Atama_kayda_yaziliyor_ve_satir_guncelleniyor()
    {
        ShortcutSettingsViewModel model = Create(out CommandRegistry registry);
        Select(model, CommandIds.RepositoryRefresh);

        model.TryApplyCapture(new KeyGesture(Key.F9)).ShouldBeTrue();

        registry.GetGesture(CommandIds.RepositoryRefresh).ShouldBe(new KeyGesture(Key.F9));
        model.Rows.Single(r => r.CommandId == CommandIds.RepositoryRefresh)
            .GestureText.ShouldBe("F9");
        model.Rows.Single(r => r.CommandId == CommandIds.RepositoryRefresh)
            .IsCustomized.ShouldBeTrue();
    }

    /// <summary>
    /// 🔴 A letter without a modifier cannot be assigned to a global command.
    /// </summary>
    /// <remarks>
    /// P08-T00/M11+M12: a global gesture takes the key from the focused control unconditionally. Were it
    /// allowed, the user could never <b>type</b> that letter into any text box again, and with no error
    /// shown they would not find out why.
    /// </remarks>
    [Fact]
    public void Kuresel_komuta_ciplak_harf_atanamaz()
    {
        ShortcutSettingsViewModel model = Create(out CommandRegistry registry);
        Select(model, CommandIds.RepositoryRefresh);

        model.TryApplyCapture(new KeyGesture(Key.S)).ShouldBeFalse();

        model.CaptureError.ShouldNotBeEmpty();
        registry.GetGesture(CommandIds.RepositoryRefresh).ShouldBe(new KeyGesture(Key.F5));
    }

    /// <summary>Function keys are the exception: because they produce no text, they do not block typing.</summary>
    [Fact]
    public void Kuresel_komuta_fonksiyon_tusu_atanabilir()
    {
        ShortcutSettingsViewModel model = Create();
        Select(model, CommandIds.RepositoryRefresh);

        model.TryApplyCapture(new KeyGesture(Key.F12)).ShouldBeTrue();
        model.CaptureError.ShouldBeEmpty();
    }

    /// <summary>A bare letter is allowed in a panel context — as it is in GitExtensions.</summary>
    [Fact]
    public void Panel_baglaminda_ciplak_harf_atanabilir()
    {
        ShortcutSettingsViewModel model = Create();
        Select(model, CommandIds.DiffStageLines);

        model.TryApplyCapture(new KeyGesture(Key.G)).ShouldBeTrue();
    }

    /// <summary>
    /// Pressing only a modifier is not an error: the gesture is simply not finished yet.
    /// </summary>
    [Fact]
    public void Yalniz_degistirici_sessizce_yok_sayilir()
    {
        ShortcutSettingsViewModel model = Create();
        Select(model, CommandIds.RepositoryRefresh);
        model.BeginCaptureCommand.Execute(null);

        model.TryApplyCapture(new KeyGesture(Key.LeftCtrl, KeyModifiers.Control)).ShouldBeFalse();

        model.CaptureError.ShouldBeEmpty("kullanıcı hata görmemeli");
        model.IsCapturing.ShouldBeTrue("yakalama sürmeli");
    }

    [Fact]
    public void Basarili_atama_yakalamayi_bitirir()
    {
        ShortcutSettingsViewModel model = Create();
        Select(model, CommandIds.RepositoryRefresh);
        model.BeginCaptureCommand.Execute(null);

        model.TryApplyCapture(new KeyGesture(Key.F9));

        model.IsCapturing.ShouldBeFalse();
    }

    /// <summary>Removing is not the same as returning to the default.</summary>
    [Fact]
    public void Kaldirmak_ve_varsayilana_donmek_ayri()
    {
        ShortcutSettingsViewModel model = Create(out CommandRegistry registry);
        Select(model, CommandIds.RepositoryRefresh);

        model.ClearCommand.Execute(null);
        registry.GetGesture(CommandIds.RepositoryRefresh).ShouldBeNull();

        model.ResetCommand.Execute(null);
        registry.GetGesture(CommandIds.RepositoryRefresh).ShouldBe(new KeyGesture(Key.F5));
    }

    [Fact]
    public void ResetAll_butun_ozellestirmeleri_geri_alir()
    {
        ShortcutSettingsViewModel model = Create(out CommandRegistry registry);

        Select(model, CommandIds.RepositoryRefresh);
        model.TryApplyCapture(new KeyGesture(Key.F9));
        Select(model, CommandIds.RemotePush);
        model.TryApplyCapture(new KeyGesture(Key.F10));

        model.ResetAllCommand.CanExecute(null).ShouldBeTrue();
        model.ResetAllCommand.Execute(null);

        registry.GetGesture(CommandIds.RepositoryRefresh).ShouldBe(new KeyGesture(Key.F5));
        model.Rows.Any(r => r.IsCustomized).ShouldBeFalse();
        model.ResetAllCommand.CanExecute(null).ShouldBeFalse();
    }

    /// <summary>
    /// 🔴 A conflict is <b>not blocked</b> but is not passed over silently either.
    /// </summary>
    /// <remarks>
    /// Were it blocked, changing two assignments one after the other would be impossible (you could not
    /// move to the second without clearing the first). Were it silent, the behaviour from P08-T00/M10
    /// would follow: Avalonia never runs the second registration and the user cannot see why.
    /// </remarks>
    [Fact]
    public void Cakisma_engellenmez_ama_bildirilir()
    {
        ShortcutSettingsViewModel model = Create();
        Select(model, CommandIds.RemotePush);

        model.TryApplyCapture(new KeyGesture(Key.F5)).ShouldBeTrue("atama engellenmemeli");

        model.HasConflicts.ShouldBeTrue();
        model.ConflictMessages.ShouldHaveSingleItem().ShouldContain("F5");
    }

    [Fact]
    public void Varsayilan_semada_cakisma_mesaji_yok()
    {
        Create().HasConflicts.ShouldBeFalse();
    }

    [Fact]
    public void Filtre_basliga_gore_suzuyor()
    {
        ShortcutSettingsViewModel model = Create();

        model.Filter = "rebase";

        model.Rows.ShouldHaveSingleItem().CommandId.ShouldBe(CommandIds.HistoryRebase);
    }

    /// <summary>"What is this key assigned to?" — the gesture itself can be searched for too.</summary>
    [Fact]
    public void Filtre_jeste_gore_de_suzuyor()
    {
        ShortcutSettingsViewModel model = Create();

        model.Filter = "Ctrl+Shift+E";

        model.Rows.ShouldHaveSingleItem().CommandId.ShouldBe(CommandIds.HistoryRebase);
    }

    /// <summary>The selection survives filtering — the list must not shift out from under an assignment.</summary>
    [Fact]
    public void Atamadan_sonra_secim_korunuyor()
    {
        ShortcutSettingsViewModel model = Create();
        Select(model, CommandIds.RepositoryRefresh);

        model.TryApplyCapture(new KeyGesture(Key.F9));

        model.Selected.ShouldNotBeNull();
        model.Selected.CommandId.ShouldBe(CommandIds.RepositoryRefresh);
        model.Selected.GestureText.ShouldBe("F9");
    }
}
