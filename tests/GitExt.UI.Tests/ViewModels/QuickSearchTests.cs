using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

public sealed class QuickSearchTests
{
    private static async Task<CommitListViewModel> LoadedAsync()
    {
        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(
            [
                FakeGitData.Commit(FakeGitData.Sha(5), [FakeGitData.Sha(4)], "fix login") with { Body = "details here" },
                FakeGitData.Commit(FakeGitData.Sha(4), [FakeGitData.Sha(3)], "add feature"),
                FakeGitData.Commit(FakeGitData.Sha(3), [FakeGitData.Sha(2)], "Fix crash"),
                FakeGitData.Commit(FakeGitData.Sha(2), [FakeGitData.Sha(1)], "docs") with
                {
                    Author = new Signature("Grace Hopper", "g@test.invalid", DateTimeOffset.UnixEpoch),
                },
                FakeGitData.Commit(FakeGitData.Sha(1), [], "initial"),
            ]),
            new FakeRefReader(FakeGitData.Refs(
                localBranches: [FakeGitData.LocalBranch("release/v2", FakeGitData.Sha(4))])),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        await list.OpenAsync("/tmp/depo");
        list.SelectedIndex = 0;

        return list;
    }

    [AvaloniaTheory]
    [InlineData("details", 0)]
    [InlineData("release", 1)]
    [InlineData("grace", 3)]
    [InlineData("CRASH", 2)]
    public async Task GitExtensions_alanlariyla_eslesiyor(string criteria, int expected)
    {
        CommitListViewModel list = await LoadedAsync();

        list.QuickSearchType(criteria).ShouldBeTrue();

        list.SelectedIndex.ShouldBe(expected);
    }

    [AvaloniaFact]
    public async Task Sha_basi_ancak_ucuncu_karakterden_sonra()
    {
        CommitListViewModel list = await LoadedAsync();
        string sha = FakeGitData.Sha(2);

        CommitListViewModel.QuickSearchMatches(list.Rows[3], sha[..2]).ShouldBeFalse();
        CommitListViewModel.QuickSearchMatches(list.Rows[3], sha[..3]).ShouldBeTrue();
        CommitListViewModel.QuickSearchMatches(list.Rows[3], sha).ShouldBeTrue();
        CommitListViewModel.QuickSearchMatches(list.Rows[2], sha).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Yazarken_bulunulan_satir_da_sayiliyor()
    {
        CommitListViewModel list = await LoadedAsync();

        list.QuickSearchType("fix").ShouldBeTrue();
        list.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Ileri_ve_geri_son_aramayi_tekrarliyor_ve_basa_sariyor()
    {
        CommitListViewModel list = await LoadedAsync();
        list.QuickSearchType("fix");
        list.ClearQuickSearch();

        list.QuickSearchNext(down: true).ShouldBeTrue();
        list.SelectedIndex.ShouldBe(2);

        list.QuickSearchNext(down: true).ShouldBeTrue();
        list.SelectedIndex.ShouldBe(0);

        list.QuickSearchNext(down: false).ShouldBeTrue();
        list.SelectedIndex.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task Arama_yokken_ileri_bir_sey_yapmiyor()
    {
        CommitListViewModel list = await LoadedAsync();

        list.QuickSearchNext(down: true).ShouldBeFalse();
        list.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Eslesme_yoksa_secim_yerinde_kaliyor()
    {
        CommitListViewModel list = await LoadedAsync();
        list.SelectedIndex = 1;

        list.QuickSearchType("zzz").ShouldBeFalse();

        list.SelectedIndex.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Listede_yazmak_ariyor_escape_temizliyor()
    {
        CommitListViewModel list = await LoadedAsync();
        CommitListView view = new() { DataContext = list };
        view.AttachShortcuts(TestCommands.Registry());
        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ListBox box = view.GetControl<ListBox>("CommitList");
        box.ContainerFromIndex(0)!.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyTextInput("gra");
        Dispatcher.UIThread.RunJobs();

        list.QuickSearchText.ShouldBe("gra");
        list.SelectedIndex.ShouldBe(3);
        view.GetControl<TextBlock>("QuickSearchLabel").IsVisible.ShouldBeTrue();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        list.QuickSearchText.ShouldBeEmpty();
        view.GetControl<TextBox>("ShaSearchBox").Text.ShouldBeNullOrEmpty();

        window.Close();
    }
}
