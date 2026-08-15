using System.Windows.Input;
using Avalonia.Input;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P08-T04 — komut paleti ve P08-T06 — kısayol referansı.
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
    /// Bulanık eşleşme: harflerin sırayla geçmesi yeterli.
    /// </summary>
    /// <remarks>
    /// Tam alt dize araması, kısaltma yazan kullanıcıyı boş sonuçla karşılardı — paletin
    /// hızlı olmasının tek yolu birkaç harf yazmak.
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

    /// <summary>Kısayolun kendisiyle de aranabiliyor: "Ctrl+B ne yapıyor?"</summary>
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
    /// Panel komutu da palette çalışıyor — kısayolla <b>aynı</b> yoldan.
    /// </summary>
    /// <remarks>
    /// Ayrı yollar olsaydı bir komut palette çalışıp kısayolla çalışmayabilirdi ve
    /// hangisinin doğru olduğu belirsiz kalırdı.
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
    /// Çalıştırılamayan komut <b>gizlenmiyor</b>, soluk gösteriliyor ve çalışmıyor.
    /// </summary>
    /// <remarks>
    /// Gizlemek, komutu arayıp bulamayan kullanıcıya "böyle bir şey yok" dedirtirdi.
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
    /// Çalıştırılabilir komutlar listenin başında.
    /// </summary>
    /// <remarks>
    /// Depo kapalıyken listenin başında çalışmayan bir komut durması, Enter'a basan
    /// kullanıcıya hiçbir şey olmamış gibi görünürdü.
    /// </remarks>
    /// <remarks>
    /// Palet <b>açıldığı anda</b> kuruluyor (pencere her açılışta yenisini üretiyor), o
    /// yüzden sıralama kurulum anındaki duruma göre hesaplanıyor. Test de aynı sırayı
    /// izliyor: önce bağlamalar, sonra palet.
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

    // -------------------------------------------------------- P08-T06 referans ekranı

    [Fact]
    public void Referans_baglama_gore_grupluyor()
    {
        ShortcutReferenceViewModel model = new(TestCommands.Registry());

        model.Groups.Select(g => g.Title).ShouldContain("Her yerde");
        model.Groups.Select(g => g.Title).ShouldContain("Diff view");

        model.Groups.Single(g => g.Title == "Diff view").Rows
            .Select(r => r.CommandId)
            .ShouldContain(CommandIds.DiffStageLines);
    }

    /// <summary>Kısayolu olmayan komutlar grubun sonunda.</summary>
    [Fact]
    public void Kisayolsuz_komutlar_sonda()
    {
        ShortcutReferenceViewModel model = new(TestCommands.Registry());

        IReadOnlyList<ShortcutRow> rows = model.Groups.Single(g => g.Title == "Her yerde").Rows;

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
