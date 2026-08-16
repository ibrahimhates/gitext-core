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
/// P04-T12 — Navigating within the diff, with <b>real key events</b>.
/// </summary>
/// <remarks>
/// <para>
/// The ViewModel tests show that the commands work correctly; these show that the key really is bound
/// to that command and does not collide with <c>ListBox</c>'s own behaviour. The same distinction
/// caught three separate bugs in Phase 03.
/// </para>
/// <para>
/// <b>MEASURED (P04-T12):</b> the clipboard <b>works</b> in headless Avalonia
/// (<c>HeadlessClipboardImplStub</c>), so copying really can be verified. But in Avalonia 12 there is
/// <b>no</b> <c>IClipboard.GetTextAsync</c> — reading goes through the <c>TryGetTextAsync()</c>
/// extension.
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
        /// Moves focus to the diff list.
        /// </summary>
        /// <remarks>
        /// <b>MEASURED (Phase 03):</b> <c>ListBox.Focusable</c> is <see langword="false"/>; the thing
        /// that takes focus is the <c>ListBoxItem</c>. Unless focus is moved, key events never pass
        /// through the view's tree.
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

        // P08-T01: the shortcuts now come from the command registry; without binding it the view runs
        // without shortcuts (which is exactly what these tests verify).
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
        // Consuming it would be a silent wall to the user; the list's own behaviour must be able to
        // take over.
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
        // If Ctrl+↓ moved between lines while typing in the box, the user would lose their place.
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
        // The two lists have different indices; navigation has to look at the active mode.
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
        // DELIBERATE: the commit list refreshes the diff on every selection. Were the panel to take
        // focus by itself, focus would slip to the diff while the user moved through the commit list
        // with the arrow keys, and list navigation would silently die. Focus arrives only when the user
        // clicks or tabs — which is why the tests above call `FocusList()` first.
        Harness harness = await CreateAsync();

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        harness.ViewModel.CurrentLineIndex.ShouldBe(-1);

        harness.Window.Close();
    }

    // ---- P05-T11: selecting a range with the keyboard ----

    [AvaloniaFact]
    public async Task Shift_ok_ile_satir_ARALIGI_secilir()
    {
        // Partial staging rests on the selection; being forced onto the mouse interrupts the flow.
        // `Shift+↑↓` is `ListBox`'s own behaviour — but `SelectionMode="Multiple"` alone is not enough,
        // as Shift does not select a range in `Toggle` mode. This test pins that down.
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
        // The selection on screen and the lines to be staged must be the same; if they diverge, the
        // user stages something other than what they see and git accepts it.
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
