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
/// P04-T16 — The comparison window.
/// </summary>
/// <remarks>
/// The real requirement here is that it be <b>modeless and multiple</b>: the user's objection was that
/// a single embedded panel made it impossible to put two changes side by side. In GitExtensions too,
/// <c>FormDiff</c> is opened with <c>Show()</c> rather than <c>ShowDialog</c>.
/// </remarks>
public class CompareWindowTests
{
    private static async Task<CommitListViewModel> LoadedListAsync(int commitCount = 5)
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(commitCount)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader([FakeGitData.Diff("a.cs")]));

        await viewModel.OpenAsync("/tmp/depo");

        return viewModel;
    }

    [AvaloniaFact]
    public async Task Iki_revizyon_karsilastirilir()
    {
        CommitListViewModel list = await LoadedListAsync();
        CompareViewModel compare = list.CreateComparison()!;

        await compare.CompareAsync(
            CommitId.Parse(FakeGitData.Sha(1)),
            CommitId.Parse(FakeGitData.Sha(2)));

        compare.Target.ShouldBe(CompareTarget.Revisions);
        compare.FromRevision.ShouldBe(FakeGitData.Sha(1));
        compare.ToRevision.ShouldBe(FakeGitData.Sha(2));
        compare.Diff.HasFiles.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Calisma_agaciyla_karsilastirmada_sag_taraf_bostur()
    {
        CommitListViewModel list = await LoadedListAsync();
        CompareViewModel compare = list.CreateComparison()!;

        await compare.CompareWithWorkingTreeAsync(FakeGitData.Sha(1));

        compare.Target.ShouldBe(CompareTarget.WorkingTree);
        compare.ToRevision.ShouldBeEmpty();
        compare.Title.ShouldContain("working tree");
    }

    [AvaloniaFact]
    public async Task Baslikta_SHA_kisaltilir_dal_adi_kisaltilmaz()
    {
        CommitListViewModel list = await LoadedListAsync();
        CompareViewModel compare = list.CreateComparison()!;

        await compare.CompareAsync(FakeGitData.Sha(1), "main");

        compare.Title.ShouldBe($"{FakeGitData.Sha(1)[..8]} ↔ main");
    }

    [AvaloniaFact]
    public async Task Yenileme_ayni_karsilastirmayi_tekrar_okur()
    {
        // Needed for a working tree comparison: the user can edit the file and leave the window open.
        FakeDiffReader reader = new([FakeGitData.Diff("a.cs")]);
        CompareViewModel compare = new(reader, "/tmp/depo");

        await compare.CompareWithWorkingTreeAsync(FakeGitData.Sha(1));
        int afterFirst = reader.ReadCallCount;

        await compare.RefreshAsync();

        reader.ReadCallCount.ShouldBeGreaterThan(afterFirst);
        compare.Target.ShouldBe(CompareTarget.WorkingTree);
    }

    [AvaloniaFact]
    public async Task Depo_yokken_karsilastirma_uretilmez()
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader([]),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        viewModel.CreateComparison().ShouldBeNull();

        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Her_pencere_KENDI_ViewModel_ine_sahip()
    {
        // With a single shared instance, the second window would change the first one's content.
        CommitListViewModel list = await LoadedListAsync();

        CompareViewModel first = list.CreateComparison()!;
        CompareViewModel second = list.CreateComparison()!;

        first.ShouldNotBeSameAs(second);
        first.Diff.ShouldNotBeSameAs(second.Diff);

        await first.CompareAsync(FakeGitData.Sha(1), FakeGitData.Sha(2));
        await second.CompareWithWorkingTreeAsync(FakeGitData.Sha(3));

        first.Target.ShouldBe(CompareTarget.Revisions);
        second.Target.ShouldBe(CompareTarget.WorkingTree);
    }

    [AvaloniaFact]
    public async Task Iki_pencere_ayni_anda_acik_kalabilir()
    {
        // This is why this task exists in the phase: MODELESS and MULTIPLE.
        CommitListViewModel list = await LoadedListAsync();

        CompareViewModel first = list.CreateComparison()!;
        CompareViewModel second = list.CreateComparison()!;

        await first.CompareAsync(FakeGitData.Sha(1), FakeGitData.Sha(2));
        await second.CompareAsync(FakeGitData.Sha(3), FakeGitData.Sha(4));

        CompareWindow firstWindow = new() { DataContext = first };
        CompareWindow secondWindow = new() { DataContext = second };

        firstWindow.Show();
        secondWindow.Show();
        Dispatcher.UIThread.RunJobs();

        firstWindow.IsVisible.ShouldBeTrue();
        secondWindow.IsVisible.ShouldBeTrue();

        // ⚠️ The titles are not compared here: the first eight characters of the fake SHAs ("0…01",
        // "0…02") are the same, so the abbreviated titles come out the same too. Where the distinction
        // is preserved is in the revisions themselves.
        first.FromRevision.ShouldNotBe(second.FromRevision);

        // The content must really have been drawn: the window hosts a DiffView.
        firstWindow.GetVisualDescendants().OfType<DiffView>().ShouldHaveSingleItem();

        firstWindow.Close();
        secondWindow.Close();
    }

    [AvaloniaFact]
    public async Task Escape_pencereyi_kapatir()
    {
        CommitListViewModel list = await LoadedListAsync();
        CompareViewModel compare = list.CreateComparison()!;

        await compare.CompareAsync(FakeGitData.Sha(1), FakeGitData.Sha(2));

        CompareWindow window = new() { DataContext = compare };

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Only the press is sent: the window closes inside `KeyDown`, and the headless harness sending
        // the release event to the CLOSED window produces an `ObjectDisposedException`. There is no
        // such path in real use.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        closed.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Ctrl_D_iki_secili_commiti_karsilastirir()
    {
        // THE FIRST CONSUMER of the multiple selection opened in P03-T14.
        CommitListViewModel list = await LoadedListAsync(10);

        CommitListView view = new() { DataContext = list };

        // P08-T01: the shortcuts now come from the command registry; without binding it the view
        // runs without shortcuts (which is exactly what these tests verify).
        view.AttachShortcuts(TestCommands.Registry());
        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ListBox commitList = view.GetVisualDescendants().OfType<ListBox>().First();

        commitList.SelectedItems!.Clear();
        commitList.SelectedItems.Add(list.Rows[0]);
        commitList.SelectedItems.Add(list.Rows[3]);
        Dispatcher.UIThread.RunJobs();

        CompareViewModel? requested = null;
        view.ComparisonRequested += (_, model) => requested = model;

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.D, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        requested.ShouldNotBeNull();

        // The list is sorted newest to oldest; the comparison has to go from old to new.
        requested.FromRevision.ShouldBe(list.Rows[3].Commit.Id.Value);
        requested.ToRevision.ShouldBe(list.Rows[0].Commit.Id.Value);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Tek_secimde_Ctrl_D_calisma_agaciyla_karsilastirir()
    {
        CommitListViewModel list = await LoadedListAsync(10);

        CommitListView view = new() { DataContext = list };

        // P08-T01: the shortcuts now come from the command registry; without binding it the view
        // runs without shortcuts (which is exactly what these tests verify).
        view.AttachShortcuts(TestCommands.Registry());
        Window window = new() { Width = 900, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        list.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        CompareViewModel? requested = null;
        view.ComparisonRequested += (_, model) => requested = model;

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.D, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        requested.ShouldNotBeNull();
        requested.Target.ShouldBe(CompareTarget.WorkingTree);
        requested.FromRevision.ShouldBe(list.Rows[2].Commit.Id.Value);

        window.Close();
    }
}
