using System.Windows.Input;
using Avalonia.Input;
using GitExt.UI.Commands;
using GitExt.UI.Localization;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P08-T04 — the command palette, and P08-T06 — the shortcut reference.
/// </summary>
public class CommandPaletteTests
{
    private sealed class Spy : ICommand
    {
        public int Count;
        public bool Enabled = true;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => Enabled;
        public void Execute(object? p) => Count++;
    }

    private static (CommandPaletteViewModel Palette, CommandRouter Router) Create()
    {
        CommandRouter router = new();

        return (new CommandPaletteViewModel(TestCommands.Registry(), router), router);
    }

    [Fact]
    public void Bos_sorguda_butun_komutlar_listeleniyor()
    {
        (CommandPaletteViewModel palette, _) = Create();

        palette.Results.Count.ShouldBe(DefaultCommandScheme.Definitions.Count);
        palette.IsEmpty.ShouldBeFalse();
    }

    /// <summary>
    /// Fuzzy matching: it is enough for the letters to appear in order.
    /// </summary>
    /// <remarks>
    /// A full substring search would meet a user typing an abbreviation with an empty result — typing a
    /// few letters is the only way the palette can be fast.
    /// </remarks>
    [Theory]
    [InlineData("crbr", CommandIds.BranchCreate)]
    [InlineData("rebase", CommandIds.HistoryRebase)]
    [InlineData("command palette", CommandIds.ToolsCommandPalette)]
    public void Bulanik_arama_komutu_buluyor(string query, string expected)
    {
        (CommandPaletteViewModel palette, _) = Create();

        palette.Query = query;

        palette.Results.Select(r => r.CommandId).ShouldContain(expected);
    }

    /// <summary>The shortcut itself can be searched for too: "what does Ctrl+B do?"</summary>
    [Fact]
    public void Jestle_de_aranabiliyor()
    {
        (CommandPaletteViewModel palette, _) = Create();

        palette.Query = "Ctrl+Shift+E";

        palette.Results.ShouldHaveSingleItem().CommandId.ShouldBe(CommandIds.HistoryRebase);
    }

    [Fact]
    public void Eslesme_yoksa_bos_bildiriliyor()
    {
        (CommandPaletteViewModel palette, _) = Create();

        palette.Query = "zzzzzzzz";

        palette.Results.ShouldBeEmpty();
        palette.IsEmpty.ShouldBeTrue();
        palette.SelectedIndex.ShouldBe(-1);
    }

    [Fact]
    public void Kuresel_komut_calistiriliyor()
    {
        (CommandPaletteViewModel palette, CommandRouter router) = Create();
        Spy spy = new();
        router.Register(CommandIds.HistoryRebase, spy);

        palette.Query = "rebase";
        palette.SelectedIndex = 0;

        palette.RunSelected().ShouldBeTrue();
        spy.Count.ShouldBe(1);
    }

    /// <summary>
    /// A panel command works in the palette too — by the <b>same</b> route as its shortcut.
    /// </summary>
    /// <remarks>
    /// Were they separate routes, a command could work in the palette and not from its shortcut, and it
    /// would be unclear which of the two was right.
    /// </remarks>
    [Fact]
    public void Panel_komutu_da_palette_calisiyor()
    {
        (CommandPaletteViewModel palette, CommandRouter router) = Create();

        int invoked = 0;
        ShortcutDispatcher dispatcher = new(TestCommands.Registry(), CommandContext.Diff);
        dispatcher.Bind(CommandIds.DiffStageLines, () => invoked++ >= 0);
        router.Register(dispatcher);

        palette.Query = "stage";
        palette.SelectedIndex = palette.Results
            .Select((r, i) => (r, i))
            .First(x => x.r.CommandId == CommandIds.DiffStageLines).i;

        palette.RunSelected().ShouldBeTrue();
        invoked.ShouldBe(1);
    }

    /// <summary>
    /// A command that cannot run is <b>not hidden</b>, it is shown dimmed and does not run.
    /// </summary>
    /// <remarks>
    /// Hiding it would leave a user who searched for it and did not find it saying "there is no such
    /// thing".
    /// </remarks>
    [Fact]
    public void Calistirilamayan_komut_gorunuyor_ama_calismiyor()
    {
        (CommandPaletteViewModel palette, CommandRouter router) = Create();
        Spy spy = new() { Enabled = false };
        router.Register(CommandIds.HistoryRebase, spy);

        palette.Query = "rebase";
        palette.SelectedIndex = 0;

        palette.Results.ShouldHaveSingleItem().CanRun.ShouldBeFalse();
        palette.RunSelected().ShouldBeFalse();
        spy.Count.ShouldBe(0);
    }

    /// <summary>
    /// Runnable commands come first in the list.
    /// </summary>
    /// <remarks>
    /// With the repository closed, a command that does not run sitting at the top of the list would look
    /// to a user pressing Enter as though nothing had happened.
    /// </remarks>
    /// <remarks>
    /// The palette is built <b>the moment it opens</b> (the window produces a new one on every open), so
    /// the ordering is computed against the state at build time. The test follows the same order: the
    /// bindings first, then the palette.
    /// </remarks>
    [Fact]
    public void Calistirilabilir_komutlar_once_siralaniyor()
    {
        CommandRouter router = new();
        router.Register(CommandIds.HelpAbout, new Spy());

        CommandPaletteViewModel palette = new(TestCommands.Registry(), router);

        palette.Results[0].CommandId.ShouldBe(CommandIds.HelpAbout);
        palette.Results[0].CanRun.ShouldBeTrue();
    }

    [Fact]
    public void Secim_uclarda_basa_sona_sariyor()
    {
        (CommandPaletteViewModel palette, _) = Create();
        palette.Query = "zzzzzzzz";

        palette.MoveSelection(1);
        palette.SelectedIndex.ShouldBe(-1, "boş listede seçim olmamalı");

        palette.Query = "";
        palette.SelectedIndex = 0;

        palette.MoveSelection(-1);
        palette.SelectedIndex.ShouldBe(palette.Results.Count - 1);

        palette.MoveSelection(1);
        palette.SelectedIndex.ShouldBe(0);
    }

    // -------------------------------------------------- P08-T06 the reference screen

    /// <summary>
    /// Pins the interface language for the tests that assert on translated text.
    /// </summary>
    /// <remarks>
    /// 🔴 The language is <b>process-wide</b> (<see cref="Loc"/>), and the screenshot tests switch
    /// it while producing the Turkish image. A test asserting an English heading therefore passed
    /// or failed depending on which test ran before it — the flakiness recorded as an open issue
    /// since Phase 11. Pinning it here removes the dependency on test order.
    /// </remarks>
    private static void UseEnglish()
    {
        Translator translator = new(new InMemorySettingsStore());
        translator.Use("en");
        Loc.Attach(translator);
    }

    [Fact]
    public void Referans_baglama_gore_grupluyor()
    {
        UseEnglish();

        ShortcutReferenceViewModel model = new(TestCommands.Registry());

        model.Groups.Select(g => g.Title).ShouldContain("Everywhere");
        model.Groups.Select(g => g.Title).ShouldContain("Diff view");

        model.Groups.Single(g => g.Title == "Diff view").Rows
            .Select(r => r.CommandId)
            .ShouldContain(CommandIds.DiffStageLines);
    }

    /// <summary>Commands without a shortcut come at the end of the group.</summary>
    [Fact]
    public void Kisayolsuz_komutlar_sonda()
    {
        UseEnglish();

        ShortcutReferenceViewModel model = new(TestCommands.Registry());

        IReadOnlyList<ShortcutRow> rows = model.Groups.Single(g => g.Title == "Everywhere").Rows;

        int firstWithout = rows.Select((r, i) => (r, i)).First(x => x.r.Gesture is null).i;
        int lastWith = rows.Select((r, i) => (r, i)).Last(x => x.r.Gesture is not null).i;

        firstWithout.ShouldBeGreaterThan(lastWith);
    }

    [Fact]
    public void Referansta_filtre_calisiyor()
    {
        ShortcutReferenceViewModel model = new(TestCommands.Registry()) { Filter = "rebase" };

        model.Groups.SelectMany(g => g.Rows).ShouldHaveSingleItem()
            .CommandId.ShouldBe(CommandIds.HistoryRebase);
    }

    [Fact]
    public void Referans_cakismalari_da_gosteriyor()
    {
        CommandRegistry registry = TestCommands.Registry();
        registry.SetGesture(CommandIds.RemotePush, new KeyGesture(Key.F5));

        ShortcutReferenceViewModel model = new(registry);

        model.HasConflicts.ShouldBeTrue();
    }
}
