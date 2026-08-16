using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P03-T14 — Keyboard navigation, with <b>real key events</b>.
/// </summary>
/// <remarks>
/// <para>
/// These tests do not replace the ViewModel tests; they see what those cannot: whether the key is
/// bound to the right method and whether it collides with <c>ListBox</c>'s own behaviour.
/// </para>
/// <para>
/// <b>MEASURED (before these tests were written):</b> <c>ListBox</c> moves the selection itself for
/// <c>↑↓</c>, <c>Home</c> and <c>End</c>; for <c>PgUp</c>/<c>PgDn</c>, however, only the
/// <c>ScrollViewer</c> scrolls (offset 0 → 288) and <b>the selection stays put</b>. That is why page
/// navigation is implemented by hand; the tests below protect that fix.
/// </para>
/// </remarks>
public class CommitListKeyboardTests
{
    private const int RowCount = 100;

    private sealed record Harness(
        Window Window,
        CommitListView View,
        CommitListViewModel ViewModel,
        ListBox List)
    {
        public void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPressQwerty(key, modifiers);
            Window.KeyReleaseQwerty(key, modifiers);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// Sets the selection and moves focus to that row.
        /// </summary>
        /// <remarks>
        /// <b>MEASURED:</b> <c>ListBox.Focusable</c> is <see langword="false"/>, and arrow key
        /// navigation is based on the <b>focused container</b>, not on <c>SelectedIndex</c>.
        /// Setting only the selection without moving focus imitates a state the user never reaches
        /// by clicking, and makes the test meaningless.
        /// </remarks>
        public void SelectAndFocus(int index)
        {
            ViewModel.SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();

            List.ScrollIntoView(index);
            List.ContainerFromIndex(index)?.Focus();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<Harness> CreateAsync(IReadOnlyList<CommitInfo>? commits = null)
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(commits ?? FakeGitData.LinearHistory(RowCount)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader());

        await viewModel.OpenAsync("/tmp/depo");

        CommitListView view = new() { DataContext = viewModel };

        // P08-T01: the shortcuts now come from the command registry; without binding it the view
        // runs without shortcuts (which is exactly what these tests verify).
        view.AttachShortcuts(TestCommands.Registry());

        // The height is deliberately fixed: the page size is computed from the visible area, and
        // unless the window is measured the test loses its meaning.
        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();

        Dispatcher.UIThread.RunJobs();

        ListBox list = view.GetVisualDescendants().OfType<ListBox>().First();

        return new Harness(window, view, viewModel, list);
    }

    [AvaloniaFact]
    public async Task PageDown_secimi_bir_sayfa_ilerletir()
    {
        Harness harness = await CreateAsync();
        harness.SelectAndFocus(0);

        harness.Press(PhysicalKey.PageDown);

        // How many rows depends on the window height; what matters is that the selection ACTUALLY
        // moves and advances by more than one row — the measurement had shown it did not.
        harness.ViewModel.SelectedIndex.ShouldBeGreaterThan(1);
        harness.ViewModel.SelectedIndex.ShouldBeLessThan(RowCount);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task PageUp_ve_PageDown_ayni_mesafeyi_gider()
    {
        Harness harness = await CreateAsync();
        harness.SelectAndFocus(0);

        harness.Press(PhysicalKey.PageDown);
        int afterPageDown = harness.ViewModel.SelectedIndex;

        harness.Press(PhysicalKey.PageUp);

        harness.ViewModel.SelectedIndex.ShouldBe(0);
        afterPageDown.ShouldBeGreaterThan(0);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task PageDown_listenin_sonunda_takilmaz()
    {
        Harness harness = await CreateAsync();
        harness.SelectAndFocus(RowCount - 1);

        harness.Press(PhysicalKey.PageDown);

        harness.ViewModel.SelectedIndex.ShouldBe(RowCount - 1);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Yon_tuslari_ve_Home_End_ListBox_tan_geliyor()
    {
        // Verifies that our own code does not handle these: had it done so, it would move twice.
        Harness harness = await CreateAsync();
        harness.SelectAndFocus(10);

        harness.Press(PhysicalKey.ArrowDown);
        harness.ViewModel.SelectedIndex.ShouldBe(11);

        harness.Press(PhysicalKey.ArrowUp);
        harness.ViewModel.SelectedIndex.ShouldBe(10);

        harness.Press(PhysicalKey.End);
        harness.ViewModel.SelectedIndex.ShouldBe(RowCount - 1);

        harness.Press(PhysicalKey.Home);
        harness.ViewModel.SelectedIndex.ShouldBe(0);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Alt_asagi_ebeveyne_Alt_yukari_cocuga_gider()
    {
        Harness harness = await CreateAsync();
        harness.SelectAndFocus(5);

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        harness.ViewModel.SelectedIndex.ShouldBe(6);

        harness.Press(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
        harness.ViewModel.SelectedIndex.ShouldBe(5);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Ebeveyn_yokken_Alt_asagi_secimi_kaydirmaz()
    {
        // Unless Alt+↓ counts as handled, the ListBox takes over and the selection slips one row down —
        // the user asks for "go to parent" and silently lands on a different commit.
        Harness harness = await CreateAsync();
        harness.SelectAndFocus(RowCount - 1);

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        harness.ViewModel.SelectedIndex.ShouldBe(RowCount - 1);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Satirlar_gorunumden_sonra_gelirse_odak_listeye_devredilir()
    {
        // In the real flow the repository load finishes AFTER the view. If focus stays in the search
        // box, the arrow keys type into the box at startup instead of moving through the list — a
        // defect seen in a render of a real repository.
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(RowCount)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader());

        CommitListView view = new() { DataContext = viewModel };

        // P08-T01: the shortcuts now come from the command registry; without binding it the view
        // runs without shortcuts (which is exactly what these tests verify).
        view.AttachShortcuts(TestCommands.Registry());
        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // No rows yet: focus sits temporarily in the search box.
        TextBox search = view.GetVisualDescendants().OfType<TextBox>().First();
        search.IsFocused.ShouldBeTrue();

        await viewModel.OpenAsync("/tmp/depo");
        Dispatcher.UIThread.RunJobs();

        search.IsFocused.ShouldBeFalse();

        // And the navigation must actually work.
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectedIndex.ShouldBe(1);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Kullanici_aramaya_yazdiysa_odak_elinden_alinmaz()
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(RowCount)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader());

        CommitListView view = new() { DataContext = viewModel };

        // P08-T01: the shortcuts now come from the command registry; without binding it the view
        // runs without shortcuts (which is exactly what these tests verify).
        view.AttachShortcuts(TestCommands.Registry());
        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextBox search = view.GetVisualDescendants().OfType<TextBox>().First();
        search.Text = "abc";

        await viewModel.OpenAsync("/tmp/depo");
        Dispatcher.UIThread.RunJobs();

        search.IsFocused.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Shift_ile_aralik_secilir()
    {
        // There is no consumer of multiple selection yet (range operations are Phase 07, comparing two
        // commits is Phase 04). The point here is to pin the capability ON: if SelectionMode is
        // accidentally changed back to Single, this test breaks.
        Harness harness = await CreateAsync();
        harness.SelectAndFocus(5);

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Shift);

        harness.List.SelectedItems!.Count.ShouldBe(3);
        harness.ViewModel.SelectedIndex.ShouldBe(5);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Ctrl_F_arama_kutusuna_odaklanir_Escape_listeye_dondurur()
    {
        Harness harness = await CreateAsync();

        harness.Press(PhysicalKey.F, RawInputModifiers.Control);

        TextBox search = harness.View.GetVisualDescendants().OfType<TextBox>().First();
        search.IsFocused.ShouldBeTrue();

        harness.Press(PhysicalKey.Escape);

        search.IsFocused.ShouldBeFalse();

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Arama_kutusunda_Enter_sha_ya_atlar()
    {
        Harness harness = await CreateAsync();

        harness.Press(PhysicalKey.F, RawInputModifiers.Control);

        harness.ViewModel.SearchText = FakeGitData.Sha(40);
        harness.Press(PhysicalKey.Enter);

        harness.ViewModel.SelectedRow!.Commit.Id.Value.ShouldBe(FakeGitData.Sha(40));
        harness.ViewModel.SearchStatus.ShouldBeNull();

        harness.Window.Close();
    }
}
