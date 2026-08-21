using System.Windows.Input;
using GitExt.Core;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P12-T03 — the dashboard: grouping, search and the context menu actions.
/// </summary>
/// <remarks>
/// The behaviour being pinned down here is GitExtensions' <c>UserRepositoriesList</c>: the recent
/// group first with the categories under it, filtering that does not touch the store, and actions
/// that never throw away more than the user asked for.
/// </remarks>
public class DashboardTests
{
    /// <summary>A command that records what it was asked to open.</summary>
    private sealed class RecordingOpenCommand : ICommand
    {
        public List<string> Opened { get; } = [];

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            if (parameter is string path)
            {
                Opened.Add(path);
            }

            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Answers the prompts without a window.</summary>
    private sealed class FakePrompt : IDashboardPrompt
    {
        public string? NameToReturn { get; set; }

        public bool ConfirmAnswer { get; set; } = true;

        public int ConfirmCount { get; private set; }

        public Task<string?> AskCategoryNameAsync(IReadOnlyList<string> existingCategories, string? currentName) =>
            Task.FromResult(NameToReturn);

        public Task<bool> ConfirmAsync(string caption, string question)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmAnswer);
        }
    }

    /// <summary>Every path is a valid repository on branch <c>main</c> unless said otherwise.</summary>
    private static RepositoryHeadInfo Valid(string path) => new(IsRepository: true, BranchName: "main");

    private static async Task<DashboardViewModel> CreateAsync(
        FakeRecentRepositoryStore store,
        Func<string, RepositoryHeadInfo>? probe = null,
        ICommand? open = null)
    {
        DashboardViewModel dashboard = new(store, open ?? new RecordingOpenCommand(), probe ?? Valid);

        await dashboard.LoadAsync();

        return dashboard;
    }

    [Fact]
    public async Task Once_son_depolar_sonra_kategoriler_alfabetik()
    {
        // The order of the groups must not shuffle as repositories are opened: the recent group is
        // pinned to the top and the categories follow it alphabetically — GitExtensions' order.
        FakeRecentRepositoryStore store = new("/r/bir", "/r/iki", "/r/uc", "/r/dort");
        store.WithCategory("/r/iki", "Work");
        store.WithCategory("/r/uc", "Archive");

        DashboardViewModel dashboard = await CreateAsync(store);

        dashboard.Groups.Select(g => g.Header).ShouldBe(["Recent repositories", "Archive", "Work"]);
        dashboard.Groups[0].Items.Select(i => i.Path).ShouldBe(["/r/bir", "/r/dort"]);
        dashboard.Groups[2].Items.Single().Path.ShouldBe("/r/iki");
    }

    [Fact]
    public async Task Kategorili_depo_son_depolar_grubunda_GORUNMUYOR()
    {
        // Otherwise it would be in the list twice, and removing it from one place would look
        // like it had not worked.
        FakeRecentRepositoryStore store = new("/r/bir");
        store.WithCategory("/r/bir", "Work");

        DashboardViewModel dashboard = await CreateAsync(store);

        dashboard.Groups.Single().Header.ShouldBe("Work");
    }

    [Fact]
    public async Task Arama_yol_ve_ad_uzerinde_calisiyor()
    {
        FakeRecentRepositoryStore store = new("/home/dev/gitext-core", "/home/dev/avalonia");

        DashboardViewModel dashboard = await CreateAsync(store);

        dashboard.SearchText = "gitext";
        dashboard.Groups.SelectMany(g => g.Items).Select(i => i.Path).ShouldBe(["/home/dev/gitext-core"]);

        // The user may remember the folder above it rather than the repository's own name.
        dashboard.SearchText = "dev";
        dashboard.Groups.SelectMany(g => g.Items).Count().ShouldBe(2);
    }

    [Fact]
    public async Task Arama_eslesmezse_grup_kalmiyor_ama_liste_bos_degil()
    {
        // Two different states: "nothing has ever been opened" and "the search matched nothing".
        // They have different answers on screen, so they need different flags.
        DashboardViewModel dashboard = await CreateAsync(new FakeRecentRepositoryStore("/r/bir"));

        dashboard.SearchText = "yok-boyle-bir-sey";

        dashboard.HasResults.ShouldBeFalse();
        dashboard.HasRepositories.ShouldBeTrue();
    }

    [Fact]
    public async Task Arama_depoyu_YENIDEN_OKUMUYOR()
    {
        // 🔴 GitExtensions passes `reloadData: false` while filtering, and for a reason: reading
        // the file system on every keystroke makes typing stutter. Here the probe counts the
        // reads — filtering may narrow the list, it may not go back to the disk for every letter.
        int probes = 0;

        DashboardViewModel dashboard = await CreateAsync(
            new FakeRecentRepositoryStore("/r/bir", "/r/iki"),
            probe: path =>
            {
                probes++;
                return Valid(path);
            });

        int afterLoad = probes;

        dashboard.SearchText = "bir";

        // One entry survives the filter, so at most that one is probed — not the whole list again.
        (probes - afterLoad).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Ulasilamayan_depo_LISTEDE_KALIYOR_isaretleniyor()
    {
        // An unmounted drive is not a reason to throw away the user's list.
        FakeRecentRepositoryStore store = new("/r/var", "/r/yok");

        DashboardViewModel dashboard = await CreateAsync(
            store,
            probe: path => path == "/r/yok" ? RepositoryHeadInfo.NotARepository : Valid(path));

        dashboard.Groups.Single().Items.Count.ShouldBe(2);
        dashboard.Groups.Single().Items.Single(i => i.Path == "/r/yok").IsValid.ShouldBeFalse();
        dashboard.HasInvalidRepositories.ShouldBeTrue();
    }

    [Fact]
    public async Task Kayip_depolari_temizle_SADECE_kayiplari_siliyor()
    {
        FakeRecentRepositoryStore store = new("/r/var", "/r/yok");

        DashboardViewModel dashboard = await CreateAsync(
            store,
            probe: path => path == "/r/yok" ? RepositoryHeadInfo.NotARepository : Valid(path));

        await dashboard.RemoveMissingCommand.ExecuteAsync(null);

        store.Paths.ShouldBe(["/r/var"]);
        dashboard.HasInvalidRepositories.ShouldBeFalse();
    }

    [Fact]
    public async Task Listeden_cikar_secili_depoyu_siliyor()
    {
        FakeRecentRepositoryStore store = new("/r/bir", "/r/iki");

        DashboardViewModel dashboard = await CreateAsync(store);
        dashboard.SelectedItem = dashboard.Groups[0].Items.Single(i => i.Path == "/r/bir");

        await dashboard.RemoveFromListCommand.ExecuteAsync(null);

        store.Paths.ShouldBe(["/r/iki"]);
    }

    [Fact]
    public async Task Secim_yokken_listeden_cikar_CALISMIYOR()
    {
        // The counter-evidence: were the command always enabled, the test above would prove
        // nothing about which repository goes.
        DashboardViewModel dashboard = await CreateAsync(new FakeRecentRepositoryStore("/r/bir"));

        dashboard.RemoveFromListCommand.CanExecute(null).ShouldBeFalse();
        dashboard.ShowInFolderCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Kategori_menusu_GitExtensions_sirasinda()
    {
        // `tsmiCategories_DropDownOpening`: (none) · the categories · "Add new...".
        FakeRecentRepositoryStore store = new("/r/bir", "/r/iki");
        store.WithCategory("/r/iki", "Work");

        DashboardViewModel dashboard = await CreateAsync(store);
        dashboard.SelectedItem = dashboard.Groups[0].Items.Single();

        dashboard.CategoryChoices.Select(c => c.Header).ShouldBe(["(none)", "Work", "Add new..."]);
    }

    [Fact]
    public async Task Bulundugu_kategori_menude_KAPALI()
    {
        FakeRecentRepositoryStore store = new("/r/bir");
        store.WithCategory("/r/bir", "Work");

        DashboardViewModel dashboard = await CreateAsync(store);
        dashboard.SelectedItem = dashboard.Groups.Single().Items.Single();

        // Filing something where it already is does nothing; the menu says so instead of
        // pretending it is an option.
        dashboard.CategoryChoices.Single(c => c.Header == "Work").IsEnabled.ShouldBeFalse();

        // …and "(none)" IS available, because that is how it is taken back out.
        dashboard.CategoryChoices.Single(c => c.Header == "(none)").IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Kategorisiz_depoda_hicbiri_secenegi_KAPALI()
    {
        DashboardViewModel dashboard = await CreateAsync(new FakeRecentRepositoryStore("/r/bir"));
        dashboard.SelectedItem = dashboard.Groups.Single().Items.Single();

        dashboard.CategoryChoices.Single(c => c.Header == "(none)").IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Yeni_kategori_ekleniyor()
    {
        FakeRecentRepositoryStore store = new("/r/bir");
        DashboardViewModel dashboard = await CreateAsync(store);

        FakePrompt prompt = new() { NameToReturn = "Work" };
        dashboard.Prompt = prompt;
        dashboard.SelectedItem = dashboard.Groups.Single().Items.Single();

        await dashboard.AddCategoryCommand.ExecuteAsync(null);

        dashboard.Groups.Single().Header.ShouldBe("Work");
        (await store.LoadAsync(TestContext.Current.CancellationToken)).Single().Category.ShouldBe("Work");
    }

    [Fact]
    public async Task Iptal_edilen_kategori_diyalogu_HICBIR_SEY_degistirmiyor()
    {
        FakeRecentRepositoryStore store = new("/r/bir");
        DashboardViewModel dashboard = await CreateAsync(store);

        dashboard.Prompt = new FakePrompt { NameToReturn = null };
        dashboard.SelectedItem = dashboard.Groups.Single().Items.Single();

        await dashboard.AddCategoryCommand.ExecuteAsync(null);

        (await store.LoadAsync(TestContext.Current.CancellationToken)).Single().IsFavourite.ShouldBeFalse();
    }

    [Fact]
    public async Task Kategori_silinince_depolar_son_depolara_DONUYOR()
    {
        // 🔴 Deleting a category must not delete the repositories in it. That would be a far
        // bigger loss than the user asked for — and it cannot be undone.
        FakeRecentRepositoryStore store = new("/r/bir");
        store.WithCategory("/r/bir", "Work");

        DashboardViewModel dashboard = await CreateAsync(store);
        dashboard.Prompt = new FakePrompt { ConfirmAnswer = true };

        await dashboard.DeleteCategoryCommand.ExecuteAsync(dashboard.Groups.Single());

        store.Paths.ShouldBe(["/r/bir"]);
        dashboard.Groups.Single().Header.ShouldBe("Recent repositories");
    }

    [Fact]
    public async Task Kategori_silme_ONAY_ISTIYOR()
    {
        FakeRecentRepositoryStore store = new("/r/bir");
        store.WithCategory("/r/bir", "Work");

        DashboardViewModel dashboard = await CreateAsync(store);
        FakePrompt prompt = new() { ConfirmAnswer = false };
        dashboard.Prompt = prompt;

        await dashboard.DeleteCategoryCommand.ExecuteAsync(dashboard.Groups.Single());

        prompt.ConfirmCount.ShouldBe(1);
        dashboard.Groups.Single().Header.ShouldBe("Work");
    }

    [Fact]
    public async Task Kategori_yeniden_adlandiriliyor()
    {
        FakeRecentRepositoryStore store = new("/r/bir", "/r/iki");
        store.WithCategory("/r/bir", "Work");
        store.WithCategory("/r/iki", "Work");

        DashboardViewModel dashboard = await CreateAsync(store);
        dashboard.Prompt = new FakePrompt { NameToReturn = "İş" };

        await dashboard.RenameCategoryCommand.ExecuteAsync(dashboard.Groups.Single());

        dashboard.Groups.Single().Header.ShouldBe("İş");
        dashboard.Groups.Single().Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Son_depolari_temizle_FAVORILERI_birakiyor()
    {
        // "Clear the recent list" is not "throw away my filing".
        FakeRecentRepositoryStore store = new("/r/bir", "/r/iki");
        store.WithCategory("/r/iki", "Work");

        DashboardViewModel dashboard = await CreateAsync(store);
        dashboard.Prompt = new FakePrompt { ConfirmAnswer = true };

        await dashboard.ClearRecentCommand.ExecuteAsync(null);

        store.Paths.ShouldBe(["/r/iki"]);
        dashboard.Groups.Single().Header.ShouldBe("Work");
    }

    [Fact]
    public async Task Ilk_kayit_aramanin_ilk_sonucu()
    {
        // Enter in the search box opens this one (GitExtensions' `TextBoxSearch_KeyDown`).
        DashboardViewModel dashboard = await CreateAsync(new FakeRecentRepositoryStore("/r/bir", "/r/iki"));

        dashboard.FirstItem.ShouldNotBeNull().Path.ShouldBe("/r/bir");

        dashboard.SearchText = "iki";
        dashboard.FirstItem.ShouldNotBeNull().Path.ShouldBe("/r/iki");
    }

    [Fact]
    public async Task Dal_adi_ve_ayrik_HEAD_kayitta_gorunuyor()
    {
        DashboardViewModel dashboard = await CreateAsync(
            new FakeRecentRepositoryStore("/r/dal", "/r/ayrik"),
            probe: path => path == "/r/ayrik"
                ? new RepositoryHeadInfo(IsRepository: true, BranchName: null)
                : new RepositoryHeadInfo(IsRepository: true, BranchName: "feature/x"));

        DashboardRepositoryItem[] items = [.. dashboard.Groups.Single().Items];

        items.Single(i => i.Path == "/r/dal").Branch.ShouldBe("feature/x");

        // A detached HEAD is not left blank: an empty line reads as "could not be read".
        items.Single(i => i.Path == "/r/ayrik").Branch.ShouldBe("(no branch)");
    }
}
