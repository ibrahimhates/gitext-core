using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P04-T12 — Diff içinde gezinme, <b>gerçek tuş olaylarıyla</b>.
/// </summary>
/// <remarks>
/// <para>
/// ViewModel testleri komutların doğru çalıştığını gösteriyor; bunlar tuşun o komuta
/// gerçekten bağlandığını ve <c>ListBox</c>'ın kendi davranışıyla çakışmadığını gösteriyor.
/// Faz 03'te aynı ayrım üç ayrı hata yakalamıştı.
/// </para>
/// <para>
/// <b>ÖLÇÜLDÜ (P04-T12):</b> headless Avalonia'da pano <b>çalışıyor</b>
/// (<c>HeadlessClipboardImplStub</c>), yani kopyalama gerçekten doğrulanabiliyor. Ama
/// Avalonia 12'de <c>IClipboard.GetTextAsync</c> <b>yok</b> — okuma
/// <c>TryGetTextAsync()</c> uzantısıyla yapılıyor.
/// </para>
/// </remarks>
public class DiffKeyboardTests
{
    private sealed record Harness(Window Window, DiffView View, DiffViewModel ViewModel)
    {
        public void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPressQwerty(key, modifiers);
            Window.KeyReleaseQwerty(key, modifiers);
            Dispatcher.UIThread.RunJobs();
        }

        public TextBox LineSearch =>
            View.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "LineSearch");

        /// <summary>
        /// Odağı diff listesine taşır.
        /// </summary>
        /// <remarks>
        /// <b>ÖLÇÜLDÜ (Faz 03):</b> <c>ListBox.Focusable</c> <see langword="false"/>'tur;
        /// odaklanan şey <c>ListBoxItem</c>. Odak taşınmazsa tuş olayları görünümün
        /// ağacından hiç geçmez.
        /// </remarks>
        public void FocusList()
        {
            ListBox list = View.GetVisualDescendants()
                .OfType<ListBox>()
                .Single(l => l.Name == (ViewModel.ShowSideBySide ? "SideDiffLines" : "DiffLines"));

            list.ContainerFromIndex(0)?.Focus();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static FileDiff Sample(string path = "a.cs") => FakeGitData.Diff(path) with
    {
        Hunks =
        [
            new DiffHunk
            {
                Header = "@@ -1,4 +1,4 @@",
                OldStart = 1,
                OldLength = 4,
                NewStart = 1,
                NewLength = 4,
                Lines =
                [
                    new DiffLine(DiffLineKind.Context, "bir") { OldLineNumber = 1, NewLineNumber = 1 },
                    new DiffLine(DiffLineKind.Removed, "iki eski") { OldLineNumber = 2 },
                    new DiffLine(DiffLineKind.Added, "iki yeni") { NewLineNumber = 2 },
                    new DiffLine(DiffLineKind.Context, "uc") { OldLineNumber = 3, NewLineNumber = 3 },
                    new DiffLine(DiffLineKind.Added, "dort yeni") { NewLineNumber = 4 },
                ],
            },
        ],
    };

    private static async Task<Harness> CreateAsync(params FileDiff[] diffs)
    {
        DiffViewModel viewModel = new(new FakeDiffReader(diffs.Length == 0 ? [Sample()] : diffs));

        await viewModel.ShowCommitAsync("/tmp/depo", CommitId.Parse(FakeGitData.Sha(7)));

        DiffView view = new() { DataContext = viewModel };

        // P08-T01: kısayollar artık komut kaydından geliyor; bağlanmazsa görünüm
        // kısayolsuz çalışır (bu testler tam da bunu doğruluyor).
        view.AttachShortcuts(TestCommands.Registry());
        Window window = new() { Width = 900, Height = 300, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return new Harness(window, view, viewModel);
    }

    [AvaloniaFact]
    public async Task Alt_asagi_sonraki_degisiklige_gider()
    {
        Harness harness = await CreateAsync();
        harness.FocusList();

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        harness.ViewModel.Lines[harness.ViewModel.CurrentLineIndex].Text.ShouldBe("iki eski");

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        harness.ViewModel.Lines[harness.ViewModel.CurrentLineIndex].Text.ShouldBe("dort yeni");

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Son_degisiklikten_sonra_Alt_asagi_tuketilmez()
    {
        // Tüketilseydi kullanıcıya sessiz bir duvar olurdu; listenin kendi davranışı
        // devralabilmeli.
        Harness harness = await CreateAsync();
        harness.FocusList();

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        int last = harness.ViewModel.CurrentLineIndex;

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        harness.ViewModel.CurrentLineIndex.ShouldBe(last);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Alt_saga_sonraki_dosyaya_gecer()
    {
        Harness harness = await CreateAsync(Sample("a.cs"), Sample("b.cs"));
        harness.FocusList();

        harness.Press(PhysicalKey.ArrowRight, RawInputModifiers.Alt);

        harness.ViewModel.SelectedFile!.Name.ShouldBe("b.cs");

        harness.Press(PhysicalKey.ArrowLeft, RawInputModifiers.Alt);

        harness.ViewModel.SelectedFile!.Name.ShouldBe("a.cs");

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Ctrl_F_arama_kutusuna_odaklanir_Escape_listeye_dondurur()
    {
        Harness harness = await CreateAsync();
        harness.FocusList();

        harness.Press(PhysicalKey.F, RawInputModifiers.Control);
        harness.LineSearch.IsFocused.ShouldBeTrue();

        harness.Press(PhysicalKey.Escape);
        harness.LineSearch.IsFocused.ShouldBeFalse();

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Arama_kutusunda_Enter_sonraki_eslesmeye_gider()
    {
        Harness harness = await CreateAsync();
        harness.FocusList();

        harness.Press(PhysicalKey.F, RawInputModifiers.Control);
        harness.ViewModel.LineSearchText = "yeni";

        harness.Press(PhysicalKey.Enter);

        harness.ViewModel.Lines[harness.ViewModel.CurrentLineIndex].Text.ShouldBe("iki yeni");

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Arama_kutusundayken_gezinme_tuslari_metne_karismaz()
    {
        // Kutuda yazarken Ctrl+↓ satır gezdirirse kullanıcı yazdığı yeri kaybeder.
        Harness harness = await CreateAsync();
        harness.FocusList();

        harness.Press(PhysicalKey.F, RawInputModifiers.Control);
        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        harness.ViewModel.CurrentLineIndex.ShouldBe(-1);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Ctrl_C_koda_Ctrl_Shift_C_yamaya_kopyalar()
    {
        Harness harness = await CreateAsync();
        harness.FocusList();

        IClipboard clipboard = TopLevel.GetTopLevel(harness.Window)!.Clipboard!;

        harness.Press(PhysicalKey.C, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        string? code = await clipboard.TryGetTextAsync();

        code.ShouldNotBeNull();
        code.ShouldContain("iki yeni");
        code.ShouldNotContain("@@");
        code.ShouldNotContain("+iki yeni");

        harness.Press(PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        string? patch = await clipboard.TryGetTextAsync();

        patch.ShouldNotBeNull();
        patch.ShouldContain("@@ -1,4 +1,4 @@");
        patch.ShouldContain("+iki yeni");
        patch.ShouldContain("-iki eski");

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Yan_yana_modda_da_gezinme_calisir()
    {
        // İki listenin indeksleri farklı; gezinme aktif moda bakmalı.
        Harness harness = await CreateAsync();
        harness.ViewModel.ShowSideBySide = true;
        Dispatcher.UIThread.RunJobs();

        harness.FocusList();

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        SideBySideLineRow row = harness.ViewModel.SideLines[harness.ViewModel.CurrentLineIndex];

        row.Left.Text.ShouldBe("iki eski");
        row.Right.Text.ShouldBe("iki yeni");

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Diff_paneli_odagi_KENDILIGINDEN_almaz()
    {
        // BİLİNÇLİ: commit listesi her seçimde diff'i yeniliyor. Panel odağı kendiliğinden
        // alsaydı kullanıcı commit listesinde ok tuşlarıyla gezerken odak diff'e kaçar ve
        // liste gezinmesi sessizce ölürdü. Odak yalnızca kullanıcı tıklayınca/Tab'layınca
        // gelir — bu yüzden yukarıdaki testler önce `FocusList()` çağırıyor.
        Harness harness = await CreateAsync();

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        harness.ViewModel.CurrentLineIndex.ShouldBe(-1);

        harness.Window.Close();
    }

    // ---- P05-T11: klavyeyle aralık seçimi ----

    [AvaloniaFact]
    public async Task Shift_ok_ile_satir_ARALIGI_secilir()
    {
        // Kısmi staging seçime dayanıyor; fareye zorunlu kalmak akışı kesiyor.
        // `Shift+↑↓` `ListBox`'ın kendi davranışı — ama `SelectionMode="Multiple"`
        // tek başına yetmiyor, `Toggle` modunda Shift aralık seçmez. Bu test onu sabitliyor.
        Harness harness = await CreateAsync();

        ListBox lines = harness.View.GetControl<ListBox>("DiffLines");

        lines.SelectedIndex = 1;
        lines.ContainerFromIndex(1)!.Focus();
        Dispatcher.UIThread.RunJobs();

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        lines.SelectedItems!.Count.ShouldBe(3);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Secilen_aralik_kismi_stagingde_KULLANILIR()
    {
        // Ekrandaki seçim ile stage'lenecek satırlar aynı olmalı; ayrışırsa kullanıcı
        // gördüğünden başka bir şeyi stage'ler ve git bunu kabul eder.
        Harness harness = await CreateAsync();

        ListBox lines = harness.View.GetControl<ListBox>("DiffLines");

        lines.SelectedIndex = 2;
        lines.ContainerFromIndex(2)!.Focus();
        Dispatcher.UIThread.RunJobs();

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        int[] selected = [.. lines.SelectedItems!.Cast<object>()
            .Select(item => lines.Items.IndexOf(item))];

        selected.Length.ShouldBe(2);

        PatchSelection selection = harness.ViewModel.BuildSelection(selected).ShouldNotBeNull();

        selection.Count.ShouldBe(2);

        harness.Window.Close();
    }
}
