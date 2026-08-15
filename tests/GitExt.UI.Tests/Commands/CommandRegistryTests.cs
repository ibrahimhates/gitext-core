using Avalonia.Input;
using GitExt.UI.Commands;
using GitExt.UI.Settings;

namespace GitExt.UI.Tests.Commands;

/// <summary>
/// P08-T01 — command registration and shortcut resolution.
/// </summary>
public class CommandRegistryTests
{
    private static readonly CommandDefinition[] Sample =
    [
        new("a.global", "A", CommandCategory.Repository, CommandContext.Global,
            new KeyGesture(Key.R, KeyModifiers.Control)),
        new("b.list", "B", CommandCategory.Navigation, CommandContext.CommitList,
            new KeyGesture(Key.D, KeyModifiers.Control)),
        new("c.diff", "C", CommandCategory.Navigation, CommandContext.Diff,
            new KeyGesture(Key.D, KeyModifiers.Control)),
        new("d.none", "D", CommandCategory.Tools, CommandContext.Global, null),
    ];

    private static (CommandRegistry Registry, ISettingsStore Settings) Create() =>
        Create(Sample);

    private static (CommandRegistry Registry, ISettingsStore Settings) Create(
        IReadOnlyList<CommandDefinition> definitions)
    {
        InMemorySettingsStore settings = new();

        return (new CommandRegistry(settings, definitions), settings);
    }

    [Fact]
    public void Varsayilan_jest_dondurulur()
    {
        (CommandRegistry registry, _) = Create();

        registry.GetGesture("a.global").ShouldBe(new KeyGesture(Key.R, KeyModifiers.Control));
        registry.GetGesture("d.none").ShouldBeNull();
        registry.GetGesture("yok.olan").ShouldBeNull();
    }

    [Fact]
    public void Kullanici_atamasi_varsayilani_gecersiz_kilar()
    {
        (CommandRegistry registry, _) = Create();

        registry.SetGesture("a.global", new KeyGesture(Key.F9));

        registry.GetGesture("a.global").ShouldBe(new KeyGesture(Key.F9));
        registry.IsCustomized("a.global").ShouldBeTrue();
    }

    /// <summary>
    /// "Remove the shortcut" and "never assigned" differ: removal does <b>not</b> return to the default.
    /// </summary>
    [Fact]
    public void Kisayolu_kaldirmak_varsayilana_DONMEZ()
    {
        (CommandRegistry registry, _) = Create();

        registry.SetGesture("a.global", null);

        registry.GetGesture("a.global").ShouldBeNull();
        registry.IsCustomized("a.global").ShouldBeTrue();

        registry.Reset("a.global");

        registry.GetGesture("a.global").ShouldBe(new KeyGesture(Key.R, KeyModifiers.Control));
        registry.IsCustomized("a.global").ShouldBeFalse();
    }

    [Fact]
    public void ResetAll_butun_atamalari_temizler()
    {
        (CommandRegistry registry, ISettingsStore settings) = Create();

        registry.SetGesture("a.global", new KeyGesture(Key.F9));
        registry.SetGesture("b.list", new KeyGesture(Key.F10));

        registry.ResetAll();

        settings.Current.Shortcuts.ShouldBeEmpty();
        registry.GetGesture("a.global").ShouldBe(new KeyGesture(Key.R, KeyModifiers.Control));
    }

    /// <summary>
    /// A corrupt gesture string in the settings file only drops that one command back to its default.
    /// </summary>
    /// <remarks>
    /// <c>KeyGesture.Parse</c> throws on text it does not recognize; if it were not caught, a single
    /// hand-edited line would make the application <b>fail to open</b>.
    /// </remarks>
    [Fact]
    public void Bozuk_jest_metni_varsayilana_duser()
    {
        (CommandRegistry registry, ISettingsStore settings) = Create();

        settings.Update(s => s.Shortcuts["a.global"] = "Ctrl+Bunu+Kimse+Anlamaz");

        registry.GetGesture("a.global").ShouldBe(new KeyGesture(Key.R, KeyModifiers.Control));
    }

    [Fact]
    public void Jest_metni_ayar_dosyasinda_geri_okunabilir_bicimde_saklanir()
    {
        (CommandRegistry registry, ISettingsStore settings) = Create();

        registry.SetGesture("a.global", new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift));

        settings.Current.Shortcuts["a.global"].ShouldBe("Ctrl+Shift+P");
        registry.GetGesture("a.global")
            .ShouldBe(new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift));
    }

    // --------------------------------------------------------------- resolution

    [Fact]
    public void Cozumleme_baglama_gore_ayirir()
    {
        (CommandRegistry registry, _) = Create();
        KeyGesture ctrlD = new(Key.D, KeyModifiers.Control);

        registry.Resolve(ctrlD, CommandContext.CommitList).ShouldBe("b.list");
        registry.Resolve(ctrlD, CommandContext.Diff).ShouldBe("c.diff");
        registry.Resolve(ctrlD, CommandContext.WorkingTree).ShouldBeNull();
    }

    /// <summary>
    /// 🔴 A global command does <b>not</b> resolve when a panel context is asked for.
    /// </summary>
    /// <remarks>
    /// If it did resolve it would run twice: global gestures are already in <c>Window.KeyBindings</c>
    /// and, as measured in P08-T00/M11, that path runs unconditionally.
    /// </remarks>
    [Fact]
    public void Kuresel_komut_panel_baglaminda_cozulmez()
    {
        (CommandRegistry registry, _) = Create();
        KeyGesture ctrlR = new(Key.R, KeyModifiers.Control);

        registry.Resolve(ctrlR, CommandContext.Global).ShouldBe("a.global");
        registry.Resolve(ctrlR, CommandContext.CommitList).ShouldBeNull();
    }

    // ----------------------------------------------------------------- conflict

    /// <summary>
    /// The same gesture in different panels is <b>not a conflict</b> — it is the same in GitExtensions.
    /// </summary>
    [Fact]
    public void Farkli_baglamlarda_ayni_jest_cakisma_sayilmaz()
    {
        (CommandRegistry registry, _) = Create();

        registry.Conflicts.ShouldBeEmpty();
    }

    [Fact]
    public void Ayni_baglamda_ayni_jest_cakismadir()
    {
        (CommandRegistry registry, _) = Create();

        registry.SetGesture("c.diff", new KeyGesture(Key.D, KeyModifiers.Control));
        registry.SetGesture("b.list", new KeyGesture(Key.Z, KeyModifiers.Control));
        registry.SetGesture("a.global", new KeyGesture(Key.D, KeyModifiers.Control));

        ShortcutConflict conflict = registry.Conflicts.ShouldHaveSingleItem();
        conflict.Gesture.ShouldBe(new KeyGesture(Key.D, KeyModifiers.Control));
        conflict.CommandIds.ShouldBe(["a.global", "c.diff"], ignoreOrder: true);
    }

    /// <summary>
    /// A global command conflicts with <b>every</b> context.
    /// </summary>
    [Fact]
    public void Kuresel_komut_her_baglamla_cakisir()
    {
        (CommandRegistry registry, _) = Create();

        registry.SetGesture("a.global", new KeyGesture(Key.D, KeyModifiers.Control));

        registry.Conflicts.Count.ShouldBe(1);
        registry.Conflicts[0].CommandIds.ShouldBe(["a.global", "b.list", "c.diff"], ignoreOrder: true);
    }

    [Fact]
    public void Kisayolsuz_komutlar_cakismaz()
    {
        CommandDefinition[] definitions =
        [
            new("x", "X", CommandCategory.Tools, CommandContext.Global, null),
            new("y", "Y", CommandCategory.Tools, CommandContext.Global, null),
        ];

        (CommandRegistry registry, _) = Create(definitions);

        registry.Conflicts.ShouldBeEmpty();
    }

    [Fact]
    public void InContext_baglama_ait_komutlari_dondurur()
    {
        (CommandRegistry registry, _) = Create();

        registry.InContext(CommandContext.CommitList).Select(d => d.Id).ShouldBe(["b.list"]);
    }
}

/// <summary>
/// P08-T02 — the default scheme. These tests prepare the scheme to be <b>frozen</b> (ADR-0006).
/// </summary>
public class DefaultCommandSchemeTests
{
    private static CommandRegistry Registry() =>
        new(new InMemorySettingsStore(), DefaultCommandScheme.Definitions);

    /// <summary>
    /// 🔴 There must be no conflict in the default scheme.
    /// </summary>
    /// <remarks>
    /// P08-T00/M10: Avalonia runs <b>only the first</b> of two conflicting registrations and says
    /// nothing at all. Inspecting the scheme by eye is not enough.
    /// <para>
    /// A concrete example — a case that came up while the scheme was being designed and that was
    /// verified to be caught <b>by sabotaging this test</b>: if "next change" in the diff were
    /// <c>Ctrl+↓</c>, the global <i>Pull/Fetch</i> would swallow it completely (in the code at the
    /// time <c>Ctrl+↓</c> really was diff navigation). Diff navigation was moved to GitExtensions'
    /// <c>Alt+↓↑</c>; this test stops it from coming back.
    /// </para>
    /// </remarks>
    [Fact]
    public void Varsayilan_semada_cakisma_YOK()
    {
        IReadOnlyList<ShortcutConflict> conflicts = Registry().Conflicts;

        conflicts.ShouldBeEmpty(
            "çakışanlar: " + string.Join(
                " · ",
                conflicts.Select(c => $"{c.Gesture} → {string.Join(", ", c.CommandIds)}")));
    }

    [Fact]
    public void Kimlikler_benzersiz()
    {
        IEnumerable<string> duplicates = DefaultCommandScheme.Definitions
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        duplicates.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔴 Bare letters and arrow keys can <b>never be global</b>.
    /// </summary>
    /// <remarks>
    /// P08-T00/M11+M12: a global gesture steals the key from the focused control unconditionally; the
    /// command still runs even if the control sets <c>Handled=true</c>. A global <c>S</c> would end
    /// typing, a global <c>↓</c> would end list navigation.
    /// </remarks>
    [Fact]
    public void Kuresel_kisayollar_degistiricisiz_harf_veya_ok_ICERMEZ()
    {
        IEnumerable<string> offenders = DefaultCommandScheme.Definitions
            .Where(d => d.Context.HasFlag(CommandContext.Global))
            .Where(d => d.DefaultGesture is { KeyModifiers: KeyModifiers.None } g
                && !IsFunctionKey(g.Key))
            .Select(d => $"{d.Id} → {d.DefaultGesture}");

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// Destructive operations are deliberately left without a shortcut.
    /// </summary>
    /// <remarks>
    /// A <c>reset</c> bound to a key pressed by accident would mean testing the undo path every single
    /// time (the rule of Phase 07: every destructive operation has an undo path — but making people
    /// use it unnecessarily is a design mistake).
    /// </remarks>
    [Theory]
    [InlineData(CommandIds.HistoryReset)]
    [InlineData(CommandIds.HistoryRevert)]
    [InlineData(CommandIds.HistoryCherryPick)]
    [InlineData(CommandIds.BranchDelete)]
    public void Yikici_islemlerin_varsayilan_kisayolu_yok(string commandId)
    {
        Registry().GetGesture(commandId).ShouldBeNull();
    }

    [Fact]
    public void Her_komutun_basligi_var()
    {
        DefaultCommandScheme.Definitions
            .Where(d => string.IsNullOrWhiteSpace(d.Title))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// Assignments deliberately kept <b>identical</b> to GitExtensions.
    /// </summary>
    /// <remarks>
    /// Source: GitExtensions <c>HotkeySettingsManager.CreateDefaultSettings()</c>. This test does not
    /// forbid deviations; it forbids <b>accidental</b> deviation. A deliberate deviation is removed
    /// from this list and written into the rationale in <see cref="DefaultCommandScheme"/>.
    /// </remarks>
    [Theory]
    [InlineData(CommandIds.RepositoryOpen, "Ctrl+O")]
    [InlineData(CommandIds.RepositoryClose, "Ctrl+W")]
    [InlineData(CommandIds.RepositoryRefresh, "F5")]
    [InlineData(CommandIds.CommitShow, "Ctrl+Space")]
    [InlineData(CommandIds.RemotePull, "Ctrl+Down")]
    [InlineData(CommandIds.RemotePush, "Ctrl+Up")]
    [InlineData(CommandIds.BranchCreate, "Ctrl+B")]
    [InlineData(CommandIds.BranchMerge, "Ctrl+M")]
    [InlineData(CommandIds.HistoryRebase, "Ctrl+Shift+E")]
    [InlineData(CommandIds.TagCreate, "Ctrl+T")]
    [InlineData(CommandIds.ViewToggleLeftPanel, "Ctrl+Alt+C")]
    [InlineData(CommandIds.ViewFocusLeftPanel, "Ctrl+D0")]
    [InlineData(CommandIds.ViewFocusCommitList, "Ctrl+D1")]
    [InlineData(CommandIds.CommitListGoToParent, "Ctrl+P")]
    [InlineData(CommandIds.CommitListGoToChild, "Ctrl+N")]
    [InlineData(CommandIds.CommitListCompareToWorkingDirectory, "Ctrl+D")]
    [InlineData(CommandIds.DiffFind, "Ctrl+F")]
    [InlineData(CommandIds.DiffFindNext, "F3")]
    [InlineData(CommandIds.DiffNextChange, "Alt+Down")]
    [InlineData(CommandIds.DiffPreviousChange, "Alt+Up")]
    [InlineData(CommandIds.DiffStageLines, "S")]
    [InlineData(CommandIds.DiffUnstageLines, "U")]
    [InlineData(CommandIds.DiffResetLines, "R")]
    [InlineData(CommandIds.RefTreeDelete, "Delete")]
    [InlineData(CommandIds.RefTreeRename, "F2")]
    public void GitExtensions_ile_ayni_kalan_atamalar(string commandId, string expected)
    {
        Registry().GetGesture(commandId)?.ToString().ShouldBe(expected);
    }

    private static bool IsFunctionKey(Key key) =>
        key is >= Key.F1 and <= Key.F24;
}
