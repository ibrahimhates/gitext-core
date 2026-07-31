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
/// P03-T14 — Klavye gezinmesi, <b>gerçek tuş olaylarıyla</b>.
/// </summary>
/// <remarks>
/// <para>
/// Bu testler ViewModel testlerinin yerine geçmez, onların göremediğini görür: tuşun doğru
/// metoda bağlanıp bağlanmadığını ve <c>ListBox</c>'ın kendi davranışıyla çakışıp
/// çakışmadığını.
/// </para>
/// <para>
/// <b>ÖLÇÜLDÜ (bu testler yazılmadan önce):</b> <c>ListBox</c> <c>↑↓</c>, <c>Home</c> ve
/// <c>End</c> ile seçimi kendisi taşıyor; <c>PgUp</c>/<c>PgDn</c>'de ise yalnızca
/// <c>ScrollViewer</c> kayıyor (offset 0 → 288) ve <b>seçim yerinde kalıyor</b>. Sayfa
/// gezinmesinin elle uygulanmasının sebebi bu; aşağıdaki testler o düzeltmeyi koruyor.
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
        /// Seçimi ayarlar ve odağı o satıra taşır.
        /// </summary>
        /// <remarks>
        /// <b>ÖLÇÜLDÜ:</b> <c>ListBox.Focusable</c> <see langword="false"/>'tur ve ok tuşu
        /// gezinmesi <c>SelectedIndex</c>'i değil <b>odaklanmış konteyneri</b> temel alır.
        /// Odağı taşımadan sadece seçimi ayarlamak, kullanıcının fareyle tıklamadığı bir
        /// durumu taklit eder ve testi anlamsız kılar.
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

        // Yükseklik bilinçli olarak sabit: sayfa boyutu görünür alandan hesaplanıyor,
        // pencere ölçülmezse test anlamını yitirir.
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

        // Kaç satır olduğu pencere yüksekliğine bağlı; kritik olan seçimin GERÇEKTEN
        // taşınması ve tek satırdan fazla ilerlemesi — ölçüm bunun olmadığını göstermişti.
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
        // Kendi kodumuzun bunları ele almadığını doğrular: ele alsaydık iki kez hareket ederdi.
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
        // Alt+↓ işlenmiş sayılmazsa ListBox devralır ve seçim bir satır aşağı kayar —
        // kullanıcı "ebeveyne git" isterken sessizce başka bir commit'e düşer.
        Harness harness = await CreateAsync();
        harness.SelectAndFocus(RowCount - 1);

        harness.Press(PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        harness.ViewModel.SelectedIndex.ShouldBe(RowCount - 1);

        harness.Window.Close();
    }

    [AvaloniaFact]
    public async Task Satirlar_gorunumden_sonra_gelirse_odak_listeye_devredilir()
    {
        // Gerçek akışta depo yüklemesi görünümden SONRA biter. Odak arama kutusunda
        // kalırsa açılışta ok tuşları listeyi gezmek yerine kutuya yazar — bu, gerçek
        // depo render'ında görülen bir kusurdu.
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(RowCount)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader());

        CommitListView view = new() { DataContext = viewModel };
        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Henüz satır yok: odak geçici olarak arama kutusunda.
        TextBox search = view.GetVisualDescendants().OfType<TextBox>().First();
        search.IsFocused.ShouldBeTrue();

        await viewModel.OpenAsync("/tmp/depo");
        Dispatcher.UIThread.RunJobs();

        search.IsFocused.ShouldBeFalse();

        // Ve gezinme gerçekten çalışıyor olmalı.
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
        // Çoklu seçimin tüketicisi henüz yok (aralık işlemleri Faz 07, iki commit'i
        // karşılaştırma Faz 04). Buradaki amaç yeteneğin AÇIK olduğunu sabitlemek:
        // SelectionMode yanlışlıkla Single'a dönerse bu test kırılır.
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
