using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T21 — Grafik penceresinin kaydırılması.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu davranışın sebebi ölçüm.</b> P03-T18'de gerçek depolar ölçüldü: git/git ve Linux'ta
/// şerit sayısı <b>medyanda ~120</b> ve commit düğümleri bu şeritlere yayılıyor —
/// 16 şeritlik sabit bir sınır Linux'ta düğümlerin yalnızca %24'ünü gösterirdi. Yani "kes at"
/// yaklaşımı işe yaramaz; sütun sabit genişlikte kalıp <b>kayan bir pencere</b> gösteriyor
/// ve pencere seçili commit'i takip ediyor.
/// </para>
/// </remarks>
public class GraphWindowTests
{
    /// <summary>
    /// <paramref name="width"/> kadar paralel dal içeren bir geçmiş üretir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Her dalın <b>kendi ara commit'i</b> var: uçların hepsi doğrudan köke bağlansaydı
    /// yerleşim motoru şeridi yeniden kullanır ve tek şeride düşerdi (bu davranış P03-T13'te
    /// bilerek eklendi). Ara commit'ler, uçlar işlenirken şeritlerin <b>açık kalmasını</b>
    /// sağlar — gerçek depolardaki geniş grafiğin küçük ölçekli hâli.
    /// </para>
    /// <para>
    /// Sıra topolojik: tüm uçlar → tüm ara commit'ler → kök.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<CommitInfo> WideHistory(int width)
    {
        List<CommitInfo> commits = [];

        // Uçlar.
        for (int i = width; i >= 1; i--)
        {
            commits.Add(FakeGitData.Commit(
                id: FakeGitData.Sha(1 + width + i),
                parents: [FakeGitData.Sha(1 + i)],
                subject: $"uç {i}"));
        }

        // Ara commit'ler — hepsi köke bağlı.
        for (int i = width; i >= 1; i--)
        {
            commits.Add(FakeGitData.Commit(
                id: FakeGitData.Sha(1 + i),
                parents: [FakeGitData.Sha(1)],
                subject: $"ara {i}"));
        }

        commits.Add(FakeGitData.Commit(FakeGitData.Sha(1), [], "kök"));

        return commits;
    }

    private static async Task<CommitListViewModel> LoadedAsync(int width)
    {
        CommitListViewModel viewModel = new(
            new FakeRepositoryLocator(),
            new FakeCommitLogReader(WideHistory(width)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),new FakeDiffReader());

        await viewModel.OpenAsync("/tmp/depo");
        return viewModel;
    }

    [AvaloniaFact]
    public async Task Pencere_bastan_baslar()
    {
        CommitListViewModel viewModel = await LoadedAsync(30);

        viewModel.FirstVisibleLane.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Sagdaki_bir_serit_secilince_pencere_kayar()
    {
        CommitListViewModel viewModel = await LoadedAsync(30);

        // Pencere dışında kalan yüksek şeritli bir satır bul.
        int index = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= viewModel.VisibleLanes);

        int lane = viewModel.Rows[index].GraphRow.Lane;

        viewModel.SelectedIndex = index;

        // Seçili commit'in düğümü DAİMA görünmeli; aksi halde kullanıcı seçtiği satırın
        // grafikte nerede olduğunu göremez.
        viewModel.FirstVisibleLane.ShouldBeLessThanOrEqualTo(lane);
        (viewModel.FirstVisibleLane + viewModel.VisibleLanes).ShouldBeGreaterThan(lane);
    }

    [AvaloniaFact]
    public async Task Pencere_icindeki_secim_pencereyi_oynatmaz()
    {
        // Her seçimde ortalamak grafiği sürekli zıplatırdı; pencere yalnızca gerektiğinde kayar.
        CommitListViewModel viewModel = await LoadedAsync(30);

        int index = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= viewModel.VisibleLanes);

        viewModel.SelectedIndex = index;
        int after = viewModel.FirstVisibleLane;

        // Aynı pencere içindeki başka bir şeride geç.
        int inWindow = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= after
                && viewModel.Rows[i].GraphRow.Lane < after + viewModel.VisibleLanes);

        viewModel.SelectedIndex = inWindow;

        viewModel.FirstVisibleLane.ShouldBe(after);
    }

    [AvaloniaFact]
    public async Task Sola_donunce_pencere_geri_kayar()
    {
        CommitListViewModel viewModel = await LoadedAsync(30);

        int far = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= viewModel.VisibleLanes);

        viewModel.SelectedIndex = far;
        viewModel.FirstVisibleLane.ShouldBeGreaterThan(0);

        int near = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane == 0);

        viewModel.SelectedIndex = near;

        viewModel.FirstVisibleLane.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Yeni_depo_acilinca_pencere_sifirlanir()
    {
        CommitListViewModel viewModel = await LoadedAsync(30);

        int far = Enumerable.Range(0, viewModel.Rows.Count)
            .First(i => viewModel.Rows[i].GraphRow.Lane >= viewModel.VisibleLanes);

        viewModel.SelectedIndex = far;
        viewModel.FirstVisibleLane.ShouldBeGreaterThan(0);

        await viewModel.OpenAsync("/tmp/baska-depo");

        viewModel.FirstVisibleLane.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Sutun_dar_depoda_daralir_genis_depoda_sinirda_durur()
    {
        // Dar depoda 12 şeritlik sabit sütun boşuna yer kaplardı; geniş depoda sınır devreye
        // girmeli. Değer tüm satırlarda ortak olduğu için sütunlar yine hizalı kalır.
        CommitListViewModel narrow = await LoadedAsync(3);
        narrow.VisibleLanes.ShouldBe(narrow.Rows.Max(r => r.GraphRow.LaneCount));
        narrow.VisibleLanes.ShouldBeLessThan(CommitListViewModel.DefaultVisibleLanes);

        CommitListViewModel wide = await LoadedAsync(30);
        wide.VisibleLanes.ShouldBe(CommitListViewModel.DefaultVisibleLanes);
    }

    [AvaloniaFact]
    public async Task Dar_gecmiste_pencere_hic_kaymaz()
    {
        // Çoğu depo dardır; orada pencere mekanizması hiç görünmemeli.
        CommitListViewModel viewModel = await LoadedAsync(3);

        for (int i = 0; i < viewModel.Rows.Count; i++)
        {
            viewModel.SelectedIndex = i;
            viewModel.FirstVisibleLane.ShouldBe(0);
        }
    }
}
